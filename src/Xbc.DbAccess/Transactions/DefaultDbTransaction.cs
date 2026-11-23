using System.Data;
using System.Data.Common;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 默认数据库事务，封装 ADO.NET 事务对象和关联连接，提供事务提交/回滚和生命周期管理。
    /// <para>
    /// 事务完成后（提交或回滚），将触发 <see cref="DbTransactionWrapper.Completed"/> 事件通知事务管理器。
    /// 释放时若事务未完成，将自动回滚事务。
    /// </para>
    /// </summary>
    internal sealed class DefaultDbTransaction : DbTransactionWrapper
    {
        private readonly DbTransaction innerTransaction;// 实际的 ADO.NET 事务对象
        private readonly DbConnection innerConnection;// 数据库连接
        private readonly object syncRoot = new object();// 同步根，用于线程安全操作
        private bool isCompleted;// 是否已完成事务
        private readonly DisposableState disposableState = new DisposableState();// 释放状态

        /// <summary>
        /// 获取关联的数据库连接（继承自 DbTransaction 类）。
        /// </summary>
        protected override DbConnection DbConnection => innerTransaction.Connection;

        /// <summary>
        /// 事务的隔离级别。
        /// </summary>
        public override IsolationLevel IsolationLevel => innerTransaction.IsolationLevel;

        /// <summary>
        /// 是否已释放资源。
        /// </summary>
        public override bool IsDisposed => disposableState.IsDisposed;

        /// <summary>
        /// 是否正在释放资源。
        /// </summary>
        public override bool IsDisposing => disposableState.IsDisposing;

        /// <summary>
        /// 获取内部事务对象（继承自 DbTransactionWrapper 类）。
        /// </summary>
        internal protected override DbTransaction InnerTransaction
        {
            get
            {
                ThrowIfDisposed();
                return innerTransaction;
            }
        }

        /// <summary>
        /// 获取内部连接对象（继承自 DbTransactionWrapper 类）。
        /// </summary>
        internal protected override DbConnection InnerConnection
        {
            get
            {
                ThrowIfDisposed();
                return innerConnection;
            }
        }

        /// <summary>
        /// 初始化默认数据库事务。
        /// </summary>
        /// <param name="innerTransaction">内部 ADO.NET 事务对象。</param>
        /// <param name="innerConnection">数据库连接。</param>
        /// <param name="manageConnection">是否在事务释放时同时释放连接。</param>
        /// <param name="transactionId">事务唯一标识，由非 async 入口生成。</param>
        public DefaultDbTransaction(DbTransaction innerTransaction,
            DbConnection innerConnection,
            bool manageConnection,
            Guid transactionId) : base(manageConnection, transactionId)
        {
            this.innerTransaction = innerTransaction ?? throw new ArgumentNullException(nameof(innerTransaction));
            this.innerConnection = innerConnection ?? throw new ArgumentNullException(nameof(innerConnection));
        }

        /// <summary>
        /// 提交事务。
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public override void Commit()
        {
            ThrowIfDisposed();

            lock (syncRoot)
            {
                if (isCompleted)
                    throw new InvalidOperationException("事务已完成，不能重复提交。");

                try
                {
                    innerTransaction.Commit();// 提交事务
                }
                finally
                {
                    MarkCompleted();// 标记事务为已完成并触发完成通知
                }
            }
        }

        /// <summary>
        /// 回滚事务。
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public override void Rollback()
        {
            ThrowIfDisposed();

            lock (syncRoot)
            {
                if (isCompleted)
                    throw new InvalidOperationException("事务已完成，不能重复回滚。");

                try
                {
                    innerTransaction.Rollback();// 回滚事务
                }
                finally
                {
                    MarkCompleted();// 标记事务为已完成并触发完成通知
                }
            }
        }

        /// <summary>
        /// 标记事务为已完成，并触发事务完成事件。
        /// </summary>
        private void MarkCompleted()
        {
            isCompleted = true;// 标记事务为已完成

            // 清除连接上的挂起事务跟踪，使连接此后可正常回收复用
            (innerConnection as DbConnectionWrapper)?.ClearPendingTransaction(innerTransaction);

            // 触发事务完成事件
            OnTransactionCompleted();
        }

        /// <summary>
        /// 释放资源。释放逻辑委托给 <see cref="DisposeTransaction"/>。
        /// </summary>
        /// <param name="disposing">是否释放托管资源。</param>
        protected override void Dispose(bool disposing)
        {
            if (!disposableState.TryDispose())
                return;

            try
            {
                if (disposing)
                {
                    lock (syncRoot)
                    {
                        DisposeTransaction();
                    }
                }
            }
            finally
            {
                disposableState.Complete();
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// 释放事务：未完成则先回滚并触发完成通知，随后释放内部事务与（按需）连接。
        /// </summary>
        private void DisposeTransaction()
        {
            try
            {
                if (!isCompleted)// 如果事务未完成，需要回滚事务
                {
                    try
                    {
                        innerTransaction.Rollback();// 回滚事务
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Error(nameof(DefaultDbTransaction), "回滚事务时发生异常：{Exception}。", ex);
                    }
                    finally
                    {
                        MarkCompleted();
                    }
                }
            }
            finally
            {
                // 回滚与完成通知抛异常也必须继续释放，否则池化连接泄漏；先释放事务再释放连接（ADO.NET 惯例）
                try
                {
                    innerTransaction.Dispose();
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Error(nameof(DefaultDbTransaction), "释放事务时发生异常：{Exception}。", ex);
                }

                // 如果需要管理连接的释放
                if (ManageConnection)
                    innerConnection.Dispose();
            }
        }
    }
}
