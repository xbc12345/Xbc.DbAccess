using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 嵌套连接作用域，支持在同一连接上创建多层嵌套的作用域。
    /// </summary>
    internal sealed class NestedDbConnectionScope : DbConnectionScopeBase
    {
        private readonly NestedDbConnectionScope parentScope;// 父级连接作用域

        /// <summary>
        /// 父级连接作用域，根作用域返回 null。
        /// </summary>
        public NestedDbConnectionScope ParentScope
        {
            get
            {
                ThrowIfDisposed();
                return parentScope;
            }
        }

        /// <summary>
        /// 嵌套层级，根作用域为 0。
        /// </summary>
        public int NestedLevel
        {
            get
            {
                ThrowIfDisposed();
                return parentScope?.NestedLevel + 1 ?? 0;
            }
        }

        /// <summary>
        /// 是否为根作用域（无父级作用域）。
        /// </summary>
        public bool IsRootScope
        {
            get
            {
                ThrowIfDisposed();
                return parentScope == null;
            }
        }

        /// <summary>
        /// 初始化根连接作用域。
        /// </summary>
        /// <param name="connection">数据库连接。</param>
        /// <param name="manageConnection">是否在作用域释放时自动释放连接。</param>
        /// <param name="scopeId">作用域唯一标识，由非 async 入口生成。</param>
        public NestedDbConnectionScope(DbConnection connection, bool manageConnection, Guid scopeId)
            : base(connection, manageConnection, scopeId)
        {
        }

        /// <summary>
        /// 初始化子连接作用域，共享父作用域的连接，不管理连接释放。
        /// </summary>
        /// <param name="parentScope">父级连接作用域。</param>
        /// <param name="scopeId">作用域唯一标识，由非 async 入口生成。</param>
        internal NestedDbConnectionScope(NestedDbConnectionScope parentScope, Guid scopeId)
            : base(parentScope.Connection, false, scopeId)
        {
            this.parentScope = parentScope;
        }

        /// <summary>
        /// 释放连接资源并触发生命周期结束事件。
        /// <para>仅根作用域且管理连接时才释放连接。</para>
        /// </summary>
        /// <param name="disposing">是否释放托管资源。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    // 如果需要管理连接的释放，且是根作用域，才释放连接
                    if (manageConnection && parentScope == null)
                        connection.Dispose();
                }
                finally
                {
                    // 即使连接释放抛出异常也必须触发生命周期结束事件，
                    // 否则管理器的作用域栈永远不会弹出，后续无法创建新作用域
                    OnLifecycleEnded();
                }
            }
        }
    }
}
