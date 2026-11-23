using System.Data;
using System.Data.Common;
using System.Text;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <content>
    /// 插入操作、自增 ID 处理及插入相关 SQL 构建（仅处理主表行，不级联写入导航属性）。
    /// <para>事务由各方法按需开启：SELECT IDENTITY 取回路径需两条语句同连接执行，批量插入以外层事务保证原子性。</para>
    /// </content>
    internal sealed partial class EntitySet<T>
    {
        #region 单条插入
        /// <summary>
        /// 异步插入实体，只插入主表行。自增列为默认值时由数据库生成并在插入后取回回填；带显式值时按该值写入，不回填。
        /// <para>1:1 引用外键同步在写入前执行，1:N 集合外键同步在写入后执行（值优先级规则见 <see cref="EntitySet{T}"/> 的同步方法），不级联写入导航实体。</para>
        /// <para>有自增列且方言不支持 OUTPUT/RETURNING 时开启事务保证 INSERT 与 SELECT IDENTITY 同连接执行。</para>
        /// <para>事务回滚后已回填的自增 ID 失效，应丢弃或重新加载实体。</para>
        /// </summary>
        /// <param name="entity">要插入的实体。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>当前实体受影响的行数。</returns>
        public async Task<int> InsertAsync(T entity, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(entity, nameof(entity));

            // 令牌列为默认值时生成初始令牌（幂等）
            EnsureConcurrencyToken(entity);

            // 1:1 引用外键同步
            SyncOneToOneForeignKeys(entity);

            int affectedRows;
            var identityColumn = entityMetadata.IdentityColumn;

            // 没有自增列，或自增列带显式值：无需回填，单语句直接执行
            if (identityColumn == null || !identityColumn.IsDefaultValue(entity))
            {
                var sql = BuildInsertSql(entity, out var parameters);
                affectedRows = await dbAccess.ExecuteNonQueryAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // 自增列为默认值：由数据库生成并回填，探测取回方式并按需开事务
                affectedRows = await InsertSingleWithIdentityAsync(entity, identityColumn, cancellationToken).ConfigureAwait(false);
            }

            // 插入完成后本方主键值已有效，同步 1:N 集合导航外键
            SyncOneToManyForeignKeys(entity);
            return affectedRows;
        }

        /// <summary>
        /// 插入自增列为默认值的实体并回填自增 ID：探测方言取回方式后分发到对应路径。
        /// <para>SELECT IDENTITY 方言的两条语句由本方法开事务保证同连接执行，其余取回方式为单语句、无需事务。</para>
        /// <para><see cref="InsertAsync"/> 的默认值分支与 <see cref="EntitySet{T}.UpsertAsync"/> 的自增主键短路分支共用本方法。</para>
        /// </summary>
        /// <param name="entity">要插入的实体。</param>
        /// <param name="identityColumn">自增列元数据。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        private async Task<int> InsertSingleWithIdentityAsync(T entity, ColumnMetadata identityColumn, CancellationToken cancellationToken)
        {
            var serverInfo = await dbAccess.Factory.GetServerInfoAsync().ConfigureAwait(false);
            var mode = sqlDialect.GetInsertIdentityMode(serverInfo);

            if (mode != InsertIdentityMode.SelectLastInsertId)
            {
                // OUTPUT/RETURNING 结果集或 RETURNING INTO 输出参数均为单语句取回，无需事务
                return await InsertWithIdentityAsync(entity, identityColumn, mode, null, cancellationToken).ConfigureAwait(false);
            }

            // 不支持 OUTPUT/RETURNING：两条语句需同连接执行，由本方法开事务保证
            using (var transaction = await dbAccess.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                // 失败路径由事务 Dispose 自动回滚，避免 Rollback 失败掩盖原始异常
                var affectedRows = await InsertWithIdentityAsync(entity, identityColumn, mode, null, cancellationToken).ConfigureAwait(false);
                transaction.Commit();
                return affectedRows;
            }
        }
        #endregion

        #region 自增列处理
        /// <summary>
        /// 插入自增列为默认值的实体并按取回方式取回自增 ID 回填。无事务职责：SELECT IDENTITY 路径的同连接要求由调用方事务保证。
        /// </summary>
        /// <param name="entity">要插入的实体。</param>
        /// <param name="identityColumn">自增列元数据。</param>
        /// <param name="mode">自增 ID 取回方式。</param>
        /// <param name="insertCommand">可复用命令租约（批量逐条路径传入以复用命令；单条路径传 null 直接执行）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        private Task<int> InsertWithIdentityAsync(object entity, ColumnMetadata identityColumn, InsertIdentityMode mode, ReusableCommand insertCommand, CancellationToken cancellationToken)
        {
            switch (mode)
            {
                case InsertIdentityMode.OutputClause:
                    return InsertWithOutputAsync(entity, identityColumn, insertCommand, cancellationToken);

                case InsertIdentityMode.ReturningInto:
                    return InsertWithReturningIntoAsync(entity, identityColumn, insertCommand, cancellationToken);

                default:
                    return InsertWithSelectIdentityAsync(entity, identityColumn, insertCommand, cancellationToken);
            }
        }

        /// <summary>
        /// 经 OUTPUT/RETURNING 结果集取回自增 ID：INSERT 语句带输出子句，执行后从结果集第一行第一列取回 ID 并回填。
        /// </summary>
        /// <param name="entity">要插入的实体。</param>
        /// <param name="identityColumn">自增列元数据。</param>
        /// <param name="insertCommand">可复用命令租约（null 时直接执行）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        private async Task<int> InsertWithOutputAsync(object entity, ColumnMetadata identityColumn, ReusableCommand insertCommand, CancellationToken cancellationToken)
        {
            var sql = BuildInsertSql(entity, out var parameters, retrieveIdentity: true);
            object id = await ExecuteScalarAsync(sql, parameters, insertCommand, cancellationToken).ConfigureAwait(false);

            // 这里的判断作为防御性编程，通常情况下如果插入失败是会立即抛出异常的
            if (id == null || id == DBNull.Value)
                throw new InvalidOperationException($"插入实体到表 '{entityMetadata.TableName}' 后未能获取自增 ID。");

            identityColumn.BackfillIdentity(entity, id);
            return 1;
        }

        /// <summary>
        /// 经 RETURNING INTO 输出参数取回自增 ID：输出参数随 INSERT 参数一并注册，执行后从输出参数读取回填值。
        /// </summary>
        /// <param name="entity">要插入的实体。</param>
        /// <param name="identityColumn">自增列元数据。</param>
        /// <param name="insertCommand">可复用命令租约（null 时直接执行）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        private async Task<int> InsertWithReturningIntoAsync(object entity, ColumnMetadata identityColumn, ReusableCommand insertCommand, CancellationToken cancellationToken)
        {
            // RETURNING INTO：自增 ID 输出参数，每次执行由提供程序写回值；
            // 名称加 out_ 前缀与输入列参数区分，经共享分配器防撞名
            var allocator = new ParameterNameAllocator();
            var identityOutputParameter = parameterBuilder.CreateParameter(
                allocator.Allocate("out_" + identityColumn.ColumnName),
                DBNull.Value,
                identityColumn.DbType,
                ParameterDirection.Output);

            var sql = BuildInsertSql(entity, out var parameters, retrieveIdentity: true, identityOutputParameter, allocator);
            var affectedRows = await ExecuteNonQueryAsync(sql, parameters, insertCommand, cancellationToken).ConfigureAwait(false);

            if (affectedRows <= 0)
                return affectedRows;

            // RETURNING INTO 无结果集，由提供程序执行后写回输出参数；未回填值时显式失败
            var id = identityOutputParameter.Value;
            if (id == null || id == DBNull.Value)
                throw new InvalidOperationException($"插入实体到表 '{entityMetadata.TableName}' 成功，但未能从 RETURNING INTO 输出参数获取到自增 ID。");

            identityColumn.BackfillIdentity(entity, id);
            return affectedRows;
        }

        /// <summary>
        /// 经 SELECT IDENTITY 取回自增 ID：先执行 INSERT，随后执行取回语句（两条语句同一连接由调用方事务保证）。
        /// </summary>
        /// <param name="entity">要插入的实体。</param>
        /// <param name="identityColumn">自增列元数据。</param>
        /// <param name="insertCommand">可复用命令租约（null 时直接执行）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        private async Task<int> InsertWithSelectIdentityAsync(object entity, ColumnMetadata identityColumn, ReusableCommand insertCommand, CancellationToken cancellationToken)
        {
            // 先生成取回语句：不支持的方言在此即抛异常，避免 INSERT 已成功、取 ID 失败的挫败路径
            var getIdSql = sqlDialect.SelectLastInsertId(identityColumn.ColumnName);
            var sql = BuildInsertSql(entity, out var parameters);
            var affectedRows = await ExecuteNonQueryAsync(sql, parameters, insertCommand, cancellationToken).ConfigureAwait(false);

            if (affectedRows <= 0)
                return affectedRows;

            var id = await ExecuteScalarAsync(getIdSql, null, insertCommand, cancellationToken).ConfigureAwait(false);
            if (id == null || id == DBNull.Value)
                throw new InvalidOperationException($"插入实体到表 '{entityMetadata.TableName}' 成功，但未能获取到自增 ID。");

            identityColumn.BackfillIdentity(entity, id);
            return affectedRows;
        }
        #endregion

        #region 批量插入
        /// <summary>
        /// 异步批量插入实体，只插入主表行。无自增列且方言未启用逐条策略时用多值插入；有自增列或方言启用逐条策略
        /// （<see cref="ISqlDialect.PreferRowByRowBatchInsert"/>）时逐条插入并经可复用命令租约复用命令与参数对象。
        /// <para>1:1 引用外键同步在写入前执行，1:N 集合外键同步在写入后执行，不自动插入对方实体。</para>
        /// <para>事务回滚后已回填的自增 ID 失效，应丢弃或重新加载实体。</para>
        /// </summary>
        /// <param name="entities">实体集合。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        public async Task<int> InsertRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            var entityList = Guard.ToValidatedList(entities, nameof(entities));
            if (entityList.Count == 1)
                return await InsertAsync(entityList[0], cancellationToken).ConfigureAwait(false);// 单条记录走 InsertAsync，由其决定是否开启事务

            // 令牌列为默认值时生成初始令牌（幂等），两条路径均在构建 SQL 前完成
            foreach (var entity in entityList)
            {
                EnsureConcurrencyToken(entity);
            }

            var identityColumn = entityMetadata.IdentityColumn;

            // 有自增列：逐条插入；取回方式探测在事务外执行（结果有缓存），
            // 外层事务保证原子性并满足 SELECT IDENTITY 路径同连接要求
            if (identityColumn != null)
            {
                var serverInfo = await dbAccess.Factory.GetServerInfoAsync().ConfigureAwait(false);
                var mode = sqlDialect.GetInsertIdentityMode(serverInfo);
                return await InsertRangeWithIdentityAsync(entityList, identityColumn, mode, cancellationToken).ConfigureAwait(false);
            }

            // 无自增列且方言未启用逐条策略：多值插入，单批可容纳时无需事务
            if (!sqlDialect.PreferRowByRowBatchInsert)
            {
                if (entityList.Count <= maxRowsPerInsertBatch)
                    return await InsertRangeMultiValueAsync(entityList, cancellationToken).ConfigureAwait(false);

                // 需要分批，使用事务保证原子性
                using (var transaction = await dbAccess.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
                {
                    var affectedRows = await InsertRangeMultiValueAsync(entityList, cancellationToken).ConfigureAwait(false);
                    transaction.Commit();
                    return affectedRows;
                }
            }

            // 无自增列但方言启用逐条策略（如 SQLite）：逐条插入并复用参数，外层事务保证原子性
            return await InsertRangeRowByRowAsync(entityList, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 逐条插入带自增列的实体列表：默认值由数据库生成并回填（经 <see cref="InsertWithIdentityAsync"/> 分发），显式值按值写入。
        /// <para>外层事务保证原子性与 SELECT IDENTITY 路径同连接要求。</para>
        /// </summary>
        /// <param name="entities">实体列表。</param>
        /// <param name="identityColumn">自增列元数据。</param>
        /// <param name="mode">自增 ID 取回方式（调用方在事务外探测）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        private async Task<int> InsertRangeWithIdentityAsync(IReadOnlyList<object> entities, ColumnMetadata identityColumn, InsertIdentityMode mode, CancellationToken cancellationToken)
        {
            using (var transaction = await dbAccess.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false))

            // 每行独立构建 SQL 与参数（列集随行变化），经可复用命令租约复用命令与参数对象
            using (var insertCommand = ((DbAccess)dbAccess).CreateReusableCommand())// 租约首次执行时才解析连接
            {
                int totalAffectedRows = 0;
                foreach (var entity in entities)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 1:1 引用外键同步
                    SyncOneToOneForeignKeys(entity);

                    if (identityColumn.IsDefaultValue(entity))
                    {
                        // 默认值：由数据库生成并回填，外层事务已覆盖，避免逐条嵌套子事务
                        totalAffectedRows += await InsertWithIdentityAsync(entity, identityColumn, mode, insertCommand, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        // 显式值：按值写入，无需回填
                        var sql = BuildInsertSql(entity, out var parameters);
                        totalAffectedRows += await ExecuteNonQueryAsync(sql, parameters, insertCommand, cancellationToken).ConfigureAwait(false);
                    }

                    // 插入完成后同步 1:N 集合导航外键
                    SyncOneToManyForeignKeys(entity);
                }

                transaction.Commit();
                return totalAffectedRows;
            }
        }

        /// <summary>
        /// 异步执行多值批量插入，按方言单语句 VALUES 行数上限自动分批。只插入主表行，不级联写入导航属性。
        /// <para>1:1 引用外键同步在写入前执行，1:N 集合外键同步在写入后执行（均纯内存同步，不自动插入对方实体）。</para>
        /// </summary>
        /// <param name="entities">实体列表。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        private async Task<int> InsertRangeMultiValueAsync(IReadOnlyList<object> entities, CancellationToken cancellationToken)
        {
            foreach (var entity in entities)
            {
                SyncOneToOneForeignKeys(entity);
            }

            // 列集对整个批量固定，构建一次供所有批次复用
            var quotedColumns = BuildInsertQuotedColumns();
            int maxRowsPerBatch = maxRowsPerInsertBatch;
            int totalAffectedRows = 0;

            for (int i = 0; i < entities.Count; i += maxRowsPerBatch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int count = Math.Min(maxRowsPerBatch, entities.Count - i);
                var sql = BuildBatchInsertSql(entities, quotedColumns, out var parameters, i, count);
                totalAffectedRows += await dbAccess.ExecuteNonQueryAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
            }

            // 插入完成后同步 1:N 集合导航（无自增列，本方主键值插入前即已有效）
            foreach (var entity in entities)
            {
                SyncOneToManyForeignKeys(entity);
            }

            return totalAffectedRows;
        }

        /// <summary>
        /// 逐条插入无自增列的实体列表（方言启用逐条策略），经可复用命令租约复用同一命令。
        /// <para>外层事务保证原子性。</para>
        /// </summary>
        /// <param name="entities">实体列表。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        private async Task<int> InsertRangeRowByRowAsync(IReadOnlyList<object> entities, CancellationToken cancellationToken)
        {
            // 无自增列：SQL 恒定、参数数量固定，首行构建一次，行间仅重绑参数值
            var columns = entityMetadata.Columns;
            var sql = BuildInsertSql(entities[0], out var parameters);

            using (var transaction = await dbAccess.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            using (var insertCommand = ((DbAccess)dbAccess).CreateReusableCommand())// 租约首次执行时才解析连接
            {
                int totalAffectedRows = 0;
                foreach (var entity in entities)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 1:1 引用外键同步
                    SyncOneToOneForeignKeys(entity);

                    // 列序与参数序一一对应，null 统一为 DBNull
                    for (int i = 0; i < columns.Count; i++)
                    {
                        parameters[i].Value = columns[i].GetValueForDb(entity) ?? DBNull.Value;
                    }

                    totalAffectedRows += await ExecuteNonQueryAsync(sql, parameters, insertCommand, cancellationToken).ConfigureAwait(false);

                    // 插入完成后同步 1:N 集合导航（无自增列，主键值插入前即已有效）
                    SyncOneToManyForeignKeys(entity);
                }

                transaction.Commit();
                return totalAffectedRows;
            }
        }
        #endregion

        #region 辅助方法
        /// <summary>
        /// 执行非查询 SQL 语句：有可复用命令租约时经所属数据库访问实例的内部重载复用命令执行，否则直接经数据库访问接口执行。
        /// </summary>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="insertCommand">可复用命令租约（null 时直接执行）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        private Task<int> ExecuteNonQueryAsync(string sql, DbParameter[] parameters, ReusableCommand insertCommand, CancellationToken cancellationToken)
        {
            return insertCommand != null
                ? insertCommand.Owner.ExecuteNonQueryAsync(insertCommand, sql, parameters, cancellationToken)
                : dbAccess.ExecuteNonQueryAsync(sql, parameters, cancellationToken);
        }

        /// <summary>
        /// 执行标量查询 SQL 语句：有可复用命令租约时经所属数据库访问实例的内部重载复用命令执行，否则直接经数据库访问接口执行。
        /// </summary>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="insertCommand">可复用命令租约（null 时直接执行）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>结果集中第一行第一列的值。</returns>
        private Task<object> ExecuteScalarAsync(string sql, DbParameter[] parameters, ReusableCommand insertCommand, CancellationToken cancellationToken)
        {
            return insertCommand != null
                ? insertCommand.Owner.ExecuteScalarAsync(insertCommand, sql, parameters, cancellationToken)
                : dbAccess.ExecuteScalarAsync(sql, parameters, cancellationToken);
        }
        #endregion

        #region INSERT 语句
        /// <summary>
        /// 构建插入实体的 SQL 语句。自增列为默认值时排除（由数据库生成），带显式值时照常进列清单。
        /// </summary>
        /// <param name="entity">实体。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="retrieveIdentity">是否构建自增 ID 取回上下文（生成 OUTPUT/RETURNING/RETURNING INTO 子句），仅取回路径传 true。</param>
        /// <param name="identityOutputParameter">自增 ID 输出参数（RETURNING INTO 路径），非 null 时随 INSERT 参数一并注册。</param>
        /// <param name="allocator">参数名分配器（与输出参数共享同一实例防撞名），未提供时内部创建。</param>
        /// <returns></returns>
        private string BuildInsertSql(
            object entity,
            out DbParameter[] parameters,
            bool retrieveIdentity = false,
            DbParameter identityOutputParameter = null,
            ParameterNameAllocator allocator = null)
        {
            // 可选参数无法直接 new，未提供时内部创建
            allocator = allocator ?? new ParameterNameAllocator();

            var columns = new List<string>();
            var values = new List<string>();
            var parametersList = new List<DbParameter>();

            // 自增列带显式值时进入列清单（按值写入），进入显式写入上下文
            bool identityExplicit = false;

            foreach (var column in entityMetadata.Columns)
            {
                // 跳过值为默认值的自增列：由数据库生成
                if (column.IsIdentity)
                {
                    if (column.IsDefaultValue(entity))
                        continue;

                    identityExplicit = true;
                }

                var columnName = column.ColumnName;
                var dbValue = column.GetValueForDb(entity);
                var dbParameter = parameterBuilder.CreateParameter(allocator.Allocate(columnName), dbValue, column.DbType);

                columns.Add(sqlDialect.QuoteIdentifier(columnName));
                values.Add(dbParameter.ParameterName);
                parametersList.Add(dbParameter);
            }

            // 全部为自增列的退化场景无法生成合法的 INSERT 语句，显式失败
            if (columns.Count == 0)
                throw new NotSupportedException($"实体表 '{entityMetadata.TableName}' 没有可插入的列（全部为自增列），无法构建 INSERT 语句。");

            // RETURNING INTO 路径：输出参数随输入参数一并注册
            if (identityOutputParameter != null)
                parametersList.Add(identityOutputParameter);

            parameters = parametersList.ToArray();

            // 自增列参与方式上下文非 null 时由 SQL 构建器按方言语法处理：取回形态生成输出子句，
            // 显式写入形态供需要显式插入标识列的方言（如 SQL Server 的 IDENTITY_INSERT）生成包裹语句
            InsertIdentityContext identityContext = null;
            if (identityExplicit)
                identityContext = InsertIdentityContext.ForExplicit(sqlDialect.QuoteIdentifier(entityMetadata.IdentityColumn.ColumnName));
            else if (retrieveIdentity)
                identityContext = InsertIdentityContext.ForRetrieval(sqlDialect.QuoteIdentifier(entityMetadata.IdentityColumn.ColumnName), identityOutputParameter?.ParameterName);

            return sqlBuilder.BuildInsertSql(
                quotedTableName,
                columns,
                values,
                identityContext);
        }

        /// <summary>
        /// 构建插入列的引用列名列表（排除自增列），由 <see cref="InsertRangeMultiValueAsync"/> 构建一次供所有批次复用。
        /// </summary>
        /// <returns>引用列名列表。</returns>
        private List<string> BuildInsertQuotedColumns()
        {
            var quotedColumns = new List<string>(entityMetadata.Columns.Count);
            foreach (var column in entityMetadata.Columns)
            {
                // 跳过标识列
                if (column.IsIdentity)
                    continue;

                quotedColumns.Add(sqlDialect.QuoteIdentifier(column.ColumnName));
            }

            // 全部为自增列的退化场景无法生成合法的 INSERT 语句，显式失败
            if (quotedColumns.Count == 0)
                throw new NotSupportedException($"实体表 '{entityMetadata.TableName}' 没有可插入的列（全部为自增列），无法构建批量 INSERT 语句。");

            return quotedColumns;
        }

        /// <summary>
        /// 构建批量插入实体的 SQL 语句（多值插入）。列集由 <paramref name="quotedColumns"/> 提供（调用方构建一次复用）。
        /// </summary>
        /// <param name="entities">实体列表。</param>
        /// <param name="quotedColumns">引用列名列表（排除自增列）。</param>
        /// <param name="parameters">参数数组。</param>
        /// <param name="startIndex">起始索引。</param>
        /// <param name="count">要处理的实体数量。</param>
        /// <returns></returns>
        private string BuildBatchInsertSql(
            IReadOnlyList<object> entities,
            List<string> quotedColumns,
            out DbParameter[] parameters,
            int startIndex,
            int count)
        {
            var allocator = new ParameterNameAllocator();
            var parametersList = new List<DbParameter>(count * quotedColumns.Count);

            // 多值批次的 SQL 构建与 bulk 插入扩展层的 ExecuteMultiValueBatchAsync 平行，两处语句格式须保持一致
            var valueGroups = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                var entity = entities[startIndex + i];
                var values = new List<string>(quotedColumns.Count);

                foreach (var column in entityMetadata.Columns)
                {
                    // 跳过标识列
                    if (column.IsIdentity)
                        continue;

                    var columnName = column.ColumnName;
                    var dbValue = column.GetValueForDb(entity);

                    // 带批内行索引的参数名（如 @Name_0）避免多行冲突，经分配器防撞名；
                    // 满批次间 SQL 文本完全相同，利于提供程序侧的计划缓存
                    var dbParameter = parameterBuilder.CreateParameter(allocator.Allocate($"{columnName}_{i}"), dbValue, column.DbType);
                    values.Add(dbParameter.ParameterName);
                    parametersList.Add(dbParameter);
                }

                valueGroups.Add($"({string.Join(", ", values)})");
            }

            parameters = parametersList.ToArray();

            // INSERT INTO 表名 (列名, 列名, ...)
            // VALUES
            //     (值, 值),
            //     (值, 值)
            // ...
            var sb = new StringBuilder();
            sb.AppendLine($"INSERT INTO {quotedTableName}");
            sb.AppendLine($"    ({string.Join(", ", quotedColumns)})");
            sb.AppendLine("VALUES");
            // 每行数据独占一行，大批次时 SQL 更易读
            sb.AppendLine($"    {string.Join($",{Environment.NewLine}    ", valueGroups)}");
            return sb.ToString();
        }
        #endregion
    }
}
