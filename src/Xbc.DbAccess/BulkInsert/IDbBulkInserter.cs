using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 原生 bulk 插入器接口，封装数据库原生批量写入 API 的执行细节。
    /// <para>由各方言包实现（如 SQL Server 的 SqlBulkCopy、PostgreSQL 的 COPY、Oracle 的 ArrayBind），经
    /// <see cref="DbAccessProviderFactory.BulkInserter"/> 暴露；未提供原生实现的方言（如 SQLite）返回 null，由调用方回退。
    /// 各方言实现同时提供公共单例，供自管连接的直连调用方使用。</para>
    /// <para>实现约定：连接与事务由调用方保证已就绪（连接已打开、事务已开始），本接口只负责执行批量写入；
    /// 行值已由调用方转换为数据库值（null 统一为 <see cref="DBNull"/>）。</para>
    /// </summary>
    public interface IDbBulkInserter
    {
        /// <summary>
        /// 异步执行原生 bulk 插入。
        /// </summary>
        /// <param name="connection">数据库连接（真实提供程序连接，已打开）。</param>
        /// <param name="transaction">数据库事务（真实提供程序事务），参与调用方事务时非 null。</param>
        /// <param name="context">bulk 插入上下文（目标表、列与行数据）。</param>
        /// <param name="options">bulk 插入选项，null 时使用方言默认值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>已写入的行数。</returns>
        Task<long> InsertAsync(DbConnection connection,
            DbTransaction transaction,
            BulkInsertContext context,
            BulkInsertOptions options = null,
            CancellationToken cancellationToken = default);
    }
}
