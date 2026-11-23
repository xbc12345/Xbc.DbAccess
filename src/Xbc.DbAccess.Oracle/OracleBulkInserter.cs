using System.Data;
using System.Data.Common;
using System.Text;
using Oracle.ManagedDataAccess.Client;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Oracle
{
    /// <summary>
    /// Oracle 原生 bulk 插入器，基于 ODP.NET 的数组绑定（ArrayBind）实现：单条 INSERT 语句绑定每列的值数组，一次往返写入全部行。
    /// <para>ArrayBind 为单次写入语义，不支持分批，<see cref="BulkInsertOptions.BatchSize"/> 被静默忽略；
    /// <see cref="BulkInsertOptions.CommandTimeout"/> 映射为命令超时。</para>
    /// <para>行数据在执行前物化为按列的值数组（ArrayBind 的绑定形态）；空集合无插入语句执行、直接返回 0，但扩展层的事务包裹仍会产生 Begin/Commit 往返。</para>
    /// </summary>
    public sealed class OracleBulkInserter : IDbBulkInserter
    {
        /// <summary>
        /// Oracle 原生 bulk 插入器实例（无状态，全局复用）。
        /// </summary>
        public static readonly OracleBulkInserter Instance = new OracleBulkInserter();

        private OracleBulkInserter()
        {
        }

        /// <summary>
        /// 异步使用数组绑定执行 bulk 插入。
        /// </summary>
        /// <param name="connection">数据库连接（<see cref="OracleConnection"/>，已打开）。</param>
        /// <param name="transaction">数据库事务（<see cref="OracleTransaction"/>），参与调用方事务时非 null。</param>
        /// <param name="context">bulk 插入上下文。</param>
        /// <param name="options">bulk 插入选项，null 时使用默认值（BatchSize 不适用，静默忽略）。</param>
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

            var oracleConnection = connection as OracleConnection;
            if (oracleConnection == null)
                throw new ArgumentException($"bulk 插入需要 Oracle 连接，实际类型为 '{connection.GetType()}'。", nameof(connection));

            var oracleTransaction = transaction as OracleTransaction;
            if (transaction != null && oracleTransaction == null)
                throw new ArgumentException($"bulk 插入需要 Oracle 事务，实际类型为 '{transaction.GetType()}'。", nameof(transaction));

            // 行数据物化为按列的值数组（ArrayBind 的绑定形态）
            var rows = new List<object[]>(context.Rows);
            if (rows.Count == 0)
                return 0;

            // 行数组长度与目标列数一一对应是上下文的文档契约，物化后校验给出明确异常优于数组绑定取值时索引越界的隐晦错误
            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r].Length != context.Columns.Count)
                    throw new ArgumentException($"bulk 插入的第 {r} 行数据包含 {rows[r].Length} 个值，与目标列数 {context.Columns.Count} 不一致。", nameof(context));
            }

            using (var command = oracleConnection.CreateCommand())
            {
                command.CommandText = BuildInsertSql(context.Schema, context.TableName, context.Columns);
                command.Transaction = oracleTransaction;
                command.ArrayBindCount = rows.Count;

                if (options != null && options.CommandTimeout != null)
                    command.CommandTimeout = options.CommandTimeout.Value;

                for (int i = 0; i < context.Columns.Count; i++)
                {
                    var columnValues = new object[rows.Count];
                    for (int r = 0; r < rows.Count; r++)
                    {
                        // bool → 1/0 归一为 decimal，与 OracleParameterBuilder 单条路径语义一致（ODP.NET 拒绝 Boolean 值绑定 NUMBER 列）
                        columnValues[r] = rows[r][i] is bool b ? (b ? 1m : 0m) : rows[r][i];
                    }

                    // 参数名用序号（p0、p1…），与列名解耦以规避特殊字符；数组绑定按首个非空元素的运行时类型显式指定 DbType
                    // （ODP.NET 对未指定 DbType 的数组绑定按首元素推断，string 等类型会抛 ArgumentException，需显式声明）
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "p" + i;
                    var first = columnValues.FirstOrDefault(v => v != null);
                    parameter.DbType = first switch
                    {
                        string => DbType.String,
                        decimal => DbType.Decimal,
                        int => DbType.Int32,
                        long => DbType.Int64,
                        DateTime => DbType.DateTime,
                        byte[] => DbType.Binary,
                        _ => parameter.DbType
                    };
                    parameter.Value = columnValues;
                    command.Parameters.Add(parameter);
                }

                return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 构建数组绑定的 INSERT 语句（表名、列清单缩进、VALUES、占位符缩进各占一行，与 SQL 构建器的 INSERT 格式一致），
        /// 表名与列名经 Oracle 方言引用。
        /// </summary>
        /// <param name="schema">架构名，null 或空时只引用表名。</param>
        /// <param name="tableName">表名。</param>
        /// <param name="columns">列集。</param>
        /// <returns>INSERT 语句。</returns>
        internal static string BuildInsertSql(string schema, string tableName, IReadOnlyList<BulkInsertColumn> columns)
        {
            var dialect = OracleDialect.Instance;
            var quotedColumns = new string[columns.Count];
            var parameterPlaceholders = new string[columns.Count];

            for (int i = 0; i < columns.Count; i++)
            {
                quotedColumns[i] = dialect.QuoteIdentifier(columns[i].ColumnName);
                parameterPlaceholders[i] = ":p" + i;
            }

            var sb = new StringBuilder();
            sb.AppendLine("INSERT INTO " + dialect.QuoteTableName(schema, tableName));
            sb.AppendLine($"    ({string.Join(", ", quotedColumns)})");
            sb.AppendLine("VALUES");
            sb.AppendLine($"    ({string.Join(", ", parameterPlaceholders)})");
            return sb.ToString();
        }
    }
}
