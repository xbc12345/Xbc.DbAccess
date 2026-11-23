using System.Linq.Expressions;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// ThenInclude 扩展方法，支持引用导航和集合导航后的多级导航。
    /// <para>ThenInclude 依赖协变解包选择重载，只能以扩展方法存在；导航链操作收口在框架实现类内
    /// （<see cref="IIncludableQueryDerivation.WithNextNavigation{TEntity, TResult}"/>）。</para>
    /// </summary>
    public static class IncludableEntityQueryExtensions
    {
        /// <summary>
        /// 继续当前导航链，包含下一级导航属性（引用导航后）。
        /// <para>引用导航无集合包装，<typeparamref name="TPrevious"/> 即导航属性类型本身，selector 直接以其为参数。</para>
        /// <para>示例：<c>.Include(x => x.Order).ThenInclude(x => x.Customer)</c></para>
        /// </summary>
        /// <typeparam name="T">根实体类型。</typeparam>
        /// <typeparam name="TPrevious">上一级导航属性的类型（引用导航即导航属性类型本身）。</typeparam>
        /// <typeparam name="TResult">当前导航属性的类型。</typeparam>
        /// <param name="source">携带导航链的查询实例。</param>
        /// <param name="selector">导航属性选择器表达式，以上一级导航属性（<typeparamref name="TPrevious"/>）为参数。</param>
        /// <returns>携带新导航链节点的查询实例。</returns>
        public static IIncludableEntityQuery<T, TResult> ThenInclude<T, TPrevious, TResult>(
            this IIncludableEntityQuery<T, TPrevious> source,
            Expression<Func<TPrevious, TResult>> selector)
            where T : class
        {
            Guard.ThrowIfNull(source, nameof(source));

            // 公共接口可能被第三方实现，仅框架内部实现（同时实现 IIncludableQueryDerivation）可参与导航链
            if (source is not IIncludableQueryDerivation derivation)
                throw new ArgumentException($"ThenInclude 不支持自定义的 IIncludableEntityQuery 实现（实际类型 '{source.GetType()}'），请使用框架查询方法返回的实例调用 ThenInclude。", nameof(source));

            // TPrevious 不进入 WithNextNavigation 签名：编译期它只服务于 selector 的类型检查，
            // 运行期链尾导航的对方实体类型已由 chainTip 节点的 TargetType 元数据携带（见 WithNextNavigation 实现）
            return derivation.WithNextNavigation<T, TResult>(selector);
        }

        /// <summary>
        /// 继续当前导航链，包含下一级导航属性（集合导航后）。
        /// <para>编译器经协变转换将携带具体集合（如 <see cref="List{T}"/>）的查询转为携带 <see cref="IEnumerable{T}"/> 的查询，
        /// 从而 <typeparamref name="TPrevious"/> 为集合元素类型。</para>
        /// <para>示例：<c>.Include(x => x.Orders).ThenInclude(x => x.OrderDetails)</c>，selector 的参数为 Order。</para>
        /// </summary>
        /// <typeparam name="T">根实体类型。</typeparam>
        /// <typeparam name="TPrevious">上一级导航属性集合的元素类型（经协变解包得出）。</typeparam>
        /// <typeparam name="TResult">当前导航属性的类型。</typeparam>
        /// <param name="source">携带导航链的查询实例（静态类型为集合元素形态，实例未变，见类型 doc 的流转机制）。</param>
        /// <param name="selector">导航属性选择器表达式，以集合元素类型（<typeparamref name="TPrevious"/>）为参数。</param>
        /// <returns>携带新导航链节点的查询实例。</returns>
        public static IIncludableEntityQuery<T, TResult> ThenInclude<T, TPrevious, TResult>(
            this IIncludableEntityQuery<T, IEnumerable<TPrevious>> source,
            Expression<Func<TPrevious, TResult>> selector)
            where T : class
        {
            Guard.ThrowIfNull(source, nameof(source));

            // 公共接口可能被第三方实现，仅框架内部实现（同时实现 IIncludableQueryDerivation）可参与导航链
            if (source is not IIncludableQueryDerivation derivation)
                throw new ArgumentException($"ThenInclude 不支持自定义的 IIncludableEntityQuery 实现（实际类型 '{source.GetType()}'），请使用框架查询方法返回的实例调用 ThenInclude。", nameof(source));

            return derivation.WithNextNavigation<T, TResult>(selector);
        }
    }
}
