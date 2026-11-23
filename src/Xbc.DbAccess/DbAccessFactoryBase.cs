using System.Text.RegularExpressions;
using Xbc.DbAccess.Common;
using Xbc.DbAccess.Diagnostics;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库访问工厂基类，提供 <see cref="IDbAccess"/> 实例的创建和连接管理。
    /// </summary>
    public abstract class DbAccessFactoryBase : DisposableBase, IDbAccessFactory
    {
        // 服务器信息缓存任务及其锁；失败（IsFaulted）与已取消（IsCanceled）的任务不缓存：
        // 暂时性故障不应被固化为永久失败，下次调用均重新探测
        private readonly object serverInfoLock = new object();
        private Task<DbServerInfo> cachedServerInfoTask;

        /// <summary>
        /// 连接选项。
        /// </summary>
        protected readonly DbConnectionOptions connectionOptions;

        /// <summary>
        /// 连接管理器，生命周期跟随工厂实例。
        /// </summary>
        protected readonly IDbConnectionManager connectionManager;

        /// <summary>
        /// 连接追踪器。
        /// </summary>
        protected readonly IDbConnectionTracker connectionTracker;

        /// <summary>
        /// 数据库访问提供程序工厂。
        /// </summary>
        public abstract DbAccessProviderFactory ProviderFactory { get; }

        /// <summary>
        /// 连接管理器。
        /// </summary>
        public IDbConnectionManager ConnectionManager => connectionManager;

        /// <summary>
        /// 初始化数据库访问工厂基类。
        /// </summary>
        /// <param name="connectionOptions">连接选项。</param>
        protected DbAccessFactoryBase(DbConnectionOptions connectionOptions)
        {
            this.connectionOptions = connectionOptions ?? throw new ArgumentNullException(nameof(connectionOptions));

            // 创建连接追踪器
            connectionTracker = CreateConnectionTracker(connectionOptions.ConnectionTrackingOptions);

            // 创建连接管理器
            connectionManager = CreateConnectionManager(connectionOptions);

            // 注册连接管理器到指标采集器（池账本与泄漏指标需要统计和连接追踪数据）
            if (connectionOptions.EnableStatistics && connectionOptions.ConnectionTrackingOptions.Enabled)
                DbAccessMetrics.RegisterManager(connectionManager,
                    DbAccessMetrics.GetProviderName(ProviderFactory.DbProviderFactory),
                    connectionOptions.PossibleLeakHoldTimeThreshold,
                    connectionOptions.CriticalLeakHoldTimeThreshold);
        }

        /// <summary>
        /// 创建连接管理器，子类可重写以自定义实现。
        /// </summary>
        /// <param name="options">连接选项。</param>
        /// <returns></returns>
        protected virtual IDbConnectionManager CreateConnectionManager(DbConnectionOptions options)
        {
            if (options.PoolOptions.Enabled)
                return new PoolingDbConnectionManager(options, ProviderFactory.DbProviderFactory, connectionTracker, ProviderFactory.SqlDialect);
            else
                return new DefaultDbConnectionManager(options, ProviderFactory.DbProviderFactory, connectionTracker);
        }

        /// <summary>
        /// 创建连接追踪器，子类可重写以自定义实现。
        /// </summary>
        /// <param name="trackingOptions">连接追踪选项。</param>
        /// <returns></returns>
        protected virtual IDbConnectionTracker CreateConnectionTracker(DbConnectionTrackingOptions trackingOptions)
        {
            if (trackingOptions != null && trackingOptions.Enabled)
                return new DefaultDbConnectionTracker(trackingOptions);
            else
                return new NullDbConnectionTracker(trackingOptions);
        }

        /// <summary>
        /// 获取数据库服务器信息：首次调用时获取并缓存，失败与已取消的任务不缓存，下次调用重新探测。
        /// <para>探测任务以 <see cref="CancellationToken.None"/> 启动：缓存任务由多个调用者共享，
        /// 绑定单个调用者的令牌会导致其取消波及其他调用者。</para>
        /// </summary>
        /// <returns>数据库服务器信息。</returns>
        public Task<DbServerInfo> GetServerInfoAsync()
        {
            lock (serverInfoLock)
            {
                // 探测任务以 CancellationToken.None 启动：缓存任务由多个调用者共享，
                // 绑定首个调用者的令牌会导致其取消波及其他调用者；
                // 失败与已取消的任务均不缓存（暂时性故障不应固化为永久失败），下次调用时重新探测
                if (cachedServerInfoTask == null || cachedServerInfoTask.IsFaulted || cachedServerInfoTask.IsCanceled)
                    cachedServerInfoTask = GetDbServerInfoAsync(CancellationToken.None);

                return cachedServerInfoTask;
            }
        }

        /// <summary>
        /// 异步获取数据库服务器信息，子类可重写以自定义实现。
        /// </summary>
        /// <returns></returns>
        protected virtual async Task<DbServerInfo> GetDbServerInfoAsync(CancellationToken cancellationToken = default)
        {
            var connection = await connectionManager.GetOrCreateAsync(cancellationToken).ConfigureAwait(false);
            var serverInfo = new DbServerInfo();

            try
            {
                serverInfo.ServerVersion = connection.ServerVersion;
                serverInfo.DataSource = connection.DataSource;
                serverInfo.Database = connection.Database;

                // 解析主版本号、次要版本号和修订版本号
                if (TryParseServerVersion(serverInfo.ServerVersion, out int major, out int minor, out int revision))
                {
                    serverInfo.MajorVersion = major;
                    serverInfo.MinorVersion = minor;
                    serverInfo.RevisionVersion = revision;
                }
            }
            catch (Exception ex)
            {
                // 读取失败时返回默认值（MajorVersion==0），记录日志避免无声改变方言按版本判定的路径
                DiagnosticLog.Error(nameof(DbAccessFactoryBase), "读取数据库服务器信息时发生异常：{Exception}。", ex);
            }
            finally
            {
                connectionManager.Release(connection);
            }

            return serverInfo;
        }

        /// <summary>
        /// 尝试从版本字符串解析版本号，格式为 major.minor.revision 或 major.minor。
        /// </summary>
        /// <param name="versionString">版本字符串。</param>
        /// <param name="major">主版本号。</param>
        /// <param name="minor">次版本号。</param>
        /// <param name="revision">修订版本号。</param>
        /// <returns></returns>
        protected virtual bool TryParseServerVersion(string versionString, out int major, out int minor, out int revision)
        {
            major = 0;
            minor = 0;
            revision = 0;

            if (string.IsNullOrEmpty(versionString))
                return false;

            try
            {
                // 使用正则表达式提取版本号，格式为 major.minor.revision
                // 如果不是这个格式的版本号字符串，需要由子类自己实现解析
                var versionRegex = new Regex(@"(\d+)\.(\d+)(?:\.(\d+))?");
                Match match = versionRegex.Match(versionString);

                if (match.Success)
                {
                    major = int.Parse(match.Groups[1].Value);
                    minor = int.Parse(match.Groups[2].Value);

                    if (match.Groups[3].Success && !string.IsNullOrEmpty(match.Groups[3].Value))
                        revision = int.Parse(match.Groups[3].Value);

                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 创建数据库访问实例（访问实现无方言分支，方言差异由 <see cref="ProviderFactory"/> 承载）。
        /// </summary>
        /// <returns></returns>
        public IDbAccess Create()
        {
            ThrowIfDisposed();
            return new DbAccess(this, connectionTracker);
        }

        /// <summary>
        /// 释放资源，注销指标采集注册并释放连接管理器。
        /// </summary>
        /// <param name="disposing">是否释放托管资源。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 注销连接管理器的指标采集注册
                if (connectionOptions.EnableStatistics && connectionOptions.ConnectionTrackingOptions.Enabled)
                    DbAccessMetrics.UnregisterManager(connectionManager.ManagerId);

                // 释放连接管理器
                (connectionManager as IDisposable)?.Dispose();
            }
        }
    }
}
