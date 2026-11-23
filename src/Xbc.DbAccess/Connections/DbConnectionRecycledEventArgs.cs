namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库连接回收事件参数。
    /// </summary>
    internal sealed class DbConnectionRecycledEventArgs : EventArgs
    {
        /// <summary>
        /// 连接唯一标识。
        /// </summary>
        public Guid ConnectionId { get; internal set; }

        /// <summary>
        /// 回收连接的时间。
        /// </summary>
        public DateTime RecycledAt { get; internal set; }
    }
}
