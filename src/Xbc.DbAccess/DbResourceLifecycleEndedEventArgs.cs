namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库资源生命周期结束事件参数。
    /// </summary>
    internal sealed class DbResourceLifecycleEndedEventArgs : EventArgs
    {
        /// <summary>
        /// 资源唯一标识。
        /// </summary>
        public Guid ResourceId { get; internal set; }

        /// <summary>
        /// 资源类型。
        /// </summary>
        public DbResourceType ResourceType { get; internal set; }

        /// <summary>
        /// 生命周期结束时间。
        /// </summary>
        public DateTime EndTime { get; internal set; }
    }
}
