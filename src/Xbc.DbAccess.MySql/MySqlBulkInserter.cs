using System.Data.Common;
using MySqlConnector;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.MySql
{
    /// <summary>
    /// MySQL 原生 bulk 插入器，基于 MySqlConnector 的 <see cref="MySqlBulkCopy"/> 实现。
    /// <para>目标表名与列名经 MySQL 方言引用后使用；<see cref="BulkInsertOptions.CommandTimeout"/> 映射为
    /// <see cref="MySqlBulkCopy.BulkCopyTimeout"/>（MySqlBulkCopy 自行缓冲行数据，不支持分批，<see cref="BulkInsertOptions.BatchSize"/> 被静默忽略）。</para>
    /// </summary>
    public sealed class MySqlBulkInserter : IDbBulkInserter
    {
        /// <summary>
        /// MySQL 原生 bulk 插入器实例（无状态，全局复用）。
        /// </summary>
        public static readonly MySqlBulkInserter Instance = new MySqlBulkInserter();

        private MySqlBulkInserter()
        {
        }

        /// <summary>
        /// 异步使用 <see cref="MySqlBulkCopy"/> 执行 bulk 插入。
        /// </summary>
        /// <param name="connection">数据库连接（<see cref="MySqlConnection"/>，已打开）。</param>
        /// <param name="transaction">数据库事务（<see cref="MySqlTransaction"/>），参与调用方事务时非 null。</param>
        /// <param name="context">bulk 插入上下文。</param>
        /// <param name="options">bulk 插入选项，null 时使用默认值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>已写入的行数。</returns>
        public async Task<long> InsertAsync(DbConnection connection,
            DbTransaction transaction,
            BulkInsertContext context,
            BulkInsertOptions options = null,
            CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(connection, nameof(connection));
            Guard.ThrowIfNull(context, nameof(context));

            var mySqlConnection = connection as MySqlConnection;
            if (mySqlConnection == null)
                throw new ArgumentException($"bulk 插入需要 MySQL 连接，实际类型为 '{connection.GetType()}'。", nameof(connection));

            var mySqlTransaction = transaction as MySqlTransaction;
            if (transaction != null && mySqlTransaction == null)
                throw new ArgumentException($"bulk 插入需要 MySQL 事务，实际类型为 '{transaction.GetType()}'。", nameof(transaction));

            var bulkCopy = new MySqlBulkCopy(mySqlConnection, mySqlTransaction);
            if (options != null && options.CommandTimeout != null)
                bulkCopy.BulkCopyTimeout = options.CommandTimeout.Value;

            bulkCopy.DestinationTableName = MySqlDialect.Instance.QuoteTableName(context.Schema, context.TableName);

            for (int i = 0; i < context.Columns.Count; i++)
            {
                bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(i, context.Columns[i].ColumnName));
            }

            var dataTable = context.BuildDataTable();
            var result = await bulkCopy.WriteToServerAsync(dataTable, cancellationToken).ConfigureAwait(false);
            return result.RowsInserted;
        }
    }
}
