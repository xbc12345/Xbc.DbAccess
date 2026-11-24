using System.Data.Common;
using System.Text;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 投影查询执行器（单列、匿名类型/元组多列与命名类型初始化器）：以派生表包裹源查询（SELECT 列组 FROM (查询) p_xxx），复用源查询的排序/窗口路径；
    /// 列值读取经 <see cref="EntityMapper{T}"/> 的统一投影 mapper（单列为 1 个元素的特例），与实体赋值的转换策略一致。
    /// </summary>
    /// <typeparam name="T">投影源实体类型。</typeparam>
    /// <typeparam name="TResult">投影结果类型：单列为属性类型，多列为匿名类型、元组（ValueTuple）或命名类型（无参构造 + 对象初始化器）。</typeparam>
    internal sealed class ProjectedEntityQuery<T, TResult> : IProjectedEntityQuery<TResult> where T : class
    {
        private readonly IDbAccess dbAccess;
        private ISqlDialect sqlDialect => dbAccess.ProviderFactory.SqlDialect;
        private readonly string originalSql;// 原始 SQL 语句
        private readonly DbParameter[] originalParameters;// 源查询的原始查询参数
        private readonly List<(ColumnMetadata Column, bool Descending)> orderings;// 源查询的排序链
        private readonly int? skip;// 窗口跳过行数（OFFSET 语义）
        private readonly int? take;// 窗口取行数（FETCH/LIMIT 语义）
        private readonly ProjectionPlan plan;// 投影映射计划（结果形态 + 投影列 + 成员绑定，构建期已解析，列序 = 结果集列序）

        /// <summary>
        /// 以源查询的执行上下文与排序/窗口状态初始化投影执行器。
        /// </summary>
        /// <param name="dbAccess">数据库访问对象。</param>
        /// <param name="sql">原始 SQL 语句。</param>
        /// <param name="parameters">查询参数。</param>
        /// <param name="orderings">源查询的排序链。</param>
        /// <param name="skip">窗口跳过行数；null 表示未设置。</param>
        /// <param name="take">窗口取行数；null 表示未设置。</param>
        /// <param name="plan">投影映射计划（结果形态 + 投影列组 + 成员绑定）。</param>
        public ProjectedEntityQuery(
            IDbAccess dbAccess,
            string sql,
            DbParameter[] parameters,
            List<(ColumnMetadata Column, bool Descending)> orderings,
            int? skip,
            int? take,
            ProjectionPlan plan)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            Guard.ThrowIfNullOrWhiteSpace(sql, nameof(sql));
            Guard.ThrowIfNull(plan, nameof(plan));

            this.dbAccess = dbAccess;
            originalSql = sql;
            originalParameters = parameters ?? Array.Empty<DbParameter>();

            this.orderings = orderings != null
                ? new List<(ColumnMetadata, bool)>(orderings)
                : new List<(ColumnMetadata, bool)>();
            this.skip = skip;
            this.take = take;
            this.plan = plan;
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
        /// 执行单条投影记录读取，按参数决定空结果是否抛异常、是否检测多条。
        /// <para>排序/窗口状态存在时改写为方言窗口投影 SQL（排序 + 限量截取 + 派生表包裹）；无状态时执行全量投影 SQL 流式直读。</para>
        /// </summary>
        /// <param name="throwIfEmpty">是否在查询结果为空时抛出异常。</param>
        /// <param name="ensureSingle">是否确保查询结果仅包含一条记录。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        private async Task<TResult> ExecuteSingleAsync(bool throwIfEmpty, bool ensureSingle, CancellationToken cancellationToken)
        {
            string executedSql;

            // 校验并取窗口态：纯排序无窗口时同样走窗口构建（单条路径总要截断）
            var hasWindowState = EntityQuerySqlHelpers.ValidateWindowState(orderings, skip, take);
            if (hasWindowState || orderings.Count > 0)
            {
                // 排序与限量截取在各方言里是一个语法单元（LIMIT/FETCH/ROW_NUMBER 均强制排序），改写后数据库走
                // top-N 计划流式中途即停，避免全量排序后逐行流出；Single 系取 2 行用于多条检测
                var fetchLimit = ensureSingle ? 2 : 1;
                var fetch = take.HasValue ? Math.Min(take.Value, fetchLimit) : (int?)fetchLimit;
                executedSql = await BuildSingleProjectionSqlAsync(fetch.Value).ConfigureAwait(false);
            }
            else
            {
                // 无排序无窗口时流式直读：无行序需兑现，服务端无物化代价；截断需按方言改写 SELECT 结构
                // （TOP 1/ROWNUM 等），风险大于「提前终止扫描」的有限收益，故不截断
                executedSql = await BuildProjectionSqlAsync().ConfigureAwait(false);
            }

            return await ExecuteSingleCoreAsync(executedSql, originalParameters, throwIfEmpty, ensureSingle, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 以指定投影 SQL 执行单条记录读取（列值经投影 mapper 读取）。
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
            EntityQuerySqlHelpers.ValidateWindowState(orderings, skip, take);
            var executedSql = await BuildProjectionSqlAsync().ConfigureAwait(false);

            using (var reader = await dbAccess.ExecuteReaderAsync(executedSql, originalParameters, cancellationToken).ConfigureAwait(false))
            {
                // 列已在构建期解析，直接以列元数据组取映射委托，免每次执行重复解析选择器
                return await EntityMapper<T>.MapProjectionListAsync<TResult>(plan, reader, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 构建单条截断的投影 SQL：源查询经排序截取后派生表包裹投影列（仅 ExecuteSingleAsync 窗口分支调用）。
        /// </summary>
        /// <param name="fetch">截取行数，由调用方算好——Single 系 2 行（多条检测）与查询级 Take 的交集、First 系 1 行；调用前提为排序链非空（窗口态强制排序或纯排序），截断与排序是各方言的语法单元不可拆分。</param>
        /// <returns>投影 SQL。</returns>
        private async Task<string> BuildSingleProjectionSqlAsync(int fetch)
        {
            var innerSql = EntityQuerySqlHelpers.PrepareWrappableBaseSql(originalSql);
            var windowedSql = await EntityQuerySqlHelpers.BuildWindowSqlAsync(
                dbAccess,
                innerSql, skip ?? 0, fetch,
                EntityQuerySqlHelpers.BuildOrderClause(orderings, sqlDialect),
                appendRowOrder: false).ConfigureAwait(false);
            return WrapDerivedTable(windowedSql);
        }

        /// <summary>
        /// 构建全量投影 SQL：源查询经窗口处理（手写 ORDER BY 检测同终结方法），再派生表包裹投影列（ToListAsync 与单条无状态分支共用）。
        /// </summary>
        /// <returns>投影 SQL。</returns>
        private async Task<string> BuildProjectionSqlAsync()
        {
            var innerSql = EntityQuerySqlHelpers.PrepareWrappableBaseSql(originalSql);

            // 窗口分支（窗口态已由入口校验）：截取包在源查询内层（ROW_NUMBER 方言的排序列须在内层可见），
            // 走无行序模式（appendRowOrder=false）（ROW_NUMBER 兼容形态的尾部裸 ORDER BY 在派生表内是语法错误），行序由包裹层追加的排序兑现
            if (skip.HasValue || take.HasValue)
            {
                var windowedSql = await EntityQuerySqlHelpers.BuildWindowSqlAsync(
                    dbAccess,
                    innerSql, skip ?? 0, take.Value,
                    EntityQuerySqlHelpers.BuildOrderClause(orderings, sqlDialect),
                    appendRowOrder: false).ConfigureAwait(false);
                return WrapDerivedTable(windowedSql);
            }

            // 无窗口态（纯排序或无状态）：派生表包裹投影列，排序由包裹层按排序链追加
            return WrapDerivedTable(innerSql);
        }

        /// <summary>
        /// 以派生表包裹内层查询（SELECT 投影列组 FROM (内层 SQL) 别名）；投影列清单逐列方言引用（结构性防注入）。
        /// <para>有查询级排序时在外层追加 ORDER BY 兑现行序（窗口层 appendRowOrder=false 的行序契约在此补齐）。</para>
        /// </summary>
        private string WrapDerivedTable(string innerSql)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"SELECT {string.Join(", ", plan.Columns.Select(c => sqlDialect.QuoteIdentifier(c.ColumnName)))} FROM (");
            builder.AppendLine(SqlFormatter.IndentLines(innerSql));
            builder.AppendLine($") {SqlIdentifierHelper.Generate("p")}");

            if (orderings.Count > 0)
                builder.AppendLine($"ORDER BY {EntityQuerySqlHelpers.BuildOrderClause(orderings, sqlDialect)}");

            return builder.ToString();
        }
    }
}
