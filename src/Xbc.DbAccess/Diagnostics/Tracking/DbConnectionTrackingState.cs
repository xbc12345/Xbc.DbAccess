namespace Xbc.DbAccess.Diagnostics
{
    /// <summary>
    /// 数据库连接追踪状态，表示连接在生命周期中的不同阶段。
    /// <para>
    /// 状态转换顺序：Created → Acquired → Active → Recycled → Acquired（复用）→ ... → Released
    /// </para>
    /// </summary>
    public enum DbConnectionTrackingState
    {
        /// <summary>
        /// 已创建（首次创建连接）。
        /// </summary>
        Created,

        /// <summary>
        /// 已获取（首次创建即获取，或从连接池中复用连接）。
        /// </summary>
        Acquired,

        /// <summary>
        /// 活动中（正在执行数据库操作）。
        /// </summary>
        Active,

        /// <summary>
        /// 已回收（连接被回收到连接池中）。
        /// </summary>
        Recycled,

        /// <summary>
        /// 已释放（连接已真实释放）。
        /// </summary>
        Released,
    }
}
