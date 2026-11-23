using System.Data.Common;
using Xbc.DbAccess.Common;

#if NETFRAMEWORK
using System.Data.SqlClient;
#else
using Microsoft.Data.SqlClient;
#endif

namespace Xbc.DbAccess.SqlServer
{
    /// <summary>
    /// SQL Server 原生 bulk 插入器，基于 <see cref="SqlBulkCopy"/> 实现。
    /// <para>目标表名与列名经 SQL Server 方言引用后使用；<see cref="BulkInsertOptions.BatchSize"/> 映射为
    /// <see cref="SqlBulkCopy.BatchSize"/>，<see cref="BulkInsertOptions.CommandTimeout"/> 映射为 <see cref="SqlBulkCopy.BulkCopyTimeout"/>。</para>
    /// <para>已知行为边界（SqlBulkCopyOptions 的 CheckConstraints/Triggers/KeepIdentity 均未启用）：CHECK 与外键约束默认不做校验、
    /// 触发器默认不触发（参数化 INSERT 路径与 PostgreSQL 的 COPY 均强制约束，跨方言行为不一致）；目标列集含标识列且行数据提供显式值时，
    /// 该值被静默丢弃、由服务端另行生成（调用方须避免在 <see cref="BulkInsertContext.Columns"/> 中包含标识列）。</para>
    /// </summary>
    public sealed class SqlServerBulkInserter : IDbBulkInserter
    {
        /// <summary>
        /// SQL Server 原生 bulk 插入器实例（无状态，全局复用）。
        /// </summary>
        public static readonly SqlServerBulkInserter Instance = new SqlServerBulkInserter();

        private SqlServerBulkInserter()
        {
        }

        /// <summary>
        /// 异步使用 <see cref="SqlBulkCopy"/> 执行 bulk 插入。
        /// </summary>
        /// <param name="connection">数据库连接（<see cref="SqlConnection"/>，已打开）。</param>
        /// <param name="transaction">数据库事务（<see cref="SqlTransaction"/>），参与调用方事务时非 null。</param>
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

            var sqlConnection = connection as SqlConnection;
            if (sqlConnection == null)
                throw new ArgumentException($"bulk 插入需要 SQL Server 连接，实际类型为 '{connection.GetType()}'。", nameof(connection));

            var sqlTransaction = transaction as SqlTransaction;
            if (transaction != null && sqlTransaction == null)
                throw new ArgumentException($"bulk 插入需要 SQL Server 事务，实际类型为 '{transaction.GetType()}'。", nameof(transaction));

            var dataTable = context.BuildDataTable();

            // KeepNulls：写入真正的 NULL 而非被目标列 DEFAULT 值替换（SqlBulkCopy 默认行为与 bcp 不带 -k 一致），
            // 保证与参数化插入路径（多值 INSERT）的 NULL 语义一致；注意 SqlBulkCopy 默认不校验 CHECK/FK 约束、不触发触发器
            using (var bulkCopy = new SqlBulkCopy(sqlConnection, SqlBulkCopyOptions.KeepNulls, sqlTransaction))
            {
                if (options != null)
                {
                    if (options.BatchSize != null)
                        bulkCopy.BatchSize = options.BatchSize.Value;

                    if (options.CommandTimeout != null)
                        bulkCopy.BulkCopyTimeout = options.CommandTimeout.Value;
                }

                bulkCopy.DestinationTableName = SqlServerDialect.Instance.QuoteTableName(context.Schema, context.TableName);

                foreach (var column in context.Columns)
                {
                    bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                }

                await bulkCopy.WriteToServerAsync(dataTable, cancellationToken).ConfigureAwait(false);

#if NETFRAMEWORK
                // net462 的 System.Data.SqlClient 无 RowsCopied 属性，按写入行数计数（与数据表行数一致）
                return dataTable.Rows.Count;
#else
                return bulkCopy.RowsCopied;
#endif
            }
        }
    }
}
