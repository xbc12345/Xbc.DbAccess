using System.Data.Common;
using Xbc.DbAccess.Diagnostics;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库连接管理器基类，提供连接创建和追踪的通用逻辑。
    /// </summary>
    internal abstract class DbConnectionManagerBase : DisposableBase, IDbConnectionManager
    {
        /// <summary>
        /// 连接选项。
        /// </summary>
        protected readonly DbConnectionOptions connectionOptions;

        /// <summary>
        /// 数据库提供程序工厂。
        /// </summary>
        protected readonly DbProviderFactory providerFactory;

        /// <summary>
        /// 连接追踪器。
        /// </summary>
        protected readonly IDbConnectionTracker connectionTracker;

        /// <summary>
        /// 连接管理器唯一标识。
        /// </summary>
        public Guid ManagerId { get; } = Guid.NewGuid();

        /// <summary>
        /// 连接字符串。
        /// </summary>
        public string ConnectionString => connectionOptions.ConnectionString;

        /// <summary>
        /// 连接追踪器。
        /// </summary>
        public IDbConnectionTracker ConnectionTracker => connectionTracker;

        /// <summary>
        /// 初始化连接管理器基类。
        /// </summary>
        /// <param name="connectionOptions">连接选项。</param>
        /// <param name="providerFactory">数据库提供程序工厂。</param>
        /// <param name="connectionTracker">连接追踪器。</param>
        public DbConnectionManagerBase(DbConnectionOptions connectionOptions,
            DbProviderFactory providerFactory,
            IDbConnectionTracker connectionTracker)
        {
            this.connectionOptions = connectionOptions ?? throw new ArgumentNullException(nameof(connectionOptions));
            this.providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
            this.connectionTracker = connectionTracker ?? throw new ArgumentNullException(nameof(connectionTracker));
        }

        /// <summary>
        /// 异步获取或创建数据库连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public abstract Task<DbConnection> GetOrCreateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步创建并打开一个连接（不开始追踪，由子类在获取耗时后调用 <see cref="IDbConnectionTracker.StartTracking"/>）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        protected async Task<DbConnectionWrapper> CreateActiveConnectionAsync(CancellationToken cancellationToken)
        {
            var connection = CreateDbConnectionWrapper();
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                OnConnectionCreationFailed(connection);
                connection.DisposeInternal();// 释放内部连接资源，不触发生命周期结束事件
                throw;
            }

            return connection;
        }

        /// <summary>
        /// 连接创建后打开失败的回调，供子类清理事件订阅等附加资源（在连接释放前调用）。
        /// </summary>
        /// <param name="connection">打开失败的连接包装器。</param>
        protected virtual void OnConnectionCreationFailed(DbConnectionWrapper connection)
        {
        }

        /// <summary>
        /// 创建数据库连接包装器实例。
        /// </summary>
        /// <returns></returns>
        protected abstract DbConnectionWrapper CreateDbConnectionWrapper();

        /// <summary>
        /// 释放指定的数据库连接。
        /// </summary>
        /// <param name="connection">要释放的数据库连接。</param>
        public abstract void Release(DbConnection connection);

        /// <summary>
        /// 获取连接管理器的统计信息。
        /// </summary>
        /// <returns></returns>
        public abstract IDbConnectionStatistics GetStatistics();
    }
}
