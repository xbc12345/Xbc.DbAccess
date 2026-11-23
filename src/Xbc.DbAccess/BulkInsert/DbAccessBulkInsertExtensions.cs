using System.Data.Common;
using System.Text;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 为 <see cref="IDbAccess"/> 提供 bulk 插入扩展方法：不经实体层，直接按 <see cref="BulkInsertContext"/> 描述的目标表、列集与行数据批量写入。
    /// </summary>
    public static class DbAccessBulkInsertExtensions
    {
        /// <summary>
        /// 异步 bulk 插入行数据。方言有原生批量写入 API 时走原生路径，否则回退批量 INSERT（多值 INSERT 自动分批，或事务内逐条 + 命令复用）。
        /// <para>自增列不回填生成的 ID；行值须已转换为数据库值（null 统一为 <see cref="DBNull"/>），数组长度与列集一一对应。
        /// 整批在事务中执行（已有活动事务时嵌套为保存点子事务）；<paramref name="options"/> 仅原生路径生效。</para>
        /// <para>原生路径行为边界随方言不同：如 SQL Server 的 SqlBulkCopy 默认不校验 CHECK/FK 约束、不触发触发器，
        /// PostgreSQL 的 COPY 强制校验约束，跨方言迁移须留意。</para>
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="context">bulk 插入上下文（目标表、列集与行数据）。</param>
        /// <param name="options">bulk 插入选项，null 时使用方言默认值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>已插入的行数。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="dbAccess"/> 或 <paramref name="context"/> 为 null。</exception>
        /// <exception cref="ArgumentException"><paramref name="options"/> 的值非法，或行数据的数组长度与目标列数不一致。</exception>
        public static Task<long> BulkInsertAsync(
            this IDbAccess dbAccess,
            BulkInsertContext context,
            BulkInsertOptions options = null,
            CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            Guard.ThrowIfNull(context, nameof(context));
            ValidateBulkInsertOptions(options);

            // 统一行长度校验（惰性，保持流式枚举，带行号）：三条路径共用此单点契约——
            // 原生 SqlBulkCopy/COPY 不物化行数据、下游无行校验，短行/长行只会以驱动级隐晦错误暴露
            context = new BulkInsertContext(
                context.Schema,
                context.TableName,
                context.Columns,
                WithRowLengthValidation(context.Rows, context.Columns.Count));

            var inserter = dbAccess.ProviderFactory.BulkInserter;
            if (inserter != null)
                return InsertWithNativeInserterAsync(dbAccess, inserter, context, options, cancellationToken);

            // 回退路径：方言启用逐条策略（如 SQLite）时逐条执行，否则多值 INSERT 自动分批
            if (dbAccess.ProviderFactory.SqlDialect.PreferRowByRowBatchInsert)
                return InsertRowByRowAsync(dbAccess, context, cancellationToken);

            return InsertWithMultiValueAsync(dbAccess, context, cancellationToken);
        }

        /// <summary>
        /// 校验 bulk 插入选项值的合法性。
        /// </summary>
        /// <param name="options">bulk 插入选项，null 时跳过校验。</param>
        private static void ValidateBulkInsertOptions(BulkInsertOptions options)
        {
            if (options == null)
                return;

            if (options.BatchSize != null && options.BatchSize.Value <= 0)
                throw new ArgumentException("bulk 插入选项的批量大小必须为正数。", nameof(options));

            if (options.CommandTimeout != null && options.CommandTimeout.Value < 0)
                throw new ArgumentException("bulk 插入选项的命令超时时间不能为负数。", nameof(options));
        }

        /// <summary>
        /// 以惰性枚举逐行校验行数组长度与目标列数一致，异常消息携带行号（延迟枚举不破坏流式写入）。
        /// </summary>
        private static IEnumerable<object[]> WithRowLengthValidation(IEnumerable<object[]> rows, int columnCount)
        {
            int rowIndex = 0;
            foreach (var row in rows)
            {
                if (row.Length != columnCount)
                    throw new ArgumentException($"bulk 插入的第 {rowIndex} 行数据包含 {row.Length} 个值，与目标列数 {columnCount} 不一致。", nameof(rows));

                rowIndex++;
                yield return row;
            }
        }

        #region BulkInsert
        /// <summary>
        /// 原生路径执行：开启事务后从框架事务包装器解包出真实提供程序的连接与事务，调用原生 bulk 插入器写入并提交。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="inserter">原生 bulk 插入器。</param>
        /// <param name="context">bulk 插入上下文。</param>
        /// <param name="options">bulk 插入选项。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>已写入的行数。</returns>
        private static async Task<long> InsertWithNativeInserterAsync(
            IDbAccess dbAccess,
            IDbBulkInserter inserter,
            BulkInsertContext context,
            BulkInsertOptions options,
            CancellationToken cancellationToken)
        {
            using (var transaction = await dbAccess.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                // 原生 API 需要真实提供程序的连接与事务（框架事务为包装器），从包装器取内部实例；
                // 连接是框架的连接包装器时还需再解一层拿到原生提供程序连接（如 SqlConnection/NpgsqlConnection）
                var transactionWrapper = transaction as DbTransactionWrapper;
                if (transactionWrapper == null)
                    throw new NotSupportedException($"bulk 插入需要框架事务包装器以获取内部连接与事务，实际类型为 '{transaction.GetType()}'。");

                var innerConnection = transactionWrapper.InnerConnection;
                if (innerConnection is DbConnectionWrapper connectionWrapper)
                    innerConnection = connectionWrapper.UnderlyingConnection;

                var affectedRows = await inserter.InsertAsync(
                    innerConnection,
                    transactionWrapper.InnerTransaction,
                    context,
                    options,
                    cancellationToken).ConfigureAwait(false);
                transaction.Commit();
                return affectedRows;
            }
        }
        #endregion

        #region 逐条 Insert
        /// <summary>
        /// 回退路径执行（逐条策略）：单行 INSERT 在事务内逐条执行，经可复用命令复用同一命令、仅重绑参数值。
        /// SQL 与参数对象只构建一次；行数据延迟枚举，大集合流式写入。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="context">bulk 插入上下文。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>已插入的行数。</returns>
        private static async Task<long> InsertRowByRowAsync(
            IDbAccess dbAccess,
            BulkInsertContext context,
            CancellationToken cancellationToken)
        {
            var sqlDialect = dbAccess.ProviderFactory.SqlDialect;
            var parameterBuilder = dbAccess.ProviderFactory.ParameterBuilder;
            var allocator = new ParameterNameAllocator();

            // 引用列集与参数名对整个批量固定，构建一次复用；参数对象同样只创建一次（后续行仅更新参数值）
            var quotedColumns = new List<string>(context.Columns.Count);
            var parameterNames = new List<string>(context.Columns.Count);
            var parameters = new DbParameter[context.Columns.Count];

            for (int c = 0; c < context.Columns.Count; c++)
            {
                var parameter = parameterBuilder.CreateParameter(allocator.Allocate(context.Columns[c].ColumnName), null);
                quotedColumns.Add(sqlDialect.QuoteIdentifier(context.Columns[c].ColumnName));
                parameterNames.Add(parameter.ParameterName);
                parameters[c] = parameter;
            }

            var sql = new StringBuilder()
                .AppendLine($"INSERT INTO {sqlDialect.QuoteTableName(context.Schema, context.TableName)}")
                .AppendLine($"    ({string.Join(", ", quotedColumns)})")
                .AppendLine("VALUES")
                .AppendLine($"    ({string.Join(", ", parameterNames)})")
                .ToString();

            long totalAffectedRows = 0;

            using (var transaction = await dbAccess.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            using (var insertCommand = ((DbAccess)dbAccess).CreateReusableCommand())
            {
                foreach (var row in context.Rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 行值已是数据库值，重绑时兜底 null → DBNull（与实体层逐条路径一致）
                    for (int c = 0; c < row.Length; c++)
                    {
                        parameters[c].Value = row[c] ?? DBNull.Value;
                    }

                    totalAffectedRows += await insertCommand.Owner.ExecuteNonQueryAsync(insertCommand, sql, parameters, cancellationToken).ConfigureAwait(false);
                }

                transaction.Commit();
            }

            return totalAffectedRows;
        }
        #endregion

        #region 多值 Insert
        /// <summary>
        /// 回退路径执行：生成多值 INSERT，按方言参数数量与单语句 VALUES 行数上限自动分批，整批在事务中执行。
        /// 行数据延迟枚举，大集合流式写入。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="context">bulk 插入上下文。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>已插入的行数。</returns>
        private static async Task<long> InsertWithMultiValueAsync(
            IDbAccess dbAccess,
            BulkInsertContext context,
            CancellationToken cancellationToken)
        {
            var sqlDialect = dbAccess.ProviderFactory.SqlDialect;

            // 引用列集对整个批量固定，构建一次供所有批次复用
            var quotedColumns = new List<string>(context.Columns.Count);
            foreach (var column in context.Columns)
            {
                quotedColumns.Add(sqlDialect.QuoteIdentifier(column.ColumnName));
            }

            // 每批最大行数公式与实体插入的分批决策共用
            var maxRowsPerBatch = sqlDialect.GetMaxRowsPerInsertBatch(context.Columns.Count);
            long totalAffectedRows = 0;

            using (var transaction = await dbAccess.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                // 缓冲区容量设上限，避免异常巨大的批次一次性分配过量内存
                var batchRows = new List<object[]>(maxRowsPerBatch < 4096 ? maxRowsPerBatch : 4096);
                foreach (var row in context.Rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    batchRows.Add(row);

                    if (batchRows.Count < maxRowsPerBatch)
                        continue;

                    totalAffectedRows += await ExecuteMultiValueBatchAsync(dbAccess, context, quotedColumns, batchRows, cancellationToken).ConfigureAwait(false);
                    batchRows.Clear();
                }

                // 尾批可能不足每批最大行数，循环结束后单独补执行
                if (batchRows.Count > 0)
                    totalAffectedRows += await ExecuteMultiValueBatchAsync(dbAccess, context, quotedColumns, batchRows, cancellationToken).ConfigureAwait(false);

                transaction.Commit();
            }

            return totalAffectedRows;
        }

        /// <summary>
        /// 执行单个批次的多值 INSERT：参数名带批内行索引避免冲突（形如 <c>@列_0</c>、<c>@列_1</c>），经分配器防撞名。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="context">bulk 插入上下文。</param>
        /// <param name="quotedColumns">引用列名列表（调用方构建一次复用）。</param>
        /// <param name="batchRows">批次行数据。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>本批次受影响的行数。</returns>
        private static Task<int> ExecuteMultiValueBatchAsync(
            IDbAccess dbAccess,
            BulkInsertContext context,
            List<string> quotedColumns,
            List<object[]> batchRows,
            CancellationToken cancellationToken)
        {
            var sqlDialect = dbAccess.ProviderFactory.SqlDialect;
            var parameterBuilder = dbAccess.ProviderFactory.ParameterBuilder;
            var allocator = new ParameterNameAllocator();
            var parametersList = new List<DbParameter>(batchRows.Count * context.Columns.Count);

            // 多值批次的 SQL 构建与实体层的 EntitySet.BuildBatchInsertSql 平行：本层仅有列名（BulkInsertColumn 无 DbType 元数据，参数不带 DbType），
            // 实体层列集含元数据、参数带列的 DbType；两处语句格式须保持一致
            var valueGroups = new List<string>(batchRows.Count);
            for (int i = 0; i < batchRows.Count; i++)
            {
                var row = batchRows[i];
                var values = new List<string>(context.Columns.Count);

                for (int c = 0; c < context.Columns.Count; c++)
                {
                    // 带批内行索引的参数名经分配器防撞（如恰好存在名为 Name_0 的列时自动让位）
                    var dbParameter = parameterBuilder.CreateParameter(allocator.Allocate(context.Columns[c].ColumnName + "_" + i), row[c]);
                    values.Add(dbParameter.ParameterName);
                    parametersList.Add(dbParameter);
                }

                valueGroups.Add($"({string.Join(", ", values)})");
            }

            // INSERT INTO 表名 (列名, 列名, ...)
            // VALUES
            //     (值, 值),
            //     (值, 值)
            // ...
            var sb = new StringBuilder();
            sb.AppendLine($"INSERT INTO {sqlDialect.QuoteTableName(context.Schema, context.TableName)}");
            sb.AppendLine($"    ({string.Join(", ", quotedColumns)})");
            sb.AppendLine("VALUES");
            // 每行独占一行，大批次时 SQL 更易读
            sb.AppendLine($"    {string.Join($",{Environment.NewLine}    ", valueGroups)}");
            return dbAccess.ExecuteNonQueryAsync(sb.ToString(), parametersList.ToArray(), cancellationToken);
        }
        #endregion
    }
}
