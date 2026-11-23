using System.Data.Common;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 嵌套连接作用域管理器，支持在同一连接上创建多层嵌套的作用域，按后进先出（LIFO）顺序释放。
    /// <para>违例释放的作用域及其栈上之子作用域会被记录日志并一并移除（不抛异常），避免异常从 Dispose 流程逃逸导致连接泄漏。</para>
    /// <para>非线程安全：设计为单个逻辑调用流内使用；并行分支共享同一实例时作用域会相互嵌套进同一栈，导致层级错乱。</para>
    /// </summary>
    internal sealed class NestedDbConnectionScopeManager : DisposableBase, IDbConnectionScopeManager
    {
        private readonly IDbConnectionManager connectionManager;
        private readonly Stack<NestedDbConnectionScope> scopeStack = new Stack<NestedDbConnectionScope>();// 连接作用域栈，利用栈的先进后出特性，实现嵌套的连接作用域
        private readonly AsyncLock asyncLock = new AsyncLock();// 异步锁

        /// <summary>
        /// 当前活动的连接作用域（栈顶），若无则返回 null。
        /// </summary>
        public IDbConnectionScope CurrentScope
        {
            get
            {
                ThrowIfDisposed();
                using (asyncLock.Lock())
                {
                    return scopeStack.Count > 0 ? scopeStack.Peek() : null;
                }
            }
        }

        /// <summary>
        /// 当前作用域内的数据库连接，若无活动作用域则返回 null。
        /// </summary>
        public DbConnection CurrentConnection
        {
            get
            {
                ThrowIfDisposed();
                using (asyncLock.Lock())
                {
                    return scopeStack.Count > 0 ? scopeStack.Peek().Connection : null;
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
                using (asyncLock.Lock())
                {
                    return scopeStack.Count > 0 && scopeStack.Peek().Connection != null;
                }
            }
        }

        /// <summary>
        /// 初始化嵌套连接作用域管理器。
        /// </summary>
        /// <param name="connectionManager">数据库连接管理器。</param>
        public NestedDbConnectionScopeManager(IDbConnectionManager connectionManager)
        {
            this.connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        }

        /// <summary>
        /// 异步创建新的连接作用域。若无活动作用域则创建根作用域，否则创建共享连接的子作用域。
        /// </summary>
        /// <param name="scopeId">作用域唯一标识，由调用者在非 async 入口生成。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public async Task<IDbConnectionScope> CreateScopeAsync(Guid scopeId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            using (await asyncLock.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                NestedDbConnectionScope newScope;
                if (scopeStack.Count == 0)
                {
                    // 创建根作用域，该作用域需要管理连接的释放
                    var connection = await connectionManager.GetOrCreateAsync(cancellationToken).ConfigureAwait(false);
                    newScope = new NestedDbConnectionScope(connection, true, scopeId);
                    newScope.LifecycleEnded += ConnectionScope_LifecycleEnded;
                }
                else
                {
                    // 创建子作用域，直接使用父作用域的连接，该作用域不需要管理连接的释放
                    var parentScope = scopeStack.Peek();
                    newScope = new NestedDbConnectionScope(parentScope, scopeId);
                    newScope.LifecycleEnded += ConnectionScope_LifecycleEnded;
                }

                scopeStack.Push(newScope);
                return newScope;
            }
        }

        /// <summary>
        /// 异步使用指定连接创建连接作用域。若已存在活动作用域，则连接必须与父作用域一致。
        /// </summary>
        /// <param name="connection">已有的数据库连接。</param>
        /// <param name="scopeId">作用域唯一标识，由调用者在非 async 入口生成。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<IDbConnectionScope> CreateScopeAsync(DbConnection connection, Guid scopeId, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(connection, nameof(connection));
            ThrowIfDisposed();

            using (await asyncLock.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                NestedDbConnectionScope newScope;
                if (scopeStack.Count == 0)
                {
                    // 创建根作用域，该作用域不需要管理连接的释放
                    newScope = new NestedDbConnectionScope(connection, false, scopeId);
                    newScope.LifecycleEnded += ConnectionScope_LifecycleEnded;
                }
                else
                {
                    var parentScope = scopeStack.Peek();
                    if (parentScope.Connection != connection)
                        throw new InvalidOperationException($"参数 '{nameof(connection)}' 与父连接作用域的连接不同，无法创建子连接作用域。");

                    // 创建子作用域，直接使用父作用域的连接，该作用域不需要管理连接的释放
                    newScope = new NestedDbConnectionScope(parentScope, scopeId);
                    newScope.LifecycleEnded += ConnectionScope_LifecycleEnded;
                }

                scopeStack.Push(newScope);
                return newScope;
            }
        }

        /// <summary>
        /// 作用域生命周期结束事件处理，弹出栈顶作用域。
        /// </summary>
        private void ConnectionScope_LifecycleEnded(object sender, DbResourceLifecycleEndedEventArgs e)
        {
            if (sender is not NestedDbConnectionScope connectionScope || connectionScope == null)
                return;

            // 已释放时跳过：Dispose 流程会兜底清空作用域栈，飞行事件无需再操作
            if (IsDisposed || IsDisposing)
                return;

            try
            {
                using (asyncLock.Lock())
                {
                    if (scopeStack.Count == 0)
                        return;

                    // 正常路径：栈顶即已结束的作用域，直接弹出
                    if (scopeStack.Peek().ScopeId == connectionScope.ScopeId)
                    {
                        scopeStack.Pop();
                        return;
                    }

                    // LIFO 违例（如根作用域先于子作用域释放）：不抛异常（异常会从 Dispose 完成通知逃逸
                    // 且栈会滞留已结束的作用域），记录日志并移除，尽力恢复栈的一致性
                    DiagnosticLog.Error(nameof(NestedDbConnectionScopeManager),
                        "连接作用域 '{ScopeId}' 未按照后进先出（LIFO）的顺序释放，已从作用域栈中移除。", connectionScope.ScopeId);

                    // 级联弹出位于违例作用域之上的子作用域（其连接引用归根作用域，滞留栈顶会让
                    // 后续操作落在已释放的连接上）；不跨锁调用子 Dispose：asyncLock 不可重入，
                    // 会形成 manager → child → manager 锁链死锁，子作用域无自有物理资源，
                    // 用户随后释放时将被容错处理
                    while (scopeStack.Count > 0)
                    {
                        var scope = scopeStack.Pop();
                        scope.LifecycleEnded -= ConnectionScope_LifecycleEnded;

                        // 弹到违例作用域自身为止，其下更早开启的作用域不受影响，保持原有顺序
                        if (scope.ScopeId == connectionScope.ScopeId)
                            break;
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // 忽略 asyncLock 已被释放的异常
            }
        }

        /// <summary>
        /// 释放所有嵌套作用域和异步锁资源。
        /// </summary>
        /// <param name="disposing">是否释放托管资源。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var scopesToDispose = new List<NestedDbConnectionScope>();
                using (asyncLock.Lock())
                {
                    // 在锁内收集需要释放的作用域，避免在持有锁时调用 Dispose 导致死锁
                    while (scopeStack.Count > 0)
                    {
                        var connectionScope = scopeStack.Pop();
                        if (connectionScope != null)
                        {
                            // 取消订阅事件，避免触发该事件造成死锁
                            connectionScope.LifecycleEnded -= ConnectionScope_LifecycleEnded;
                            scopesToDispose.Add(connectionScope);
                        }
                    }
                }

                // 在锁外释放作用域，避免与 LifecycleEnded 事件中的锁顺序冲突导致死锁
                foreach (var connectionScope in scopesToDispose)
                {
                    try
                    {
                        connectionScope.Dispose();
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Error(nameof(NestedDbConnectionScopeManager), "释放数据库连接作用域时发生异常：{Exception}。", ex);
                    }
                }
            }

            asyncLock.Dispose();
        }
    }
}
