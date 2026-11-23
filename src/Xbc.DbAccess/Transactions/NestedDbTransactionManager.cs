using System.Data;
using System.Data.Common;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 嵌套事务管理器，管理 <see cref="NestedDbTransaction"/> 的生命周期。
    /// <para>已存在活动事务时再次开启将创建子事务（保存点实现），复用父事务的连接和事务对象。
    /// 事务应按后进先出（LIFO）顺序完成；违例完成的事务及其栈上之子事务会被记录日志并一并移除（不抛异常），避免异常从 Dispose 流程逃逸导致内部事务与连接泄漏。</para>
    /// <para>非线程安全：设计为单个逻辑调用流内使用；并发开启事务会相互嵌套进同一栈，导致保存点语义错误。</para>
    /// </summary>
    internal sealed class NestedDbTransactionManager : DisposableBase, IDbTransactionManager
    {
        private readonly IDbConnectionManager connectionManager;
        private readonly Stack<NestedDbTransaction> transactionStack = new Stack<NestedDbTransaction>();// 事务栈，利用栈的先进后出特性，实现嵌套的事务
        private readonly ISavepointSqlDialect savepointSqlDialect;// 子事务的保存点 SQL 方言
        private readonly AsyncLock syncLock = new AsyncLock();// 同步锁，用于线程安全操作（支持同步与异步获取）

        /// <summary>
        /// 获取当前活动事务（栈顶事务），若无活动事务则返回 null。
        /// </summary>
        public DbTransaction CurrentTransaction
        {
            get
            {
                ThrowIfDisposed();
                using (syncLock.Lock())
                {
                    return transactionStack.Count > 0 ? transactionStack.Peek() : null;
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
                    return transactionStack.Count > 0 ? transactionStack.Peek().InnerConnection : null;
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
                    return transactionStack.Count > 0 && transactionStack.Peek().InnerConnection != null;
                }
            }
        }

        /// <summary>
        /// 初始化嵌套事务管理器。
        /// </summary>
        /// <param name="connectionManager">数据库连接管理器。</param>
        /// <param name="savepointSqlDialect">保存点 SQL 方言。</param>
        public NestedDbTransactionManager(IDbConnectionManager connectionManager, ISavepointSqlDialect savepointSqlDialect)
        {
            this.connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            this.savepointSqlDialect = savepointSqlDialect ?? throw new ArgumentNullException(nameof(savepointSqlDialect));
        }

        /// <summary>
        /// 异步开启事务。若无活动事务则创建根事务，否则创建子事务（通过保存点实现）。
        /// </summary>
        /// <param name="transactionId">事务唯一标识，由调用者在非 async 入口生成。</param>
        /// <param name="isolationLevel">事务隔离级别。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public async Task<DbTransaction> BeginTransactionAsync(Guid transactionId, IsolationLevel isolationLevel = IsolationLevel.Unspecified, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            using (await syncLock.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                NestedDbTransaction newTransaction;
                if (transactionStack.Count == 0)
                {
                    var innerConnection = await connectionManager.GetOrCreateAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        var innerTransaction = innerConnection.BeginTransaction(isolationLevel);

                        // 创建根事务，该事务负责管理连接的释放
                        newTransaction = new NestedDbTransaction(innerTransaction, innerConnection, true, transactionId);
                        newTransaction.Completed += Transaction_Completed;
                    }
                    catch
                    {
                        // 如果开启事务时发生异常，需要释放连接以避免连接泄漏
                        connectionManager.Release(innerConnection);
                        throw;
                    }
                }
                else
                {
                    var parentTransaction = transactionStack.Peek();

                    // 保存点名称须短（部分数据库标识符长度有限）；数据库不支持保存点时跳过创建，子事务的 Commit/Rollback 将为 no-op
                    string savepointName = SqlIdentifierHelper.Generate("sp");
                    await BeginSavepointAsync(savepointName, parentTransaction, cancellationToken).ConfigureAwait(false);

                    // 创建子事务，复用父事务的连接和事务对象，不管理连接的释放
                    newTransaction = new NestedDbTransaction(parentTransaction, savepointName, savepointSqlDialect, transactionId);
                    newTransaction.Completed += Transaction_Completed;
                }

                transactionStack.Push(newTransaction);
                return newTransaction;
            }
        }

        /// <summary>
        /// 异步使用指定的连接开启事务。若无活动事务则创建根事务，否则创建子事务（通过保存点实现）。
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
                NestedDbTransaction newTransaction;
                if (transactionStack.Count == 0)
                {
                    var innerTransaction = connection.BeginTransaction(isolationLevel);

                    // 创建根事务，该事务不负责管理连接的释放
                    newTransaction = new NestedDbTransaction(innerTransaction, connection, false, transactionId);
                    newTransaction.Completed += Transaction_Completed;
                }
                else
                {
                    var parentTransaction = transactionStack.Peek();
                    if (parentTransaction.InnerConnection != connection)
                        throw new InvalidOperationException($"参数 '{nameof(connection)}' 与父事务的连接不同，无法创建子事务。");

                    // 保存点名称须短（部分数据库标识符长度有限）；数据库不支持保存点时跳过创建，子事务的 Commit/Rollback 将为 no-op
                    string savepointName = SqlIdentifierHelper.Generate("sp");
                    await BeginSavepointAsync(savepointName, parentTransaction, cancellationToken).ConfigureAwait(false);

                    // 创建子事务，复用父事务的连接和事务对象，不管理连接的释放
                    newTransaction = new NestedDbTransaction(parentTransaction, savepointName, savepointSqlDialect, transactionId);
                    newTransaction.Completed += Transaction_Completed;
                }

                transactionStack.Push(newTransaction);
                return newTransaction;
            }
        }

        /// <summary>
        /// 异步在父事务上创建保存点。
        /// </summary>
        /// <param name="savepointName">保存点名称。</param>
        /// <param name="parentTransaction">父级事务。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task BeginSavepointAsync(string savepointName, NestedDbTransaction parentTransaction, CancellationToken cancellationToken)
        {
            // 子事务回滚时回滚到该保存点；不支持保存点的数据库跳过创建
            if (savepointSqlDialect.IsSavepointSupported)
            {
                using (var savePointCommand = parentTransaction.InnerConnection.CreateCommand())
                {
                    savePointCommand.CommandText = savepointSqlDialect.CreateSavepoint(savepointName);
                    savePointCommand.Transaction = parentTransaction.InnerTransaction;
                    await savePointCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// 事务完成事件处理，从事务栈中弹出已完成的事务。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="e">事件参数。</param>
        private void Transaction_Completed(object sender, DbTransactionCompletedEventArgs e)
        {
            if (sender is not NestedDbTransaction nestedTransaction)
                return;

            // 正在释放时跳过：Dispose 中异步锁先于状态销毁，窗口期放行会在 Lock() 处抛 ObjectDisposedException
            if (IsDisposed || IsDisposing)
                return;

            try
            {
                using (syncLock.Lock())
                {
                    if (transactionStack.Count == 0)
                        return;

                    // 正常路径：栈顶即已完成的事务，直接弹出
                    if (transactionStack.Peek().TransactionId == nestedTransaction.TransactionId)
                    {
                        transactionStack.Pop();
                        return;
                    }

                    // LIFO 违例（如根事务先于子事务释放）：不抛异常（异常会从 Dispose 完成通知逃逸，
                    // 跳过后续内部事务与连接释放，造成泄漏与栈永久滞留），记录日志并移除，尽力恢复栈一致性
                    DiagnosticLog.Error(nameof(NestedDbTransactionManager),
                        "事务 '{TransactionId}' 未按照后进先出（LIFO）的顺序完成，已从事务栈中移除。", nestedTransaction.TransactionId);

                    // 级联弹出位于违例事务之上的子事务（物理上已随根事务回滚/提交失效，滞留栈顶会让
                    // 后续在已释放连接上创建保存点）；不跨锁调用子 Dispose：syncLock 不可重入，
                    // 会形成 manager → child → manager 锁链死锁，子事务无自有物理资源，
                    // 用户随后释放时回滚保存点失败将被容错记录
                    while (transactionStack.Count > 0)
                    {
                        var transaction = transactionStack.Pop();
                        transaction.Completed -= Transaction_Completed;

                        // 弹到违例事务自身为止，其下更早开启的事务不受影响
                        if (transaction.TransactionId == nestedTransaction.TransactionId)
                            break;
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // 上方守卫通过后、Lock() 前，管理器可能已在另一线程完成 Dispose，飞行事件在此窗口到达
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
                var transactionsToDispose = new List<NestedDbTransaction>();

                using (syncLock.Lock())
                {
                    // 在锁内收集需要释放的事务，避免在持有锁时调用 Dispose 导致死锁
                    while (transactionStack.Count > 0)
                    {
                        var transaction = transactionStack.Pop();
                        if (transaction != null)
                        {
                            // 取消订阅事件，避免触发事件时造成死锁
                            transaction.Completed -= Transaction_Completed;
                            transactionsToDispose.Add(transaction);
                        }
                    }
                }

                // 在锁外释放事务，避免与 Commit/Rollback 中的锁顺序冲突导致死锁
                foreach (var transaction in transactionsToDispose)
                {
                    try
                    {
                        // 释放事务，若事务未完成，则会自动回滚事务（根事务）或回滚到保存点（子事务）
                        transaction.Dispose();
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Error(nameof(NestedDbTransactionManager), "释放事务时发生异常：{Exception}。", ex);
                    }
                }

                syncLock.Dispose();
            }
        }
    }
}
