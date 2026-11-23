using System.Data.Common;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <content>
    /// Upsert 操作（存在则更新，不存在则插入）及 Upsert 语句构建（仅处理主表行，不级联写入导航属性）。
    /// <para>事务按需开启：自增主键短路复用插入路径共享方法 <see cref="EntitySet{T}.InsertSingleWithIdentityAsync"/>，非主键自增列的按主键回读由 <see cref="UpsertAsync"/> 以事务包裹。</para>
    /// </content>
    internal sealed partial class EntitySet<T>
    {
        #region Upsert 操作
        /// <summary>
        /// 异步 Upsert 实体（存在则更新，不存在则插入），只处理主表行。冲突键为实体主键，实体没有主键时抛出异常。
        /// <para>更新列覆盖除主键、自增列外的全部列（不做变更追踪），调用方需自行避免覆盖并发修改。</para>
        /// <para>自增主键为默认值时必走插入路径并回填 ID，取回机制与 <see cref="InsertAsync"/> 一致；
        /// 自增列非主键且为默认值时，部分方言在同事务内按主键回读回填，其余方言单语句取回。</para>
        /// <para>1:1 引用外键同步在写入前执行，1:N 集合外键同步在写入后执行（与 <see cref="InsertAsync"/> 一致）。</para>
        /// <para>配置并发令牌列时：每次 Upsert 换发新令牌写入（插入路径成为新行初始令牌，更新路径使旧令牌失效）；
        /// Upsert 本身不做令牌校验（冲突键恒为主键），失败时恢复实体的旧令牌。</para>
        /// </summary>
        /// <param name="entity">要插入或更新的实体。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数（MySQL 更新路径返回 2，其余方言为 1）。</returns>
        public async Task<int> UpsertAsync(T entity, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(entity, nameof(entity));
            ThrowIfNoPrimaryKey();

            // 1:1 引用外键同步
            SyncOneToOneForeignKeys(entity);

            // 乐观并发令牌换发（无令牌列跳过）：总是生成新值设到实体上，构建语句时自然进入插入列（即更新列），
            // 插入路径成为新行初始令牌、更新路径使旧令牌失效；失败时恢复旧令牌（首次调用可能为 null）
            var tokenColumn = entityMetadata.ConcurrencyTokenColumn;
            byte[] oldToken = null;
            if (tokenColumn != null)
            {
                oldToken = tokenColumn.GetValue(entity) as byte[];
                tokenColumn.SetValue(entity, GenerateConcurrencyToken());
            }

            try
            {
                var affectedRows = await UpsertCoreAsync(entity, cancellationToken).ConfigureAwait(false);

                // 写入完成后本方主键值已有效，同步 1:N 集合导航外键
                SyncOneToManyForeignKeys(entity);
                return affectedRows;
            }
            catch
            {
                // 语句失败：数据库未变，恢复旧令牌保持内存状态一致
                tokenColumn?.SetValue(entity, oldToken);
                throw;
            }
        }

        /// <summary>
        /// Upsert 核心执行（令牌已由调用方换发到实体，构建语句时自然取新值）：按自增列形态选择执行路径。
        /// </summary>
        /// <param name="entity">要插入或更新的实体。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        private async Task<int> UpsertCoreAsync(T entity, CancellationToken cancellationToken)
        {
            int affectedRows;
            var identityColumn = entityMetadata.IdentityColumn;

            // 无自增列，或自增列带显式值（按值写入、参与冲突判定，无需回填）：单语句直接执行
            if (identityColumn == null || !identityColumn.IsDefaultValue(entity))
            {
                var sql = BuildUpsertSql(entity, out var parameters);
                affectedRows = await dbAccess.ExecuteNonQueryAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
            }
            else if (identityColumn.IsPrimaryKey)
            {
                // 自增主键为默认值：主键缺席冲突判定，必走插入路径，复用插入路径的取回机制
                // （含 Oracle 普通 INSERT 的 RETURNING INTO，规避 MERGE 的取回限制）
                affectedRows = await InsertSingleWithIdentityAsync(entity, identityColumn, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // 自增列非主键且需回填：按方言能力（结合引擎版本）选择自增 ID 取回方式
                var serverInfo = await dbAccess.Factory.GetServerInfoAsync().ConfigureAwait(false);
                var mode = sqlDialect.GetInsertIdentityMode(serverInfo);

                if (mode == InsertIdentityMode.OutputClause)
                {
                    // RETURNING/MERGE OUTPUT 以结果集单语句取回，无需事务
                    var sql = BuildUpsertSql(entity, out var parameters, retrieveIdentity: true);
                    var id = await dbAccess.ExecuteScalarAsync(sql, parameters, cancellationToken).ConfigureAwait(false);

                    // 防御性判断：通常插入失败会立即抛出异常，不会走到这里
                    if (id == null || id == DBNull.Value)
                        throw new InvalidOperationException($"Upsert 实体到表 '{entityMetadata.TableName}' 后未能获取自增 ID。");

                    identityColumn.BackfillIdentity(entity, id);
                    affectedRows = 1;
                }
                else
                {
                    // Oracle（MERGE 不支持 RETURNING INTO）或 MySQL（ON DUPLICATE KEY UPDATE 无取回子句）：
                    // 事务包裹 Upsert 与按主键回读（行锁保护下回读期间行不会被并发修改），多一次回读往返
                    using (var transaction = await dbAccess.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
                    {
                        // 失败路径由事务 Dispose 自动回滚，避免 Rollback 失败掩盖原始异常
                        var sql = BuildUpsertSql(entity, out var parameters);
                        affectedRows = await dbAccess.ExecuteNonQueryAsync(sql, parameters, cancellationToken).ConfigureAwait(false);

                        await BackfillIdentityBySelectAsync(entity, identityColumn, cancellationToken).ConfigureAwait(false);
                        transaction.Commit();
                    }
                }
            }

            return affectedRows;
        }

        /// <summary>
        /// 以按主键回读方式补齐 Upsert 后的自增列回填，用于无取回子句的方言。
        /// <para>插入路径取新生成值，更新路径取既有值。</para>
        /// <para>由调用方开事务包裹 Upsert 与本方法：未提交前本方持有该行排他锁，回读期间行不会被并发修改；
        /// 回读结果为空视为异常状态并抛出。</para>
        /// </summary>
        /// <param name="entity">实体。</param>
        /// <param name="identityColumn">自增列元数据。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task BackfillIdentityBySelectAsync(T entity, ColumnMetadata identityColumn, CancellationToken cancellationToken)
        {
            // 连接级取回（如 MySQL 的 LAST_INSERT_ID）在更新路径会返回旧值，不可用，按主键回读取当前行值
            var parametersList = new List<DbParameter>();
            var whereClause = BuildPrimaryKeyEqualsClause(entity, new ParameterNameAllocator(), parametersList);

            var sql = sqlBuilder.BuildSelectSql(
                quotedTableName,
                new List<string> { sqlDialect.QuoteIdentifier(identityColumn.ColumnName) },
                whereClause);

            var id = await dbAccess.ExecuteScalarAsync(sql, parametersList.ToArray(), cancellationToken).ConfigureAwait(false);

            // 防御性判断：行锁保护下回读为空属于异常状态
            if (id == null || id == DBNull.Value)
                throw new InvalidOperationException($"Upsert 实体到表 '{entityMetadata.TableName}' 后未能获取自增 ID。");

            identityColumn.BackfillIdentity(entity, id);
        }
        #endregion

        #region Upsert 语句
        /// <summary>
        /// 构建 Upsert 实体的 SQL 语句（冲突键为主键）。自增列为默认值时排除（由数据库生成）；带显式值时包含、参与冲突判定但不参与更新。
        /// </summary>
        /// <param name="entity">实体。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="retrieveIdentity">是否构建自增 ID 取回上下文（仅支持结果集取回的方言生效），仅取回路径传 true。</param>
        /// <returns></returns>
        private string BuildUpsertSql(
            object entity,
            out DbParameter[] parameters,
            bool retrieveIdentity = false)
        {
            ThrowIfNoPrimaryKey();

            // 语句内参数名统一经分配器分配（清理与防撞）
            var allocator = new ParameterNameAllocator();
            var columns = new List<string>();
            var values = new List<string>();
            var parametersList = new List<DbParameter>();

            // 自增列带显式值时进入列清单（按值写入），进入显式写入上下文
            bool identityExplicit = false;

            // 除冲突键（主键）与自增列外的插入列即更新列：自增列带显式值时参与插入但不参与更新
            //（如 SQL Server 的 MERGE 不允许更新标识列），主键与自增列之外无其他列时无法生成更新子句
            bool hasUpdateColumn = false;

            foreach (var column in entityMetadata.Columns)
            {
                // 跳过值为默认值的自增列（必走插入路径）；带显式值时照常进列清单（可走更新路径）
                if (column.IsIdentity)
                {
                    if (column.IsDefaultValue(entity))
                        continue;

                    identityExplicit = true;
                }

                if (!column.IsPrimaryKey && !column.IsIdentity)
                    hasUpdateColumn = true;

                var columnName = column.ColumnName;
                var dbValue = column.GetValueForDb(entity);
                var dbParameter = parameterBuilder.CreateParameter(allocator.Allocate(columnName), dbValue, column.DbType);

                columns.Add(sqlDialect.QuoteIdentifier(columnName));
                values.Add(dbParameter.ParameterName);
                parametersList.Add(dbParameter);
            }

            // 全部为自增列的退化场景无法生成合法的 Upsert 语句，显式失败
            if (columns.Count == 0)
                throw new NotSupportedException($"实体表 '{entityMetadata.TableName}' 没有可插入的列（全部为自增列），无法构建 Upsert 语句。");

            if (!hasUpdateColumn)
                throw new NotSupportedException($"实体表 '{entityMetadata.TableName}' 除主键与自增列外没有可更新的列，无法构建 Upsert 语句。");

            parameters = parametersList.ToArray();

            // 自增列参与方式上下文非 null 时由 SQL 构建器按方言能力处理：取回形态生成取回子句，
            // 显式写入形态供需要显式插入标识列的方言（如 SQL Server 的 IDENTITY_INSERT）生成包裹语句
            InsertIdentityContext identity = null;
            if (identityExplicit)
                identity = InsertIdentityContext.ForExplicit(sqlDialect.QuoteIdentifier(entityMetadata.IdentityColumn.ColumnName));
            else if (retrieveIdentity)
                identity = InsertIdentityContext.ForRetrieval(sqlDialect.QuoteIdentifier(entityMetadata.IdentityColumn.ColumnName));

            var conflictKey = sqlDialect.QuoteIdentifier(entityMetadata.PrimaryKey.ColumnName);// 冲突键 = 主键
            return sqlBuilder.BuildUpsertSql(quotedTableName, columns, values, conflictKey, identity);
        }
        #endregion
    }
}
