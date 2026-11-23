using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Xbc.DbAccess.Common;
using Xbc.DbAccess.Diagnostics;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库访问实现，由 <see cref="DbAccessFactoryBase"/> 创建；方言差异由注入的工厂体系承载，本类无方言分支。
    /// <para>连接与事务经 AsyncLocal 上下文路由到当前异步调用链；执行入口均在返回 Task 前于调用方执行上下文
    /// 同步初始化 AsyncLocal 上下文（写入若发生在 async 方法内部，会随方法返回而丢失）。</para>
    /// <para><b>并发警示</b>：同一实例禁止以 Task.WhenAll 等方式并发调用各自开启事务的方法：分叉的异步流
    /// 共享执行上下文，并发事务会在共享事务栈上相互嵌套为保存点父子事务，产生部分提交。需要并行时请使用独立实例。</para>
    /// </summary>
    internal sealed class DbAccess : DisposableBase, IDbAccess
    {
        /// <summary>
        /// 数据库访问上下文，维护当前异步调用链的连接作用域管理器和事务管理器。
        /// </summary>
        private class DbAccessContext
        {
            /// <summary>
            /// 连接作用域管理器。
            /// </summary>
            public IDbConnectionScopeManager ConnectionScopeManager { get; set; }

            /// <summary>
            /// 事务管理器。
            /// </summary>
            public IDbTransactionManager TransactionManager { get; set; }

            /// <summary>
            /// 获取当前数据库连接。
            /// <para>优先级：事务管理器最高，其次连接作用域管理器，两者均无时为 null。</para>
            /// </summary>
            /// <returns></returns>
            public DbConnection GetCurrentConnection()
            {
                return GetConnectionAccessor()?.CurrentConnection;
            }

            /// <summary>
            /// 获取当前数据库连接访问器。
            /// <para>优先级：事务管理器最高，其次连接作用域管理器，两者均无时为 null。</para>
            /// </summary>
            /// <returns></returns>
            public IDbConnectionAccessor GetConnectionAccessor()
            {
                var transManager = TransactionManager;
                if (transManager?.HasActiveConnection == true)
                    return transManager;

                var scopeManager = ConnectionScopeManager;
                if (scopeManager?.HasActiveConnection == true)
                    return scopeManager;

                return null;
            }

            /// <summary>
            /// 是否在事务或连接作用域中。
            /// </summary>
            public bool InTransactionOrConnectionScope => GetConnectionAccessor() != null;
        }

        private readonly IDbAccessFactory factory;

        // 使用 AsyncLocal 支持异步上下文传播
        private readonly AsyncLocal<DbAccessContext> dbAccessContexts
            = new AsyncLocal<DbAccessContext>();

        // 用于跟踪所有的数据库访问上下文（弱引用：分叉的异步流结束后其上下文可被回收，
        // 避免长生命周期实例上上下文只增不减导致内存持续增长；主干上下文由 AsyncLocal 持强引用不会被误回收）
        private readonly ConcurrentBag<WeakReference<DbAccessContext>> allContexts
            = new ConcurrentBag<WeakReference<DbAccessContext>>();

        // 追踪器
        private readonly IDbConnectionTracker connectionTracker;

        // 指标使用的提供程序名称（构造时解析一次，避免每次执行时重复解析）
        private readonly string metricsProviderName;

        // 当前异步调用链上的 SQL 执行 span（AsyncLocal 隔离同一实例上的并发执行流；未接 listener 时为 null）
        private readonly AsyncLocal<Activity> currentExecutionActivity = new AsyncLocal<Activity>();

        /// <summary>
        /// 数据库访问提供程序工厂。
        /// </summary>
        public DbAccessProviderFactory ProviderFactory => factory.ProviderFactory;

        /// <summary>
        /// 数据库访问工厂。
        /// </summary>
        public IDbAccessFactory Factory => factory;

        /// <summary>
        /// 初始化数据库访问实例。
        /// </summary>
        /// <param name="factory">数据库访问工厂。</param>
        /// <param name="connectionTracker">连接追踪器。</param>
        internal DbAccess(IDbAccessFactory factory, IDbConnectionTracker connectionTracker)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.connectionTracker = connectionTracker;

            // 解析指标使用的提供程序名称
            metricsProviderName = DbAccessMetrics.GetProviderName(ProviderFactory.DbProviderFactory);
        }

        /// <summary>
        /// 确保数据库访问上下文已创建。
        /// </summary>
        private void EnsureDbAccessContext()
        {
            if (dbAccessContexts.Value == null)
            {
                var context = new DbAccessContext()
                {
                    ConnectionScopeManager = new NestedDbConnectionScopeManager(factory.ConnectionManager),
                    TransactionManager = new NestedDbTransactionManager(factory.ConnectionManager, ProviderFactory.SavepointSqlDialect),
                };

                // 初始化当前线程（AsyncLocal）的上下文，并跟踪到 allContexts 以便释放时统一释放（弱引用，见字段说明）
                dbAccessContexts.Value = context;
                allContexts.Add(new WeakReference<DbAccessContext>(context));
            }
        }

        #region 连接管理
        /// <summary>
        /// 异步获取或创建数据库连接：优先使用入口快照的连接访问器（事务/连接作用域路由），
        /// 无访问器时从连接管理器获取独立连接；后者无论成功或失败均记录获取耗时指标。
        /// </summary>
        /// <param name="connectionAccessor">入口同步段快照的连接访问器，可为 null。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        private async Task<DbConnection> GetOrCreateConnectionAsync(IDbConnectionAccessor connectionAccessor, CancellationToken cancellationToken = default)
        {
            if (connectionAccessor != null)
                return connectionAccessor.CurrentConnection;

            // 从连接管理器获取连接，记录获取耗时与状态（成功与失败两路径均记录）
            var startTimestamp = StopwatchClock.ElapsedTicks;
            try
            {
                var connection = await factory.ConnectionManager.GetOrCreateAsync(cancellationToken).ConfigureAwait(false);

                // 成功路径记录连接获取耗时指标
                DbAccessMetrics.RecordConnectionAcquire(metricsProviderName, "ok", StopwatchClock.GetDurationFrom(startTimestamp).TotalMilliseconds);
                return connection;
            }
            catch
            {
                // 失败路径同样记录指标，status=error 使获取失败次数可见
                DbAccessMetrics.RecordConnectionAcquire(metricsProviderName, "error", StopwatchClock.GetDurationFrom(startTimestamp).TotalMilliseconds);
                throw;
            }
        }

        /// <summary>
        /// 释放数据库连接；当前处于事务或连接作用域中时不释放，由对应管理器负责。
        /// <para>internal 供 <see cref="ReusableCommand"/> 归还其持有的连接。</para>
        /// <para>判定归属时管理器可能已释放（<see cref="ObjectDisposedException"/>），此时降级为直接归还
        /// 连接管理器（内建管理器的 Release 幂等），避免借出的连接与池名额泄漏。</para>
        /// </summary>
        /// <param name="connection">数据库连接。</param>
        internal void DisposeDbConnection(DbConnection connection)
        {
            if (connection == null)
                return;

            // 处于事务或连接作用域时由对应管理器负责释放连接
            // （执行入口均在调用方执行上下文同步初始化 AsyncLocal 上下文，此处必然非 null）
            try
            {
                if (dbAccessContexts.Value.GetConnectionAccessor() != null)
                    return;
            }
            catch (ObjectDisposedException)
            {
                // 管理器已释放、归属无法判定：降级为归还连接管理器（Release 幂等），不让异常从释放路径逃逸
            }

            // 其他情况下，由连接管理器释放连接
            factory.ConnectionManager.Release(connection);// 等效于connection.Dispose();
        }
        #endregion

        #region 连接作用域和事务
        /// <summary>
        /// 异步开启连接作用域。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public Task<IDbConnectionScope> BeginConnectionScopeAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // 说明：AsyncLocal 的写入必须发生在调用者的执行上下文中；
            // 若放入 async 方法内部，写入会随方法返回而丢失，导致作用域内的后续执行无法看到连接作用域。
            EnsureDbAccessContext();

            var scopeId = Guid.NewGuid();
            var currentContext = dbAccessContexts.Value;

            // 如果当前正在执行事务，使用事务中的连接来开启连接作用域
            var transactionConnection = currentContext.TransactionManager.CurrentConnection;
            if (transactionConnection != null)
                return currentContext.ConnectionScopeManager.CreateScopeAsync(transactionConnection, scopeId, cancellationToken);
            else
                return currentContext.ConnectionScopeManager.CreateScopeAsync(scopeId, cancellationToken);
        }

        /// <summary>
        /// 异步开启事务；ADO.NET 未提供异步开启事务的标准 API，开启事务本身保持同步调用。
        /// </summary>
        /// <param name="isolationLevel">事务隔离级别。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public Task<DbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.Unspecified, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // 说明：AsyncLocal 的写入必须发生在调用者的执行上下文中；
            // 若放入 async 方法内部，写入会随方法返回而丢失，导致事务内的后续执行无法路由事务连接。
            EnsureDbAccessContext();

            var transactionId = Guid.NewGuid();
            var currentContext = dbAccessContexts.Value;

            // 如果当前已开启了连接作用域，使用连接作用域中的连接来开启事务
            var scopeConnection = currentContext.ConnectionScopeManager.CurrentConnection;
            if (scopeConnection != null)
                return currentContext.TransactionManager.BeginTransactionAsync(scopeConnection, transactionId, isolationLevel, cancellationToken);
            else
                return currentContext.TransactionManager.BeginTransactionAsync(transactionId, isolationLevel, cancellationToken);
        }
        #endregion

        #region 辅助方法
        /// <summary>
        /// 校验语句参数数量不超过当前方言的单语句参数上限，超限抛 <see cref="InvalidOperationException"/>，
        /// 优于提供程序的底层报错（如 Oracle 的 ORA-01036）。
        /// <para>调用位置约定：各执行入口的同步段、<see cref="EnsureDbAccessContext"/> 之前，
        /// 参数无效时不初始化任何执行上下文（同步抛出，不进 Task）。</para>
        /// </summary>
        /// <param name="parameters">参数数组，可为 null。</param>
        private void ValidateParameterCount(DbParameter[] parameters)
        {
            if (parameters == null)
                return;

            var maxParameterCount = ProviderFactory.SqlDialect.MaxParameterCount;
            if (parameters.Length > maxParameterCount)
                throw new InvalidOperationException($"语句参数数量 {parameters.Length} 超出当前方言上限 {maxParameterCount}，请分批执行或缩小条件范围。");
        }

        /// <summary>
        /// 创建并完成配置的数据库命令：先按通用扩展方法装配 SQL、命令类型、参数与事务，
        /// 再应用提供程序级配置（如 Oracle 的 BindByName，见 <see cref="DbAccessProviderFactory.ConfigureCommand"/>）。
        /// </summary>
        /// <param name="connection">数据库连接。</param>
        /// <param name="sql">SQL 语句或存储过程名称。</param>
        /// <param name="commandType">命令类型。</param>
        /// <param name="transaction">数据库事务。</param>
        /// <param name="parameters">参数数组。</param>
        /// <returns>配置完成的数据库命令。</returns>
        internal DbCommand CreateConfiguredCommand(DbConnection connection, string sql, CommandType commandType, DbTransaction transaction, DbParameter[] parameters)
        {
            var command = connection.CreateCommand(sql, commandType, transaction, parameters);
            ProviderFactory.ConfigureCommand(command);
            return command;
        }

        /// <summary>
        /// 释放一次性命令：先清空参数集合解除参数的集合归属（多数驱动的 Dispose 不解除 Parent 引用，
        /// 参数已属其他集合时再 AddRange 会抛底层异常），清空后调用方持有的参数数组可再次提交执行。
        /// </summary>
        /// <param name="command">待释放的命令，可为 null。</param>
        internal static void DisposeCommand(DbCommand command)
        {
            if (command == null)
                return;

            command.Parameters.Clear();
            command.Dispose();
        }

        /// <summary>
        /// 创建可复用命令租约（内部 API，供实体层批量写入使用）。
        /// <para>租约在同一连接作用域/事务内复用同一命令，SQL 与参数的复用规则见 <see cref="ReusableCommand"/>；
        /// 执行经 <see cref="ExecuteNonQueryAsync(ReusableCommand, string, DbParameter[], CancellationToken)"/> 等内部重载。</para>
        /// <para>租约须在其首次执行所处的连接作用域/事务内使用并释放，作用域结束后持有的命令与连接引用失效。</para>
        /// </summary>
        /// <returns>可复用命令租约。</returns>
        internal ReusableCommand CreateReusableCommand()
        {
            ThrowIfDisposed();
            return new ReusableCommand(this);
        }
        #endregion

        #region ExecuteNonQuery
        /// <summary>
        /// 异步执行非查询 SQL 语句，返回受影响的行数。
        /// </summary>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public Task<int> ExecuteNonQueryAsync(string sql, DbParameter[] parameters = null, CancellationToken cancellationToken = default)
        {
            return ExecuteNonQueryAsync(sql, CommandType.Text, parameters, cancellationToken);
        }

        /// <summary>
        /// 异步执行非查询 SQL 语句或存储过程，返回受影响的行数。
        /// </summary>
        /// <param name="sql">SQL 语句或存储过程名称。</param>
        /// <param name="commandType">命令类型。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public Task<int> ExecuteNonQueryAsync(string sql, CommandType commandType, DbParameter[] parameters = null, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNullOrWhiteSpace(sql, nameof(sql));
            ThrowIfDisposed();
            ValidateParameterCount(parameters);

            // 说明：AsyncLocal 的写入必须发生在调用者的执行上下文中；
            // 若放入 async 方法内部，写入会随方法返回而丢失，导致本次执行无法路由连接/事务上下文。
            EnsureDbAccessContext();

            return ExecuteNonQueryCoreAsync(sql, commandType, parameters, null, cancellationToken);
        }

        /// <summary>
        /// 异步经可复用命令租约执行非查询 SQL 语句，返回受影响的行数（内部 API，供批量写入的逐条路径使用）。
        /// <para>与公共执行入口共用同一核心，SQL 与参数的复用规则见 <see cref="ReusableCommand"/>。</para>
        /// </summary>
        /// <param name="reusableCommand">可复用命令租约（经 <see cref="CreateReusableCommand"/> 创建）。</param>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        internal Task<int> ExecuteNonQueryAsync(ReusableCommand reusableCommand, string sql, DbParameter[] parameters = null, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(reusableCommand, nameof(reusableCommand));
            Guard.ThrowIfNullOrWhiteSpace(sql, nameof(sql));
            ThrowIfDisposed();
            ValidateParameterCount(parameters);

            // 说明：AsyncLocal 的写入必须发生在调用者的执行上下文中（与公共执行入口一致）；
            // 若放入 async 方法内部，写入会随方法返回而丢失，导致本次执行无法路由连接/事务上下文。
            EnsureDbAccessContext();

            return ExecuteNonQueryCoreAsync(sql, CommandType.Text, parameters, reusableCommand, cancellationToken);
        }

        /// <summary>
        /// 执行非查询语句的核心实现（一次性执行与可复用命令共用）。
        /// </summary>
        private async Task<int> ExecuteNonQueryCoreAsync(string sql, CommandType commandType, DbParameter[] parameters, ReusableCommand reusableCommand, CancellationToken cancellationToken)
        {
            // 同步快照上下文（任何 await 之前读取）：分叉的兄弟异步流可能在此期间改变共享的 AsyncLocal
            // 上下文，await 之后二次读取会绑定到不一致的事务或误判连接归属（独立连接泄漏）
            var context = dbAccessContexts.Value;
            var connectionAccessor = context.GetConnectionAccessor();
            var transaction = context.TransactionManager.CurrentTransaction;

            // 获取或创建连接：复用命令时优先取租约已解析的连接
            var connection = reusableCommand?.Connection ?? await GetOrCreateConnectionAsync(connectionAccessor, cancellationToken).ConfigureAwait(false);

            var operation = nameof(ExecuteNonQueryAsync);
            long startTimestamp = 0;

            try
            {
                // 标记连接为活动状态并开始计时（自此处起纳入 try：借出的连接在任何后续异常下均经 finally 归还）
                startTimestamp = BeginExecution(connection, sql, operation);

                // 获取命令：一次性执行每次新建；复用执行由租约配置
                var command = reusableCommand != null
                    ? reusableCommand.Configure(connection, sql, transaction, parameters)
                    : CreateConfiguredCommand(connection, sql, commandType, transaction, parameters);

                try
                {
                    int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                    RecordExecutionSuccess(operation, sql, startTimestamp);
                    return rowsAffected;
                }
                finally
                {
                    // 一次性执行的命令用完即释放（先解除参数归属）；复用执行的命令由租约持有
                    if (reusableCommand == null)
                        DisposeCommand(command);
                }
            }
            catch (Exception ex)
            {
                RecordExecutionError(operation, ex, startTimestamp);
                throw;
            }
            finally
            {
                // 一次性执行的连接用完即归还（复用执行的连接由租约持有，租约释放时归还）；
                // 独立获取的连接（快照无访问器）由本次执行自管归还，不依赖执行期间的上下文状态
                if (reusableCommand == null && connectionAccessor == null)
                    factory.ConnectionManager.Release(connection);
            }
        }
        #endregion

        #region ExecuteScalar
        /// <summary>
        /// 异步执行查询 SQL 语句，返回结果集中第一行第一列的值。
        /// <para>返回值遵循 ADO.NET 契约：结果集为空（零行）时返回 null，有行但第一列为 NULL 时返回 <see cref="DBNull.Value"/>（原样透传，不做归一）。</para>
        /// </summary>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>第一行第一列的值；结果集为空时为 null，空列时为 <see cref="DBNull.Value"/>。</returns>
        public Task<object> ExecuteScalarAsync(string sql, DbParameter[] parameters = null, CancellationToken cancellationToken = default)
        {
            return ExecuteScalarAsync(sql, CommandType.Text, parameters, cancellationToken);
        }

        /// <summary>
        /// 异步执行查询 SQL 语句或存储过程，返回结果集中第一行第一列的值。
        /// </summary>
        /// <param name="sql">SQL 语句或存储过程名称。</param>
        /// <param name="commandType">命令类型。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public Task<object> ExecuteScalarAsync(string sql, CommandType commandType, DbParameter[] parameters = null, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNullOrWhiteSpace(sql, nameof(sql));
            ThrowIfDisposed();
            ValidateParameterCount(parameters);

            // 说明：AsyncLocal 的写入必须发生在调用者的执行上下文中；
            // 若放入 async 方法内部，写入会随方法返回而丢失，导致本次执行无法路由连接/事务上下文。
            EnsureDbAccessContext();

            return ExecuteScalarCoreAsync(sql, commandType, parameters, null, cancellationToken);
        }

        /// <summary>
        /// 异步经可复用命令租约执行查询 SQL 语句，返回结果集中第一行第一列的值（内部 API，供批量写入的逐条路径使用）。
        /// <para>与公共执行入口共用同一核心，SQL 与参数的复用规则见 <see cref="ReusableCommand"/>。</para>
        /// </summary>
        /// <param name="reusableCommand">可复用命令租约（经 <see cref="CreateReusableCommand"/> 创建）。</param>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>结果集中第一行第一列的值。</returns>
        internal Task<object> ExecuteScalarAsync(ReusableCommand reusableCommand, string sql, DbParameter[] parameters = null, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(reusableCommand, nameof(reusableCommand));
            Guard.ThrowIfNullOrWhiteSpace(sql, nameof(sql));
            ThrowIfDisposed();
            ValidateParameterCount(parameters);

            // 说明：AsyncLocal 的写入必须发生在调用者的执行上下文中（与公共执行入口一致）；
            // 若放入 async 方法内部，写入会随方法返回而丢失，导致本次执行无法路由连接/事务上下文。
            EnsureDbAccessContext();

            return ExecuteScalarCoreAsync(sql, CommandType.Text, parameters, reusableCommand, cancellationToken);
        }

        /// <summary>
        /// 执行标量查询的核心实现（一次性执行与可复用命令共用）。
        /// </summary>
        private async Task<object> ExecuteScalarCoreAsync(string sql, CommandType commandType, DbParameter[] parameters, ReusableCommand reusableCommand, CancellationToken cancellationToken)
        {
            // 同步快照上下文（任何 await 之前读取）：分叉的兄弟异步流可能在此期间改变共享的 AsyncLocal
            // 上下文，await 之后二次读取会绑定到不一致的事务或误判连接归属（独立连接泄漏）
            var context = dbAccessContexts.Value;
            var connectionAccessor = context.GetConnectionAccessor();
            var transaction = context.TransactionManager.CurrentTransaction;

            // 获取或创建连接：复用命令时优先取租约已解析的连接
            var connection = reusableCommand?.Connection ?? await GetOrCreateConnectionAsync(connectionAccessor, cancellationToken).ConfigureAwait(false);

            var operation = nameof(ExecuteScalarAsync);
            long startTimestamp = 0;

            try
            {
                // 标记连接为活动状态并开始计时（自此处起纳入 try：借出的连接在任何后续异常下均经 finally 归还）
                startTimestamp = BeginExecution(connection, sql, operation);

                // 获取命令：一次性执行每次新建；复用执行由租约配置
                var command = reusableCommand != null
                    ? reusableCommand.Configure(connection, sql, transaction, parameters)
                    : CreateConfiguredCommand(connection, sql, commandType, transaction, parameters);

                try
                {
                    object result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

                    RecordExecutionSuccess(operation, sql, startTimestamp);
                    return result;
                }
                finally
                {
                    // 一次性执行的命令用完即释放（先解除参数归属）；复用执行的命令由租约持有
                    if (reusableCommand == null)
                        DisposeCommand(command);
                }
            }
            catch (Exception ex)
            {
                RecordExecutionError(operation, ex, startTimestamp);
                throw;
            }
            finally
            {
                // 一次性执行的连接用完即归还（复用执行的连接由租约持有，租约释放时归还）；
                // 独立获取的连接（快照无访问器）由本次执行自管归还，不依赖执行期间的上下文状态
                if (reusableCommand == null && connectionAccessor == null)
                    factory.ConnectionManager.Release(connection);
            }
        }
        #endregion

        #region ExecuteReader
        /// <summary>
        /// 异步执行查询 SQL 语句，返回 <see cref="DbDataReader"/> 实例。
        /// <para>连接的生命周期由框架自动管理：</para>
        /// <list type="bullet">
        /// <item>如果存在事务或连接作用域，连接不会自动释放，由事务管理器或连接作用域管理器负责。</item>
        /// <item>如果不存在事务或连接作用域，连接会在读取器释放时自动释放或回收到连接池。</item>
        /// </list>
        /// </summary>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public Task<DbDataReader> ExecuteReaderAsync(string sql, DbParameter[] parameters = null, CancellationToken cancellationToken = default)
        {
            return ExecuteReaderAsync(sql, CommandType.Text, parameters, cancellationToken);
        }

        /// <summary>
        /// 异步执行查询 SQL 语句或存储过程，返回 <see cref="DbDataReader"/> 实例。
        /// <para>连接的生命周期由框架自动管理：</para>
        /// <list type="bullet">
        /// <item>如果存在事务或连接作用域，连接不会自动释放，由事务管理器或连接作用域管理器负责。</item>
        /// <item>如果不存在事务或连接作用域，连接会在读取器释放时自动释放或回收到连接池。</item>
        /// </list>
        /// </summary>
        /// <param name="sql">SQL 语句或存储过程名称。</param>
        /// <param name="commandType">命令类型。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public Task<DbDataReader> ExecuteReaderAsync(string sql, CommandType commandType, DbParameter[] parameters = null, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNullOrWhiteSpace(sql, nameof(sql));
            ThrowIfDisposed();
            ValidateParameterCount(parameters);

            // 说明：AsyncLocal 的写入必须发生在调用者的执行上下文中；
            // 若放入 async 方法内部，写入会随方法返回而丢失，导致本次执行无法路由连接/事务上下文。
            EnsureDbAccessContext();

            return ExecuteReaderCoreAsync(sql, commandType, parameters, cancellationToken);
        }

        /// <summary>
        /// 执行读取器查询的核心实现。
        /// </summary>
        private async Task<DbDataReader> ExecuteReaderCoreAsync(string sql, CommandType commandType, DbParameter[] parameters, CancellationToken cancellationToken)
        {
            // 同步快照上下文（任何 await 之前读取）：分叉的兄弟异步流可能在此期间改变共享的 AsyncLocal
            // 上下文，await 之后二次读取会绑定到不一致的事务或误判连接归属（独立连接泄漏）
            var context = dbAccessContexts.Value;
            var connectionAccessor = context.GetConnectionAccessor();
            var transaction = context.TransactionManager.CurrentTransaction;
            bool manageConnection = connectionAccessor == null;// 只有不在事务或连接作用域中时（独立获取连接），才管理连接的释放

            var connection = await GetOrCreateConnectionAsync(connectionAccessor, cancellationToken).ConfigureAwait(false);

            var operation = nameof(ExecuteReaderAsync);
            long startTimestamp = 0;
            DbCommand command = null;
            bool readerReturned = false;

            try
            {
                // 标记连接为活动状态并开始计时（自此处起纳入 try：借出的连接在任何后续异常下均经 finally 归还）
                startTimestamp = BeginExecution(connection, sql, operation);

                command = CreateConfiguredCommand(connection, sql, commandType, transaction, parameters);
                var innerDataReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                // 由 DbDataReaderWrapper 管理连接和命令的生命周期
                var dataReader = new DbDataReaderWrapper(innerDataReader, connection, command, manageConnection);

                RecordExecutionSuccess(operation, sql, startTimestamp);
                readerReturned = true;
                return dataReader;
            }
            catch (Exception ex)
            {
                RecordExecutionError(operation, ex, startTimestamp);

                // 执行失败时包装器尚未接管命令生命周期，需在此显式释放，避免异常路径泄漏 DbCommand
                DisposeCommand(command);
                throw;
            }
            finally
            {
                // 读取器成功返回后连接与命令由包装器接管；未返回时归还独立获取的连接
                if (!readerReturned && manageConnection)
                    factory.ConnectionManager.Release(connection);
            }
        }
        #endregion

        #region ExecuteDataSet
        /// <summary>
        /// 异步执行查询 SQL 语句，返回 <see cref="DataSet"/> 实例。
        /// </summary>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public Task<DataSet> ExecuteDataSetAsync(string sql, DbParameter[] parameters = null, CancellationToken cancellationToken = default)
        {
            return ExecuteDataSetAsync(sql, CommandType.Text, parameters, cancellationToken);
        }

        /// <summary>
        /// 异步执行查询 SQL 语句或存储过程，返回 <see cref="DataSet"/> 实例。
        /// </summary>
        /// <param name="sql">SQL 语句或存储过程名称。</param>
        /// <param name="commandType">命令类型。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public Task<DataSet> ExecuteDataSetAsync(string sql, CommandType commandType, DbParameter[] parameters = null, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNullOrWhiteSpace(sql, nameof(sql));
            ThrowIfDisposed();
            ValidateParameterCount(parameters);

            // 说明：AsyncLocal 的写入必须发生在调用者的执行上下文中；
            // 若放入 async 方法内部，写入会随方法返回而丢失，导致本次执行无法路由连接/事务上下文。
            EnsureDbAccessContext();

            return ExecuteDataSetCoreAsync(sql, commandType, parameters, cancellationToken);
        }

        /// <summary>
        /// 执行数据集查询的核心实现。
        /// </summary>
        private async Task<DataSet> ExecuteDataSetCoreAsync(string sql, CommandType commandType, DbParameter[] parameters, CancellationToken cancellationToken)
        {
            // 同步快照上下文（任何 await 之前读取）：分叉的兄弟异步流可能在此期间改变共享的 AsyncLocal
            // 上下文，await 之后二次读取会绑定到不一致的事务或误判连接归属（独立连接泄漏）
            var context = dbAccessContexts.Value;
            var connectionAccessor = context.GetConnectionAccessor();
            var transaction = context.TransactionManager.CurrentTransaction;

            var connection = await GetOrCreateConnectionAsync(connectionAccessor, cancellationToken).ConfigureAwait(false);

            var operation = nameof(ExecuteDataSetAsync);
            long startTimestamp = 0;

            try
            {
                // 标记连接为活动状态并开始计时（自此处起纳入 try：借出的连接在任何后续异常下均经 finally 归还）
                startTimestamp = BeginExecution(connection, sql, operation);

                return await Task.Run(() =>
                {
                    using (var command = CreateConfiguredCommand(connection, sql, commandType, transaction, parameters))
                    {
                        using (var adapter = ProviderFactory.CreateDataAdapter())
                        {
                            // 执行 SQL 语句，注意：
                            // 如果连接未打开，Fill 方法会隐式的打开和关闭连接；
                            // 如果连接在调用 Fill 方法之前已打开，Fill 方法不会关闭连接
                            var dataSet = new DataSet();
                            adapter.SelectCommand = command;
                            adapter.Fill(dataSet);

                            // 释放前解除参数归属，调用方参数数组可再次提交
                            command.Parameters.Clear();

                            RecordExecutionSuccess(operation, sql, startTimestamp);
                            return dataSet;
                        }
                    }
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RecordExecutionError(operation, ex, startTimestamp);
                throw;
            }
            finally
            {
                // 独立获取的连接（快照无访问器）由本次执行自管归还，不依赖执行期间的上下文状态
                if (connectionAccessor == null)
                    factory.ConnectionManager.Release(connection);
            }
        }
        #endregion

        #region OTel
        /// <summary>
        /// 标记连接为活动状态，开始 SQL 执行 span 与指标计时。
        /// </summary>
        /// <param name="connection">数据库连接。</param>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="operation">操作名称（如 ExecuteNonQuery），作为 span 的 db.operation tag。</param>
        /// <returns>指标计时的起始时间戳。</returns>
        private long BeginExecution(DbConnection connection, string sql, string operation)
        {
            // 解析资源 ID，标记连接为活动状态
            Guid connectionId;
            if (connection is DbConnectionWrapper dbConnectionWrapper)
                connectionId = dbConnectionWrapper.ConnectionId;
            else
                connectionId = Guid.Empty;

            connectionTracker?.MarkActive(connectionId, sql);

            // 开始 SQL 执行 span（承载 SQL 文本供慢 SQL 定位）；未接监听器时返回 null，
            // 跳过 AsyncLocal 写入（写会复制执行上下文，未接监听时是纯开销）
            var activity = DbAccessActivitySource.StartExecution(metricsProviderName, operation, sql);
            if (activity != null)
                currentExecutionActivity.Value = activity;

            return StopwatchClock.ElapsedTicks;
        }

        /// <summary>
        /// 记录一次成功执行的 SQL 指标，并结束 SQL 执行 span（状态 Ok）；耗时达到慢查询阈值时输出告警日志。
        /// </summary>
        /// <param name="operation">操作名称（如 ExecuteNonQuery）。</param>
        /// <param name="sql">SQL 命令文本（用于慢查询告警）。</param>
        /// <param name="startTimestamp">起始时间戳。</param>
        private void RecordExecutionSuccess(string operation, string sql, long startTimestamp)
        {
            var duration = StopwatchClock.GetDurationFrom(startTimestamp);

            // 停止当前 span，设置状态为 Ok
            StopExecutionActivity(ActivityStatusCode.Ok);

            // 记录成功指标
            DbAccessMetrics.RecordSqlExecution(
                metricsProviderName,
                operation,
                "ok",
                duration.TotalMilliseconds);

            // 慢查询告警：耗时达到阈值时经诊断日志输出 SQL 文本（阈值未设置时零操作）
            DbAccessDiagnostics.WarnIfSlowQuery(operation, sql, duration);
        }

        /// <summary>
        /// 记录一次失败执行的 SQL 指标，并结束 SQL 执行 span（状态 Error，附带异常详情）。
        /// </summary>
        /// <param name="operation">操作名称（如 ExecuteNonQuery）。</param>
        /// <param name="exception">执行抛出的异常。</param>
        /// <param name="startTimestamp">起始时间戳。</param>
        private void RecordExecutionError(string operation, Exception exception, long startTimestamp)
        {
            // 停止当前 span，设置状态为 Error 并记录异常详情（exception 事件与 error.type tag）
            StopExecutionActivity(ActivityStatusCode.Error, exception);

            // 记录错误指标
            DbAccessMetrics.RecordSqlExecution(
                metricsProviderName,
                operation,
                "error",
                StopwatchClock.GetDurationFrom(startTimestamp).TotalMilliseconds);
        }

        /// <summary>
        /// 结束当前异步调用链上的 SQL 执行 span：设置状态、Stop 并复位。
        /// <para>成功与异常路径均经由 RecordExecutionSuccess / RecordExecutionError 触发，保证 span 必被 Stop。</para>
        /// </summary>
        /// <param name="status">span 结束状态。</param>
        /// <param name="exception">失败时的异常；status 为 Error 且非 null 时在 span 上记录 exception 事件与 error.type tag。</param>
        private void StopExecutionActivity(ActivityStatusCode status, Exception exception = null)
        {
            var activity = currentExecutionActivity.Value;
            if (activity == null)
                return;

            currentExecutionActivity.Value = null;

            // 失败 span 在 Stop 前记录 exception 事件与 error.type tag，异常信息随 span 导出
            if (status == ActivityStatusCode.Error && exception != null)
                DbAccessActivitySource.RecordException(activity, exception);

            activity.SetStatus(status);
            activity.Stop();

            // 恢复 Current 到父级，避免已 Stop 的 span 污染调用方后续 span 的父子关系
            if (Activity.Current == activity)
                Activity.Current = activity.Parent;
        }
        #endregion

        #region 释放资源
        /// <summary>
        /// 释放资源。
        /// </summary>
        /// <param name="disposing">是否释放托管资源。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 释放所有线程（异步流）的上下文资源
                while (allContexts.TryTake(out var contextRef))
                {
                    // 弱引用目标可能已被回收（分叉的异步流早已结束），跳过即可
                    if (!contextRef.TryGetTarget(out var context))
                        continue;

                    // 安全地释放连接作用域管理器
                    var scopeManager = context.ConnectionScopeManager;
                    if (scopeManager != null)
                    {
                        try
                        {
                            scopeManager.Dispose();
                        }
                        catch (Exception ex)
                        {
                            DiagnosticLog.Error(nameof(DbAccess), "释放连接作用域管理器时发生异常：{Exception}。", ex);
                        }
                    }

                    // 安全地释放事务管理器
                    var transManager = context.TransactionManager;
                    if (transManager != null)
                    {
                        try
                        {
                            transManager.Dispose();
                        }
                        catch (Exception ex)
                        {
                            DiagnosticLog.Error(nameof(DbAccess), "释放事务管理器时发生异常：{Exception}。", ex);
                        }
                    }
                }

                dbAccessContexts.Value = null;
            }
        }
        #endregion
    }
}
