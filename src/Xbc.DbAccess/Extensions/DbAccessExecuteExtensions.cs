using System.Data;
using System.Data.Common;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 为 <see cref="IDbAccess"/> 提供执行 SQL 语句的扩展方法，支持 object 类型参数。
    /// </summary>
    public static class DbAccessExecuteExtensions
    {
        #region ExecuteNonQuery
        /// <summary>
        /// 异步执行非查询 SQL 语句，返回受影响的行数。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="param">参数（匿名对象、字典或 DbParameter 集合等）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public static Task<int> ExecuteNonQueryAsync(this IDbAccess dbAccess, string sql, object param = null, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));

            var parameters = dbAccess.ConvertToDbParameters(param, ref sql);
            return dbAccess.ExecuteNonQueryAsync(sql, parameters, cancellationToken);
        }

        /// <summary>
        /// 异步执行非查询 SQL 语句或存储过程，返回受影响的行数。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="sql">SQL 语句或存储过程名称。</param>
        /// <param name="commandType">命令类型。</param>
        /// <param name="param">参数（匿名对象、字典或 DbParameter 集合等）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public static Task<int> ExecuteNonQueryAsync(this IDbAccess dbAccess, string sql, CommandType commandType, object param = null, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));

            var parameters = dbAccess.ConvertToDbParameters(param, ref sql, commandType);
            return dbAccess.ExecuteNonQueryAsync(sql, commandType, parameters, cancellationToken);
        }
        #endregion

        #region ExecuteScalar
        /// <summary>
        /// 异步执行查询 SQL 语句，返回结果集中第一行第一列的值。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="param">参数（匿名对象、字典或 DbParameter 集合等）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public static Task<object> ExecuteScalarAsync(this IDbAccess dbAccess, string sql, object param = null, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));

            var parameters = dbAccess.ConvertToDbParameters(param, ref sql);
            return dbAccess.ExecuteScalarAsync(sql, parameters, cancellationToken);
        }

        /// <summary>
        /// 异步执行查询 SQL 语句或存储过程，返回结果集中第一行第一列的值。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="sql">SQL 语句或存储过程名称。</param>
        /// <param name="commandType">命令类型。</param>
        /// <param name="param">参数（匿名对象、字典或 DbParameter 集合等）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public static Task<object> ExecuteScalarAsync(this IDbAccess dbAccess, string sql, CommandType commandType, object param = null, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));

            var parameters = dbAccess.ConvertToDbParameters(param, ref sql, commandType);
            return dbAccess.ExecuteScalarAsync(sql, commandType, parameters, cancellationToken);
        }

        /// <summary>
        /// 异步执行查询 SQL 语句，返回结果集中第一行第一列的值并转换为指定类型。
        /// </summary>
        /// <typeparam name="T">返回值类型。</typeparam>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="param">参数（匿名对象、字典或 DbParameter 集合等）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public static async Task<T> ExecuteScalarAsync<T>(this IDbAccess dbAccess,
            string sql,
            object param = null,
            CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));

            var parameters = dbAccess.ConvertToDbParameters(param, ref sql);
            object result = await dbAccess.ExecuteScalarAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
            return DbValueConvert.ChangeType<T>(result);
        }
        #endregion

        #region ExecuteReader
        /// <summary>
        /// 异步执行查询 SQL 语句，返回 <see cref="DbDataReader"/> 实例。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="param">参数（匿名对象、字典或 DbParameter 集合等）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public static Task<DbDataReader> ExecuteReaderAsync(this IDbAccess dbAccess, string sql, object param = null, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));

            var parameters = dbAccess.ConvertToDbParameters(param, ref sql);
            return dbAccess.ExecuteReaderAsync(sql, parameters, cancellationToken);
        }

        /// <summary>
        /// 异步执行查询 SQL 语句或存储过程，返回 <see cref="DbDataReader"/> 实例。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="sql">SQL 语句或存储过程名称。</param>
        /// <param name="commandType">命令类型。</param>
        /// <param name="param">参数（匿名对象、字典或 DbParameter 集合等）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public static Task<DbDataReader> ExecuteReaderAsync(this IDbAccess dbAccess, string sql, CommandType commandType, object param = null, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));

            var parameters = dbAccess.ConvertToDbParameters(param, ref sql, commandType);
            return dbAccess.ExecuteReaderAsync(sql, commandType, parameters, cancellationToken);
        }
        #endregion

        #region ExecuteDataSet
        /// <summary>
        /// 异步执行查询 SQL 语句，返回 <see cref="DataSet"/> 实例。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="param">参数（匿名对象、字典或 DbParameter 集合等）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public static Task<DataSet> ExecuteDataSetAsync(this IDbAccess dbAccess, string sql, object param = null, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));

            var parameters = dbAccess.ConvertToDbParameters(param, ref sql);
            return dbAccess.ExecuteDataSetAsync(sql, parameters, cancellationToken);
        }

        /// <summary>
        /// 异步执行查询 SQL 语句或存储过程，返回 <see cref="DataSet"/> 实例。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="sql">SQL 语句或存储过程名称。</param>
        /// <param name="commandType">命令类型。</param>
        /// <param name="param">参数（匿名对象、字典或 DbParameter 集合等）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public static Task<DataSet> ExecuteDataSetAsync(this IDbAccess dbAccess, string sql, CommandType commandType, object param = null, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));

            var parameters = dbAccess.ConvertToDbParameters(param, ref sql, commandType);
            return dbAccess.ExecuteDataSetAsync(sql, commandType, parameters, cancellationToken);
        }
        #endregion

        #region CreateParameters
        /// <summary>
        /// 创建新的参数集合。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <returns></returns>
        public static DbParameterCollection NewParameters(this IDbAccess dbAccess)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            return new DbParameterCollection(dbAccess.ProviderFactory.ParameterBuilder);
        }

        /// <summary>
        /// 创建参数。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="name">参数名。</param>
        /// <param name="value">参数值。</param>
        /// <param name="dbType">参数数据库类型。</param>
        /// <returns></returns>
        public static DbParameter CreateParameter(this IDbAccess dbAccess, string name, object value, DbType? dbType = null)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            return dbAccess.ProviderFactory.ParameterBuilder.CreateParameter(name, value, dbType);
        }

        /// <summary>
        /// 创建参数。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="name">参数名。</param>
        /// <param name="value">参数值。</param>
        /// <param name="dbType">参数数据库类型。</param>
        /// <param name="direction">参数方向。</param>
        /// <returns></returns>
        public static DbParameter CreateParameter(this IDbAccess dbAccess, string name, object value, DbType? dbType, ParameterDirection direction)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            return dbAccess.ProviderFactory.ParameterBuilder.CreateParameter(name, value, dbType, direction);
        }

        /// <summary>
        /// 创建输出参数。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="name">参数名。</param>
        /// <param name="dbType">参数数据库类型。</param>
        /// <param name="direction">参数方向，默认为 Output。</param>
        /// <param name="size">参数长度（字符/字节数），null 时对输出类方向的字符串/二进制参数使用默认值 4000。</param>
        /// <returns></returns>
        public static DbParameter CreateOutputParameter(this IDbAccess dbAccess, string name, DbType? dbType, ParameterDirection direction = ParameterDirection.Output, int? size = null)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            return dbAccess.ProviderFactory.ParameterBuilder.CreateParameter(name, DBNull.Value, dbType, direction, size);
        }
        #endregion
    }
}
