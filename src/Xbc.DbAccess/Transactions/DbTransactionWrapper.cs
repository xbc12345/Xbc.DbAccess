using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库事务包装器基类，封装内部事务和连接，提供事务唯一标识、线程安全和生命周期管理。
    /// <para>
    /// 派生类包括 <see cref="DefaultDbTransaction"/>（简单事务）和 <see cref="NestedDbTransaction"/>（嵌套事务）。
    /// </para>
    /// </summary>
    internal abstract class DbTransactionWrapper : DbTransaction
    {
        private readonly bool manageConnection;// 是否管理连接的释放

        /// <summary>
        /// 获取内部事务，仅用于内部操作。
        /// </summary>
        internal protected abstract DbTransaction InnerTransaction { get; }

        /// <summary>
        /// 获取内部连接。事务完成（提交或回滚）后该属性仍返回关联的连接实例；事务释放后访问将抛出 <see cref="ObjectDisposedException"/>。
        /// <para>事务完成后 ADO.NET 会将 <see cref="DbTransaction.Connection"/> 置为 null，此属性保留原始连接引用。</para>
        /// </summary>
        internal protected abstract DbConnection InnerConnection { get; }

        /// <summary>
        /// 事务唯一标识。
        /// </summary>
        public Guid TransactionId { get; }

        /// <summary>
        /// 创建事务的线程 ID。
        /// </summary>
        public int ThreadId { get; } = Thread.CurrentThread.ManagedThreadId;

        /// <summary>
        /// 是否在事务释放时同时释放关联连接。
        /// </summary>
        public bool ManageConnection => manageConnection;

        /// <summary>
        /// 事务完成事件。
        /// </summary>
        internal event EventHandler<DbTransactionCompletedEventArgs> Completed;

        /// <summary>
        /// 是否已释放资源。
        /// </summary>
        public abstract bool IsDisposed { get; }

        /// <summary>
        /// 是否正在释放资源（Dispose() 已开始但尚未完成）。
        /// <para>守卫检查需与 <see cref="IsDisposed"/> 同时判断，否则释放过程中的并发访问会绕过守卫。</para>
        /// </summary>
        public abstract bool IsDisposing { get; }

        /// <summary>
        /// 初始化事务包装器。
        /// </summary>
        /// <param name="manageConnection">是否在事务释放时同时释放关联连接。</param>
        /// <param name="transactionId">事务唯一标识，由非 async 入口生成。</param>
        protected DbTransactionWrapper(bool manageConnection, Guid transactionId)
        {
            this.manageConnection = manageConnection;
            TransactionId = transactionId;
        }

        /// <summary>
        /// 触发事务完成事件。
        /// </summary>
        protected virtual void OnTransactionCompleted()
        {
            Completed?.Invoke(this, new DbTransactionCompletedEventArgs()
            {
                TransactionId = TransactionId,
                CompletedAt = DateTime.Now,
                CreatedThreadId = ThreadId,
                CompletedThreadId = Thread.CurrentThread.ManagedThreadId,
            });
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
    }
}
