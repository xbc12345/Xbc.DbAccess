using System.Data.Common;
using System.Linq.Expressions;
using System.Text;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <content>
    /// 删除操作、批量删除及删除相关 SQL 构建（仅删除主实体行，不级联删除对方实体）。
    /// </content>
    internal sealed partial class EntitySet<T>
    {
        #region 删除操作
        /// <summary>
        /// 异步删除实体（按主键匹配），只删除主实体行，不级联删除对方实体（被外键引用时删除失败）。
        /// <para>配置并发令牌列时，影响行数为 0（目标行不存在或令牌已被并发修改）抛 <see cref="DbConcurrencyException"/>；未配置时静默返回 0。</para>
        /// </summary>
        /// <param name="entity">实体。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>当前实体受影响的行数。</returns>
        public Task<int> DeleteAsync(T entity, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(entity, nameof(entity));
            ThrowIfNoPrimaryKey();

            // 只执行一次删除操作，直接删除当前实体
            return DeleteEntityAsync(entity, cancellationToken);
        }

        /// <summary>
        /// 异步批量删除实体（按主键匹配），只删除主实体行，不级联删除对方实体（被外键引用时删除失败）。
        /// <para>配置并发令牌列时退化为事务内逐条删除：任一冲突抛 <see cref="DbConcurrencyException"/> 并整体回滚。</para>
        /// </summary>
        /// <param name="entities">实体集合。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        public async Task<int> DeleteRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            var entityList = Guard.ToValidatedList(entities, nameof(entities));
            ThrowIfNoPrimaryKey();

            // 配置并发令牌时 IN 删除无法逐行校验令牌，退化为逐条删除（事务保证原子性）
            if (entityMetadata.ConcurrencyTokenColumn != null)
            {
                // 单实体无需事务：一条 DELETE 语句天然原子
                if (entityList.Count == 1)
                    return await DeleteEntityAsync(entityList[0], cancellationToken).ConfigureAwait(false);

                using (var transaction = await dbAccess.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
                {
                    // 失败路径由事务 Dispose 自动回滚，避免 Rollback 失败掩盖原始异常
                    int affectedRows = 0;
                    foreach (var entity in entityList)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        affectedRows += await DeleteEntityAsync(entity, cancellationToken).ConfigureAwait(false);
                    }

                    transaction.Commit();
                    return affectedRows;
                }
            }

            // DELETE 每行仅 1 个主键参数，单批可容纳时无需事务
            int maxRowsPerBatch = maxKeysPerInClauseBatch;
            if (entityList.Count <= maxRowsPerBatch)
                return await DeleteEntitiesAsync(entityList, cancellationToken).ConfigureAwait(false);

            // 需要分批，使用事务保证原子性
            using (var transaction = await dbAccess.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                var affectedRows = await DeleteEntitiesAsync(entityList, cancellationToken).ConfigureAwait(false);
                transaction.Commit();
                return affectedRows;
            }
        }

        /// <summary>
        /// 异步删除实体行（按主键匹配），只删除主实体行。
        /// <para>配置并发令牌列时：影响行数为 0（目标行不存在或令牌已被并发修改）抛 <see cref="DbConcurrencyException"/>；被外键引用时抛数据库原生外键约束异常。</para>
        /// </summary>
        /// <param name="entity">实体。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        private async Task<int> DeleteEntityAsync(object entity, CancellationToken cancellationToken)
        {
            var sql = BuildDeleteSql(entity, out var parameters);
            var affectedRows = await dbAccess.ExecuteNonQueryAsync(sql, parameters, cancellationToken).ConfigureAwait(false);

            // 配置了并发令牌时影响行数为 0 即并发冲突，显式抛出
            ThrowIfConcurrencyConflict(affectedRows);
            return affectedRows;
        }

        /// <summary>
        /// 异步执行批量删除，按参数数量限制自动分批。只删除主实体行，不级联删除对方实体。
        /// </summary>
        /// <param name="entities">实体列表。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        private async Task<int> DeleteEntitiesAsync(IReadOnlyList<object> entities, CancellationToken cancellationToken)
        {
            // DELETE 每行仅需 1 个主键参数，分批上限即数据库参数数量上限
            int maxRowsPerBatch = maxKeysPerInClauseBatch;
            int totalAffectedRows = 0;

            for (int i = 0; i < entities.Count; i += maxRowsPerBatch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int count = Math.Min(maxRowsPerBatch, entities.Count - i);
                var sql = BuildBatchDeleteSql(entities, i, count, out var parameters);
                totalAffectedRows += await dbAccess.ExecuteNonQueryAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
            }

            return totalAffectedRows;
        }

        /// <summary>
        /// 异步条件删除：删除满足条件的所有行，返回受影响的行数。不级联删除对方实体（被外键引用时抛数据库原生外键约束异常）。
        /// <para>条件表达式支持比较/逻辑运算、布尔列作谓词、与 null 比较（IS [NOT] NULL）、集合 Contains（IN）、
        /// 字符串 Contains/StartsWith/EndsWith（LIKE，自动转义通配符）；闭包变量一律参数化；导航属性、嵌套成员路径与算术运算抛出异常。</para>
        /// </summary>
        /// <param name="predicate">条件表达式。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        public Task<int> DeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(predicate, nameof(predicate));

            var translation = TranslatePredicate(predicate, new ParameterNameAllocator());
            var sb = new StringBuilder();
            sb.AppendLine($"DELETE FROM {quotedTableName}");
            sb.AppendLine($"WHERE {translation.WhereClause}");
            return dbAccess.ExecuteNonQueryAsync(sb.ToString(), translation.Parameters.ToArray(), cancellationToken);
        }
        #endregion

        #region DELETE 语句
        /// <summary>
        /// 构建删除实体的 SQL 语句。配置并发令牌列时，WHERE 在主键条件外附带旧令牌等值条件。
        /// </summary>
        /// <param name="entity">实体。</param>
        /// <param name="parameters">参数数组。</param>
        /// <returns></returns>
        private string BuildDeleteSql(object entity, out DbParameter[] parameters)
        {
            // 同一语句的全部参数共享分配器，主键与并发令牌互不冲突
            var allocator = new ParameterNameAllocator();

            // WHERE 条件：主键等值条件 + 令牌等值条件（配置并发令牌时）
            var conditions = new List<string>();
            var parametersList = new List<DbParameter>();
            conditions.Add(BuildPrimaryKeyEqualsClause(entity, allocator, parametersList));

            // 乐观并发：WHERE 附带旧令牌等值条件
            var tokenColumn = entityMetadata.ConcurrencyTokenColumn;
            if (tokenColumn != null)
            {
                var oldToken = GetRequiredConcurrencyToken(entity);
                conditions.Add(BuildConcurrencyTokenCondition(tokenColumn, oldToken, allocator, parametersList));
            }

            parameters = parametersList.ToArray();

            // DELETE FROM 表名 WHERE 主键 = @主键 [AND 令牌 = @令牌_Old]
            var sb = new StringBuilder();
            sb.AppendLine($"DELETE FROM {quotedTableName}");
            sb.AppendLine($"WHERE {string.Join(" AND ", conditions)}");
            return sb.ToString();
        }

        /// <summary>
        /// 构建批量删除实体的 SQL 语句（使用 IN 子句）。
        /// </summary>
        /// <param name="entities">实体列表。</param>
        /// <param name="startIndex">起始索引。</param>
        /// <param name="count">要处理的实体数量。</param>
        /// <param name="parameters">参数数组。</param>
        /// <returns>SQL 语句，如：DELETE FROM Table WHERE [Id] IN (@Id_0, @Id_1, @Id_2)</returns>
        private string BuildBatchDeleteSql(
            IReadOnlyList<object> entities,
            int startIndex,
            int count,
            out DbParameter[] parameters)
        {
            var pk = entityMetadata.PrimaryKey;
            var pkParamNames = new List<string>();
            var parametersList = new List<DbParameter>();
            var allocator = new ParameterNameAllocator();

            // 为每个实体的主键创建参数
            for (int i = 0; i < count; i++)
            {
                // 参数名以批次内行号组合（如 @Id_0）：满批次 SQL 文本完全相同，利于提供程序侧的计划缓存；
                // 列名经分配器清洗（用户列名可能含特殊字符）
                var pkValue = pk.GetValueForDb(entities[startIndex + i]);
                var pkParam = parameterBuilder.CreateParameter(allocator.Allocate($"{pk.ColumnName}_{i}"), pkValue, pk.DbType);
                pkParamNames.Add(pkParam.ParameterName);
                parametersList.Add(pkParam);
            }

            // 转为数组，调用 ExecuteNonQueryAsync 时需要
            parameters = parametersList.ToArray();

            // DELETE FROM 表名 WHERE 主键 IN (...)
            var sb = new StringBuilder();
            sb.AppendLine($"DELETE FROM {quotedTableName}");
            sb.AppendLine($"WHERE {quotedPrimaryKeyColumnName} IN ({string.Join(", ", pkParamNames)})");
            return sb.ToString();
        }
        #endregion
    }
}
