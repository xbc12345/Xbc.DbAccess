using System.Data;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 为 <see cref="IDbAccess"/> 接口提供实体操作相关的扩展方法，包括实体查询、多结果集查询和实体集合获取。
    /// </summary>
    public static class DbAccessEntityExtensions
    {
        /// <summary>
        /// 以手写 SQL 创建实体查询对象（仅支持文本 SQL：分页与导航属性分裂查询均基于文本 SQL 改写/拼接）。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="sql">查询 SQL 语句。</param>
        /// <param name="param">SQL 参数。</param>
        /// <returns>实体查询对象。</returns>
        public static IEntityQuery<T> FromSql<T>(this IDbAccess dbAccess,
            string sql,
            object param = null)
            where T : class
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            Guard.ThrowIfNullOrWhiteSpace(sql, nameof(sql));

            var parameters = dbAccess.ConvertToDbParameters(param, ref sql);

            // 可通过 Include 加载导航属性（分裂查询），不调 Include 则为普通查询
            return new EntityQuery<T>(dbAccess, sql, parameters);
        }

        /// <summary>
        /// 异步执行查询 SQL 语句，返回多结果集实体映射器。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="sql">查询 SQL 语句。</param>
        /// <param name="param">SQL 参数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>多结果集实体映射器。</returns>
        public static Task<EntityMultiReader> ExecuteMultipleAsync(this IDbAccess dbAccess,
            string sql,
            object param = null,
            CancellationToken cancellationToken = default)
        {
            return dbAccess.ExecuteMultipleAsync(sql, CommandType.Text, param, cancellationToken);
        }

        /// <summary>
        /// 异步执行查询 SQL 语句或存储过程，返回多结果集实体映射器。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="sql">查询 SQL 语句或存储过程名。</param>
        /// <param name="commandType">命令类型。</param>
        /// <param name="param">SQL 参数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>多结果集实体映射器。</returns>
        public static async Task<EntityMultiReader> ExecuteMultipleAsync(this IDbAccess dbAccess,
            string sql,
            CommandType commandType,
            object param = null,
            CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));

            var parameters = dbAccess.ConvertToDbParameters(param, ref sql, commandType);
            var reader = await dbAccess.ExecuteReaderAsync(sql, commandType, parameters, cancellationToken).ConfigureAwait(false);
            return new EntityMultiReader(reader);
        }

        /// <summary>
        /// 获取实体集合对象。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <returns>实体集合对象。</returns>
        public static IEntitySet<T> Set<T>(this IDbAccess dbAccess) where T : class
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            return new EntitySet<T>(dbAccess);
        }
    }
}
