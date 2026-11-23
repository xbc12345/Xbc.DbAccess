using System.Data;
using System.Data.Common;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 默认事务管理器，管理 <see cref="DefaultDbTransaction"/> 的生命周期。
    /// <para>
    /// 该管理器不支持嵌套事务，同一时刻只允许一个活动事务。
    /// 开启新事务时若已存在活动事务，将抛出 <see cref="InvalidOperationException"/>。
    /// </para>
    /// </summary>
    internal sealed class DefaultDbTransactionManager : DisposableBase, IDbTransactionManager
    {
        private readonly IDbConnectionManager connectionManager;
        private DefaultDbTransaction currentTransaction;// 当前事务
        private readonly AsyncLock syncLock = new AsyncLock();// 同步锁，用于线程安全操作（支持同步与异步获取）

        /// <summary>
        /// 获取当前活动事务，若无活动事务则返回 null。
        /// </summary>
        public DbTransaction CurrentTransaction
        {
            get
            {
                ThrowIfDisposed();
                using (syncLock.Lock())
                {
                    return currentTransaction;
                }
            }
        }

        /// <summary>
        /// 获取当前事务关联的数据库连接，若无活动事务则返回 null。
        /// </summary>
        public DbConnection CurrentConnection
        {
            get
            {
                ThrowIfDisposed();
                using (syncLock.Lock())
                {
                    return currentTransaction?.InnerConnection;
                }
            }
        }

        /// <summary>
        /// 是否存在活动的连接。
        /// </summary>
        public bool HasActiveConnection
        {
            get
            {
                ThrowIfDisposed();
                using (syncLock.Lock())
                {
                    return currentTransaction?.InnerConnection != null;
                }
            }
        }

        /// <summary>
        /// 初始化默认事务管理器。
        /// </summary>
        /// <param name="connectionManager">数据库连接管理器。</param>
        public DefaultDbTransactionManager(IDbConnectionManager connectionManager)
        {
            this.connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        }

        /// <summary>
        /// 异步开启事务，事务释放时自动释放关联的连接。
        /// </summary>
        /// <param name="transactionId">事务唯一标识，由调用者在非 async 入口生成。</param>
        /// <param name="isolationLevel">事务隔离级别。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<DbTransaction> BeginTransactionAsync(Guid transactionId, IsolationLevel isolationLevel = IsolationLevel.Unspecified, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            using (await syncLock.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                if (currentTransaction != null)
                    throw new InvalidOperationException("当前已存在活动的事务，无法开始新的事务。");

                var innerConnection = await connectionManager.GetOrCreateAsync(cancellationToken).ConfigureAwait(false);
                DefaultDbTransaction newTransaction;

                try
                {
                    var innerTransaction = innerConnection.BeginTransaction(isolationLevel);

                    // 创建事务，包装内部事务和连接，该事务负责管理连接的释放
                    newTransaction = new DefaultDbTransaction(innerTransaction, innerConnection, true, transactionId);
                    newTransaction.Completed += Transaction_Completed;
                }
                catch
                {
                    // 如果开启事务时发生异常，需要释放连接以避免连接泄漏
                    connectionManager.Release(innerConnection);
                    throw;
                }

                currentTransaction = newTransaction;
                return newTransaction;
            }
        }

        /// <summary>
        /// 异步使用指定的连接开启事务，事务完成后不释放该连接。
        /// </summary>
        /// <param name="connection">数据库连接。</param>
        /// <param name="transactionId">事务唯一标识，由调用者在非 async 入口生成。</param>
        /// <param name="isolationLevel">事务隔离级别。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<DbTransaction> BeginTransactionAsync(DbConnection connection, Guid transactionId, IsolationLevel isolationLevel = IsolationLevel.Unspecified, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(connection, nameof(connection));
            ThrowIfDisposed();

            using (await syncLock.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                if (currentTransaction != null)
                    throw new InvalidOperationException("当前已存在活动的事务，无法开始新的事务。");

                var innerTransaction = connection.BeginTransaction(isolationLevel);

                // 创建事务，包装内部事务和连接，该事务不负责管理连接的释放
                var newTransaction = new DefaultDbTransaction(innerTransaction, connection, false, transactionId);
                newTransaction.Completed += Transaction_Completed;

                currentTransaction = newTransaction;
                return newTransaction;
            }
        }

        /// <summary>
        /// 事务完成事件处理，清除当前事务引用。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="e">事件参数。</param>
        /// <exception cref="InvalidOperationException"></exception>
        private void Transaction_Completed(object sender, DbTransactionCompletedEventArgs e)
        {
            if (sender is not DefaultDbTransaction defaultTransaction)
                return;

            // 正在释放时跳过：Dispose 中异步锁先于状态销毁，窗口期放行会在 Lock() 处抛 ObjectDisposedException
            if (IsDisposed || IsDisposing)
                return;

            using (syncLock.Lock())
            {
                if (currentTransaction == null)
                    return;

                if (currentTransaction.TransactionId != defaultTransaction.TransactionId)
                    throw new InvalidOperationException("当前事务与已完成的事务不是同一事务。");

                // 清除当前事务引用
                currentTransaction = null;
            }
        }

        /// <summary>
        /// 释放资源。
        /// </summary>
        /// <param name="disposing">是否释放托管资源。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DefaultDbTransaction transactionToDispose = null;

                using (syncLock.Lock())
                {
                    if (currentTransaction != null)
                    {
                        // 取消订阅事件，避免触发事件时造成死锁
                        currentTransaction.Completed -= Transaction_Completed;

                        transactionToDispose = currentTransaction;
                        currentTransaction = null;
                    }
                }

                // 在锁外释放事务，避免与 Commit/Rollback 中的锁顺序冲突导致死锁
                if (transactionToDispose != null)
                {
                    try
                    {
                        // 释放事务，如果事务未完成，则会自动回滚事务
                        transactionToDispose.Dispose();
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Error(nameof(DefaultDbTransactionManager), "释放事务时发生异常：{Exception}。", ex);
                    }
                }

                syncLock.Dispose();
            }
        }
    }
}
