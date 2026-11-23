using System.Data;
using System.Data.Common;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 嵌套数据库事务，子事务通过保存点（Savepoint）实现嵌套。
    /// <para>
    /// 根事务直接包装 ADO.NET 事务对象；子事务复用父事务的连接和事务对象，
    /// 通过创建/回滚/释放保存点来模拟独立的事务行为：提交/回滚语义详见 <see cref="Commit"/> 与 <see cref="Rollback"/>，
    /// 释放时未完成事务的自动回滚详见 <see cref="DisposeRoot"/> 与 <see cref="DisposeChild"/>。
    /// </para>
    /// </summary>
    internal sealed class NestedDbTransaction : DbTransactionWrapper
    {
        private readonly DbTransaction innerTransaction;// 实际的 ADO.NET 事务对象
        private readonly DbConnection innerConnection;// 数据库连接
        private readonly NestedDbTransaction parentTransaction;// 父级事务
        private readonly ISavepointSqlDialect savepointSqlDialect;// 保存点 SQL 方言，用于生成不同数据库的保存点 SQL 语句
        private readonly string savepointName;// 保存点名称
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
        /// 是否为根事务（无父事务）。
        /// </summary>
        public bool IsRootTransaction => parentTransaction == null;

        /// <summary>
        /// 初始化根事务。
        /// </summary>
        /// <param name="innerTransaction">内部 ADO.NET 事务对象。</param>
        /// <param name="innerConnection">数据库连接。</param>
        /// <param name="manageConnection">是否在事务释放时同时释放连接。</param>
        /// <param name="transactionId">事务唯一标识，由非 async 入口生成。</param>
        public NestedDbTransaction(DbTransaction innerTransaction,
            DbConnection innerConnection,
            bool manageConnection,
            Guid transactionId) : base(manageConnection, transactionId)
        {
            this.innerTransaction = innerTransaction ?? throw new ArgumentNullException(nameof(innerTransaction));
            this.innerConnection = innerConnection ?? throw new ArgumentNullException(nameof(innerConnection));
        }

        /// <summary>
        /// 初始化子事务，子事务不管理连接的释放。
        /// </summary>
        /// <param name="parentTransaction">父级事务。</param>
        /// <param name="savepointName">保存点名称。</param>
        /// <param name="savepointSqlDialect">保存点 SQL 方言。</param>
        /// <param name="transactionId">事务唯一标识，由非 async 入口生成。</param>
        public NestedDbTransaction(NestedDbTransaction parentTransaction,
            string savepointName,
            ISavepointSqlDialect savepointSqlDialect,
            Guid transactionId) : base(false, transactionId)
        {
            Guard.ThrowIfNull(parentTransaction, nameof(parentTransaction));
            Guard.ThrowIfNullOrWhiteSpace(savepointName, nameof(savepointName));
            Guard.ThrowIfNull(savepointSqlDialect, nameof(savepointSqlDialect));

            innerTransaction = parentTransaction.InnerTransaction;
            innerConnection = parentTransaction.InnerConnection;
            this.parentTransaction = parentTransaction;
            this.savepointName = savepointName;
            this.savepointSqlDialect = savepointSqlDialect;
        }

        /// <summary>
        /// 提交事务。根事务执行真正的事务提交，子事务释放对应的保存点。
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
                    if (IsRootTransaction)
                        innerTransaction.Commit();// 如果是根事务才需要提交事务
                    else
                        ReleaseSavepoint();// 如果是子事务，需要释放保存点（保存点是在开启子事务时创建的）
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

            // 根事务完成时清除挂起事务跟踪（子事务只是保存点），使连接此后可正常回收复用
            if (IsRootTransaction)
                (innerConnection as DbConnectionWrapper)?.ClearPendingTransaction(innerTransaction);

            OnTransactionCompleted();// 触发事务完成事件
        }

        /// <summary>
        /// 释放保存点。部分数据库不支持手动释放，会在事务提交时自动释放。
        /// </summary>
        private void ReleaseSavepoint()
        {
            if (savepointSqlDialect.IsSavepointSupported && savepointSqlDialect.IsReleaseSavepointSupported)
            {
                using (var releaseCommand = innerConnection.CreateCommand())
                {
                    releaseCommand.CommandText = savepointSqlDialect.ReleaseSavepoint(savepointName);
                    releaseCommand.Transaction = innerTransaction;
                    releaseCommand.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 回滚到当前事务的保存点。
        /// </summary>
        private void RollbackToSavepoint()
        {
            if (savepointSqlDialect.IsSavepointSupported)
            {
                using (var rollbackCommand = innerConnection.CreateCommand())
                {
                    rollbackCommand.CommandText = savepointSqlDialect.RollbackToSavepoint(savepointName);
                    rollbackCommand.Transaction = innerTransaction;
                    rollbackCommand.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 回滚事务。根事务执行真正的事务回滚，子事务回滚到对应的保存点。
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
                    if (IsRootTransaction)
                        innerTransaction.Rollback();// 如果是根事务才需要回滚事务
                    else
                        RollbackToSavepoint();// 对于嵌套事务（子事务），需要回滚到指定的保存点
                }
                finally
                {
                    MarkCompleted();// 标记事务为已完成并触发完成通知
                }
            }
        }

        /// <summary>
        /// 释放资源。根事务与子事务的释放逻辑不同，分别委托给 <see cref="DisposeRoot"/> 与 <see cref="DisposeChild"/>。
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
                        if (IsRootTransaction)
                            DisposeRoot();
                        else
                            DisposeChild();
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
        /// 释放根事务：未完成则先回滚并触发完成通知，随后释放内部事务与（按需）连接。
        /// </summary>
        private void DisposeRoot()
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
                        DiagnosticLog.Error(nameof(NestedDbTransaction), "回滚事务时发生异常：{Exception}。", ex);
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
                    DiagnosticLog.Error(nameof(NestedDbTransaction), "释放事务时发生异常：{Exception}。", ex);
                }

                // 如果需要管理连接的释放
                if (ManageConnection)
                    innerConnection.Dispose();
            }
        }

        /// <summary>
        /// 释放子事务：未完成则回滚到指定的保存点并触发完成通知。
        /// <para>子事务不持有事务与连接资源，无需释放。</para>
        /// </summary>
        private void DisposeChild()
        {
            if (!isCompleted)// 如果事务未完成，需要回滚到指定的保存点
            {
                try
                {
                    RollbackToSavepoint();// 回滚到指定的保存点
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Error(nameof(NestedDbTransaction), "回滚到保存点 '{SavepointName}' 时发生异常：{Exception}。", savepointName, ex);
                }
                finally
                {
                    MarkCompleted();
                }
            }
        }
    }
}
