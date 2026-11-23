namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库访问工厂接口，用于创建 <see cref="IDbAccess"/> 实例。
    /// </summary>
    public interface IDbAccessFactory : IDisposable
    {
        /// <summary>
        /// 数据库访问提供程序工厂。
        /// </summary>
        DbAccessProviderFactory ProviderFactory { get; }

        /// <summary>
        /// 连接管理器。
        /// </summary>
        IDbConnectionManager ConnectionManager { get; }

        /// <summary>
        /// 获取数据库服务器信息：首次调用时获取并缓存，失败与已取消的任务不缓存，下次调用重新探测。
        /// <para>探测任务以 <see cref="CancellationToken.None"/> 启动：缓存任务由多个调用者共享，
        /// 绑定单个调用者的令牌会导致其取消波及其他调用者。</para>
        /// </summary>
        /// <returns>数据库服务器信息。</returns>
        Task<DbServerInfo> GetServerInfoAsync();

        /// <summary>
        /// 创建数据库访问实例。
        /// </summary>
        /// <returns></returns>
        IDbAccess Create();
    }
}
