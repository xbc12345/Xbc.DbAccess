using System.Collections.Concurrent;
using System.Data.Common;
using Xbc.DbAccess.Common;
using Xbc.DbAccess.Diagnostics;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 池化数据库连接管理器，维护连接池以复用数据库连接。
    /// <para>连接使用完毕后归还到池中等待复用。以信号量控制借出名额，等待者由连接回收或销毁时归还名额唤醒，而非超时轮询。</para>
    /// <para>名额账目模型（名额跟借用者走，与连接来源无关）：
    /// ①每次借用领一张名额，无论连接取自空闲队列还是现场新建；②预创建连接是免费库存，创建时不消耗名额，首个借用者为其消耗；
    /// ③回收时连接入空闲队列并归还名额，此后该空闲连接的名额以「已归还」形式滞留证池，等待复用者重新消耗。
    /// 由此恒等式 MaxPoolSize = 空闲连接数 + 可创建余量 仅在预热后（预创建连接都经历过至少一次借还）成立；
    /// 任何时刻均有 名额数 = MaxPoolSize − 活动连接数，且存活连接总数恒不超过 MaxPoolSize（名额先扣后取 + 借用者先耗尽空闲队列再创建）。</para>
    /// <para>MinPoolSize 决定冷启动预创建的库存量并作为空闲清理下限；MaxPoolSize 经名额限制同时在借的连接数，与库存无关。</para>
    /// </summary>
    internal sealed class PoolingDbConnectionManager : DbConnectionManagerBase
    {
        private readonly DbConnectionPoolOptions poolOptions;

        // 空闲连接队列
        private readonly ConcurrentQueue<PoolingDbConnection> idleConnections
            = new ConcurrentQueue<PoolingDbConnection>();

        // 活动连接字典：ConnectionId -> PoolingDbConnection
        private readonly ConcurrentDictionary<Guid, PoolingDbConnection> activeConnections
            = new ConcurrentDictionary<Guid, PoolingDbConnection>();

        // 可用连接名额信号量：总名额 MaxPoolSize，限制同时在借的连接数；预创建的空闲连接不占名额（免费库存），
        // 其首个借用者消耗名额，回收连接归还名额
        private readonly SemaphoreSlim connectionSemaphore;
        private readonly string validationQuery;// 健康检查命令文本（SELECT 1 附加方言 FROM 子句，如 Oracle 的 FROM DUAL）
        private readonly Timer cleanupTimer;// 清理定时器
        private readonly Statistics statistics;// 统计信息
        private int poolWarmupStage;// 预创建懒预热标记：0=未开始，1=已开始/已完成（含超时后转按需创建）

        /// <summary>
        /// 初始化池化连接管理器。
        /// </summary>
        /// <param name="connectionOptions">连接选项。</param>
        /// <param name="providerFactory">数据库提供程序工厂。</param>
        /// <param name="connectionTracker">连接追踪器。</param>
        /// <param name="sqlDialect">SQL 方言，用于生成方言正确的健康检查命令；null 时退回 SELECT 1。</param>
        public PoolingDbConnectionManager(
            DbConnectionOptions connectionOptions,
            DbProviderFactory providerFactory,
            IDbConnectionTracker connectionTracker,
            ISqlDialect sqlDialect = null)
            : base(connectionOptions, providerFactory, connectionTracker)
        {
            poolOptions = connectionOptions.PoolOptions ?? new DbConnectionPoolOptions();
            poolOptions.Validate();

            // 初始化统计信息
            statistics = new Statistics(connectionOptions.EnableStatistics, poolOptions.MinPoolSize, poolOptions.MaxPoolSize);

            // SELECT 1 对 Oracle（23c 之前）不是合法语句，需附加方言的 FROM 子句
            validationQuery = "SELECT 1" + (sqlDialect?.ScalarSelectFromClause ?? string.Empty);

            // 初始可用名额全部为可创建余量（预创建连接是免费库存，不占用名额，见类注释的账目模型）
            connectionSemaphore = new SemaphoreSlim(poolOptions.MaxPoolSize, poolOptions.MaxPoolSize);

            // 启动清理定时器
            cleanupTimer = new Timer(_ => CleanupIdleConnections(),
                null,
                poolOptions.CleanupInterval,
                poolOptions.CleanupInterval);
        }

        #region 预创建连接池
        /// <summary>
        /// 预创建 <see cref="DbConnectionPoolOptions.MinPoolSize"/> 个空闲连接。
        /// <para>由 <see cref="EnsurePoolWarmedUp"/> 在首次借用请求时同步触发（不阻塞构造），最长阻塞 30 秒并记录超时日志；
        /// 单条连接创建失败仅记录日志并跳过，后续按需创建。</para>
        /// </summary>
        private void InitializePool()
        {
            var tasks = new List<Task>();
            for (int i = 0; i < poolOptions.MinPoolSize; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    PoolingDbConnection connection = null;

                    try
                    {
                        connection = await CreateActiveConnectionAsync(CancellationToken.None).ConfigureAwait(false) as PoolingDbConnection;
                        idleConnections.Enqueue(connection);
                        statistics.RecordCreated();
                        connectionTracker.StartTracking(connection.ConnectionId, ManagerId, 0);
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Error(nameof(PoolingDbConnectionManager), "初始化数据库连接池时发生异常：{Exception}。", ex);
                        statistics.RecordCreateFailed();

                        // Open 失败的连接已由基类释放（connection 为 null）；此处仅兜底入队/追踪环节抛异常的窄窗口
                        connection?.ActualDispose();
                    }
                }));
            }

            if (!Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(30)))
                DiagnosticLog.Warn(nameof(PoolingDbConnectionManager), "预创建数据库连接池超过 30 秒仍未完成，未完成的连接将转入按需创建。");
        }

        /// <summary>
        /// 按需预热连接池（预创建 MinPoolSize 个空闲连接）。
        /// <para>预创建移出构造函数以避免阻塞构造：首个借用请求触发同步预热；并发请求经 <see cref="Interlocked.Exchange"/> 去重后
        /// 跳过预热直接按需创建，不等待预热完成。预热是尽力而为的库存优化，不承载容量语义（容量由名额信号量独立保证）。</para>
        /// </summary>
        private void EnsurePoolWarmedUp()
        {
            if (poolOptions.MinPoolSize == 0)
                return;

            if (Interlocked.Exchange(ref poolWarmupStage, 1) == 1)
                return;

            InitializePool();
        }
        #endregion

        #region 获取或创建连接
        /// <summary>
        /// 异步获取或创建数据库连接，优先复用空闲连接。
        /// <para>池满载时阻塞等待连接归还或销毁唤醒；超过等待超时仍无名额则抛 <see cref="DbConnectionExhaustedException"/>。</para>
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        /// <exception cref="DbConnectionExhaustedException">等待超时仍无可用的连接名额。</exception>
        public override async Task<DbConnection> GetOrCreateAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            EnsurePoolWarmedUp();
            statistics.RecordRequest();
            var startTimestamp = StopwatchClock.ElapsedTicks;

            // 快速路径：非阻塞取得许可证（无竞争时的常用路径）
            if (connectionSemaphore.Wait(0))// 有名额立即取走，无名额立即失败，不阻塞
                return await GetIdleOrCreateAsync(startTimestamp, cancellationToken).ConfigureAwait(false);

            // 满载时阻塞等待名额，由连接回收或销毁唤醒
            statistics.RecordQueued();
            bool isAcquired;

            try
            {
                isAcquired = await connectionSemaphore.WaitAsync(poolOptions.ConnectionWaitTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                statistics.RecordCancelled();// 记录取消这一结局
                throw;
            }

            if (!isAcquired)
            {
                statistics.RecordExhausted();// 记录连接池耗尽
                throw new DbConnectionExhaustedException(poolOptions.MaxPoolSize);
            }

            // 取得许可证后与快速路径相同
            return await GetIdleOrCreateAsync(startTimestamp, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 已持有许可证时获取连接：优先复用空闲连接，队列为空时创建新连接。
        /// </summary>
        /// <param name="startTimestamp">获取连接的起始时间戳，用于记录真实耗时。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        private async Task<DbConnection> GetIdleOrCreateAsync(long startTimestamp, CancellationToken cancellationToken)
        {
            var connection = await TryGetIdleConnectionAsync(startTimestamp, cancellationToken).ConfigureAwait(false);
            if (connection != null)
                return connection;

            PoolingDbConnection newConnection;

            try
            {
                newConnection = await CreateActiveConnectionAsync(cancellationToken).ConfigureAwait(false) as PoolingDbConnection;
            }
            catch (Exception ex)
            {
                statistics.RecordCreateFailed();

                // 借用者取消导致的创建中断单列统计：连接由基类销毁，请求结局语义仍为创建失败
                if (ex is OperationCanceledException)
                    statistics.RecordDestroyedByCancellation();

                ReleaseLicenseSafely();// 创建失败，归还许可证
                throw;
            }

            // 登记与记账环节不进上方 catch：连接已计入活动集合，吞异常会经活动销毁分支叠加归还造成超发
            activeConnections.TryAdd(newConnection.ConnectionId, newConnection);
            statistics.RecordCreated();

            connectionTracker.StartTracking(newConnection.ConnectionId, ManagerId, StopwatchClock.GetDurationFrom(startTimestamp).TotalMilliseconds);
            return newConnection;
        }

        /// <summary>
        /// 创建 <see cref="PoolingDbConnection"/> 包装器实例。
        /// </summary>
        /// <returns></returns>
        protected override DbConnectionWrapper CreateDbConnectionWrapper()
        {
            var innerConnection = providerFactory.CreateConnection();
            var newConnection = new PoolingDbConnection(innerConnection)
            {
                ConnectionString = connectionOptions.ConnectionString
            };
            newConnection.Recycled += Connection_Recycled;
            newConnection.LifecycleEnded += Connection_LifecycleEnded;
            return newConnection;
        }

        /// <summary>
        /// 连接打开失败时退订事件：失败的连接不会再进入池，退订可避免残留订阅在连接延迟释放时触发回收/生命周期回调。
        /// </summary>
        /// <param name="connection">打开失败的连接包装器。</param>
        protected override void OnConnectionCreationFailed(DbConnectionWrapper connection)
        {
            if (connection is PoolingDbConnection poolingConnection)
            {
                poolingConnection.Recycled -= Connection_Recycled;
                poolingConnection.LifecycleEnded -= Connection_LifecycleEnded;
            }
        }

        /// <summary>
        /// 尝试从空闲连接队列获取连接。
        /// </summary>
        /// <param name="startTimestamp">获取连接的起始时间戳，用于记录耗时。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>获取到的连接，若队列中无可用连接则返回 null。</returns>
        private async Task<PoolingDbConnection> TryGetIdleConnectionAsync(long startTimestamp, CancellationToken cancellationToken)
        {
            while (idleConnections.TryDequeue(out var connection))
            {
                if (connection.IsDisposed)
                    continue;

                // 超过最大寿命的空闲连接直接销毁：防止 NAT/负载均衡器静默断链的僵尸连接流入借用者。
                // 空闲连接不占名额（回收时已归还 / 预创建从未消耗），销毁无需归还名额
                if (poolOptions.MaxLifetime > TimeSpan.Zero && connection.Age > poolOptions.MaxLifetime)
                {
                    connection.ActualDispose();
                    continue;
                }

                // 激活异常视同激活失败：连接不在活动集合，ActualDispose 不归还许可证，无双重归还
                bool activated = false;
                try
                {
                    activated = await connection.ActivateAsync(poolOptions.ValidationSkipInterval, poolOptions.ValidationCommandTimeout, validationQuery, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 忽略激活连接时发生的异常，视同激活失败；借用者取消单列统计，便于评估取消对健康连接的误杀
                    if (ex is OperationCanceledException)
                        statistics.RecordDestroyedByCancellation();
                }

                if (!activated)
                {
                    // 激活连接失败时，实际释放连接资源，同时触发生命周期结束事件
                    connection.ActualDispose();
                    continue;
                }

                // 登记与记账环节不兜异常：连接已计入活动集合，吞异常会按失败路径归还许可证造成超发
                activeConnections.TryAdd(connection.ConnectionId, connection);
                connectionTracker.MarkReacquired(connection.ConnectionId, StopwatchClock.GetDurationFrom(startTimestamp).TotalMilliseconds);
                statistics.RecordHitIdle();
                return connection;
            }

            return null;
        }
        #endregion

        #region 连接回收
        /// <summary>
        /// 连接回收事件处理。
        /// </summary>
        private void Connection_Recycled(object sender, DbConnectionRecycledEventArgs e)
        {
            if (sender is not PoolingDbConnection connection || connection == null)
                return;

            // 管理器已释放时直接释放连接，不入池
            if (IsDisposed || IsDisposing)
            {
                DisposeConnection(connection);
                return;
            }

            // 从活动连接中移除
            activeConnections.TryRemove(connection.ConnectionId, out _);

            // 连接已被释放（并发下 ActualDispose 可能晚于 Recycled 事件）：不入队，仅归还许可证为可创建余量。
            // 当前调用图中本分支不可达：Active 状态下的 ActualDispose 均经创建失败退订（OnConnectionCreationFailed）、
            // 管理器释放退订（DisposeConnection）或 HasPendingTransaction 同线程销毁（先于 Recycled 事件触发），
            // 不会在 Recycled 事件之后再触发归还。作为纵深防御保留，防止未来新增 ActualDispose 调用者导致许可证双重归还
            if (connection.IsDisposed)
            {
                ReleaseLicenseSafely();
                return;
            }

            // 超过最大寿命的连接不入池：归还许可证为可创建余量后销毁换新（一直被借用的长寿命连接在归还时淘汰），
            // 由下一个借用者现场重建；销毁经生命周期结束事件统计并结束追踪，无需在此重复记账。
            // 先归还后销毁：销毁异常不吞掉已归还的名额，避免许可证泄漏
            if (poolOptions.MaxLifetime > TimeSpan.Zero && connection.Age > poolOptions.MaxLifetime)
            {
                ReleaseLicenseSafely();
                connection.ActualDispose();
                return;
            }

            connectionTracker.MarkRecycled(connection.ConnectionId);
            statistics.RecordRecycled();

            // 入队时连接状态可能是 Open、Broken 或 Closed
            idleConnections.Enqueue(connection);

            // 必须先入队再归还许可证：先归还会令等待者转而创建新连接，存活连接数超出上限
            ReleaseLicenseSafely();
        }

        /// <summary>
        /// 释放单个连接：退订事件、结束追踪并实际释放资源（仅供管理器释放流程调用）。
        /// </summary>
        /// <param name="connection">要释放的连接。</param>
        private void DisposeConnection(PoolingDbConnection connection)
        {
            // 取消订阅事件，避免调用 ActualDispose 方法时触发事件
            connection.LifecycleEnded -= Connection_LifecycleEnded;

            // 事件已退订，需显式结束追踪，否则追踪记录会永久滞留在追踪器中
            connectionTracker.EndTracking(connection.ConnectionId);
            connection.ActualDispose();
        }
        #endregion

        #region 释放连接
        /// <summary>
        /// 连接生命周期结束事件处理。
        /// </summary>
        private void Connection_LifecycleEnded(object sender, DbResourceLifecycleEndedEventArgs e)
        {
            if (sender is not PoolingDbConnection connection || connection == null)
                return;

            // 正在释放时跳过：Dispose 中信号量先于状态销毁，窗口期放行会在 Release 处抛 ObjectDisposedException。
            // 释放竞态窗口内仍需结束追踪：清理回调正处理的连接经 ActualDispose 到达此处时，不再有统一收尾兜底，
            // 直接跳过会让该连接的追踪记录永久滞留（对未追踪的 id 是无害 no-op）
            if (IsDisposed || IsDisposing)
            {
                connectionTracker.EndTracking(e.ResourceId);
                return;
            }

            if (activeConnections.TryRemove(e.ResourceId, out _))
            {
                // 活动连接销毁：归还借用名额，唤醒等待者
                ReleaseLicenseSafely();
            }
            else
            {
                // 空闲连接销毁：名额已在回收/预创建时归还，无需再归还
                statistics.RecordReleased();
            }

            // 结束连接追踪
            connectionTracker.EndTracking(e.ResourceId);
        }

        /// <summary>
        /// 归还连接名额许可证；信号量随管理器并发释放已销毁时吞掉 <see cref="ObjectDisposedException"/>，
        /// 兜底入口状态检查与 Release 之间无锁的竞态窗口。
        /// </summary>
        private void ReleaseLicenseSafely()
        {
            try
            {
                connectionSemaphore.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>
        /// 释放指定的数据库连接。
        /// </summary>
        /// <param name="connection">要释放的数据库连接。</param>
        public override void Release(DbConnection connection)
        {
            if (connection == null)
                return;

            if (IsDisposed)
                return;

            // 直接调用 Dispose 方法，池化连接会自动返回其所属连接池
            if (connection is PoolingDbConnection poolingConnection)
                poolingConnection.Dispose();
            else
                DiagnosticLog.Warn(nameof(PoolingDbConnectionManager), "释放的连接不是池化连接，已忽略：{ConnectionType}。", connection.GetType().ToString());
        }
        #endregion

        #region 定时清理空闲连接
        /// <summary>
        /// 定时清理超时的空闲连接（保留 MinPoolSize 下限），单次限量处理，避免影响正常业务。
        /// </summary>
        private void CleanupIdleConnections()
        {
            if (IsDisposed || IsDisposing)
                return;

            try
            {
                const int maxCheckCount = 100;// 单次最多检查 100 个
                var now = DateTime.Now;
                var processedCount = 0;// 已处理连接数
                var breakConnectionId = Guid.Empty;// 边界标记，用于跳出循环

                while (processedCount < maxCheckCount && idleConnections.TryDequeue(out var connection))
                {
                    processedCount++;

                    // 管理器已释放时直接释放连接，不入池
                    if (IsDisposed || IsDisposing)
                    {
                        DisposeConnection(connection);
                        continue;
                    }

                    if (connection.IsDisposed)
                        continue;

                    // 当前空闲连接数 = idleConnections.Count + 1（含当前连接）
                    int currentIdleCount = idleConnections.Count + 1;

                    // 超过最大寿命的连接无条件清理：寿命为安全上限，优先于 MinPoolSize 保温下限
                    bool lifetimeExpired = poolOptions.MaxLifetime > TimeSpan.Zero && connection.Age > poolOptions.MaxLifetime;
                    bool shouldDispose = lifetimeExpired ||
                        (now - connection.LastActiveAt > poolOptions.MaxIdleTime && currentIdleCount > poolOptions.MinPoolSize);

                    if (shouldDispose)
                    {
                        connection.ActualDispose();
                    }
                    else
                    {
                        // 保留连接，重新入队
                        idleConnections.Enqueue(connection);

                        if (breakConnectionId == Guid.Empty)
                        {
                            // 第一次保留连接时记录其 ID 作为边界标记
                            breakConnectionId = connection.ConnectionId;
                        }
                        else if (connection.ConnectionId == breakConnectionId)
                        {
                            // 绕行一圈回到边界连接即终止：now 已冻结，本周期内释放判定不会翻转
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error(nameof(PoolingDbConnectionManager), "清理空闲连接时发生异常：{Exception}。", ex);
            }
        }
        #endregion

        /// <summary>
        /// 释放资源。
        /// </summary>
        /// <param name="disposing">是否释放托管资源。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 停止定时器
                var waitHandle = new ManualResetEvent(false);
                cleanupTimer.Dispose(waitHandle);
                waitHandle.WaitOne(TimeSpan.FromSeconds(3));
                waitHandle.Dispose();

                // 清理活动连接
                foreach (var connection in activeConnections.Values)
                {
                    DisposeConnection(connection);
                }

                // 使用中释放属调用方误用（.NET 惯例为未定义行为），入口守卫已拒绝新请求，此处仅做单次排空
                while (idleConnections.TryDequeue(out var idle))
                {
                    DisposeConnection(idle);
                }

                // 释放信号量
                connectionSemaphore.Dispose();
            }
        }

        #region 连接统计信息
        /// <summary>
        /// 获取当前连接池的统计信息。
        /// </summary>
        /// <returns></returns>
        public override IDbConnectionStatistics GetStatistics()
        {
            ThrowIfDisposed();

            // 活动数/空闲数快照时从集合测量
            return statistics.GetSnapshot(activeConnections.Count, idleConnections.Count);
        }

        /// <summary>
        /// 统计信息。
        /// <para>只维护单调递增的事件累计器；活动/空闲仪表由 GetSnapshot 快照时从集合实时测量传入，恒等式天然成立。</para>
        /// </summary>
        private class Statistics
        {
            // 保持每个属性的顺序不变，与 PoolingDbConnectionStatistics 对应，方便维护
            private readonly bool isEnabled;

            // 通用累计器（单调递增，只计事件）
            private long totalRequests;
            private long totalCreated;
            private long totalCreateFailed;
            private long totalReleased;

            // 连接池累计器（单调递增，只计事件）
            private readonly int minPoolSize;
            private readonly int maxPoolSize;
            private long totalHits;
            private long totalExhausted;
            private long totalQueued;
            private long totalRecycled;
            private long totalCancelled;
            private long totalDestroyedByCancellation;

            public Statistics(bool isEnabled, int minPoolSize, int maxPoolSize)
            {
                this.isEnabled = isEnabled;
                this.minPoolSize = minPoolSize;
                this.maxPoolSize = maxPoolSize;
            }

            /// <summary>
            /// 记录连接请求。
            /// </summary>
            public void RecordRequest()
            {
                if (!isEnabled)
                    return;

                Interlocked.Increment(ref totalRequests);
            }

            /// <summary>
            /// 记录连接创建成功（含预创建与按需创建）。
            /// </summary>
            public void RecordCreated()
            {
                if (!isEnabled)
                    return;

                Interlocked.Increment(ref totalCreated);
            }

            /// <summary>
            /// 记录连接创建失败。
            /// </summary>
            public void RecordCreateFailed()
            {
                if (!isEnabled)
                    return;

                Interlocked.Increment(ref totalCreateFailed);
            }

            /// <summary>
            /// 记录空闲连接销毁（定时清理、激活失败等）。
            /// </summary>
            public void RecordReleased()
            {
                if (!isEnabled)
                    return;

                Interlocked.Increment(ref totalReleased);
            }

            /// <summary>
            /// 记录命中空闲连接。
            /// </summary>
            public void RecordHitIdle()
            {
                if (!isEnabled)
                    return;

                Interlocked.Increment(ref totalHits);
            }

            /// <summary>
            /// 记录连接回收。
            /// </summary>
            public void RecordRecycled()
            {
                if (!isEnabled)
                    return;

                Interlocked.Increment(ref totalRecycled);
            }

            /// <summary>
            /// 记录连接池耗尽。
            /// </summary>
            public void RecordExhausted()
            {
                if (!isEnabled)
                    return;

                Interlocked.Increment(ref totalExhausted);
            }

            /// <summary>
            /// 记录连接排队。
            /// </summary>
            public void RecordQueued()
            {
                if (!isEnabled)
                    return;

                Interlocked.Increment(ref totalQueued);
            }

            /// <summary>
            /// 记录排队等待期间请求被取消。
            /// </summary>
            public void RecordCancelled()
            {
                if (!isEnabled)
                    return;

                Interlocked.Increment(ref totalCancelled);
            }

            /// <summary>
            /// 记录因借用者取消而销毁的连接（激活阶段取消误杀健康空闲连接 / 创建阶段取消中断新建连接）。
            /// </summary>
            public void RecordDestroyedByCancellation()
            {
                if (!isEnabled)
                    return;

                Interlocked.Increment(ref totalDestroyedByCancellation);
            }

            /// <summary>
            /// 获取统计信息快照。
            /// </summary>
            /// <param name="activeCount">活动集合实时计数（调用方测量传入）。</param>
            /// <param name="idleCount">空闲队列实时计数（调用方测量传入）。</param>
            /// <returns></returns>
            public IDbConnectionStatistics GetSnapshot(int activeCount, int idleCount)
            {
                if (!isEnabled)
                    return null;

                return new PoolingDbConnectionStatistics
                {
                    TotalRequests = Interlocked.Read(ref totalRequests),
                    TotalCreated = Interlocked.Read(ref totalCreated),
                    TotalCreateFailed = Interlocked.Read(ref totalCreateFailed),
                    TotalReleased = Interlocked.Read(ref totalReleased),
                    ActiveConnections = activeCount,
                    MinPoolSize = minPoolSize,
                    MaxPoolSize = maxPoolSize,
                    IdleConnections = idleCount,
                    TotalHits = Interlocked.Read(ref totalHits),
                    TotalRecycled = Interlocked.Read(ref totalRecycled),
                    TotalExhausted = Interlocked.Read(ref totalExhausted),
                    TotalQueued = Interlocked.Read(ref totalQueued),
                    TotalCancelled = Interlocked.Read(ref totalCancelled),
                    TotalDestroyedByCancellation = Interlocked.Read(ref totalDestroyedByCancellation),
                };
            }
        }
        #endregion
    }
}
