using System.Data.Common;
using System.Linq.Expressions;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 实体查询实现类，提供基于 SQL 的实体查询功能。
    /// <para>调用 Include 后返回 IIncludableEntityQuery 支持多级导航；Include/OrderBy/ThenBy/Skip/Take 均为不可变构建器：
    /// 每次链式调用复制状态新建实例，原实例状态不变。</para>
    /// </summary>
    internal sealed class EntityQuery<T> : EntityQueryBase<T>, IEntityQuery<T> where T : class
    {
        protected override bool HasNavigations => false;

        public EntityQuery(
            IDbAccess dbAccess,
            string sql,
            DbParameter[] parameters,
            List<(ColumnMetadata Column, bool Descending)> orderings = null,
            int? skip = null,
            int? take = null)
            : base(dbAccess, sql, parameters, orderings, skip, take)
        {
        }

        #region 导航链与装配
        /// <summary>
        /// 包含导航属性，返回可继续 Include/ThenInclude 的查询对象（排序/窗口状态随新实例传递）。
        /// <para>本类型是无导航链的起点，重复 Include 的幂等去重由返回类型承担（见 <see cref="IIncludableEntityQuery{T,TPrevious}.Include"/>）。</para>
        /// </summary>
        public IIncludableEntityQuery<T, TResult> Include<TResult>(Expression<Func<T, TResult>> selector)
        {
            Guard.ThrowIfNull(selector, nameof(selector));

            var navigation = entityMetadata.ResolveNavigation(selector);
            var newNavInclude = new NavigationInclude(navigation, null);
            var navigationIncludes = new List<NavigationInclude> { newNavInclude };

            return new IncludableEntityQuery<T, TResult>(dbAccess, originalSql, originalParameters,
                navigationIncludes, newNavInclude, orderings, skip, take);
        }

        protected override Task PopulateNavigationsAsync(List<T> entities, CancellationToken cancellationToken)
        {
            // HasNavigations 始终为 false，此方法不会被调用
            return Task.CompletedTask;
        }
        #endregion

        #region 排序与窗口
        /// <summary>
        /// 设置查询级排序（升序），开启新的排序链（多次调用时后者替换整条链，与 LINQ 语义一致）。
        /// </summary>
        public IEntityQuery<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
        {
            return (IEntityQuery<T>)OrderByCore(keySelector, false);
        }

        /// <summary>
        /// 设置查询级排序（降序），开启新的排序链（多次调用时后者替换整条链，与 LINQ 语义一致）。
        /// </summary>
        public IEntityQuery<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
        {
            return (IEntityQuery<T>)OrderByCore(keySelector, true);
        }

        /// <summary>
        /// 在既有排序链末尾追加次级排序键（升序）。
        /// </summary>
        public IEntityQuery<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector)
        {
            return (IEntityQuery<T>)ThenByCore(keySelector, false);
        }

        /// <summary>
        /// 在既有排序链末尾追加次级排序键（降序）。
        /// </summary>
        public IEntityQuery<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
        {
            return (IEntityQuery<T>)ThenByCore(keySelector, true);
        }

        /// <summary>
        /// 设置窗口跳过行数（要求已设置排序；必须配 Take 使用，终结方法执行时校验）。
        /// </summary>
        public IEntityQuery<T> Skip(int count)
        {
            return (IEntityQuery<T>)SkipCore(count);
        }

        /// <summary>
        /// 设置窗口取行数（要求已设置排序；可单独使用，等价 offset 为 0 的窗口）。
        /// </summary>
        public IEntityQuery<T> Take(int count)
        {
            return (IEntityQuery<T>)TakeCore(count);
        }

        /// <summary>
        /// 创建携带指定排序/窗口状态的 EntityQuery 派生实例。
        /// </summary>
        protected override EntityQueryBase<T> CreateDerived(List<(ColumnMetadata Column, bool Descending)> orderings, int? skip, int? take)
        {
            return new EntityQuery<T>(dbAccess, originalSql, originalParameters, orderings, skip, take);
        }
        #endregion

        #region 分组聚合
        /// <summary>
        /// 分组聚合：前置状态校验（排序/窗口必须为空）后解析键选择器为键列组（早期校验，方言无关），构造分组查询。
        /// </summary>
        public IGroupedEntityQuery<T, TKey> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
        {
            Guard.ThrowIfNull(keySelector, nameof(keySelector));

            // 调用前置校验：源行序对分组结果无意义、窗口语义属分组后的结果集，排序与窗口须在 GroupBy 之后链
            if (orderings.Count > 0 || skip.HasValue || take.HasValue)
                throw new InvalidOperationException("GroupBy 之前不能设置排序或窗口（OrderBy/ThenBy/Skip/Take），源行序对分组结果无意义、窗口语义属分组后的结果集，请在 GroupBy 之后链排序与窗口。");

            var keyColumns = AggregateSqlTranslator.ParseKeySelector(keySelector, entityMetadata);
            return new GroupedEntityQuery<T, TKey>(dbAccess, originalSql, originalParameters, keyColumns);
        }
        #endregion
    }
}
