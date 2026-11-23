using System.Collections.Concurrent;
using System.Data.Common;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Npgsql
{
    /// <summary>
    /// PostgreSQL 原生 bulk 插入器，基于 COPY BINARY（<see cref="NpgsqlConnection.BeginBinaryImport(string)"/>）实现。
    /// <para>COPY 为单次流式写入语义，不支持分批与超时控制，<see cref="BulkInsertOptions"/> 的选项被静默忽略；
    /// 返回的行数由本实现按写入行数计数（与服务端实际写入行数一致）。</para>
    /// <para>列值按运行时类型映射为 <see cref="NpgsqlDbType"/> 写入；二进制 COPY 要求值类型与目标列类型精确匹配，
    /// DateTime 按不带时区的 timestamp 映射， DateTimeOffset 按带时区的 timestamptz 映射。</para>
    /// </summary>
    public sealed class NpgsqlBulkInserter : IDbBulkInserter
    {
        /// <summary>
        /// PostgreSQL 原生 bulk 插入器实例（无状态，全局复用；类型映射表为进程级缓存）。
        /// </summary>
        public static readonly NpgsqlBulkInserter Instance = new NpgsqlBulkInserter();

        // CLR 类型到 NpgsqlDbType 的映射缓存（含未映射类型的 null 负缓存）
        private static readonly ConcurrentDictionary<Type, NpgsqlDbType?> clrTypeMapCache = new ConcurrentDictionary<Type, NpgsqlDbType?>();

        private NpgsqlBulkInserter()
        {
        }

        /// <summary>
        /// 异步使用 COPY BINARY 执行 bulk 插入。
        /// </summary>
        /// <param name="connection">数据库连接（<see cref="NpgsqlConnection"/>，已打开）。</param>
        /// <param name="transaction">数据库事务（<see cref="NpgsqlTransaction"/>），参与调用方事务时非 null。</param>
        /// <param name="context">bulk 插入上下文。</param>
        /// <param name="options">bulk 插入选项（COPY 不支持，静默忽略）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>已写入的行数。</returns>
        public Task<long> InsertAsync(DbConnection connection,
            DbTransaction transaction,
            BulkInsertContext context,
            BulkInsertOptions options = null,
            CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(connection, nameof(connection));
            Guard.ThrowIfNull(context, nameof(context));

            var npgsqlConnection = connection as NpgsqlConnection;
            if (npgsqlConnection == null)
                throw new ArgumentException($"bulk 插入需要 PostgreSQL 连接，实际类型为 '{connection.GetType()}'。", nameof(connection));

            var npgsqlTransaction = transaction as NpgsqlTransaction;
            if (transaction != null && npgsqlTransaction == null)
                throw new ArgumentException($"bulk 插入需要 PostgreSQL 事务，实际类型为 '{transaction.GetType()}'。", nameof(transaction));

            var copySql = BuildCopySql(context.Schema, context.TableName, context.Columns);

#if NETFRAMEWORK
            // net462 的 Npgsql 下限版本（4.1）无异步 COPY API，同步写入后返回已完成任务（取消令牌在行间检查）
            return Task.FromResult(WriteRows(npgsqlConnection, copySql, context, cancellationToken));
#else
            return InsertRowsAsync(npgsqlConnection, copySql, context, cancellationToken);
#endif
        }

#if NETFRAMEWORK
        /// <summary>
        /// 同步执行 COPY BINARY 写入（net462 的 Npgsql 下限版本无异步 COPY API，本路径为同步执行，取消令牌在行间检查）。
        /// </summary>
        /// <param name="npgsqlConnection">PostgreSQL 连接。</param>
        /// <param name="copySql">COPY 语句。</param>
        /// <param name="context">bulk 插入上下文。</param>
        /// <param name="cancellationToken">取消令牌（同步执行，在行间检查）。</param>
        /// <returns>已写入的行数。</returns>
        private static long WriteRows(NpgsqlConnection npgsqlConnection, string copySql, BulkInsertContext context, CancellationToken cancellationToken)
        {
            long rowCount = 0;
            using (var writer = npgsqlConnection.BeginBinaryImport(copySql))
            {
                foreach (var row in context.Rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    writer.StartRow();

                    foreach (var value in row)
                    {
                        if (value == null || value == DBNull.Value)
                            writer.WriteNull();
                        else
                            writer.Write(value, GetNpgsqlDbType(value.GetType()));
                    }

                    rowCount++;
                }

                writer.Complete();
            }

            return rowCount;
        }
#else
        /// <summary>
        /// 异步执行 COPY BINARY 写入。
        /// </summary>
        /// <param name="npgsqlConnection">PostgreSQL 连接。</param>
        /// <param name="copySql">COPY 语句。</param>
        /// <param name="context">bulk 插入上下文。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>已写入的行数。</returns>
        private static async Task<long> InsertRowsAsync(NpgsqlConnection npgsqlConnection, string copySql, BulkInsertContext context, CancellationToken cancellationToken)
        {
            long rowCount = 0;
            using (var writer = await npgsqlConnection.BeginBinaryImportAsync(copySql, cancellationToken).ConfigureAwait(false))
            {
                foreach (var row in context.Rows)
                {
                    await writer.StartRowAsync(cancellationToken).ConfigureAwait(false);

                    foreach (var value in row)
                    {
                        if (value == null || value == DBNull.Value)
                            await writer.WriteNullAsync(cancellationToken).ConfigureAwait(false);
                        else
                            await writer.WriteAsync(value, GetNpgsqlDbType(value.GetType()), cancellationToken).ConfigureAwait(false);
                    }

                    rowCount++;
                }

                await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
            }

            return rowCount;
        }
#endif

        /// <summary>
        /// 构建 COPY BINARY 语句（表名、列清单缩进、FROM STDIN 各占一行，与 SQL 构建器的语句格式一致），
        /// 表名与列名经 PostgreSQL 方言引用。
        /// <para>使用括号选项形式的 FORMAT BINARY：PostgreSQL 18 起不再接受旧式的裸 BINARY 选项。</para>
        /// </summary>
        /// <param name="schema">架构名，null 或空时只引用表名。</param>
        /// <param name="tableName">表名。</param>
        /// <param name="columns">列集。</param>
        /// <returns>COPY 语句。</returns>
        internal static string BuildCopySql(string schema, string tableName, IReadOnlyList<BulkInsertColumn> columns)
        {
            var dialect = NpgsqlDialect.Instance;
            var quotedColumns = new string[columns.Count];

            for (int i = 0; i < columns.Count; i++)
            {
                quotedColumns[i] = dialect.QuoteIdentifier(columns[i].ColumnName);
            }

            var sb = new StringBuilder();
            sb.AppendLine("COPY " + dialect.QuoteTableName(schema, tableName));
            sb.AppendLine($"    ({string.Join(", ", quotedColumns)})");
            sb.AppendLine("FROM STDIN (FORMAT BINARY)");
            return sb.ToString();
        }

        /// <summary>
        /// 获取 CLR 类型对应的 <see cref="NpgsqlDbType"/>（带缓存）；未映射的类型抛出异常。
        /// </summary>
        /// <param name="clrType">值的运行时类型。</param>
        /// <returns>对应的 <see cref="NpgsqlDbType"/>。</returns>
        private static NpgsqlDbType GetNpgsqlDbType(Type clrType)
        {
            var npgsqlDbType = clrTypeMapCache.GetOrAdd(clrType, MapClrType);
            if (npgsqlDbType == null)
                throw new NotSupportedException($"PostgreSQL 的 bulk 插入不支持 CLR 类型 '{clrType}'，请转换为受支持的数据库值类型。");

            return npgsqlDbType.Value;
        }

        /// <summary>
        /// CLR 类型到 <see cref="NpgsqlDbType"/> 的映射（未映射返回 null，作为缓存负值）。
        /// <para>DateTime 按不带时区的 timestamp 映射（与实体属性 DateTime 的常规建表选择一致），DateTimeOffset 按带时区的 timestamptz 映射。</para>
        /// </summary>
        /// <param name="clrType">CLR 类型。</param>
        /// <returns>对应的 <see cref="NpgsqlDbType"/>，未映射时为 null。</returns>
        private static NpgsqlDbType? MapClrType(Type clrType)
        {
            if (clrType == typeof(bool))
                return NpgsqlDbType.Boolean;

            if (clrType == typeof(byte) || clrType == typeof(short))
                return NpgsqlDbType.Smallint;

            if (clrType == typeof(int))
                return NpgsqlDbType.Integer;

            if (clrType == typeof(long))
                return NpgsqlDbType.Bigint;

            if (clrType == typeof(float))
                return NpgsqlDbType.Real;

            if (clrType == typeof(double))
                return NpgsqlDbType.Double;

            if (clrType == typeof(decimal))
                return NpgsqlDbType.Numeric;

            if (clrType == typeof(string))
                return NpgsqlDbType.Text;

            if (clrType == typeof(byte[]))
                return NpgsqlDbType.Bytea;

            if (clrType == typeof(DateTime))
                return NpgsqlDbType.Timestamp;

            if (clrType == typeof(DateTimeOffset))
                return NpgsqlDbType.TimestampTz;

            if (clrType == typeof(TimeSpan))
                return NpgsqlDbType.Interval;

            if (clrType == typeof(Guid))
                return NpgsqlDbType.Uuid;

            return null;
        }
    }
}
