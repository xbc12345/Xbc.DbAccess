using System.Data.Common;
using System.Linq.Expressions;
using System.Text;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <content>
    /// 更新操作及更新相关 SQL 构建（仅更新主实体行，导航属性仅做外键内存同步，不级联写入对方实体）。
    /// </content>
    internal sealed partial class EntitySet<T>
    {
        #region 更新操作
        /// <summary>
        /// 异步更新实体（按主键匹配），只更新主实体行（含 1:1 外键列）。
        /// <para><b>注意：</b>更新不做变更追踪，除主键与自增列外的所有列都会进入 SET 子句，实体须为完整查询加载的完整实体，部分投影或手工构造的实体会以默认值覆盖数据库原值。</para>
        /// <para>1:1 引用外键同步在写入前执行，1:N 集合外键同步在写入后执行，均纯内存同步，不自动更新对方实体。</para>
        /// </summary>
        /// <param name="entity">要更新的实体。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>当前实体受影响的行数。</returns>
        public Task<int> UpdateAsync(T entity, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(entity, nameof(entity));
            ThrowIfNoPrimaryKey();

            return UpdateEntityAsync(entity, cancellationToken);
        }

        /// <summary>
        /// 异步批量更新实体（按主键匹配），只更新主实体行，事务包裹保证原子性。
        /// <para><b>注意：</b>与 <see cref="UpdateAsync(T, CancellationToken)"/> 一致，不做变更追踪、全列 SET，实体须为完整查询加载的完整实体。</para>
        /// <para>任一实体更新失败（含并发冲突）时整体回滚，且已成功更新的实体恢复旧并发令牌，保持内存状态与回滚后的数据库一致。</para>
        /// </summary>
        /// <param name="entities">实体集合。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        public async Task<int> UpdateRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            var entityList = Guard.ToValidatedList(entities, nameof(entities));
            ThrowIfNoPrimaryKey();

            if (entityList.Count == 1)
                return await UpdateEntityAsync(entityList[0], cancellationToken).ConfigureAwait(false);

            // 使用事务包裹更新，确保数据一致性
            using (var transaction = await dbAccess.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                int affectedRows = 0;

                // 记录各实体旧令牌：任一失败整体回滚后恢复，避免已回填的新令牌与数据库旧令牌不一致导致下次误报冲突
                var tokenColumn = entityMetadata.ConcurrencyTokenColumn;
                var tokenRollbacks = tokenColumn != null ? new List<KeyValuePair<object, byte[]>>() : null;

                try
                {
                    foreach (var entity in entityList)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (tokenColumn != null)
                            tokenRollbacks.Add(new KeyValuePair<object, byte[]>(entity, GetRequiredConcurrencyToken(entity)));

                        affectedRows += await UpdateEntityAsync(entity, cancellationToken).ConfigureAwait(false);
                    }

                    transaction.Commit();
                    return affectedRows;
                }
                catch
                {
                    // 事务由 Dispose 回滚：恢复已处理实体的旧令牌，使内存状态与回滚后的数据库一致
                    if (tokenColumn != null)
                    {
                        // 倒序回放：同一实体重复出现时使首次记录的原始令牌最后落地，终态为数据库回滚后的真实令牌
                        for (int i = tokenRollbacks.Count - 1; i >= 0; i--)
                        {
                            var rollback = tokenRollbacks[i];
                            tokenColumn.SetValue(rollback.Key, rollback.Value);
                        }
                    }

                    throw;
                }
            }
        }

        /// <summary>
        /// 更新单个实体（无事务职责，由调用方保证）。全列 SET，要求完整实体（详见 <see cref="UpdateAsync(T, CancellationToken)"/>）。
        /// <para>配置并发令牌列时：SET 换发新令牌并在成功后回填，WHERE 附带旧令牌等值条件；影响行数为 0 抛 <see cref="DbConcurrencyException"/>。</para>
        /// </summary>
        /// <param name="entity">实体。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        private async Task<int> UpdateEntityAsync(object entity, CancellationToken cancellationToken)
        {
            // 1:1 引用外键同步须在构建 SQL 前执行
            SyncOneToOneForeignKeys(entity);

            // 生新令牌 → 进 SQL → 成功后回填
            var tokenColumn = entityMetadata.ConcurrencyTokenColumn;
            byte[] newToken = tokenColumn != null ? GenerateConcurrencyToken() : null;

            var sql = BuildUpdateSql(entity, newToken, out var parameters);
            var affectedRows = await dbAccess.ExecuteNonQueryAsync(sql, parameters, cancellationToken).ConfigureAwait(false);

            // 配置了并发令牌时影响行数为 0 即并发冲突，显式抛出
            ThrowIfConcurrencyConflict(affectedRows);

            // 更新成功后回填新令牌（失败时实体保留旧令牌）
            if (newToken != null)
                tokenColumn.SetValue(entity, newToken);

            // 更新完成后同步 1:N 集合导航外键（本方主键不被 UPDATE 修改，失败时不污染对方实体内存状态）
            SyncOneToManyForeignKeys(entity);
            return affectedRows;
        }
        #endregion

        #region 按条件更新
        /// <summary>
        /// 异步条件更新：将满足条件的所有行的指定列更新为赋值表达式的值，返回受影响的行数。
        /// <para>赋值表达式支持对象初始化器与匿名类型（<c>x => new { Age = 18 }</c>）；赋值到主键列、标识列、并发令牌列、
        /// 导航属性或重复赋值同一列时抛出异常；匿名类型成员值的类型正确性由调用方保证。</para>
        /// <para>条件表达式支持比较/逻辑运算、布尔列作谓词、与 null 比较（IS [NOT] NULL）、集合 Contains（IN）、
        /// 字符串 Contains/StartsWith/EndsWith（LIKE，自动转义通配符）；导航属性、嵌套成员路径与算术运算抛出异常。</para>
        /// <para>配置并发令牌列时：SET 中换发新随机令牌（无实体引用不回填）；令牌校验不参与条件更新（WHERE 由调用方谓词定义）。</para>
        /// </summary>
        /// <typeparam name="TValues">赋值载体类型（匿名类型或实体类型）。</typeparam>
        /// <param name="assignment">赋值表达式，如 <c>x => new { Age = 18 }</c>。</param>
        /// <param name="predicate">条件表达式，如 <c>x => x.Name == "Alice"</c>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        public Task<int> UpdateAsync<TValues>(
            Expression<Func<T, TValues>> assignment,
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(assignment, nameof(assignment));
            Guard.ThrowIfNull(predicate, nameof(predicate));

            // SET 与 WHERE 共享参数名分配器，避免同名列参数重名
            var allocator = new ParameterNameAllocator();
            var sets = SetClauseTranslator.Translate(assignment, entityMetadata);

            var assignments = new List<string>(sets.Count);
            var parameters = new List<DbParameter>(sets.Count);

            foreach (var set in sets)
            {
                var name = allocator.Allocate(set.Column.ColumnName);
                assignments.Add($"{sqlDialect.QuoteIdentifier(set.Column.ColumnName)} = {parameterBuilder.GetParameterName(name)}");
                parameters.Add(parameterBuilder.CreateParameter(name, set.DbValue, set.Column.DbType));
            }

            // 乐观并发：条件更新同样换发新令牌（SET 令牌列 = 新随机值），无实体引用不回填，
            // 旧令牌失效保证持有旧令牌的实体在后续更新中检出冲突
            var tokenColumn = entityMetadata.ConcurrencyTokenColumn;
            if (tokenColumn != null)
            {
                var tokenName = allocator.Allocate(tokenColumn.ColumnName);
                assignments.Add($"{sqlDialect.QuoteIdentifier(tokenColumn.ColumnName)} = {parameterBuilder.GetParameterName(tokenName)}");
                parameters.Add(parameterBuilder.CreateParameter(tokenName, GenerateConcurrencyToken(), tokenColumn.DbType));
            }

            var translation = TranslatePredicate(predicate, allocator);
            parameters.AddRange(translation.Parameters);

            var sb = new StringBuilder();
            sb.AppendLine($"UPDATE {quotedTableName}");
            sb.AppendLine("SET");
            sb.AppendLine($"    {string.Join($",{Environment.NewLine}    ", assignments)}");
            sb.AppendLine("WHERE");
            sb.AppendLine($"    {translation.WhereClause}");
            return dbAccess.ExecuteNonQueryAsync(sb.ToString(), parameters.ToArray(), cancellationToken);
        }
        #endregion

        #region UPDATE 语句
        /// <summary>
        /// 构建更新实体的 SQL 语句。<paramref name="newConcurrencyToken"/> 非 null 时：SET 写入新令牌，WHERE 附带旧令牌等值条件。
        /// </summary>
        /// <param name="entity">实体。</param>
        /// <param name="newConcurrencyToken">调用方生成的新并发令牌；未配置令牌列时传 null（令牌子句均不生成）。</param>
        /// <param name="parameters">参数数组。</param>
        /// <returns></returns>
        private string BuildUpdateSql(object entity, byte[] newConcurrencyToken, out DbParameter[] parameters)
        {
            // 同一语句的全部参数共享分配器，SET 列、令牌列、WHERE 主键互不冲突
            var allocator = new ParameterNameAllocator();
            var sets = new List<string>();
            var conditions = new List<string>();
            var parametersList = new List<DbParameter>();

            foreach (var column in entityMetadata.Columns)
            {
                // 跳过主键、标识列与并发令牌列；其余列全部进入 SET 子句（无变更追踪，未加载的列以默认值写入）
                if (column.IsPrimaryKey || column.IsIdentity || column.IsConcurrencyToken)
                    continue;

                var dbValue = column.GetValueForDb(entity);
                var dbParameter = parameterBuilder.CreateParameter(allocator.Allocate(column.ColumnName), dbValue, column.DbType);

                // 例如：[ColumnName] = @ColumnName
                sets.Add($"{sqlDialect.QuoteIdentifier(column.ColumnName)} = {dbParameter.ParameterName}");
                parametersList.Add(dbParameter);
            }

            // WHERE 条件：主键等值条件 + 令牌等值条件（配置并发令牌时）
            conditions.Add(BuildPrimaryKeyEqualsClause(entity, allocator, parametersList));

            // 乐观并发：旧令牌进 WHERE，新令牌进 SET
            if (newConcurrencyToken != null)
            {
                var tokenColumn = entityMetadata.ConcurrencyTokenColumn;
                var oldToken = GetRequiredConcurrencyToken(entity);

                sets.Add(BuildConcurrencyTokenSetClause(tokenColumn, newConcurrencyToken, allocator, parametersList));
                conditions.Add(BuildConcurrencyTokenCondition(tokenColumn, oldToken, allocator, parametersList));
            }

            // 全部为主键、自增列或令牌列的退化场景无法生成合法的 UPDATE 语句，显式失败
            if (sets.Count == 0)
                throw new NotSupportedException($"实体表 '{entityMetadata.TableName}' 没有可更新的列（全部为主键或自增列），无法构建 UPDATE 语句。");

            parameters = parametersList.ToArray();

            var sb = new StringBuilder();
            sb.AppendLine($"UPDATE {quotedTableName}");
            sb.AppendLine("SET");
            sb.AppendLine($"    {string.Join($",{Environment.NewLine}    ", sets)}");
            sb.AppendLine("WHERE");
            sb.AppendLine($"    {string.Join(" AND ", conditions)}");
            return sb.ToString();
        }
        #endregion
    }
}
