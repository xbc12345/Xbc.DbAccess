using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库连接包装器基类，封装内部数据库连接并提供生命周期管理。
    /// <para>
    /// 派生类包括 <see cref="DefaultDbConnection"/>（简单连接）和 <see cref="PoolingDbConnection"/>（连接池连接）。
    /// </para>
    /// </summary>
    internal abstract class DbConnectionWrapper : DbConnection
    {
        /// <summary>
        /// 内部数据库连接。
        /// </summary>
        protected readonly DbConnection innerConnection;

        /// <summary>
        /// 连接唯一标识。
        /// </summary>
        public Guid ConnectionId { get; } = Guid.NewGuid();

        /// <summary>
        /// 内部的原生提供程序连接（如 SqlConnection/NpgsqlConnection）。
        /// <para>供需要驱动专属 API 的调用方（如原生 bulk 插入）解包获取真实连接；常规命令执行走本包装器即可。</para>
        /// <para>禁止在此连接上直接调用 <c>BeginTransaction</c> 开启事务：挂起事务防护（<see cref="HasPendingTransaction"/>）
        /// 依赖包装器记录事务，绕过记录会导致带活事务的连接被回池复用；需要事务时一律经框架事务包装器获取内部事务。</para>
        /// </summary>
        internal DbConnection UnderlyingConnection => innerConnection;

        /// <summary>
        /// 连接生命周期结束事件，当连接资源被释放时触发。
        /// </summary>
        internal event EventHandler<DbResourceLifecycleEndedEventArgs> LifecycleEnded;

        // 当前挂起的根事务（未完成），用于池化连接的回收/复用防护
        private volatile DbTransaction pendingTransaction;

        /// <summary>
        /// 连接上是否挂有未完成的事务。
        /// <para>挂事务的连接回池重借后，新借用者的命令会隐式加入旧事务或因缺少事务关联而失败，连接池据此拒绝回收与复用此类连接。</para>
        /// </summary>
        internal bool HasPendingTransaction => pendingTransaction != null;

        /// <summary>
        /// 是否已释放资源。
        /// </summary>
        public abstract bool IsDisposed { get; }

        /// <summary>
        /// 是否正在释放资源（Dispose() 已开始但尚未完成）。
        /// <para>守卫检查需与 <see cref="IsDisposed"/> 同时判断；无释放窗口期的实现（先进入终态再清理）重写为恒 false。</para>
        /// </summary>
        public abstract bool IsDisposing { get; }

        /// <summary>
        /// 连接字符串。
        /// </summary>
        public override string ConnectionString
        {
            get { return innerConnection.ConnectionString; }
            set { innerConnection.ConnectionString = value; }
        }

        /// <summary>
        /// 连接状态变化事件。
        /// </summary>
        public override event StateChangeEventHandler StateChange
        {
            add { innerConnection.StateChange += value; }
            remove { innerConnection.StateChange -= value; }
        }

        /// <summary>
        /// 数据库名称。
        /// </summary>
        public override string Database => innerConnection.Database;

        /// <summary>
        /// 数据源名称。
        /// </summary>
        public override string DataSource => innerConnection.DataSource;

        /// <summary>
        /// 服务器版本。
        /// </summary>
        public override string ServerVersion => innerConnection.ServerVersion;

        /// <summary>
        /// 连接状态。
        /// </summary>
        public override ConnectionState State => innerConnection.State;

        /// <summary>
        /// 连接超时时间。
        /// </summary>
        public override int ConnectionTimeout => innerConnection.ConnectionTimeout;

        /// <summary>
        /// 初始化连接包装器。
        /// </summary>
        /// <param name="innerConnection">内部数据库连接。</param>
        public DbConnectionWrapper(DbConnection innerConnection)
        {
            this.innerConnection = innerConnection ?? throw new ArgumentNullException(nameof(innerConnection));
        }

        /// <summary>
        /// 打开连接。
        /// </summary>
        public override void Open()
        {
            ThrowIfDisposed();
            innerConnection.Open();
        }

        /// <summary>
        /// 异步打开连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public async override Task OpenAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await innerConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 关闭连接。已释放的连接视为已关闭，直接返回（ADO.NET 惯例：Close 可安全重复调用）。
        /// </summary>
        public override void Close()
        {
            if (IsDisposed || IsDisposing)
                return;

            innerConnection.Close();
        }

        /// <summary>
        /// 若已释放或正在释放则抛出 <see cref="ObjectDisposedException"/>。
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        protected virtual void ThrowIfDisposed()
        {
            if (IsDisposed || IsDisposing)
                throw new ObjectDisposedException(GetType().ToString());
        }

        /// <summary>
        /// 关闭并释放内部连接，不触发生命周期结束事件。
        /// </summary>
        internal void DisposeInternal()
        {
            // 关闭并释放内部连接
            if (innerConnection.State != ConnectionState.Closed)
                innerConnection.Close();

            innerConnection.Dispose();
            // 注意：base.Dispose(bool) 会虚调用 Close()——池化连接的 Close 被重写为 ReturnToPool。
            // 此处安全依赖两点：①ActualDispose 已先 Exchange 进 Disposed 终态，ReturnToPool 的 CAS 全部落空；
            // ②Open 失败路径中基类保证 OnConnectionCreationFailed（退订回收/生命周期事件）先于本方法执行。
            // 改动状态机转换或该调用顺序时需重估此链条
            base.Dispose(true);
        }

        /// <summary>
        /// 触发连接生命周期结束事件。
        /// </summary>
        protected virtual void OnLifecycleEnded()
        {
            LifecycleEnded?.Invoke(this, new DbResourceLifecycleEndedEventArgs()
            {
                ResourceId = ConnectionId,
                ResourceType = DbResourceType.Connection,
                EndTime = DateTime.Now,
            });
        }

        /// <summary>
        /// 获取数据库架构。
        /// </summary>
        /// <returns></returns>
        public override DataTable GetSchema()
        {
            ThrowIfDisposed();
            return innerConnection.GetSchema();
        }

        /// <summary>
        /// 获取数据库架构。
        /// </summary>
        /// <param name="collectionName">架构集合名称。</param>
        /// <returns></returns>
        public override DataTable GetSchema(string collectionName)
        {
            ThrowIfDisposed();
            return innerConnection.GetSchema(collectionName);
        }

        /// <summary>
        /// 获取数据库架构。
        /// </summary>
        /// <param name="collectionName">架构集合名称。</param>
        /// <param name="restrictionValues">限制值。</param>
        /// <returns></returns>
        public override DataTable GetSchema(string collectionName, string[] restrictionValues)
        {
            ThrowIfDisposed();
            return innerConnection.GetSchema(collectionName, restrictionValues);
        }

        /// <summary>
        /// 创建数据库命令。
        /// </summary>
        /// <returns></returns>
        protected override DbCommand CreateDbCommand()
        {
            ThrowIfDisposed();
            return innerConnection.CreateCommand();
        }

        /// <summary>
        /// 开始数据库事务。
        /// </summary>
        /// <param name="isolationLevel">事务隔离级别。</param>
        /// <returns></returns>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            ThrowIfDisposed();
            var transaction = innerConnection.BeginTransaction(isolationLevel);
            pendingTransaction = transaction;// 记录挂起事务，用于回收/复用防护
            return transaction;
        }

        /// <summary>
        /// 清除挂起事务跟踪。仅当清除的正是当前跟踪的事务时生效，乱序完成不会误清后续新事务。
        /// </summary>
        /// <param name="transaction">已完成的事务。</param>
        internal void ClearPendingTransaction(DbTransaction transaction)
        {
            if (ReferenceEquals(pendingTransaction, transaction))
                pendingTransaction = null;
        }

        /// <summary>
        /// 切换数据库。
        /// </summary>
        /// <param name="databaseName">数据库名称。</param>
        public override void ChangeDatabase(string databaseName)
        {
            ThrowIfDisposed();
            innerConnection.ChangeDatabase(databaseName);
        }
    }
}
