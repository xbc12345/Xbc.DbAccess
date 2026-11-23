using System.Data.Common;
using System.Linq.Expressions;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 可包含导航属性的实体查询实现，持有导航链列表，支持多级导航的分裂查询加载。
    /// <para>装配不做身份解析：同一数据库行在根列表与导航属性中是不同对象实例，修改互不反映；
    /// 1:N 集合的关联查询不带 ORDER BY，元素顺序不保证稳定，需稳定顺序时对装配结果自行排序。</para>
    /// </summary>
    /// <typeparam name="T">根实体类型。</typeparam>
    /// <typeparam name="TPrevious">上一级导航属性的类型。</typeparam>
    internal sealed class IncludableEntityQuery<T, TPrevious> : EntityQueryBase<T>,
        IIncludableEntityQuery<T, TPrevious>,
        IIncludableQueryDerivation
        where T : class
    {
        private readonly List<NavigationInclude> includes;
        private readonly NavigationInclude chainTip;// 导航链链尾节点（ThenInclude 挂载点）

        protected override bool HasNavigations => true;

        /// <summary>
        /// 初始化查询实例。
        /// </summary>
        public IncludableEntityQuery(
            IDbAccess dbAccess,
            string sql,
            DbParameter[] parameters,
            List<NavigationInclude> includes,
            NavigationInclude chainTip,
            List<(ColumnMetadata Column, bool Descending)> orderings = null,
            int? skip = null,
            int? take = null)
            : base(dbAccess, sql, parameters, orderings, skip, take)
        {
            this.includes = includes ?? throw new ArgumentNullException(nameof(includes));
            this.chainTip = chainTip ?? throw new ArgumentNullException(nameof(chainTip));
        }

        #region 导航链构建
        /// <summary>
        /// 从根实体开始新的导航链。
        /// </summary>
        /// <typeparam name="TResult">导航属性类型。</typeparam>
        /// <param name="selector">导航属性选择器表达式。</param>
        /// <returns>新的导航链查询实例。</returns>
        public IIncludableEntityQuery<T, TResult> Include<TResult>(Expression<Func<T, TResult>> selector)
        {
            Guard.ThrowIfNull(selector, nameof(selector));

            // 创建新的根导航节点（Parent 为 null，因为 Include 始终从根实体开始）
            var navigation = entityMetadata.ResolveNavigation(selector);
            var newNavInclude = new NavigationInclude(navigation, null);

            // 去重：已存在相同路径的根导航则复用节点，链尾指向既有节点（保证后续 ThenInclude 挂载与装配共享同一节点）
            var existing = includes.FirstOrDefault(i => i.Parent == null && i.Path == newNavInclude.Path);
            if (existing != null)
                return new IncludableEntityQuery<T, TResult>(dbAccess, originalSql, originalParameters,
                    new List<NavigationInclude>(includes), existing, orderings, skip, take);

            // 复制导航链并追加新导航
            var newNavIncludes = new List<NavigationInclude>(includes.Count + 1);
            newNavIncludes.AddRange(includes);
            newNavIncludes.Add(newNavInclude);

            // 创建新的导航链查询实例，链尾指向新追加的根导航
            return new IncludableEntityQuery<T, TResult>(dbAccess, originalSql, originalParameters,
                newNavIncludes, newNavInclude, orderings, skip, take);
        }

        IIncludableEntityQuery<TEntity, TResult> IIncludableQueryDerivation.WithNextNavigation<TEntity, TResult>(LambdaExpression selector)
        {
            Guard.ThrowIfNull(selector, nameof(selector));

            // 用链尾节点作挂载点；不能用列表最后一个节点：Include 命中去重时不追加节点，列表尾与链尾可能不一致
            var previousNav = chainTip;

            // 运行时校验选择器参数类型与链尾导航的对方实体类型匹配，防止配置错误静默挂错链
            var previousTargetType = previousNav.NavigationMetadata.TargetType;
            if (!previousTargetType.IsAssignableFrom(selector.Parameters[0].Type))
                throw new InvalidOperationException(
                    $"ThenInclude 的选择器参数类型 '{selector.Parameters[0].Type}' 与" +
                    $"导航链尾 '{previousNav.Path}' 的实体类型 '{previousTargetType}' 不匹配，" +
                    $"请确保选择器以上一级导航的实体类型为参数。");

            var targetMetadata = previousNav.NavigationMetadata.TargetEntityMetadata;
            var navigation = targetMetadata.ResolveNavigation(selector);
            var newNavInclude = new NavigationInclude(navigation, previousNav);

            // 去重：已存在相同路径则复用，链尾指向既有节点
            var existing = includes.FirstOrDefault(i => i.Path == newNavInclude.Path);
            if (existing != null)
                return new IncludableEntityQuery<TEntity, TResult>(dbAccess, originalSql, originalParameters,
                    new List<NavigationInclude>(includes), existing, orderings, skip, take);

            var newNavIncludes = new List<NavigationInclude>(includes.Count + 1);
            newNavIncludes.AddRange(includes);
            newNavIncludes.Add(newNavInclude);

            return new IncludableEntityQuery<TEntity, TResult>(dbAccess, originalSql, originalParameters,
                newNavIncludes, newNavInclude, orderings, skip, take);
        }
        #endregion

        #region 导航装配
        /// <summary>
        /// 从查询结果中填充导航属性，异步版本。
        /// </summary>
        /// <param name="entities">查询结果实体列表。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        protected override async Task PopulateNavigationsAsync(List<T> entities, CancellationToken cancellationToken)
        {
            if (entities == null || entities.Count == 0)
                return;

            var levels = BuildNavigationLevels();

            // 缓存每个导航链节点对应的实体列表，避免下级导航从头遍历
            var entityCache = new Dictionary<NavigationInclude, List<object>>(includes.Count);

            // 根导航的本方实体为查询结果，下级导航的本方实体取自缓存的父级节点列表
            foreach (var level in levels)
            {
                foreach (var include in level)
                {
                    var parentEntities = include.Parent == null
                        ? (IReadOnlyList<object>)entities// List<T> 经 IReadOnlyList<out T> 协变转换
                        : entityCache.TryGetValue(include.Parent, out var parents) ? parents : null;
                    if (parentEntities == null || parentEntities.Count == 0)
                        continue;

                    await PopulateNavigationAsync(include, parentEntities, entityCache, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// 构建导航属性的层级列表，确保父级在子级之前执行。
        /// <para>例如 Customer.Include(x => x.Orders).ThenInclude(x => x.OrderDetails) 产生：</para>
        /// <list type="bullet">
        ///   <item>Level 0: [Orders]       （Parent == null，根导航）</item>
        ///   <item>Level 1: [OrderDetails] （Parent == Orders，第2级导航）</item>
        /// </list>
        /// <para>
        /// 分裂查询（Split Query）按层级顺序执行 SQL：先查主表拿主键，再用主键查第1级导航，
        /// 用第1级的外键查第2级，逐层向下。
        /// </para>
        /// </summary>
        private List<List<NavigationInclude>> BuildNavigationLevels()
        {
            var levels = new List<List<NavigationInclude>>();
            var currentLevel = includes.Where(i => i.Parent == null).ToList();// 根导航

            while (currentLevel.Count > 0)
            {
                // 当前层导航链
                levels.Add(currentLevel);

                // 下一层：父级在当前层中的导航属性
                currentLevel = includes.Where(i => i.Parent != null && currentLevel.Contains(i.Parent)).ToList();
            }

            return levels;
        }

        /// <summary>
        /// 填充单个导航属性的关联数据。
        /// <para>按提供程序参数数量上限分批执行 IN 查询，先收集全部批次再统一装配一次：逐批装配会让后续批次以空列表覆盖前批已装配的集合导航。</para>
        /// </summary>
        /// <param name="include">导航属性包含项。</param>
        /// <param name="parentEntities">本方实体列表（根导航为查询结果，下级导航为缓存的父级节点列表）。</param>
        /// <param name="entityCache">导航链节点对应的实体列表缓存。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private async Task PopulateNavigationAsync(
            NavigationInclude include,
            IReadOnlyList<object> parentEntities,
            Dictionary<NavigationInclude, List<object>> entityCache,
            CancellationToken cancellationToken)
        {
            // 从本方实体列表提取 ThisKey 键值（去重），用于查询关联数据
            var nav = include.NavigationMetadata;
            var keyValues = ExtractKeyValues(parentEntities, nav);
            if (keyValues.Count == 0)
                return;

            // IN 查询每个键值占一个参数，按提供程序参数上限分批
            var maxKeysPerBatch = sqlDialect.GetMaxKeysPerInClauseBatch();

            // 包裹与批次无关，循环外构建一次；对方键列：1:N 为对方外键，1:1 为对方主键
            var targetTable = sqlDialect.QuoteTableName(nav.TargetEntityMetadata.Schema, nav.TargetEntityMetadata.TableName);
            var otherKeyCol = sqlDialect.QuoteIdentifier(nav.OtherKeyColumn.ColumnName);
            var relatedEntities = new List<object>();

            for (int i = 0; i < keyValues.Count; i += maxKeysPerBatch)
            {
                var batchCount = Math.Min(maxKeysPerBatch, keyValues.Count - i);

                // 直接构建参数与参数名列表，不走匿名对象参数化，避免每批次分配与反射开销；
                // 参数名按批次内序号组合（如 @CustomerId_0），满批次间 SQL 文本相同，列名经分配器清洗
                var allocator = new ParameterNameAllocator();
                var parameters = new DbParameter[batchCount];
                var parameterNames = new string[batchCount];

                for (int j = 0; j < batchCount; j++)
                {
                    var name = parameterBuilder.GetParameterName(allocator.Allocate($"{nav.OtherKeyColumn.ColumnName}_{j}"));
                    parameterNames[j] = name;
                    parameters[j] = parameterBuilder.CreateParameter(name, keyValues[i + j]);
                }

                var relatedSql = sqlBuilder.BuildSelectSql(targetTable, null, $"{otherKeyCol} IN ({string.Join(", ", parameterNames)})");
                using (var reader = await dbAccess.ExecuteReaderAsync(relatedSql, parameters, cancellationToken).ConfigureAwait(false))
                {
                    // 分批只负责读取关联实体，统一累积供装配与下级导航使用
                    var batchRelated = await EntityMapper.MapListAsync(nav.TargetType, reader, cancellationToken).ConfigureAwait(false);
                    relatedEntities.AddRange(batchRelated);
                }
            }

            // 全部批次读取完毕后统一装配一次，本方实体仅遍历一遍
            if (nav.NavigationType == NavigationType.OneToOne)
                NavigationAssembler.PopulateReferences(parentEntities, nav, relatedEntities);
            else
                NavigationAssembler.PopulateChildren(parentEntities, nav, relatedEntities);

            entityCache[include] = relatedEntities;
        }

        /// <summary>
        /// 从本方实体列表提取 ThisKey 键值（去重，经 <see cref="ColumnMetadata.GetValueForDb"/> 转换为数据库值域）。
        /// <para>1:1 的本方键是可能带存储变换的外键列（自定义转换器/枚举/JSON），原始 .NET 值直接参数化会静默不命中。</para>
        /// </summary>
        /// <param name="entities">本方实体列表。</param>
        /// <param name="navigation">导航属性元数据。</param>
        /// <returns>去重后的数据库域键值列表。</returns>
        private List<object> ExtractKeyValues(IReadOnlyList<object> entities, NavigationMetadata navigation)
        {
            // 去重集合 + 有序列表双结构：同一键值落入不同 IN 批次会取回重复关联行，
            // netstandard2.0 无同时保证插入顺序与去重的单结构
            var keySet = new HashSet<object>();// 去重
            var keyValues = new List<object>(entities.Count);// 容量按本方实体数量预估

            foreach (var entity in entities)
            {
                // 数据库域键值：null 统一为 DBNull，与存储值同域
                var val = navigation.ThisKeyColumn.GetValueForDb(entity);
                if (val != DBNull.Value && keySet.Add(val))
                    keyValues.Add(val);
            }

            return keyValues;
        }
        #endregion

        #region 排序与窗口
        /// <summary>
        /// 设置查询级排序（升序），开启新的排序链（多次调用时后者替换整条链，与 LINQ 语义一致）。
        /// </summary>
        public IIncludableEntityQuery<T, TPrevious> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
        {
            return (IIncludableEntityQuery<T, TPrevious>)OrderByCore(keySelector, false);
        }

        /// <summary>
        /// 设置查询级排序（降序），开启新的排序链（多次调用时后者替换整条链，与 LINQ 语义一致）。
        /// </summary>
        public IIncludableEntityQuery<T, TPrevious> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
        {
            return (IIncludableEntityQuery<T, TPrevious>)OrderByCore(keySelector, true);
        }

        /// <summary>
        /// 在既有排序链末尾追加次级排序键（升序）。
        /// </summary>
        public IIncludableEntityQuery<T, TPrevious> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector)
        {
            return (IIncludableEntityQuery<T, TPrevious>)ThenByCore(keySelector, false);
        }

        /// <summary>
        /// 在既有排序链末尾追加次级排序键（降序）。
        /// </summary>
        public IIncludableEntityQuery<T, TPrevious> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
        {
            return (IIncludableEntityQuery<T, TPrevious>)ThenByCore(keySelector, true);
        }

        /// <summary>
        /// 设置窗口跳过行数（要求已设置排序；必须配 Take 使用，终结方法执行时校验）。
        /// </summary>
        public IIncludableEntityQuery<T, TPrevious> Skip(int count)
        {
            return (IIncludableEntityQuery<T, TPrevious>)SkipCore(count);
        }

        /// <summary>
        /// 设置窗口取行数（要求已设置排序；可单独使用，等价 offset 为 0 的窗口）。
        /// </summary>
        public IIncludableEntityQuery<T, TPrevious> Take(int count)
        {
            return (IIncludableEntityQuery<T, TPrevious>)TakeCore(count);
        }

        /// <summary>
        /// 创建携带指定排序/窗口状态的 IncludableEntityQuery 派生实例（导航链与链尾随实例传递）。
        /// </summary>
        protected override EntityQueryBase<T> CreateDerived(List<(ColumnMetadata Column, bool Descending)> orderings, int? skip, int? take)
        {
            return new IncludableEntityQuery<T, TPrevious>(dbAccess, originalSql, originalParameters, includes, chainTip, orderings, skip, take);
        }
        #endregion
    }
}
