using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库连接作用域基类，在多个数据库操作之间共享同一个连接。
    /// <para>
    /// 派生类包括 <see cref="DefaultDbConnectionScope"/>（简单连接作用域）和 <see cref="NestedDbConnectionScope"/>（嵌套连接作用域）。
    /// </para>
    /// </summary>
    internal abstract class DbConnectionScopeBase : DisposableBase, IDbConnectionScope
    {
        protected readonly bool manageConnection;
        protected readonly DbConnection connection;

        /// <summary>
        /// 作用域唯一标识。
        /// </summary>
        public Guid ScopeId { get; }

        /// <summary>
        /// 是否在作用域释放时自动释放连接。
        /// </summary>
        public bool ManageConnection => manageConnection;

        /// <summary>
        /// 作用域内的数据库连接。
        /// </summary>
        internal DbConnection Connection
        {
            get
            {
                ThrowIfDisposed();
                return connection;
            }
        }

        /// <summary>
        /// 作用域生命周期结束事件。
        /// </summary>
        internal event EventHandler<DbResourceLifecycleEndedEventArgs> LifecycleEnded;

        /// <summary>
        /// 初始化连接作用域。
        /// </summary>
        /// <param name="connection">数据库连接。</param>
        /// <param name="manageConnection">是否在作用域释放时自动释放连接。</param>
        /// <param name="scopeId">作用域唯一标识，由非 async 入口生成。</param>
        protected DbConnectionScopeBase(DbConnection connection, bool manageConnection, Guid scopeId)
        {
            this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
            this.manageConnection = manageConnection;
            ScopeId = scopeId;
        }

        /// <summary>
        /// 触发作用域生命周期结束事件。
        /// </summary>
        protected virtual void OnLifecycleEnded()
        {
            LifecycleEnded?.Invoke(this, new DbResourceLifecycleEndedEventArgs()
            {
                ResourceId = ScopeId,
                ResourceType = DbResourceType.ConnectionScope,
                EndTime = DateTime.Now,
            });
        }
    }
}
