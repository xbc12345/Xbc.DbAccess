using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 分组投影执行器（键列、聚合列与键/聚合多列组合）：以分组层 SQL（SELECT 键列与聚合别名 + GROUP BY/HAVING）为源，
    /// 外层按 Select 项配对序裁剪（SELECT 项引用 FROM (分组层) p），复用分组排序与窗口路径；
    /// 列值读取经 <see cref="EntityMapper{T}"/> 的统一投影 mapper（COUNT 类聚合列无源列元数据，走 mapper 裸读取分支）。
    /// <para>执行骨架与 <see cref="ProjectedEntityQuery{T, TResult}"/> 逐行同构（单条截断/全量投影两段构建、派生表包裹追加外层排序），
    /// 差异仅 SQL 组装层与参数清单（HAVING 参数并入执行参数）。</para>
    /// </summary>
    /// <typeparam name="T">投影源实体类型。</typeparam>
    /// <typeparam name="TResult">投影结果类型：单列为键/聚合值类型，多列为匿名类型、元组（ValueTuple）、带参构造的命名类型（编译器按实参绑定的构造函数）或命名类型（无参构造 + 对象初始化器）。</typeparam>
    internal sealed class GroupedProjectionQuery<T, TResult> : IProjectedEntityQuery<TResult> where T : class
    {
        private readonly IDbAccess dbAccess;
        private ISqlDialect sqlDialect => dbAccess.ProviderFactory.SqlDialect;
        private readonly string originalSql;// 原始 SQL 语句
        private readonly DbParameter[] originalParameters;// 源查询的原始查询参数
        private readonly List<GroupedKeyColumn> keyColumns;// 分组键列组
        private readonly List<LambdaExpression> havingPredicates;// HAVING 谓词组（AND 合并）
        private readonly List<GroupedOrderingTarget> orderings;// 分组排序目标链
        private readonly int? skip;// 窗口跳过组数（OFFSET 语义）
        private readonly int? take;// 窗口取组数（FETCH/LIMIT 语义）
        private readonly GroupedOutputColumn[] outputColumns;// 分组投影输出列（构建期已解析，按序对应结果集各列）
        private readonly ProjectionPlan plan;// 投影映射计划（结果形态 + 投影列 + 成员绑定；COUNT 类聚合列为 null 走裸读取）

        /// <summary>
        /// 以源查询的执行上下文、分组链状态与投影输出列初始化分组投影执行器。
        /// </summary>
        /// <param name="dbAccess">数据库访问对象。</param>
        /// <param name="sql">原始 SQL 语句。</param>
        /// <param name="parameters">查询参数。</param>
        /// <param name="keyColumns">分组键列组。</param>
        /// <param name="havingPredicates">HAVING 谓词组。</param>
        /// <param name="orderings">分组排序目标链。</param>
        /// <param name="skip">窗口跳过组数；null 表示未设置。</param>
        /// <param name="take">窗口取组数；null 表示未设置。</param>
        /// <param name="outputColumns">分组投影输出列组（Select 时已解析）。</param>
        /// <param name="members">命名类型初始化器各列绑定的目标成员（解析产物）；位置构造（匿名类型/元组/命名构造）/单列为 null。</param>
        /// <param name="constructor">命名类型带参构造绑定的构造函数（编译器按实参绑定）；其余形态为 null。</param>
        /// <param name="shape">投影结果形态（解析层按选择器形态判定）。</param>
        public GroupedProjectionQuery(
            IDbAccess dbAccess,
            string sql,
            DbParameter[] parameters,
            List<GroupedKeyColumn> keyColumns,
            List<LambdaExpression> havingPredicates,
            List<GroupedOrderingTarget> orderings,
            int? skip,
            int? take,
            GroupedOutputColumn[] outputColumns,
            MemberInfo[] members,
            ConstructorInfo constructor,
            ProjectionPlan.Shape shape)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            Guard.ThrowIfNullOrWhiteSpace(sql, nameof(sql));
            Guard.ThrowIfNullOrEmpty(keyColumns, nameof(keyColumns));
            Guard.ThrowIfNullOrEmpty(outputColumns, nameof(outputColumns));

            this.dbAccess = dbAccess;
            originalSql = sql;
            originalParameters = parameters ?? Array.Empty<DbParameter>();

            this.keyColumns = new List<GroupedKeyColumn>(keyColumns);
            this.havingPredicates = havingPredicates != null
                ? new List<LambdaExpression>(havingPredicates)
                : new List<LambdaExpression>();
            this.orderings = orderings != null
                ? new List<GroupedOrderingTarget>(orderings)
                : new List<GroupedOrderingTarget>();
            this.skip = skip;
            this.take = take;
            this.outputColumns = (GroupedOutputColumn[])outputColumns.Clone();

            // 投影映射计划：键列/聚合源列直用（COUNT 类聚合为 null，mapper 裸读取分支），形态与绑定构造函数由解析层按选择器判定
            var mapperColumns = this.outputColumns.Select(o => o.Column).ToArray();
            plan = shape switch
            {
                ProjectionPlan.Shape.Scalar => ProjectionPlan.Scalar(mapperColumns),
                ProjectionPlan.Shape.MemberBinding => ProjectionPlan.MemberBinding(mapperColumns, (MemberInfo[])members?.Clone()),
                _ => ProjectionPlan.Positional(mapperColumns, constructor ?? ResolveSinglePublicConstructor())
            };
        }

        /// <summary>
        /// 解析匿名/元组位置构造形态的唯一公共构造函数，统一固化为计划携带。
        /// 匿名类型由编译器保证唯一公共构造；元组经解析层 TRest 守卫后唯一（8 元嵌套形态已在构建期拒绝，此处防御手搭穿透）。
        /// </summary>
        private ConstructorInfo ResolveSinglePublicConstructor()
        {
            // 元组的 TRest 嵌套守卫（8 元素及以上）：构造第 8 参为嵌套 ValueTuple 而非元素，按列位置装配无法承载
            if (TypeHelper.IsOverLengthValueTuple(typeof(TResult)))
                throw new NotSupportedException("暂不支持 8 元素及以上的元组投影结果类型（TRest 嵌套构造），请使用 7 元素以内的元组或匿名类型。");

            var constructors = typeof(TResult).GetConstructors();
            if (constructors.Length != 1)
                throw new NotSupportedException($"位置构造投影的结果类型 '{typeof(TResult)}' 必须恰有一个公共构造函数（按构造参数位置映射列）。");

            return constructors[0];
        }

        /// <inheritdoc/>
        public Task<TResult> FirstAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteSingleAsync(true, false, cancellationToken);
        }

        /// <inheritdoc/>
        public Task<TResult> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteSingleAsync(false, false, cancellationToken);
        }

        /// <inheritdoc/>
        public Task<TResult> SingleAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteSingleAsync(true, true, cancellationToken);
        }

        /// <inheritdoc/>
        public Task<TResult> SingleOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteSingleAsync(false, true, cancellationToken);
        }

        /// <summary>
        /// 执行单条分组投影记录读取，按参数决定空结果是否抛异常、是否检测多条。
        /// <para>排序/窗口状态存在时改写为方言窗口分组投影 SQL（排序 + 限量截取 + 派生表裁剪包裹）；无状态时执行全量分组投影 SQL 流式直读。</para>
        /// </summary>
        /// <param name="throwIfEmpty">是否在查询结果为空时抛出异常。</param>
        /// <param name="ensureSingle">是否确保查询结果仅包含一条记录。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        private async Task<TResult> ExecuteSingleAsync(bool throwIfEmpty, bool ensureSingle, CancellationToken cancellationToken)
        {
            string executedSql;
            DbParameter[] executedParameters;

            // 校验并取窗口态：纯排序无窗口时同样走窗口构建（单条路径总要截断）
            var hasWindowState = GroupedLayerBuilder.ValidateWindowState(orderings.Count, skip, take);
            if (hasWindowState || orderings.Count > 0)
            {
                // 排序与限量截取在各方言里是一个语法单元（LIMIT/FETCH/ROW_NUMBER 均强制排序），改写后数据库走
                // top-N 计划流式中途即停，避免全量排序后逐行流出；Single 系取 2 行用于多条检测
                var fetchLimit = ensureSingle ? 2 : 1;
                var fetch = take.HasValue ? Math.Min(take.Value, fetchLimit) : (int?)fetchLimit;
                (executedSql, executedParameters) = await BuildSingleProjectionSqlAsync(cancellationToken, fetch.Value).ConfigureAwait(false);
            }
            else
            {
                // 无排序无窗口时流式直读：无行序需兑现，服务端无物化代价
                (executedSql, executedParameters) = await BuildProjectionSqlAsync(cancellationToken).ConfigureAwait(false);
            }

            return await ExecuteSingleCoreAsync(executedSql, executedParameters, throwIfEmpty, ensureSingle, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 以指定分组投影 SQL 执行单条记录读取（列值经投影 mapper 读取，COUNT 类聚合列走裸读取分支）。
        /// </summary>
        private async Task<TResult> ExecuteSingleCoreAsync(string sql, DbParameter[] parameters, bool throwIfEmpty, bool ensureSingle, CancellationToken cancellationToken)
        {
            using (var reader = await dbAccess.ExecuteReaderAsync(sql, parameters, cancellationToken).ConfigureAwait(false))
            {
                var (hasRow, value) = await EntityMapper<T>.MapProjectionAsync<TResult>(plan, reader, cancellationToken).ConfigureAwait(false);
                if (!hasRow)
                {
                    if (throwIfEmpty)
                        throw new InvalidOperationException("查询结果不包含任何记录。");

                    return default;
                }

                if (ensureSingle && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new InvalidOperationException("查询结果包含多条记录。");

                return value;
            }
        }

        /// <inheritdoc/>
        public async Task<List<TResult>> ToListAsync(CancellationToken cancellationToken = default)
        {
            // 校验并取窗口态
            GroupedLayerBuilder.ValidateWindowState(orderings.Count, skip, take);
            var (executedSql, executedParameters) = await BuildProjectionSqlAsync(cancellationToken).ConfigureAwait(false);

            using (var reader = await dbAccess.ExecuteReaderAsync(executedSql, executedParameters, cancellationToken).ConfigureAwait(false))
            {
                // 列已在构建期解析，直接以列元数据组取映射委托，免每次执行重复解析选择器
                return await EntityMapper<T>.MapProjectionListAsync<TResult>(plan, reader, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 构建单条截断的分组投影 SQL：分组层经排序截取后派生表裁剪包裹（仅 ExecuteSingleAsync 窗口分支调用）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="fetch">截取组数，由调用方算好——Single 系 2 组（多条检测）与查询级 Take 的交集、First 系 1 组；调用前提为排序链非空（窗口态强制排序或纯排序），截断与排序是各方言的语法单元不可拆分。</param>
        /// <returns>分组投影 SQL 与执行参数。</returns>
        private async Task<(string Sql, DbParameter[] Parameters)> BuildSingleProjectionSqlAsync(CancellationToken cancellationToken, int fetch)
        {
            var layer = BuildGroupedLayer();
            var windowedSql = await EntityQuerySqlHelpers.BuildWindowSqlAsync(
                dbAccess,
                layer.Sql, skip ?? 0, fetch,
                layer.OrderByClause,
                appendRowOrder: false).ConfigureAwait(false);
            return (WrapTrimSelect(windowedSql, layer), layer.Parameters);
        }

        /// <summary>
        /// 构建全量分组投影 SQL：分组层经窗口处理（手写 ORDER BY 检测同终结方法），再派生表裁剪包裹（ToListAsync 与单条无状态分支共用）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>分组投影 SQL 与执行参数。</returns>
        private async Task<(string Sql, DbParameter[] Parameters)> BuildProjectionSqlAsync(CancellationToken cancellationToken)
        {
            var layer = BuildGroupedLayer();

            // 窗口分支（窗口态已由入口校验）：截取包在分组结果外层（排序引用片段作为窗口 orderBy——键列名/聚合别名在包裹层可见），
            // 走无行序模式（appendRowOrder=false）（ROW_NUMBER 兼容形态的尾部裸 ORDER BY 在派生表内是语法错误），行序由包裹层追加的排序兑现
            if (skip.HasValue || take.HasValue)
            {
                var windowedSql = await EntityQuerySqlHelpers.BuildWindowSqlAsync(
                    dbAccess,
                    layer.Sql, skip ?? 0, take.Value,
                    layer.OrderByClause,
                    appendRowOrder: false).ConfigureAwait(false);
                return (WrapTrimSelect(windowedSql, layer), layer.Parameters);
            }

            // 无窗口态（纯排序或无状态）：派生表裁剪包裹 Select 项引用，排序由包裹层按排序引用片段追加
            return (WrapTrimSelect(layer.Sql, layer), layer.Parameters);
        }

        /// <summary>
        /// 构建分组层 SQL：投影聚合与排序目标聚合一并暴露（别名分配），HAVING 参数并入执行参数。
        /// </summary>
        private GroupedLayerBuilder.GroupedLayer BuildGroupedLayer()
        {
            var projectionAggregates = outputColumns
                .Where(o => o.Aggregate != null)
                .Select(o => o.Aggregate)
                .ToList();
            return GroupedLayerBuilder.Build(
                dbAccess, originalSql, originalParameters, EntityMetadataCache<T>.Metadata,
                keyColumns, havingPredicates, orderings,
                projectionAggregates,
                exposeOrderingAggregates: true);
        }

        /// <summary>
        /// 以派生表包裹分组层（SELECT Select 项引用 FROM (分组层 SQL) 别名）；Select 项引用为键列自然名与聚合生成别名
        /// （结构性防注入），分组层暴露的超集在此外层裁剪为恰好 Select 项数列，位置映射契约由此保持。
        /// <para>有排序时在外层追加 ORDER BY 兑现行序（窗口层 appendRowOrder=false 的行序契约在此补齐）。</para>
        /// </summary>
        private string WrapTrimSelect(string innerSql, GroupedLayerBuilder.GroupedLayer layer)
        {
            var builder = new StringBuilder();
            var selectReferences = new string[outputColumns.Length];
            for (var i = 0; i < outputColumns.Length; i++)
            {
                var output = outputColumns[i];
                selectReferences[i] = output.Aggregate != null
                    ? sqlDialect.QuoteIdentifier(layer.AggregateAliases[output.Aggregate.Sql])
                    : sqlDialect.QuoteIdentifier(output.Column.ColumnName);
            }

            builder.AppendLine($"SELECT {string.Join(", ", selectReferences)} FROM (");
            builder.AppendLine(SqlFormatter.IndentLines(innerSql));
            builder.AppendLine($") {SqlIdentifierHelper.Generate("p")}");

            if (layer.OrderByClause != null)
                builder.AppendLine($"ORDER BY {layer.OrderByClause}");

            return builder.ToString();
        }
    }
}
