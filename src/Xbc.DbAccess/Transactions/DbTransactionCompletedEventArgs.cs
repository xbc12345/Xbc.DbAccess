namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库事务完成事件参数。
    /// </summary>
    internal sealed class DbTransactionCompletedEventArgs : EventArgs
    {
        /// <summary>
        /// 事务唯一标识。
        /// </summary>
        public Guid TransactionId { get; internal set; }

        /// <summary>
        /// 完成事务的时间。
        /// </summary>
        public DateTime CompletedAt { get; internal set; }

        /// <summary>
        /// 创建事务的线程 ID。
        /// </summary>
        public int CreatedThreadId { get; internal set; }

        /// <summary>
        /// 完成事务的线程 ID。
        /// </summary>
        public int CompletedThreadId { get; internal set; }
    }
}
