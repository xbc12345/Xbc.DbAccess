using System.Data.Common;
using System.Linq.Expressions;
using System.Text;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 分组层 SQL 构建共享器：GroupedEntityQuery（CountAsync 终结）与 GroupedProjectionQuery（投影终结）共用的分组层组装。
    /// <para>组装规则见设计文档 §4.2：分组 SELECT 暴露「全部键列（自然名）+ 引用到的聚合（生成别名）」超集，
    /// GROUP BY 键列组，HAVING 谓词 AND 合并；排序引用片段为分组 SELECT 列的方言引用
    /// （键列名/聚合别名）——五方言窗口构建器对 orderBy 的求值位置均在分组查询之外的派生表层，
    /// 裸聚合片段在外层不可引用，别名机制是排序合法性的前提。</para>
    /// </summary>
    internal static class GroupedLayerBuilder
    {
        /// <summary>
        /// 分组层构建产物：分组层 SQL（不含窗口与外层裁剪）、合并后的参数、排序引用片段串与聚合别名表。
        /// </summary>
        internal sealed class GroupedLayer
        {
            /// <summary>分组层 SQL（SELECT 键列与聚合别名 + GROUP BY/HAVING）。</summary>
            public string Sql { get; set; }

            /// <summary>执行参数（源参数 + HAVING 参数，顺序与 SQL 占位一致）。</summary>
            public DbParameter[] Parameters { get; set; }

            /// <summary>排序引用片段串（键列名/聚合别名 + 方向，逗号连接）；无排序或排序被忽略时为 null。</summary>
            public string OrderByClause { get; set; }

            /// <summary>聚合片段文本 → 生成别名（外层裁剪 SELECT 按别名引用聚合列）。</summary>
            public Dictionary<string, string> AggregateAliases { get; set; }
        }

        /// <summary>
        /// 构建分组层 SQL：聚合片段别名分配（按片段文本去重，同一聚合在 Select 与排序中重复出现时共用别名）、
        /// SELECT/GROUP BY/HAVING 组装与排序引用片段生成。
        /// </summary>
        /// <param name="dbAccess">数据库访问对象。</param>
        /// <param name="originalSql">源查询 SQL。</param>
        /// <param name="originalParameters">源查询参数。</param>
        /// <param name="entityMetadata">实体元数据（HAVING 聚合列解析）。</param>
        /// <param name="keyColumns">分组键列组。</param>
        /// <param name="havingPredicates">HAVING 谓词组（AND 合并）。</param>
        /// <param name="orderings">分组排序目标链。</param>
        /// <param name="projectionAggregates">投影聚合片段（参与别名分配与暴露）；CountAsync 无投影时为 null。</param>
        /// <param name="exposeOrderingAggregates">排序目标聚合是否进分组 SELECT（窗口/外层排序需要其可引用；
        /// CountAsync 无窗口时忽略排序链传 false，与 EntityQueryBase 聚合路径忽略排序的约定一致）。</param>
        /// <returns>分组层构建产物。</returns>
        internal static GroupedLayer Build(
            IDbAccess dbAccess,
            string originalSql,
            DbParameter[] originalParameters,
            EntityMetadata entityMetadata,
            List<GroupedKeyColumn> keyColumns,
            List<LambdaExpression> havingPredicates,
            List<GroupedOrderingTarget> orderings,
            List<AggregateFragment> projectionAggregates,
            bool exposeOrderingAggregates)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            Guard.ThrowIfNullOrWhiteSpace(originalSql, nameof(originalSql));
            Guard.ThrowIfNull(entityMetadata, nameof(entityMetadata));
            Guard.ThrowIfNullOrEmpty(keyColumns, nameof(keyColumns));
            Guard.ThrowIfNull(havingPredicates, nameof(havingPredicates));
            Guard.ThrowIfNull(orderings, nameof(orderings));

            var sqlDialect = dbAccess.ProviderFactory.SqlDialect;
            var parameterBuilder = dbAccess.ProviderFactory.ParameterBuilder;

            // ① 分组 SELECT 列表：全部键列（自然名引用，按列名去重——两个键成员可指向同列）+ 聚合片段（生成别名）
            var selectParts = new List<string>();
            var groupByParts = new List<string>();
            var seenKeyColumns = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in keyColumns)
            {
                if (seenKeyColumns.Add(key.Column.ColumnName))
                {
                    var quoted = sqlDialect.QuoteIdentifier(key.Column.ColumnName);
                    selectParts.Add(quoted);
                    groupByParts.Add(quoted);
                }
            }

            // ② 聚合别名分配：投影聚合在前、排序聚合在后，按片段文本去重；
            //    生成别名（SqlIdentifierHelper + 方言引用）结构性防注入，且外层可见可引用
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            var fragments = new List<AggregateFragment>();
            if (projectionAggregates != null)
                fragments.AddRange(projectionAggregates);

            if (exposeOrderingAggregates)
            {
                foreach (var ordering in orderings)
                {
                    if (ordering.Aggregate != null)
                        fragments.Add(ordering.Aggregate);
                }
            }

            foreach (var fragment in fragments)
            {
                if (aliases.ContainsKey(fragment.Sql))
                    continue;

                var alias = SqlIdentifierHelper.Generate("s");
                aliases[fragment.Sql] = alias;
                selectParts.Add($"{fragment.Sql} AS {sqlDialect.QuoteIdentifier(alias)}");
            }

            // ③ 分组层组装：派生表别名带随机段防撞（与 q/p 生成同规矩）
            var builder = new StringBuilder();
            builder.AppendLine($"SELECT {string.Join(", ", selectParts)} FROM (");
            builder.AppendLine(SqlFormatter.IndentLines(EntityQuerySqlHelpers.PrepareWrappableBaseSql(originalSql)));
            builder.AppendLine($") {SqlIdentifierHelper.Generate("t")}");
            builder.AppendLine($"GROUP BY {string.Join(", ", groupByParts)}");

            // ④ HAVING：多谓词 AND 合并（各谓词片段包裹括号，OR 优先级不被合并破坏）；
            //    参数名分配器预留源参数名域，HAVING 参数自动避让
            var havingParameters = new List<DbParameter>();
            if (havingPredicates.Count > 0)
            {
                var allocator = new ParameterNameAllocator(originalParameters, parameterBuilder.ParameterPrefix);
                var predicateFragments = new List<string>(havingPredicates.Count);
                foreach (var predicate in havingPredicates)
                {
                    var translation = AggregateSqlTranslator.TranslateHaving(predicate, entityMetadata, keyColumns, sqlDialect, parameterBuilder, allocator);
                    predicateFragments.Add($"({translation.Fragment})");
                    havingParameters.AddRange(translation.Parameters);
                }

                builder.AppendLine($"HAVING {string.Join(" AND ", predicateFragments)}");
            }

            // ⑤ 排序引用片段：键列 → 自然名引用、聚合 → 别名引用（仅暴露排序聚合时可生成，未暴露时排序被忽略方不生成）
            string orderByClause = null;
            if (exposeOrderingAggregates && orderings.Count > 0)
            {
                var parts = new string[orderings.Count];
                for (var i = 0; i < orderings.Count; i++)
                {
                    var ordering = orderings[i];
                    var reference = ordering.KeyColumn != null
                        ? sqlDialect.QuoteIdentifier(ordering.KeyColumn.ColumnName)
                        : sqlDialect.QuoteIdentifier(aliases[ordering.Aggregate.Sql]);
                    parts[i] = reference + (ordering.Descending ? " DESC" : " ASC");
                }

                orderByClause = string.Join(", ", parts);
            }

            return new GroupedLayer
            {
                Sql = builder.ToString(),
                Parameters = havingParameters.Count == 0
                    ? originalParameters
                    : originalParameters.Concat(havingParameters).ToArray(),
                OrderByClause = orderByClause,
                AggregateAliases = aliases,
            };
        }

        /// <summary>
        /// 终结执行前的窗口状态校验（无副作用）：窗口必须伴随排序、Skip 必须配 Take。
        /// <para>与 <see cref="EntityQuerySqlHelpers.ValidateWindowState"/> 同规则（分组排序链类型独立、无法直接复用列类型签名），
        /// 消息文案保持一致。</para>
        /// </summary>
        /// <param name="orderingCount">排序链长度。</param>
        /// <param name="skip">窗口跳过行数；null 表示未设置。</param>
        /// <param name="take">窗口取行数；null 表示未设置。</param>
        /// <returns>处于窗口态（Skip/Take 任一设置）时返回 true。</returns>
        internal static bool ValidateWindowState(int orderingCount, int? skip, int? take)
        {
            var hasWindowState = skip.HasValue || take.HasValue;
            if (hasWindowState && orderingCount == 0)
                throw new InvalidOperationException("窗口（Skip/Take）要求稳定行序，但当前查询未设置排序（OrderBy/OrderByDescending）；正常链式调用不可达此状态，终结端防御校验。");

            if (skip.HasValue && !take.HasValue)
                throw new InvalidOperationException("Skip 之后必须调用 Take 设置取行数，单独的 OFFSET 在不同数据库间合法性不一，且无界窗口的语义不完整。");

            return hasWindowState;
        }
    }

    /// <summary>
    /// 分组源查询实现：GroupBy 之后的链式构建层（Having 过滤、排序链、窗口），终结执行 CountAsync、分派 Select。
    /// <para>键列在 GroupBy 时解析（早期校验，方言无关）；HAVING 谓词与排序引用片段在终结构建时产出（方言相关）。</para>
    /// <para>所有链式方法复制状态新建实例（不可变构建器，与 EntityQuery 同规矩）。</para>
    /// </summary>
    /// <typeparam name="T">实体类型。</typeparam>
    /// <typeparam name="TKey">分组键类型。</typeparam>
    internal sealed class GroupedEntityQuery<T, TKey> : IGroupedEntityQuery<T, TKey> where T : class
    {
        private readonly IDbAccess dbAccess;
        private ISqlDialect sqlDialect => dbAccess.ProviderFactory.SqlDialect;
        private IDbParameterBuilder parameterBuilder => dbAccess.ProviderFactory.ParameterBuilder;
        private readonly EntityMetadata entityMetadata;
        private readonly string originalSql;// 原始 SQL 语句
        private readonly DbParameter[] originalParameters;// 源查询的原始查询参数
        private readonly List<GroupedKeyColumn> keyColumns;// 分组键列组
        private readonly List<LambdaExpression> havingPredicates;// HAVING 谓词组（AND 合并）
        private readonly List<GroupedOrderingTarget> orderings;// 分组排序目标链
        private readonly int? skip;// 窗口跳过组数（OFFSET 语义）
        private readonly int? take;// 窗口取组数（FETCH/LIMIT 语义）

        /// <summary>
        /// 以源查询的执行上下文与分组键列初始化分组查询。
        /// </summary>
        /// <param name="dbAccess">数据库访问对象。</param>
        /// <param name="sql">原始 SQL 语句。</param>
        /// <param name="parameters">查询参数。</param>
        /// <param name="keyColumns">分组键列组（GroupBy 时已解析）。</param>
        /// <param name="havingPredicates">HAVING 谓词组；null 归一为空列表。</param>
        /// <param name="orderings">分组排序目标链；null 归一为空列表。</param>
        /// <param name="skip">窗口跳过组数；null 表示未设置。</param>
        /// <param name="take">窗口取组数；null 表示未设置。</param>
        public GroupedEntityQuery(
            IDbAccess dbAccess,
            string sql,
            DbParameter[] parameters,
            List<GroupedKeyColumn> keyColumns,
            List<LambdaExpression> havingPredicates = null,
            List<GroupedOrderingTarget> orderings = null,
            int? skip = null,
            int? take = null)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            Guard.ThrowIfNullOrWhiteSpace(sql, nameof(sql));
            Guard.ThrowIfNullOrEmpty(keyColumns, nameof(keyColumns));

            this.dbAccess = dbAccess;
            originalSql = sql;
            originalParameters = parameters ?? Array.Empty<DbParameter>();
            entityMetadata = EntityMetadataCache<T>.Metadata;

            // 浅复制链状态：链式派生传入的列表与原实例解除引用共享，从机制上封死跨实例写
            this.keyColumns = new List<GroupedKeyColumn>(keyColumns);
            this.havingPredicates = havingPredicates != null
                ? new List<LambdaExpression>(havingPredicates)
                : new List<LambdaExpression>();
            this.orderings = orderings != null
                ? new List<GroupedOrderingTarget>(orderings)
                : new List<GroupedOrderingTarget>();
            this.skip = skip;
            this.take = take;
        }

        #region 分组过滤

        /// <summary>
        /// 分组后过滤（HAVING）：多次调用按 AND 合并，构造携带新谓词组的派生实例。
        /// </summary>
        public IGroupedEntityQuery<T, TKey> Having(Expression<Func<IGrouping<TKey, T>, bool>> predicate)
        {
            Guard.ThrowIfNull(predicate, nameof(predicate));

            var newPredicates = new List<LambdaExpression>(havingPredicates.Count + 1);
            newPredicates.AddRange(havingPredicates);
            newPredicates.Add(predicate);
            return Clone(newPredicates, orderings, skip, take);
        }

        #endregion

        #region 排序与窗口

        /// <summary>
        /// 设置分组结果排序（升序），开启新的排序链（多次调用时后者替换整条链，与 LINQ 语义一致）。
        /// </summary>
        public IGroupedEntityQuery<T, TKey> OrderBy<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> keySelector)
        {
            return OrderByCore(keySelector, false);
        }

        /// <summary>
        /// 设置分组结果排序（降序），开启新的排序链（多次调用时后者替换整条链，与 LINQ 语义一致）。
        /// </summary>
        public IGroupedEntityQuery<T, TKey> OrderByDescending<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> keySelector)
        {
            return OrderByCore(keySelector, true);
        }

        /// <summary>
        /// 在既有排序链末尾追加次级排序键（升序）。
        /// </summary>
        public IGroupedEntityQuery<T, TKey> ThenBy<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> keySelector)
        {
            return ThenByCore(keySelector, false);
        }

        /// <summary>
        /// 在既有排序链末尾追加次级排序键（降序）。
        /// </summary>
        public IGroupedEntityQuery<T, TKey> ThenByDescending<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> keySelector)
        {
            return ThenByCore(keySelector, true);
        }

        /// <summary>
        /// OrderBy/OrderByDescending 共用核心：解析排序目标后开启新的排序链，构造派生实例。
        /// </summary>
        private IGroupedEntityQuery<T, TKey> OrderByCore<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> keySelector, bool descending)
        {
            Guard.ThrowIfNull(keySelector, nameof(keySelector));

            var targets = AggregateSqlTranslator.ParseOrderingSelector(keySelector, keyColumns, entityMetadata, sqlDialect, descending);
            return Clone(havingPredicates, targets, skip, take);
        }

        /// <summary>
        /// ThenBy/ThenByDescending 共用核心：校验后向既有排序链末尾追加目标，构造派生实例。
        /// </summary>
        private IGroupedEntityQuery<T, TKey> ThenByCore<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> keySelector, bool descending)
        {
            Guard.ThrowIfNull(keySelector, nameof(keySelector));

            // 调用前置校验：必须已有排序链起点（OrderBy/OrderByDescending）
            if (orderings.Count == 0)
                throw new InvalidOperationException("ThenBy/ThenByDescending 必须在 OrderBy/OrderByDescending 之后调用，不能作为首个排序键。");

            var targets = AggregateSqlTranslator.ParseOrderingSelector(keySelector, keyColumns, entityMetadata, sqlDialect, descending);
            var newOrderings = new List<GroupedOrderingTarget>(orderings.Count + targets.Count);
            newOrderings.AddRange(orderings);
            newOrderings.AddRange(targets);
            return Clone(havingPredicates, newOrderings, skip, take);
        }

        /// <summary>
        /// 设置窗口跳过组数（要求已设置排序；必须配 Take 使用，终结方法执行时校验）。
        /// </summary>
        public IGroupedEntityQuery<T, TKey> Skip(int count)
        {
            // 调用前置校验：窗口要求稳定组序（无排序时窗口跨页/跨次执行会重复或丢组）
            if (orderings.Count == 0)
                throw new InvalidOperationException("Skip/Take 要求稳定行序，请先调用 OrderBy/OrderByDescending 设置排序后再使用窗口（Skip/Take）。");

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), count, "窗口跳过行数不能为负数。");

            return Clone(havingPredicates, orderings, count, take);
        }

        /// <summary>
        /// 设置窗口取组数（要求已设置排序；可单独使用，等价 offset 为 0 的窗口）。
        /// </summary>
        public IGroupedEntityQuery<T, TKey> Take(int count)
        {
            // 调用前置校验：窗口要求稳定组序（无排序时窗口跨页/跨次执行会重复或丢组）
            if (orderings.Count == 0)
                throw new InvalidOperationException("Skip/Take 要求稳定行序，请先调用 OrderBy/OrderByDescending 设置排序后再使用窗口（Skip/Take）。");

            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), count, "窗口行数必须为正数。");

            return Clone(havingPredicates, orderings, skip, count);
        }

        #endregion

        #region 终结执行

        /// <summary>
        /// 异步统计分组组数：SELECT COUNT(*) 包裹分组 SQL（HAVING 过滤生效），已设窗口时统计窗口内组数。
        /// <para>无窗口时忽略排序链（排序与组数无关，与 EntityQueryBase 聚合路径的约定一致）。</para>
        /// </summary>
        public async Task<long> CountAsync(CancellationToken cancellationToken = default)
        {
            var hasWindowState = GroupedLayerBuilder.ValidateWindowState(orderings.Count, skip, take);
            var layer = GroupedLayerBuilder.Build(
                dbAccess, originalSql, originalParameters, entityMetadata,
                keyColumns, havingPredicates, orderings,
                projectionAggregates: null,
                exposeOrderingAggregates: hasWindowState);

            var innerSql = layer.Sql;
            if (hasWindowState)
            {
                // 窗口套在分组结果外（行序无关，走无行序模式：ROW_NUMBER 兼容形态的尾部裸 ORDER BY 在派生表/COUNT 包裹内不可用）
                innerSql = await EntityQuerySqlHelpers.BuildWindowSqlAsync(
                    dbAccess,
                    innerSql, skip ?? 0, take.Value,
                    layer.OrderByClause,
                    appendRowOrder: false).ConfigureAwait(false);
            }

            // COUNT 的结果经实体列值转换链归一（不同驱动返回 int/long/decimal 不一）
            var builder = new StringBuilder();
            builder.AppendLine("SELECT COUNT(*) FROM (");
            builder.AppendLine(SqlFormatter.IndentLines(innerSql));
            builder.AppendLine($") {SqlIdentifierHelper.Generate("q")}");

            var result = await dbAccess.ExecuteScalarAsync(builder.ToString(), layer.Parameters, cancellationToken).ConfigureAwait(false);
            return EntityValueConverter.ConvertFrom<long>(result);
        }

        /// <summary>
        /// 分组投影：解析选择器为输出列组（键列引用/聚合片段）后构造投影终结执行器。
        /// </summary>
        public IProjectedEntityQuery<TResult> Select<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> selector)
        {
            Guard.ThrowIfNull(selector, nameof(selector));

            var (outputColumns, members, constructor, shape) = AggregateSqlTranslator.ParseSelectSelector(
                selector, keyColumns, typeof(TResult), entityMetadata, sqlDialect);
            return new GroupedProjectionQuery<T, TResult>(
                dbAccess, originalSql, originalParameters,
                keyColumns, havingPredicates, orderings, skip, take,
                outputColumns, members, constructor, shape);
        }

        #endregion

        #region 派生实例

        /// <summary>
        /// 创建携带指定链状态的派生分组查询实例（不可变构建器）。
        /// </summary>
        private IGroupedEntityQuery<T, TKey> Clone(
            List<LambdaExpression> havingPredicates,
            List<GroupedOrderingTarget> orderings,
            int? skip,
            int? take)
        {
            return new GroupedEntityQuery<T, TKey>(dbAccess, originalSql, originalParameters, keyColumns, havingPredicates, orderings, skip, take);
        }

        #endregion
    }
}
