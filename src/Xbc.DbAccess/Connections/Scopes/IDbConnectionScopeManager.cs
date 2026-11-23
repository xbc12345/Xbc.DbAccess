using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库连接作用域管理器，负责创建和管理连接作用域。
    /// </summary>
    public interface IDbConnectionScopeManager : IDbConnectionAccessor, IDisposable
    {
        /// <summary>
        /// 当前活动的连接作用域，若无则返回 null。
        /// </summary>
        IDbConnectionScope CurrentScope { get; }

        /// <summary>
        /// 异步创建新的连接作用域。
        /// </summary>
        /// <param name="scopeId">作用域唯一标识，由调用者在非 async 入口生成。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        Task<IDbConnectionScope> CreateScopeAsync(Guid scopeId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步使用指定连接创建连接作用域，作用域不管理连接的释放。
        /// </summary>
        /// <param name="connection">已有的数据库连接。</param>
        /// <param name="scopeId">作用域唯一标识，由调用者在非 async 入口生成。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        Task<IDbConnectionScope> CreateScopeAsync(DbConnection connection, Guid scopeId, CancellationToken cancellationToken = default);
    }
}
