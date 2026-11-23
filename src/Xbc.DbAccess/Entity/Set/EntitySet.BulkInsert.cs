namespace Xbc.DbAccess.Entity
{
    /// <content>
    /// 实体版 bulk 插入操作：构建写入上下文与导航同步等实体逻辑，执行部分（事务、原生路径连接解包、回退路径分批）
    /// 委托给 <see cref="DbAccessBulkInsertExtensions.BulkInsertAsync(IDbAccess, BulkInsertContext, BulkInsertOptions, CancellationToken)"/>。
    /// </content>
    internal sealed partial class EntitySet<T>
    {
        /// <summary>
        /// 异步 bulk 插入实体，只插入主表行。方言有原生批量写入 API 时走原生路径，否则回退多值 INSERT（执行委托给
        /// <see cref="DbAccessBulkInsertExtensions.BulkInsertAsync(IDbAccess, BulkInsertContext, BulkInsertOptions, CancellationToken)"/>）。
        /// <para>自增列被忽略且不回填生成的 ID；全部列为自增列的退化场景抛出异常。</para>
        /// <para>导航属性同步与 <see cref="InsertRangeAsync"/> 一致（1:1 前置、1:N 后置，均不递归写入）。</para>
        /// <para>整批在事务中执行（已有活动事务时自动嵌套为保存点子事务）；<paramref name="options"/> 仅原生路径生效，回退路径静默忽略。</para>
        /// </summary>
        /// <param name="entities">实体集合。</param>
        /// <param name="options">bulk 插入选项，null 时使用方言默认值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>已插入的行数。</returns>
        /// <exception cref="ArgumentException"><paramref name="entities"/> 为 null、空集合或包含 null 元素，或选项值非法。</exception>
        public async Task<long> BulkInsertAsync(
            IEnumerable<T> entities,
            BulkInsertOptions options = null,
            CancellationToken cancellationToken = default)
        {
            var entityList = Guard.ToValidatedList(entities, nameof(entities));

            // 写入前逐实体前置：初始并发令牌（幂等）与 1:1 引用外键同步
            foreach (var entity in entityList)
            {
                EnsureConcurrencyToken(entity);
                SyncOneToOneForeignKeys(entity);
            }

            // 构建上下文（行值延迟枚举，利于大集合流式写入），事务与执行由扩展方法负责
            var context = BuildBulkInsertContext(entityList);

            // 有原生批量写入 API 的方言（SQL Server SqlBulkCopy、MySQL MySqlBulkCopy、PostgreSQL COPY、Oracle ArrayBind）走原生路径，
            // 其余回退多值 INSERT；原生 API 均无法返回逐行自增 ID（自增列不回填），部分原生路径（如 SQL Server）
            // 默认不校验 CHECK/外键约束、不触发触发器，NULL 值按字面写入（KeepNulls）
            var affectedRows = await dbAccess.BulkInsertAsync(context, options, cancellationToken).ConfigureAwait(false);

            // 插入完成后同步 1:N 集合导航（自增列不回填，本方主键值有效时才发生实际回填）
            foreach (var entity in entityList)
            {
                SyncOneToManyForeignKeys(entity);
            }

            return affectedRows;
        }

        /// <summary>
        /// 构建 bulk 插入上下文（列集为全部非自增列，CLR 类型已归一：可空值类型取基础类型、枚举取底层整数类型）。
        /// </summary>
        /// <param name="entityList">实体列表。</param>
        /// <returns>bulk 插入上下文。</returns>
        private BulkInsertContext BuildBulkInsertContext(IReadOnlyList<T> entityList)
        {
            // 全部非自增列（bulk 插入语义：自增列被忽略且不回填）
            var columnMetadataList = entityMetadata.Columns.Where(c => !c.IsIdentity).ToList();

            // 全部为自增列的退化场景无法生成合法的插入，显式失败
            if (columnMetadataList.Count == 0)
                throw new NotSupportedException($"实体表 '{entityMetadata.TableName}' 没有可插入的列（全部为自增列），无法执行 bulk 插入。");

            var columns = new List<BulkInsertColumn>(columnMetadataList.Count);
            foreach (var column in columnMetadataList)
            {
                // 行值转换为数据库值（枚举转整数/名称字符串、JSON 转文本、null 转 DBNull），CLR 类型做相同归一对齐
                var clrType = column.UnderlyingType ?? column.PropertyType;
                if (column.IsJson || column.EnumAsString)
                    clrType = typeof(string);
                else if (clrType.IsEnum)
                    clrType = Enum.GetUnderlyingType(clrType);

                columns.Add(new BulkInsertColumn(column.ColumnName, clrType));
            }

            // 行值延迟枚举：执行时才逐行取值转换，构建上下文本身不遍历实体
            IEnumerable<object[]> EnumerateRows()
            {
                foreach (var entity in entityList)
                {
                    var row = new object[columnMetadataList.Count];
                    for (int i = 0; i < columnMetadataList.Count; i++)
                    {
                        // null 统一为 DBNull（直连路径下游无 CreateParameter 的归一保护）
                        row[i] = columnMetadataList[i].GetValueForDb(entity) ?? DBNull.Value;
                    }

                    yield return row;
                }
            }

            return new BulkInsertContext(entityMetadata.Schema, entityMetadata.TableName, columns, EnumerateRows());
        }
    }
}
