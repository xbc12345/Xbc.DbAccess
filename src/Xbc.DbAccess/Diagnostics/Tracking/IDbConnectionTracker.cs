namespace Xbc.DbAccess.Diagnostics
{
    /// <summary>
    /// 数据库连接追踪器接口，记录连接从创建到释放的状态转换，用于连接泄漏检测与使用情况分析。
    /// </summary>
    public interface IDbConnectionTracker
    {
        /// <summary>
        /// 追踪选项，控制追踪行为（如是否记录堆栈信息）。
        /// </summary>
        DbConnectionTrackingOptions TrackingOptions { get; }

        /// <summary>
        /// 开始追踪新的连接，在连接首次创建时调用。
        /// </summary>
        /// <param name="connectionId">连接唯一标识。</param>
        /// <param name="managerId">连接管理器唯一标识。</param>
        /// <param name="acquireDurationMs">获取连接的耗时（毫秒），即从请求连接到获得连接的等待时间。</param>
        /// <returns></returns>
        DbConnectionTrackingInfo StartTracking(Guid connectionId, Guid managerId, double acquireDurationMs);

        /// <summary>
        /// 标记连接为活动中，在连接执行 SQL 时调用。
        /// </summary>
        /// <param name="connectionId">连接唯一标识。</param>
        /// <param name="sql">当前正在执行的 SQL 语句（可选）。</param>
        void MarkActive(Guid connectionId, string sql = null);

        /// <summary>
        /// 标记连接为已回收，在连接回收到连接池时调用。
        /// </summary>
        /// <param name="connectionId">连接唯一标识。</param>
        void MarkRecycled(Guid connectionId);

        /// <summary>
        /// 标记连接为已重新获取，在连接从连接池取出复用时调用。
        /// </summary>
        /// <param name="connectionId">连接唯一标识。</param>
        /// <param name="acquireDurationMs">获取连接的耗时（毫秒），即从请求连接到获得连接的等待时间。</param>
        void MarkReacquired(Guid connectionId, double acquireDurationMs);

        /// <summary>
        /// 结束连接的追踪，在连接真正释放时调用。
        /// </summary>
        /// <param name="connectionId">连接唯一标识。</param>
        void EndTracking(Guid connectionId);

        /// <summary>
        /// 获取所有追踪中的连接列表（获取、活动与回收状态）；predicate 在内部直接过滤，避免全量复制后再过滤。
        /// </summary>
        /// <param name="predicate">过滤条件，为 null 时返回所有追踪中的连接。</param>
        /// <returns></returns>
        IReadOnlyList<DbConnectionTrackingInfo> FindAll(Predicate<DbConnectionTrackingInfo> predicate = null);
    }
}
