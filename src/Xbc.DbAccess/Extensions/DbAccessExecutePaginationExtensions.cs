using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 为 <see cref="IDbAccess"/> 提供执行分页查询相关的扩展方法。
    /// </summary>
    public static class DbAccessExecutePaginationExtensions
    {
        /// <summary>
        /// 异步执行分页查询 SQL 语句，返回 <see cref="DbDataReader"/> 实例。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="querySql">查询 SQL 语句。</param>
        /// <param name="pageIndex">页码（从 1 开始）。</param>
        /// <param name="pageSize">每页记录数。</param>
        /// <param name="orderBy">排序字段。直接拼接进 SQL，禁止拼接用户输入，防止 SQL 注入。</param>
        /// <param name="param">参数（匿名对象、字典或 DbParameter 集合等）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public static async Task<DbDataReader> ExecuteReaderWithPaginationAsync(this IDbAccess dbAccess,
            string querySql,
            int pageIndex,
            int pageSize,
            string orderBy,
            object param = null,
            CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            Guard.ThrowIfNullOrWhiteSpace(querySql, nameof(querySql));

            var sqlBuilder = dbAccess.ProviderFactory.SqlBuilder;
            var serverInfo = await dbAccess.Factory.GetServerInfoAsync().ConfigureAwait(false);
            var sql = BuildPageWindowSql(sqlBuilder, querySql, pageIndex, pageSize, orderBy, serverInfo);
            var parameters = dbAccess.ConvertToDbParameters(param, ref sql);

            return await dbAccess.ExecuteReaderAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 异步执行分页查询 SQL 语句，返回 <see cref="DataSet"/> 实例。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="querySql">查询 SQL 语句。</param>
        /// <param name="pageIndex">页码（从 1 开始）。</param>
        /// <param name="pageSize">每页记录数。</param>
        /// <param name="orderBy">排序字段。直接拼接进 SQL，禁止拼接用户输入，防止 SQL 注入。</param>
        /// <param name="param">参数（匿名对象、字典或 DbParameter 集合等）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public static async Task<DataSet> ExecuteDataSetWithPaginationAsync(this IDbAccess dbAccess,
            string querySql,
            int pageIndex,
            int pageSize,
            string orderBy,
            object param = null,
            CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            Guard.ThrowIfNullOrWhiteSpace(querySql, nameof(querySql));

            var sqlBuilder = dbAccess.ProviderFactory.SqlBuilder;
            var serverInfo = await dbAccess.Factory.GetServerInfoAsync().ConfigureAwait(false);
            var sql = BuildPageWindowSql(sqlBuilder, querySql, pageIndex, pageSize, orderBy, serverInfo);
            var parameters = dbAccess.ConvertToDbParameters(param, ref sql);

            return await dbAccess.ExecuteDataSetAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 校验页码并换算为行窗口（skip = (pageIndex - 1) * pageSize），交由 <see cref="ISqlBuilder.BuildWindowSql"/> 构建。
        /// </summary>
        private static string BuildPageWindowSql(ISqlBuilder sqlBuilder,
            string querySql,
            int pageIndex,
            int pageSize,
            string orderBy,
            DbServerInfo serverInfo)
        {
            Guard.ThrowIfNegativeOrZero(pageIndex, nameof(pageIndex));
            Guard.ThrowIfNegativeOrZero(pageSize, nameof(pageSize));
            Guard.ThrowIfNullOrWhiteSpace(orderBy, nameof(orderBy));

            // long 计算防 int 溢出生成负数 OFFSET；超出 int.MaxValue 属无效分页参数（SQL 窗口以 int 行计数），按越界抛出
            long offset = (long)(pageIndex - 1) * pageSize;
            if (offset > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(pageIndex), $"分页参数超出范围，跳过行数 {offset} 超过 int.MaxValue。");

            return sqlBuilder.BuildWindowSql(querySql, (int)offset, pageSize, orderBy, appendRowOrder: true, serverInfo);
        }
    }
}
