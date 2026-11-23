using System.Data.Common;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 默认连接作用域管理器，同一时间只允许存在一个活动作用域。
    /// </summary>
    internal sealed class DefaultDbConnectionScopeManager : DisposableBase, IDbConnectionScopeManager
    {
        private readonly IDbConnectionManager connectionManager;
        private DefaultDbConnectionScope currentScope;// 当前数据库连接作用域
        private readonly AsyncLock asyncLock = new AsyncLock();// 异步锁

        /// <summary>
        /// 当前活动的连接作用域，若无则返回 null。
        /// </summary>
        public IDbConnectionScope CurrentScope
        {
            get
            {
                ThrowIfDisposed();
                using (asyncLock.Lock())
                {
                    return currentScope;
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
                    return currentScope?.Connection;
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
                    return currentScope?.Connection != null;
                }
            }
        }

        /// <summary>
        /// 初始化连接作用域管理器。
        /// </summary>
        /// <param name="connectionManager">数据库连接管理器。</param>
        public DefaultDbConnectionScopeManager(IDbConnectionManager connectionManager)
        {
            this.connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        }

        /// <summary>
        /// 异步创建新的连接作用域。
        /// </summary>
        /// <param name="scopeId">作用域唯一标识，由调用者在非 async 入口生成。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<IDbConnectionScope> CreateScopeAsync(Guid scopeId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            using (await asyncLock.LockAsync(cancellationToken).ConfigureAwait(false))
            {
                if (currentScope != null)
                    throw new InvalidOperationException("当前已存在活动的连接作用域，无法开始新的连接作用域。");

                var connection = await connectionManager.GetOrCreateAsync(cancellationToken).ConfigureAwait(false);
                currentScope = CreateConnectionScope(connection, true, scopeId);
                return currentScope;
            }
        }

        /// <summary>
        /// 异步使用指定连接创建连接作用域，作用域不管理连接的释放。
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
                if (currentScope != null)
                    throw new InvalidOperationException("当前已存在活动的连接作用域，无法开始新的连接作用域。");

                currentScope = CreateConnectionScope(connection, false, scopeId);
                return currentScope;
            }
        }

        /// <summary>
        /// 创建连接作用域实例并订阅生命周期事件。
        /// </summary>
        /// <param name="connection">数据库连接。</param>
        /// <param name="managedConnection">是否管理连接释放。</param>
        /// <param name="scopeId">作用域唯一标识，由非 async 入口生成。</param>
        /// <returns></returns>
        private DefaultDbConnectionScope CreateConnectionScope(DbConnection connection, bool managedConnection, Guid scopeId)
        {
            var connectionScope = new DefaultDbConnectionScope(connection, managedConnection, scopeId);
            connectionScope.LifecycleEnded += ConnectionScope_LifecycleEnded;
            return connectionScope;
        }

        /// <summary>
        /// 作用域生命周期结束事件处理，清除当前作用域引用。
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void ConnectionScope_LifecycleEnded(object sender, DbResourceLifecycleEndedEventArgs e)
        {
            if (sender is not DefaultDbConnectionScope connectionScope || connectionScope == null)
                return;

            // 已释放或正在释放时跳过：Dispose 流程会兜底清理作用域引用，窗口期到达的飞行事件无需再操作
            if (IsDisposed || IsDisposing)
                return;

            try
            {
                using (asyncLock.Lock())
                {
                    if (currentScope == null)
                        return;

                    if (currentScope.ScopeId != connectionScope.ScopeId)
                        throw new InvalidOperationException("当前连接作用域与被释放的连接作用域不是同一连接作用域。");

                    currentScope = null;
                }
            }
            catch (ObjectDisposedException)
            {
                // 忽略 asyncLock 已被释放的异常
            }
        }

        /// <summary>
        /// 释放当前作用域和异步锁资源。
        /// </summary>
        /// <param name="disposing">是否释放托管资源。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                using (asyncLock.Lock())
                {
                    if (currentScope != null)
                    {
                        try
                        {
                            // 取消订阅事件，避免触发该事件造成死锁
                            currentScope.LifecycleEnded -= ConnectionScope_LifecycleEnded;
                            currentScope.Dispose();
                        }
                        catch (Exception ex)
                        {
                            DiagnosticLog.Error(nameof(DefaultDbConnectionScopeManager), "释放数据库连接作用域时发生异常：{Exception}。", ex);
                        }
                        finally
                        {
                            currentScope = null;// 清除当前连接作用域引用
                        }
                    }
                }

                asyncLock.Dispose();
            }
        }
    }
}
