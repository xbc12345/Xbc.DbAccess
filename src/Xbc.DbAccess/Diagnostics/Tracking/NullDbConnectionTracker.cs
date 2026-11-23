namespace Xbc.DbAccess.Diagnostics
{
    /// <summary>
    /// 空数据库连接追踪器，所有方法均为空实现，在未启用连接追踪时使用。
    /// </summary>
    internal sealed class NullDbConnectionTracker : IDbConnectionTracker
    {
        private readonly DbConnectionTrackingOptions trackingOptions;

        /// <summary>
        /// 追踪选项。
        /// </summary>
        public DbConnectionTrackingOptions TrackingOptions => trackingOptions;

        /// <summary>
        /// 初始化空连接追踪器。
        /// </summary>
        /// <param name="trackingOptions">连接追踪选项。</param>
        public NullDbConnectionTracker(DbConnectionTrackingOptions trackingOptions)
        {
            this.trackingOptions = trackingOptions;
        }

        /// <summary>
        /// 开始追踪新的连接（空实现，返回 null；调用方不使用返回值）。
        /// </summary>
        /// <param name="connectionId">连接唯一标识。</param>
        /// <param name="managerId">连接管理器唯一标识。</param>
        /// <param name="acquireDurationMs">获取连接的耗时（毫秒）。</param>
        /// <returns></returns>
        public DbConnectionTrackingInfo StartTracking(Guid connectionId, Guid managerId, double acquireDurationMs) => null;

        /// <summary>
        /// 标记连接为活动中（空实现）。
        /// </summary>
        /// <param name="connectionId">连接唯一标识。</param>
        /// <param name="sql">当前正在执行的 SQL 语句（可选）。</param>
        public void MarkActive(Guid connectionId, string sql = null) { }

        /// <summary>
        /// 标记连接为已回收（空实现）。
        /// </summary>
        /// <param name="connectionId">连接唯一标识。</param>
        public void MarkRecycled(Guid connectionId) { }

        /// <summary>
        /// 标记连接为已重新获取（空实现）。
        /// </summary>
        /// <param name="connectionId">连接唯一标识。</param>
        /// <param name="acquireDurationMs">获取连接的耗时（毫秒）。</param>
        public void MarkReacquired(Guid connectionId, double acquireDurationMs) { }

        /// <summary>
        /// 结束连接的追踪（空实现）。
        /// </summary>
        /// <param name="connectionId">连接唯一标识。</param>
        public void EndTracking(Guid connectionId) { }

        /// <summary>
        /// 获取所有追踪中的连接列表（空实现，返回空列表而非 null）。
        /// </summary>
        /// <param name="predicate">过滤条件，为 null 时返回所有追踪中的连接。</param>
        /// <returns></returns>
        public IReadOnlyList<DbConnectionTrackingInfo> FindAll(Predicate<DbConnectionTrackingInfo> predicate = null) => Array.Empty<DbConnectionTrackingInfo>();
    }
}
