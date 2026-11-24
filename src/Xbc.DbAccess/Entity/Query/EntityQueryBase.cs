using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 实体查询抽象基类，封装共享的查询执行逻辑与排序链、窗口状态。
    /// <para><see cref="EntityQuery{T}"/> 和 <see cref="IncludableEntityQuery{T,TPrevious}"/> 均继承此基类。</para>
    /// <para>OrderBy/ThenBy 仅支持根实体映射列（列名经方言引用，结构性防注入）；Skip/Take 走方言窗口路径。无排序且无窗口时不改动 SQL。</para>
    /// </summary>
    /// <typeparam name="T">实体类型。</typeparam>
    internal abstract class EntityQueryBase<T> where T : class
    {
        protected readonly IDbAccess dbAccess;
        protected ISqlDialect sqlDialect => dbAccess.ProviderFactory.SqlDialect;
        protected IDbParameterBuilder parameterBuilder => dbAccess.ProviderFactory.ParameterBuilder;
        protected ISqlBuilder sqlBuilder => dbAccess.ProviderFactory.SqlBuilder;
        protected readonly string originalSql;// 原始 SQL 语句
        protected readonly DbParameter[] originalParameters;
        protected readonly EntityMetadata entityMetadata;

        // 查询级排序链与窗口状态（链式方法复制状态新建实例，构建后不再修改）
        protected readonly List<(ColumnMetadata Column, bool Descending)> orderings;
        protected readonly int? skip;// 窗口跳过行数（OFFSET 语义）
        protected readonly int? take;// 窗口取行数（FETCH/LIMIT 语义）

        /// <summary>
        /// 是否包含导航属性。
        /// </summary>
        protected abstract bool HasNavigations { get; }

        /// <summary>
        /// 初始化查询执行上下文与排序/窗口状态。
        /// </summary>
        /// <param name="dbAccess">数据库访问对象。</param>
        /// <param name="sql">原始 SQL 语句。</param>
        /// <param name="parameters">查询参数。</param>
        /// <param name="orderings">查询级排序链；null 归一为空列表（无排序）。</param>
        /// <param name="skip">窗口跳过行数；null 表示未设置。</param>
        /// <param name="take">窗口取行数；null 表示未设置。</param>
        protected EntityQueryBase(IDbAccess dbAccess,
            string sql,
            DbParameter[] parameters,
            List<(ColumnMetadata Column, bool Descending)> orderings = null,
            int? skip = null,
            int? take = null)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            Guard.ThrowIfNullOrWhiteSpace(sql, nameof(sql));

            this.dbAccess = dbAccess;
            originalSql = sql;
            originalParameters = parameters ?? Array.Empty<DbParameter>();

            // 浅复制排序链：链式派生传入的列表与原实例解除引用共享，从机制上封死跨实例写
            this.orderings = orderings != null
                ? new List<(ColumnMetadata, bool)>(orderings)
                : new List<(ColumnMetadata, bool)>();
            this.skip = skip;
            this.take = take;

            entityMetadata = EntityMetadataCache<T>.Metadata;
        }

        #region 单行与列表终结（First / Single / ToList）
        public Task<T> FirstAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteSingleAsync(true, false, cancellationToken);
        }

        public Task<T> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteSingleAsync(false, false, cancellationToken);
        }

        public Task<T> SingleAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteSingleAsync(true, true, cancellationToken);
        }

        public Task<T> SingleOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteSingleAsync(false, true, cancellationToken);
        }

        /// <summary>
        /// 执行单条记录查询，按参数决定空结果是否抛异常、是否检测多条。
        /// <para>排序/窗口状态存在时改写为方言窗口 SQL（排序 + 限量截取）；无状态时直接执行原 SQL 流式直读。</para>
        /// </summary>
        /// <param name="throwIfEmpty">是否在查询结果为空时抛出异常。</param>
        /// <param name="ensureSingle">是否确保查询结果仅包含一条记录。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        private async Task<T> ExecuteSingleAsync(bool throwIfEmpty, bool ensureSingle, CancellationToken cancellationToken)
        {
            string executedSql;

            // 校验并取窗口态：纯排序无窗口时同样走窗口构建（单条路径总要截断）
            var hasWindowState = EntityQuerySqlHelpers.ValidateWindowState(orderings, skip, take);
            if (hasWindowState || orderings.Count > 0)
            {
                executedSql = EntityQuerySqlHelpers.PrepareWrappableBaseSql(originalSql);

                // 排序与限量截取在各方言里是一个语法单元（LIMIT/FETCH/ROW_NUMBER 均强制排序），改写后数据库走
                // top-N 计划流式中途即停，避免全量排序后逐行流出；Single 系取 2 行用于多条检测
                var fetchLimit = ensureSingle ? 2 : 1;
                var fetch = take.HasValue ? Math.Min(take.Value, fetchLimit) : fetchLimit;
                executedSql = await EntityQuerySqlHelpers.BuildWindowSqlAsync(
                    dbAccess,
                    executedSql, skip ?? 0, fetch,
                    EntityQuerySqlHelpers.BuildOrderClause(orderings, sqlDialect),
                    appendRowOrder: true).ConfigureAwait(false);
            }
            else
            {
                // 无排序无窗口时流式直读：无行序需兑现，服务端无物化代价；截断需按方言改写 SELECT 结构
                // （TOP 1/ROWNUM 等），风险大于「提前终止扫描」的有限收益，故不截断
                executedSql = originalSql;
            }

            return await ExecuteSingleCoreAsync(executedSql, originalParameters, throwIfEmpty, ensureSingle, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 以指定 SQL 执行单条记录查询（导航属性在连接作用域中装配）。
        /// </summary>
        private async Task<T> ExecuteSingleCoreAsync(string sql, DbParameter[] parameters, bool throwIfEmpty, bool ensureSingle, CancellationToken cancellationToken)
        {
            if (!HasNavigations)
            {
                using (var reader = await dbAccess.ExecuteReaderAsync(sql, parameters, cancellationToken).ConfigureAwait(false))
                {
                    return await ReadSingleAsync(reader, throwIfEmpty, ensureSingle, cancellationToken).ConfigureAwait(false);
                }
            }

            // 有导航属性时，在连接作用域中执行，复用同一个连接
            using (var scope = await dbAccess.BeginConnectionScopeAsync(cancellationToken).ConfigureAwait(false))
            {
                T entity;
                using (var reader = await dbAccess.ExecuteReaderAsync(sql, parameters, cancellationToken).ConfigureAwait(false))
                {
                    entity = await ReadSingleAsync(reader, throwIfEmpty, ensureSingle, cancellationToken).ConfigureAwait(false);
                }

                // 导航装配须在 reader 释放后、作用域结束前执行：包装读取器按 manageConnection 规则处置连接
                // （无事务/作用域时随 reader 释放归还连接池），且 reader 持有期间底层连接被游标占用不能再开命令；
                // 作用域内连接由作用域持有，导航查询得以复用同一连接
                if (entity != null)
                    await PopulateNavigationsAsync(new List<T> { entity }, cancellationToken).ConfigureAwait(false);

                return entity;
            }
        }

        /// <summary>
        /// 从数据库读取单条记录。
        /// </summary>
        /// <param name="reader">数据库读取器。</param>
        /// <param name="throwIfEmpty">是否在查询结果为空时抛出异常。</param>
        /// <param name="ensureSingle">是否确保查询结果仅包含一条记录。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        private static async Task<T> ReadSingleAsync(DbDataReader reader, bool throwIfEmpty, bool ensureSingle, CancellationToken cancellationToken)
        {
            var entity = await EntityMapper<T>.MapAsync(reader, cancellationToken).ConfigureAwait(false);
            if (entity == null)
            {
                if (throwIfEmpty)
                    throw new InvalidOperationException("查询结果不包含任何记录。");

                return null;
            }

            if (ensureSingle && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("查询结果包含多条记录。");

            return entity;
        }

        /// <summary>
        /// 异步执行列表查询，返回全部匹配实体（排序/窗口状态在执行时应用）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>实体列表。</returns>
        public async Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
        {
            string executedSql;

            // 校验并取窗口态
            var hasWindowState = EntityQuerySqlHelpers.ValidateWindowState(orderings, skip, take);
            if (hasWindowState)
            {
                // 含窗口：走方言窗口路径（窗口子查询包裹源查询；ValidateWindowState 已保证窗口态必有 Take）
                executedSql = EntityQuerySqlHelpers.PrepareWrappableBaseSql(originalSql);
                executedSql = await EntityQuerySqlHelpers.BuildWindowSqlAsync(
                    dbAccess,
                    executedSql, skip ?? 0, take.Value,
                    EntityQuerySqlHelpers.BuildOrderClause(orderings, sqlDialect),
                    appendRowOrder: true).ConfigureAwait(false);
            }
            else if (orderings.Count > 0)
            {
                // 纯排序：在 SQL 末尾附加 ORDER BY（PrepareWrappableBaseSql 校验无手写顶层 ORDER BY 且已去尾分号/空白，直接拼接）
                executedSql = EntityQuerySqlHelpers.PrepareWrappableBaseSql(originalSql);
                //executedSql = $"{executedSql}{Environment.NewLine}ORDER BY {EntityQuerySqlHelpers.BuildOrderClause(orderings, sqlDialect)}";
                executedSql = executedSql + Environment.NewLine
                    + "ORDER BY " + EntityQuerySqlHelpers.BuildOrderClause(orderings, sqlDialect) + Environment.NewLine;
            }
            else
            {
                // 无排序无窗口时原样执行
                executedSql = originalSql;
            }

            return await ExecuteListCoreAsync(executedSql, originalParameters, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 以指定 SQL 执行列表查询（导航属性在连接作用域中装配）。
        /// </summary>
        private async Task<List<T>> ExecuteListCoreAsync(string sql, DbParameter[] parameters, CancellationToken cancellationToken)
        {
            if (!HasNavigations)
            {
                using (var reader = await dbAccess.ExecuteReaderAsync(sql, parameters, cancellationToken).ConfigureAwait(false))
                {
                    return await EntityMapper<T>.MapListAsync(reader, cancellationToken).ConfigureAwait(false);
                }
            }

            using (var scope = await dbAccess.BeginConnectionScopeAsync(cancellationToken).ConfigureAwait(false))
            {
                List<T> results;
                using (var reader = await dbAccess.ExecuteReaderAsync(sql, parameters, cancellationToken).ConfigureAwait(false))
                {
                    results = await EntityMapper<T>.MapListAsync(reader, cancellationToken).ConfigureAwait(false);
                }

                await PopulateNavigationsAsync(results, cancellationToken).ConfigureAwait(false);
                return results;
            }
        }
        #endregion

        #region 抽象方法
        /// <summary>
        /// 异步为实体列表装配导航属性。
        /// </summary>
        protected abstract Task PopulateNavigationsAsync(List<T> entities, CancellationToken cancellationToken);

        /// <summary>
        /// 创建携带指定排序/窗口状态的派生查询实例：子类按自身形态构造（EntityQuery/IncludableEntityQuery），
        /// 排序方向与状态复制的核心逻辑收敛于基类，链式方法仅做校验 + 本工厂调用。
        /// </summary>
        /// <param name="orderings">新实例的排序链。</param>
        /// <param name="skip">新实例的窗口跳过行数；null 表示未设置。</param>
        /// <param name="take">新实例的窗口取行数；null 表示未设置。</param>
        /// <returns>携带新状态的派生查询实例。</returns>
        protected abstract EntityQueryBase<T> CreateDerived(List<(ColumnMetadata Column, bool Descending)> orderings, int? skip, int? take);
        #endregion

        #region 排序与窗口
        /// <summary>
        /// OrderBy/OrderByDescending 共用核心：校验后开启新的单键排序链，构造派生实例。
        /// </summary>
        /// <typeparam name="TKey">排序键类型。</typeparam>
        /// <param name="keySelector">排序键选择器表达式。</param>
        /// <param name="descending">是否降序。</param>
        /// <returns>带新排序链的派生查询实例。</returns>
        protected EntityQueryBase<T> OrderByCore<TKey>(Expression<Func<T, TKey>> keySelector, bool descending)
        {
            // 调用前置校验：查询 SQL 不得自带 ORDER BY，混用会生成非法或语义冲突的 SQL
            // （词边界检测不感知字符串字面量：内容恰含 ORDER BY 文本时会误判为子句，此类 SQL 极罕见）
            if (EntityQuerySqlHelpers.ContainsOrderByClause(originalSql))
                throw new InvalidOperationException("查询 SQL 已包含 ORDER BY 子句，不能再设置查询级排序（OrderBy/ThenBy），混用会生成非法或语义冲突的 SQL，请移除其一。");

            var column = ResolveMappedColumn(keySelector);
            return CreateDerived(new List<(ColumnMetadata, bool)> { (column, descending) }, skip, take);
        }

        /// <summary>
        /// ThenBy/ThenByDescending 共用核心：校验后在既有排序链末尾追加键，构造派生实例。
        /// </summary>
        /// <typeparam name="TKey">排序键类型。</typeparam>
        /// <param name="keySelector">排序键选择器表达式。</param>
        /// <param name="descending">是否降序。</param>
        /// <returns>追加了排序键的派生查询实例。</returns>
        protected EntityQueryBase<T> ThenByCore<TKey>(Expression<Func<T, TKey>> keySelector, bool descending)
        {
            // 调用前置校验：必须已有排序链起点（OrderBy/OrderByDescending）
            if (orderings.Count == 0)
                throw new InvalidOperationException("ThenBy/ThenByDescending 必须在 OrderBy/OrderByDescending 之后调用，不能作为首个排序键。");

            var column = ResolveMappedColumn(keySelector);
            var newOrderings = new List<(ColumnMetadata, bool)>(orderings.Count + 1);
            newOrderings.AddRange(orderings);
            newOrderings.Add((column, descending));
            return CreateDerived(newOrderings, skip, take);
        }

        /// <summary>
        /// Skip 共用核心：校验后设置窗口跳过行数，构造派生实例。
        /// </summary>
        /// <param name="count">跳过的行数，非负；0 合法（从第一行开始，动态分页 <c>pageSize * (page - 1)</c> 首页的常规值，翻译为 OFFSET 0）。</param>
        /// <returns>带窗口跳过的派生查询实例。</returns>
        protected EntityQueryBase<T> SkipCore(int count)
        {
            // 调用前置校验：窗口要求稳定行序（无排序时窗口跨页/跨次执行会重复或丢行）
            if (orderings.Count == 0)
                throw new InvalidOperationException("Skip/Take 要求稳定行序，请先调用 OrderBy/OrderByDescending 设置排序后再使用窗口（Skip/Take）。");

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), count, "窗口跳过行数不能为负数。");

            return CreateDerived(orderings, count, take);
        }

        /// <summary>
        /// Take 共用核心：校验后设置窗口取行数，构造派生实例。
        /// </summary>
        /// <param name="count">取回的行数，正数（Take(0) 的空窗口无实用语义，且各数据库对 0 的窗口截断行为不一）。</param>
        /// <returns>带窗口截断的派生查询实例。</returns>
        protected EntityQueryBase<T> TakeCore(int count)
        {
            // 调用前置校验：窗口要求稳定行序（无排序时窗口跨页/跨次执行会重复或丢行）
            if (orderings.Count == 0)
                throw new InvalidOperationException("Skip/Take 要求稳定行序，请先调用 OrderBy/OrderByDescending 设置排序后再使用窗口（Skip/Take）。");

            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), count, "窗口行数必须为正数。");

            return CreateDerived(orderings, skip, count);
        }
        #endregion

        #region 通用方法
        /// <summary>
        /// 解析排序键/聚合列的列选择器为经过资格验证的映射列：单级成员访问校验与未映射拒绝由 <see cref="EntityMetadata.ResolveColumn"/> 执行，
        /// 列资格守卫（JSON 存储属性/并发令牌/存储变换属性拒绝，枚举映射语义确定放行）在此内联执行。
        /// </summary>
        /// <typeparam name="TKey">选择器返回类型。</typeparam>
        /// <param name="keySelector">列选择器表达式。</param>
        /// <returns>命中的列元数据。</returns>
        private ColumnMetadata ResolveMappedColumn<TKey>(Expression<Func<T, TKey>> keySelector)
        {
            var column = entityMetadata.ResolveColumn(keySelector);

            // JSON 存储属性参与比较/排序的是序列化文本而非结构化内容，显式禁止（与 WHERE 侧禁止对齐）
            if (column.IsJson)
                throw new NotSupportedException($"属性 '{column.PropertyName}' 启用了 JSON 存储（[Column(IsJson = true)]），不支持排序/聚合，参与比较/排序的是序列化文本而非结构化内容。");

            // 并发令牌由框架乐观锁专用管理（换发与乐观锁条件均在框架内部），不参与排序/聚合
            if (column.IsConcurrencyToken)
                throw new NotSupportedException($"属性 '{column.PropertyName}' 标记为并发令牌（[Column(IsConcurrencyToken = true)]），由框架乐观锁专用管理，不支持排序/聚合。");

            // 转换器守卫（JSON 已在上方拦下）：存储值序与属性值序可能不一致
            if (column.HasConverterTransform)
                throw new NotSupportedException($"属性 '{column.PropertyName}' 的存储值经自定义转换器变换，存储值的序与属性值的序可能不一致，不支持排序/聚合，请改用存储类型一致的属性。");

            return column;
        }
        #endregion

        #region 聚合终结（Count / Any / All / Sum / Average / Max / Min）
        /// <summary>
        /// 异步统计查询结果行数：COUNT 包裹查询（窗口状态时包裹在窗口子查询外，统计窗口内行数）。
        /// </summary>
        public async Task<long> CountAsync(CancellationToken cancellationToken = default)
        {
            var innerSql = await BuildAggregateTargetSqlAsync().ConfigureAwait(false);

            // 派生表别名：SQL Server 必须有，Oracle 不允许派生表用 AS（与 SqlBuilderBase 窗口路径一致），直接跟别名
            var sqlBuilder = new StringBuilder();
            sqlBuilder.AppendLine("SELECT COUNT(*) FROM (");
            sqlBuilder.AppendLine(SqlFormatter.IndentLines(innerSql));
            sqlBuilder.AppendLine($") {SqlIdentifierHelper.Generate("q")}");

            // COUNT 的结果经实体列值转换链归一（不同驱动返回 int/long/decimal 不一），框架内部不走扩展方法（param 转换层多余）
            var result = await dbAccess.ExecuteScalarAsync(sqlBuilder.ToString(), originalParameters, cancellationToken).ConfigureAwait(false);
            return EntityValueConverter.ConvertFrom<long>(result);
        }

        /// <summary>
        /// 异步判断查询是否存在结果行（EXISTS 布尔化，避免驱动的布尔字面量差异）。
        /// </summary>
        public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        {
            var innerSql = await BuildAggregateTargetSqlAsync().ConfigureAwait(false);

            // ScalarSelectFromClause：Oracle 需要 FROM DUAL，其他方言为 null/空串无影响
            var sqlBuilder = new StringBuilder();
            sqlBuilder.AppendLine("SELECT CASE WHEN EXISTS (");
            sqlBuilder.AppendLine(SqlFormatter.IndentLines(innerSql));
            sqlBuilder.AppendLine(") THEN 1 ELSE 0 END" + sqlDialect.ScalarSelectFromClause);

            // CASE WHEN 的 1/0 经实体列值转换链归一（不同驱动对 CASE 表达式的返回类型不一：int/long/decimal），== 1 判真
            var result = await dbAccess.ExecuteScalarAsync(sqlBuilder.ToString(), originalParameters, cancellationToken).ConfigureAwait(false);
            return EntityValueConverter.ConvertFrom<int>(result) == 1;
        }

        /// <summary>
        /// 异步判断查询结果是否所有行都满足条件（NOT EXISTS 反语义：SELECT CASE WHEN NOT EXISTS(SELECT 1 ... WHERE NOT (条件)) THEN 1 ELSE 0 END）。
        /// <para>消费链上已构建的 WHERE 与窗口状态（已设置窗口时对窗口内行判断）；空结果集返回 true（与 SQL 标准的 NOT EXISTS 语义一致）。</para>
        /// </summary>
        /// <param name="predicate">条件表达式，如 <c>x => x.Age &gt; 18</c>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>所有行都满足条件（或无行）时返回 true；存在不满足条件的行时返回 false。</returns>
        public async Task<bool> AllAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(predicate, nameof(predicate));

            // 谓词与链 WHERE 同语句共存：链参数名先预留（剥方言前缀的裸名域，与翻译器一致），谓词参数自动避让
            var allocator = new ParameterNameAllocator(originalParameters, parameterBuilder.ParameterPrefix);
            var translator = new WhereClauseTranslator(entityMetadata, sqlDialect, parameterBuilder, allocator);
            var translation = translator.Translate(predicate);
            var allParameters = originalParameters.Concat(translation.Parameters).ToArray();

            var innerSql = await BuildAggregateTargetSqlAsync().ConfigureAwait(false);
            var sqlBuilder = new StringBuilder();
            sqlBuilder.AppendLine("SELECT CASE WHEN NOT EXISTS (");
            sqlBuilder.AppendLine("    SELECT 1 FROM (");
            sqlBuilder.AppendLine(SqlFormatter.IndentLines(innerSql, 2));
            sqlBuilder.AppendLine($"    ) {SqlIdentifierHelper.Generate("q")}");
            sqlBuilder.AppendLine($"    WHERE NOT ({translation.WhereClause})");
            sqlBuilder.AppendLine(") THEN 1 ELSE 0 END" + sqlDialect.ScalarSelectFromClause);

            // CASE WHEN 的 1/0 经实体列值转换链归一为 bool（非零为 true），与 AnyAsync 同链路
            var result = await dbAccess.ExecuteScalarAsync(sqlBuilder.ToString(), allParameters, cancellationToken).ConfigureAwait(false);
            return EntityValueConverter.ConvertFrom<bool>(result);
        }

        /// <summary>
        /// 异步对查询结果求指定数值列的总和（SUM 包裹查询），已设置的窗口生效。
        /// <para>无匹配行时返回 null；结果统一转换为 <see cref="decimal"/>。</para>
        /// </summary>
        /// <typeparam name="TResult">选择器返回类型（列的属性类型）。</typeparam>
        /// <param name="selector">数值列选择器，如 <c>x => x.Amount</c>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>数值列总和；无匹配行时返回 null。</returns>
        public async Task<decimal?> SumAsync<TResult>(Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default)
        {
            var column = ResolveMappedColumn(selector);
            var result = await ExecuteAggregateScalarAsync(column, "SUM", cancellationToken).ConfigureAwait(false);

            // DBNull → null（无匹配行/全 NULL 值行），其余经 Convert.ToDecimal 归一（驱动返回类型不一：int/long/double/decimal）
            return result == null || result == DBNull.Value
                ? null :
                Convert.ToDecimal(result);
        }

        /// <summary>
        /// 异步对查询结果求指定数值列的平均值（AVG 包裹查询），已设置的窗口生效。
        /// <para>无匹配行时返回 null；NULL 值行不参与平均。列侧表达式经 <see cref="ISqlDialect.BuildAverageExpression"/>
        /// 生成，避免整型列 AVG 的整数截断。</para>
        /// </summary>
        /// <typeparam name="TResult">选择器返回类型（列的属性类型）。</typeparam>
        /// <param name="selector">数值列选择器，如 <c>x => x.Amount</c>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>数值列平均值；无匹配行时返回 null。</returns>
        public async Task<decimal?> AverageAsync<TResult>(Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default)
        {
            var column = ResolveMappedColumn(selector);
            var result = await ExecuteAggregateScalarAsync(column, "AVG", cancellationToken).ConfigureAwait(false);

            // DBNull → null（无匹配行/全 NULL 值行），其余经 Convert.ToDecimal 归一（驱动返回类型不一：int/long/double/decimal）
            return result == null || result == DBNull.Value
                ? null :
                Convert.ToDecimal(result);
        }

        /// <summary>
        /// 异步求查询结果中指定列的最大值（MAX 包裹查询）。选择器仅支持单级映射列访问。
        /// <para>无匹配行时返回 null；NULL 值行不参与比较。结果经实体列值转换链归一，兼容不同引擎的返回类型差异。</para>
        /// </summary>
        /// <typeparam name="TResult">选择器返回类型（非可空列的属性类型）。</typeparam>
        /// <param name="selector">列选择器，如 <c>x => x.Amount</c>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>列最大值；无匹配行时返回 null。</returns>
        public async Task<TResult?> MaxAsync<TResult>(Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default) where TResult : struct
        {
            var column = ResolveMappedColumn(selector);
            var result = await ExecuteAggregateScalarAsync(column, "MAX", cancellationToken).ConfigureAwait(false);
            return ToNullable<TResult>(result);
        }

        /// <summary>
        /// 异步求查询结果中指定可空列的最大值。可空列重载：选择器返回类型为 <typeparamref name="TResult"/> 的可空形式。
        /// </summary>
        /// <typeparam name="TResult">选择器返回类型的基础类型（可空列属性类型归一后的基础值类型）。</typeparam>
        /// <param name="selector">可空列选择器，如 <c>x => x.Score</c>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>列最大值；无匹配行时返回 null。</returns>
        public async Task<TResult?> MaxAsync<TResult>(Expression<Func<T, TResult?>> selector, CancellationToken cancellationToken = default) where TResult : struct
        {
            var column = ResolveMappedColumn(selector);
            var result = await ExecuteAggregateScalarAsync(column, "MAX", cancellationToken).ConfigureAwait(false);
            return ToNullable<TResult>(result);
        }

        /// <summary>
        /// 异步求查询结果中指定列的最小值（MIN 包裹查询）。选择器仅支持单级映射列访问。
        /// <para>无匹配行时返回 null；NULL 值行不参与比较。结果经实体列值转换链归一，兼容不同引擎的返回类型差异。</para>
        /// </summary>
        /// <typeparam name="TResult">选择器返回类型（非可空列的属性类型）。</typeparam>
        /// <param name="selector">列选择器，如 <c>x => x.Amount</c>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>列最小值；无匹配行时返回 null。</returns>
        public async Task<TResult?> MinAsync<TResult>(Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default) where TResult : struct
        {
            var column = ResolveMappedColumn(selector);
            var result = await ExecuteAggregateScalarAsync(column, "MIN", cancellationToken).ConfigureAwait(false);
            return ToNullable<TResult>(result);
        }

        /// <summary>
        /// 异步求查询结果中指定可空列的最小值。可空列重载：选择器返回类型为 <typeparamref name="TResult"/> 的可空形式。
        /// </summary>
        /// <typeparam name="TResult">选择器返回类型的基础类型（可空列属性类型归一后的基础值类型）。</typeparam>
        /// <param name="selector">可空列选择器，如 <c>x => x.Score</c>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>列最小值；无匹配行时返回 null。</returns>
        public async Task<TResult?> MinAsync<TResult>(Expression<Func<T, TResult?>> selector, CancellationToken cancellationToken = default) where TResult : struct
        {
            var column = ResolveMappedColumn(selector);
            var result = await ExecuteAggregateScalarAsync(column, "MIN", cancellationToken).ConfigureAwait(false);
            return ToNullable<TResult>(result);
        }

        /// <summary>
        /// 执行标量聚合：SELECT 聚合表达式 FROM (聚合目标 SQL) AS 别名，返回第一行第一列的原始值（可能为 DBNull）。
        /// </summary>
        /// <param name="column">聚合目标列元数据。</param>
        /// <param name="aggregateFunction">聚合函数名（SUM/AVG/MAX/MIN），由调用方拼接，列名经方言引用，无用户输入。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>聚合结果的原始值；无匹配行时为 <see cref="DBNull.Value"/>。</returns>
        private async Task<object> ExecuteAggregateScalarAsync(ColumnMetadata column, string aggregateFunction, CancellationToken cancellationToken)
        {
            var innerSql = await BuildAggregateTargetSqlAsync().ConfigureAwait(false);

            // 别名带随机段，避免与查询 SQL 中已有别名/列名撞名
            var queryAlias = SqlIdentifierHelper.Generate("q");
            var quotedColumn = sqlDialect.QuoteIdentifier(column.ColumnName);

            // AVG 走方言表达式（其余聚合直接拼接函数名）：SQL Server 对整型列 AVG 返回整型（截断小数），
            // 方言层以 CAST 到 DECIMAL 保住真均值（如 AVG(CAST(col AS DECIMAL(38,6)))）；其余方言整型 AVG 自带高精度，默认实现原样返回
            var aggregateExpression = aggregateFunction == "AVG"
                ? sqlDialect.BuildAverageExpression($"{queryAlias}.{quotedColumn}")
                : $"{aggregateFunction}({queryAlias}.{quotedColumn})";

            var sqlBuilder = new StringBuilder();
            sqlBuilder.AppendLine($"SELECT {aggregateExpression} FROM (");
            sqlBuilder.AppendLine(SqlFormatter.IndentLines(innerSql));
            sqlBuilder.AppendLine($") {queryAlias}");
            return await dbAccess.ExecuteScalarAsync(sqlBuilder.ToString(), originalParameters, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 构建聚合目标 SQL：窗口状态时为窗口行集（统计窗口内行，窗口状态先经终结端校验）；否则为原始查询。
        /// </summary>
        private async Task<string> BuildAggregateTargetSqlAsync()
        {
            var hasWindowState = EntityQuerySqlHelpers.ValidateWindowState(orderings, skip, take);
            if (hasWindowState)
            {
                var executedSql = EntityQuerySqlHelpers.PrepareWrappableBaseSql(originalSql);

                // 走无行序模式（appendRowOrder=false）：聚合与行序无关，且 ROW_NUMBER 兼容形态的尾部裸 ORDER BY 在派生表/EXISTS 内
                // 是 SQL Server 语法错误（Msg 1033）；排序仅参与窗口内行的 ROW_NUMBER 计算
                return await EntityQuerySqlHelpers.BuildWindowSqlAsync(
                    dbAccess,
                    executedSql, skip ?? 0, take.Value,
                    EntityQuerySqlHelpers.BuildOrderClause(orderings, sqlDialect),
                    appendRowOrder: false).ConfigureAwait(false);
            }

            // 无窗口态有意忽略排序链：聚合结果只由行集决定（COUNT/SUM/EXISTS 与行序无关），应用排序只是无意义的全量排序成本；
            // 排序链在此方法里唯一的存在意义是窗口分支的窗口成员定义（OFFSET/ROW_NUMBER 语义定义在有序序列上），
            // 该路径已由 ValidateWindowState 的「窗口必有排序」强制保证非空
            return EntityQuerySqlHelpers.PrepareWrappableBaseSql(originalSql);
        }

        /// <summary>
        /// 将聚合结果转换为可空值类型 TResult：DBNull/null 返回 null，其余经实体列值转换链归一（与读取路径的列值转换一致），
        /// 兼容不同驱动对 MAX/MIN 的返回类型差异。
        /// </summary>
        /// <typeparam name="TResult">目标基础值类型（选择器签名中的可空形式的基础类型）。</typeparam>
        /// <param name="result">聚合原始结果。</param>
        /// <returns>可空 TResult 结果。</returns>
        private static TResult? ToNullable<TResult>(object result) where TResult : struct
        {
            if (result == null || result == DBNull.Value)
                return null;

            return (TResult?)EntityValueConverter.ConvertFrom(result, typeof(TResult));
        }
        #endregion

        #region Select 投影
        /// <summary>
        /// 投影：将查询结果投影为映射列的值（单列成员访问、匿名类型多列、元组多列、命名类型带参构造或命名类型初始化器，接口说明见 <see cref="IEntityQuery{T}.Select{TResult}"/>）。
        /// </summary>
        /// <typeparam name="TResult">投影结果类型：单列为属性类型，多列为匿名类型、元组（ValueTuple）、带参构造的命名类型（编译器按实参绑定的构造函数）或命名类型（无参构造 + 对象初始化器）。</typeparam>
        /// <param name="selector">映射列选择器表达式。</param>
        /// <returns>投影查询对象。</returns>
        public IProjectedEntityQuery<TResult> Select<TResult>(Expression<Func<T, TResult>> selector)
        {
            Guard.ThrowIfNull(selector, nameof(selector));

            if (HasNavigations)
                throw new InvalidOperationException("投影（Select）与 Include 互斥：投影结果为非实体类型，无法装配导航属性。");

            var plan = ResolveProjectionPlan(selector);
            return new ProjectedEntityQuery<T, TResult>(dbAccess, originalSql, originalParameters, orderings, skip, take, plan);
        }

        /// <summary>
        /// 解析投影选择器为列组与成员绑定：单级映射列 → 单列（经 <see cref="EntityMetadata.ResolveColumn"/> 统一解析，未映射/导航/计算表达式由其拒绝）；
        /// 匿名类型（<c>x => new {{ ... }}</c>）与元组（<c>x => ValueTuple.Create(x.A, x.B)</c>）→ 按序多列（构造器位置映射；
        /// 元组字面量在表达式树中不可编译（CS8143），仅支持 Create 调用形态且无元素名）；
        /// 命名类型带参构造（<c>x => new Dto(x.A, x.B)</c>）→ 实参序多列（编译器按实参类型绑定的构造函数位置映射，多构造重载由编译器各自消歧）；
        /// 命名类型初始化器（<c>x => new Dto {{ M1 = x.A, M2 = x.B }}</c>）→ 绑定序多列（无参构造 + 按成员名赋值，要求目标成员可写）。
        /// <para>映射形态识别与共同守卫（带参构造 + 初始化器混合形态、非简单赋值绑定）由 <see cref="MemberMappingParser"/> 单点执行；
        /// 剩余校验（抛 <see cref="NotSupportedException"/>）：空形态（零配对）、带参构造的构造函数归属/参数数不符（手搭树）、重复绑定同一成员、绑定到只读成员、绑定右侧非映射列（由 <see cref="EntityMetadata.ResolveColumn"/> 拒绝）。</para>
        /// </summary>
        /// <param name="selector">投影选择器表达式。</param>
        /// <returns>投影映射计划（结果形态 + 投影列组 + 成员绑定，列序 = 结果集列序）。</returns>
        private ProjectionPlan ResolveProjectionPlan<TResult>(Expression<Func<T, TResult>> selector)
        {
            var body = selector.Body.StripConversions();
            var kind = MemberMappingParser.TryParse(body, out var mappings);

            // 非映射形态为单列投影（成员访问等），走统一的列选择器解析——
            // 未映射成员、导航属性与计算表达式由 ResolveColumn 以对应的异常类型与消息拒绝
            if (kind == MemberMappingKind.None)
                return ProjectionPlan.Scalar(new[] { entityMetadata.ResolveColumn(selector) });

            if (mappings.Count == 0)
                throw new NotSupportedException("投影表达式未包含任何列，初始化器、匿名类型或元组至少需要一列。");

            // 元组的 TRest 嵌套守卫：8 元素即 ValueTuple<T1..T7, TRest>，构造第 8 参为嵌套 ValueTuple 而非元素，位置装配无法承载，构建期即拒绝
            if (kind == MemberMappingKind.Tuple && TypeHelper.IsOverLengthValueTuple(typeof(TResult)))
                throw new NotSupportedException("暂不支持 8 元素及以上的元组投影（TRest 嵌套构造），请使用 7 元素以内的元组或匿名类型。");

            var columns = new ColumnMetadata[mappings.Count];
            if (kind == MemberMappingKind.Positional || kind == MemberMappingKind.Tuple)
            {
                // 匿名类型与元组：位置映射，成员名不参与映射（匿名类型由编译器保证属性名唯一且只读，经构造函数赋值；
                // 元组无元素名可提取，经构造函数位置赋值，消费端按位置或解构语义访问）。
                // 构造函数统一在此捕获固化进计划（匿名：编译器绑定的 NewExpression.Constructor；
                // 元组 Create 形态是方法调用、树中无 ConstructorInfo，反射取唯一公共构造——TRest 守卫已排除 8 元嵌套形态）
                ConstructorInfo constructor;
                if (body is NewExpression newExpression)
                {
                    constructor = newExpression.Constructor;
                }
                else
                {
                    var constructors = typeof(TResult).GetConstructors();
                    if (constructors.Length != 1)
                        throw new NotSupportedException($"元组类型 '{typeof(TResult)}' 必须恰有一个公共构造函数（按构造参数位置映射列）。");

                    constructor = constructors[0];
                }

                for (var i = 0; i < mappings.Count; i++)
                {
                    // 匿名形态在此丢弃成员名（名字不承载映射信息，属性序 = 构造参数序）；元组形态为 null 占位，本无名字可丢
                    var (_, valueExpression) = mappings[i];
                    columns[i] = entityMetadata.ResolveColumn(Expression.Lambda(valueExpression, selector.Parameters[0]));
                }

                return ProjectionPlan.Positional(columns, constructor);
            }

            if (kind == MemberMappingKind.Constructor)
            {
                // 命名类型带参构造：编译器按实参类型绑定的构造函数（多构造重载已由 Roslyn 各自消歧），按实参序位置映射列，
                // 成员名恒 null 不参与映射（构造实参不携带成员名，位置即映射——与元组形态同款式）
                var constructor = ((NewExpression)body).Constructor;

                // 构造函数须归属结果类型自身：newobj 按 DeclaringType 精确创建实例，基类构造无法产出 TResult 实例兑现 lambda 返回契约；
                // 编译器绑定（x => new TResult(...)）恒满足，手搭表达式树绑定基类/无关类型构造在构建期即拒绝
                if (constructor.DeclaringType != typeof(TResult))
                    throw new NotSupportedException($"投影构造函数声明类型 '{constructor.DeclaringType}' 与结果类型 '{typeof(TResult)}' 不一致，构造函数须归属结果类型自身。");

                for (var i = 0; i < mappings.Count; i++)
                {
                    var (_, valueExpression) = mappings[i];
                    columns[i] = entityMetadata.ResolveColumn(Expression.Lambda(valueExpression, selector.Parameters[0]));
                }

                return ProjectionPlan.Positional(columns, constructor);
            }
            else
            {
                // 命名类型初始化器：成员是结果落点（绑定序 = 列序），须可写且不可重复绑定；
                // 解析产物（MemberInfo）固化进 plan，mapper 直接绑定（解析规则单点在 ReflectionHelper.ResolveWritableMember）
                var members = new MemberInfo[mappings.Count];
                var boundMembers = new HashSet<string>(StringComparer.Ordinal);

                for (var i = 0; i < mappings.Count; i++)
                {
                    var (memberName, valueExpression) = mappings[i];

                    // 绑定目标必须是 TResult 的可写公共属性/字段：mapper 以无参构造实例化后按成员赋值填充
                    var member = ReflectionHelper.ResolveWritableMember(typeof(TResult), memberName);
                    if (member == null)
                        throw new NotSupportedException($"成员 '{memberName}' 不是投影结果类型 '{typeof(TResult)}' 的可写公共属性或字段，无法用于投影赋值。");

                    if (!boundMembers.Add(memberName))
                        throw new NotSupportedException($"投影表达式对成员 '{memberName}' 重复赋值。");

                    // 右侧是实体成员子表达式（非 Lambda），包成单参 Lambda 走统一的列选择器解析
                    columns[i] = entityMetadata.ResolveColumn(Expression.Lambda(valueExpression, selector.Parameters[0]));
                    members[i] = member;
                }

                return ProjectionPlan.MemberBinding(columns, members);
            }
        }
        #endregion

        #region keyset 分页
        /// <summary>
        /// 异步 keyset 分页：按键集排序返回游标之后的下一页（排序方向上的后续行）。
        /// <para>键集与方向即查询级排序链（OrderBy/ThenBy），须先设置排序；最后一个键应为唯一列（如主键），否则同键行可能跨页丢失或重复。
        /// 键集条件以展开的 OR 链生成，全方言可移植；与 Skip/Take 窗口互斥（游标定位与 offset 是两种行定位方式）。</para>
        /// </summary>
        /// <param name="cursor">游标实体（上一页最后一条），null 表示从第一页开始。</param>
        /// <param name="pageSize">页大小，必须为正数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>游标之后（按键集排序）的下一页实体列表；无更多行时为空列表。</returns>
        public Task<List<T>> KeysetAfterAsync(T cursor, int pageSize, CancellationToken cancellationToken = default)
        {
            return KeysetPaginateAsync(cursor, pageSize, before: false, cancellationToken);
        }

        /// <summary>
        /// 异步 keyset 分页：按键集排序返回游标之前的上一页（排序方向上的前导行），结果按键集排序方向输出。
        /// <para>上一页以全部键方向翻转的 ORDER BY + 行数限制实现，取回后内存反转为正向顺序。</para>
        /// </summary>
        /// <param name="cursor">游标实体（下一页第一条），不能为 null（第一页请使用 <see cref="KeysetAfterAsync"/>）。</param>
        /// <param name="pageSize">页大小，必须为正数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>游标之前（按键集排序）紧邻的一页实体列表，按键集排序方向输出；无更多行时为空列表。</returns>
        public Task<List<T>> KeysetBeforeAsync(T cursor, int pageSize, CancellationToken cancellationToken = default)
        {
            return KeysetPaginateAsync(cursor, pageSize, before: true, cancellationToken);
        }

        /// <summary>
        /// keyset 分页核心：校验排序链、窗口互斥与键列守卫；游标存在时以派生表包裹附加键集条件，
        /// 再经窗口路径（skip=0、take=pageSize）截取行页；Before 方向按翻转序取页后内存反转。
        /// </summary>
        private async Task<List<T>> KeysetPaginateAsync(T cursor, int pageSize, bool before, CancellationToken cancellationToken)
        {
            if (pageSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "页大小必须为正数。");

            if (orderings.Count == 0)
                throw new InvalidOperationException("keyset 分页要求先设置排序（OrderBy/ThenBy）作为键集。");

            if (skip.HasValue || take.HasValue)
                throw new InvalidOperationException("keyset 分页与 Skip/Take 窗口互斥，不能同时使用。");

            if (before && cursor == null)
                throw new ArgumentNullException(nameof(cursor), "向前翻页需要游标实体定位取页起点，第一页请使用 KeysetAfterAsync（游标为 null 表示从第一页开始）。");

            // 游标键列的存储变换（JSON/令牌/转换器）已在排序键解析端统一拒绝；枚举名称字符串放行——
            // 游标参数经 GetValueForDb 与存储值同域，行序（名称字母序）与游标比较序一致，翻页自洽
            var innerSql = EntityQuerySqlHelpers.PrepareWrappableBaseSql(originalSql);
            var allParameters = originalParameters;

            if (cursor != null)
            {
                // 键参数名预留链上已有参数名（剥方言前缀的裸名域，避免与 WHERE 谓词参数撞名）
                var allocator = new ParameterNameAllocator(originalParameters, parameterBuilder.ParameterPrefix);
                var keysetParameters = new DbParameter[orderings.Count];
                var clause = new StringBuilder();

                for (int i = 0; i < orderings.Count; i++)
                {
                    var column = orderings[i].Column;
                    var rawValue = column.GetValue(cursor);

                    if (rawValue == null)
                        throw new ArgumentException($"游标实体的键列 '{column.PropertyName}' 值为 null，keyset 分页要求键列为非空列。", nameof(cursor));

                    keysetParameters[i] = parameterBuilder.CreateParameter(
                        allocator.Allocate(column.ColumnName),
                        column.GetValueForDb(cursor),
                        column.DbType);

                    if (i > 0)
                        clause.Append(" OR ");

                    clause.Append('(');

                    for (int j = 0; j < i; j++)
                    {
                        if (j > 0)
                            clause.Append(" AND ");

                        clause.Append(sqlDialect.QuoteIdentifier(orderings[j].Column.ColumnName))
                              .Append(" = ")
                              .Append(keysetParameters[j].ParameterName);
                    }

                    if (i > 0)
                        clause.Append(" AND ");

                    // 比较方向与 ORDER BY 同源：升序键向后翻页为 >（降序键为 <），Before 方向取反。
                    // orderings 存降序标记，与 BuildOrderClause 的翻转参数（Descending != flipDescending）成对偶
                    var useGreaterThan = orderings[i].Descending == before;
                    clause.Append(sqlDialect.QuoteIdentifier(column.ColumnName))
                          .Append(useGreaterThan ? " > " : " < ")
                          .Append(keysetParameters[i].ParameterName);
                    clause.Append(')');
                }

                // 合并基础参数与键集参数
                allParameters = originalParameters.Concat(keysetParameters).ToArray();

                // 谓词已翻译进基础 SQL 的 WHERE，键集条件经派生表包裹附加（Oracle 派生表别名不允许 AS，
                // 闭合括号独占一行防内嵌 SQL 尾部行注释；内嵌 SQL 经 IndentLines 与包裹层树状对齐）
                var keysetBuilder = new StringBuilder();
                keysetBuilder.AppendLine("SELECT * FROM (");
                keysetBuilder.AppendLine(SqlFormatter.IndentLines(innerSql));
                keysetBuilder.AppendLine($") {SqlIdentifierHelper.Generate("k")} WHERE {clause}");
                innerSql = keysetBuilder.ToString();
            }

            var executedSql = await EntityQuerySqlHelpers.BuildWindowSqlAsync(
                dbAccess,
                innerSql, 0, pageSize,
                EntityQuerySqlHelpers.BuildOrderClause(orderings, sqlDialect, before),
                appendRowOrder: true).ConfigureAwait(false);
            var results = await ExecuteListCoreAsync(executedSql, allParameters, cancellationToken).ConfigureAwait(false);

            // Before 方向：SQL 按翻转序取的是从游标往远处数的一页，反转后即紧邻游标的正向顺序
            if (before)
                results.Reverse();

            return results;
        }
        #endregion
    }
}
