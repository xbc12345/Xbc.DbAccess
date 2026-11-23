using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 为 <see cref="DbConnection"/> 提供创建数据库命令的扩展方法。
    /// </summary>
    public static class DbConnectionExtensions
    {
        /// <summary>
        /// 创建数据库命令。
        /// </summary>
        /// <param name="connection">数据库连接。</param>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="transaction">数据库事务。</param>
        /// <param name="parameters">参数数组。</param>
        /// <returns></returns>
        public static DbCommand CreateCommand(
            this DbConnection connection,
            string sql,
            DbTransaction transaction = null,
            params DbParameter[] parameters)
        {
            Guard.ThrowIfNull(connection, nameof(connection));
            Guard.ThrowIfNullOrWhiteSpace(sql, nameof(sql));

            return connection.CreateCommand(sql, CommandType.Text, transaction, parameters);
        }

        /// <summary>
        /// 创建数据库命令。
        /// </summary>
        /// <param name="connection">数据库连接。</param>
        /// <param name="sql">SQL 语句或存储过程名称。</param>
        /// <param name="commandType">命令类型。</param>
        /// <param name="transaction">数据库事务。</param>
        /// <param name="parameters">参数数组。</param>
        /// <returns></returns>
        public static DbCommand CreateCommand(
            this DbConnection connection,
            string sql,
            CommandType commandType,
            DbTransaction transaction = null,
            params DbParameter[] parameters)
        {
            Guard.ThrowIfNull(connection, nameof(connection));
            Guard.ThrowIfNullOrWhiteSpace(sql, nameof(sql));

            var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = commandType;

            if (parameters != null && parameters.Length > 0)
                command.Parameters.AddRange(parameters);

            if (transaction != null)
            {
                // 包装事务需解包为内部事务再绑定
                if (transaction is DbTransactionWrapper transactionWrapper)
                    command.Transaction = transactionWrapper.InnerTransaction;
                else
                    command.Transaction = transaction;
            }

            return command;
        }
    }
}
