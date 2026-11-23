using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 为 <see cref="IDbAccess"/> 提供执行存储过程相关的扩展方法。
    /// </summary>
    public static class DbAccessExecuteProcedureExtensions
    {
        /// <summary>
        /// 异步执行非查询存储过程，返回受影响的行数和输出参数。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="procedureName">存储过程名称。</param>
        /// <param name="param">参数（匿名对象、字典或 DbParameter 集合等）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public static async Task<ProcedureResult> ExecuteNonQueryProcedureAsync(this IDbAccess dbAccess,
            string procedureName,
            object param = null,
            CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            Guard.ThrowIfNullOrWhiteSpace(procedureName, nameof(procedureName));

            var sql = string.Empty;
            var parameters = dbAccess.ConvertToDbParameters(param, ref sql, CommandType.StoredProcedure);
            int rowsAffected = await dbAccess.ExecuteNonQueryAsync(procedureName, CommandType.StoredProcedure, parameters, cancellationToken).ConfigureAwait(false);
            var outputParameters = CreateOutputParameters(dbAccess, parameters);

            return new ProcedureResult
            {
                RowsAffected = rowsAffected,
                OutputParameters = outputParameters
            };
        }

        /// <summary>
        /// 异步执行查询存储过程，返回数据集和输出参数。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="procedureName">存储过程名称。</param>
        /// <param name="param">参数（匿名对象、字典或 DbParameter 集合等）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public static async Task<ProcedureResult> ExecuteQueryProcedureAsync(this IDbAccess dbAccess,
            string procedureName,
            object param = null,
            CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            Guard.ThrowIfNullOrWhiteSpace(procedureName, nameof(procedureName));

            var sql = string.Empty;
            var parameters = dbAccess.ConvertToDbParameters(param, ref sql, CommandType.StoredProcedure);
            var dataSet = await dbAccess.ExecuteDataSetAsync(procedureName, CommandType.StoredProcedure, parameters, cancellationToken).ConfigureAwait(false);
            var outputParameters = CreateOutputParameters(dbAccess, parameters);

            return new ProcedureResult
            {
                DataSet = dataSet,
                OutputParameters = outputParameters
            };
        }

        /// <summary>
        /// 从参数数组中提取输出参数、输入输出参数和返回值参数。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="parameters">参数数组。</param>
        /// <returns></returns>
        private static DbParameterCollection CreateOutputParameters(IDbAccess dbAccess, DbParameter[] parameters)
        {
            var outputParameters = new DbParameterCollection(dbAccess.ProviderFactory.ParameterBuilder);
            if (parameters != null)
            {
                foreach (DbParameter parameter in parameters)
                {
                    if (parameter.Direction == ParameterDirection.Output ||
                        parameter.Direction == ParameterDirection.InputOutput ||
                        parameter.Direction == ParameterDirection.ReturnValue)
                    {
                        outputParameters.Add(parameter);
                    }
                }
            }

            return outputParameters;
        }
    }
}
