using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 默认连接作用域，同一时间只允许存在一个活动作用域。
    /// </summary>
    internal sealed class DefaultDbConnectionScope : DbConnectionScopeBase
    {
        /// <summary>
        /// 初始化连接作用域。
        /// </summary>
        /// <param name="connection">数据库连接。</param>
        /// <param name="manageConnection">是否在作用域释放时自动释放连接。</param>
        /// <param name="scopeId">作用域唯一标识，由非 async 入口生成。</param>
        internal DefaultDbConnectionScope(DbConnection connection, bool manageConnection, Guid scopeId)
            : base(connection, manageConnection, scopeId)
        {
        }

        /// <summary>
        /// 释放连接资源并触发生命周期结束事件。
        /// </summary>
        /// <param name="disposing">是否释放托管资源。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    if (manageConnection)
                        connection.Dispose();
                }
                finally
                {
                    // 即使连接释放抛出异常也必须触发生命周期结束事件，
                    // 否则管理器的当前作用域引用永远不会被清除，后续无法创建新作用域
                    OnLifecycleEnded();
                }
            }
        }
    }
}
