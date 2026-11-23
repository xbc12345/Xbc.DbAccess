using System.Data.Common;
using Xbc.DbAccess.Diagnostics;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库连接管理器接口，负责获取或创建数据库连接，以及释放连接。
    /// </summary>
    public interface IDbConnectionManager : IDbConnectionStatisticsProvider, IDisposable
    {
        /// <summary>
        /// 连接管理器唯一标识。
        /// </summary>
        Guid ManagerId { get; }

        /// <summary>
        /// 连接字符串。
        /// </summary>
        string ConnectionString { get; }

        /// <summary>
        /// 连接追踪器。
        /// </summary>
        IDbConnectionTracker ConnectionTracker { get; }

        /// <summary>
        /// 异步获取或创建数据库连接。
        /// <para>池化模式优先复用空闲连接，无空闲则新建；池满且等待超时仍无可用连接时抛 <see cref="DbConnectionExhaustedException"/>。非池化模式每次新建连接。</para>
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        Task<DbConnection> GetOrCreateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 释放指定的数据库连接：池化模式回收到池中等待复用，非池化模式立即释放；非本管理器创建的连接会忽略并记录警告。
        /// </summary>
        /// <param name="connection">要释放的数据库连接。</param>
        void Release(DbConnection connection);
    }
}
