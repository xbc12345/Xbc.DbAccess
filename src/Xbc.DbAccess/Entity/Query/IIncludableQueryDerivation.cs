using System.Linq.Expressions;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 内部接口：导航链派生钩子，供 <see cref="IncludableEntityQueryExtensions"/> 的 ThenInclude 扩展方法调用。
    /// <para>ThenInclude 依赖协变解包选择重载，只能以扩展方法存在，故以此接口把导航链操作收口在框架查询实现类内；
    /// 方法不得命名为 ThenInclude，否则实例方法在重载解析中优先于扩展方法，集合导航的协变解包路径被劫持。</para>
    /// </summary>
    internal interface IIncludableQueryDerivation
    {
        /// <summary>
        /// 以当前导航链的链尾节点为父级，解析选择器指定的下一级导航属性，并复制重建查询实例。
        /// </summary>
        /// <typeparam name="TEntity">根实体类型。</typeparam>
        /// <typeparam name="TResult">当前导航属性的类型。</typeparam>
        /// <param name="selector">导航属性选择器，参数类型需与链尾导航的对方实体类型匹配，否则抛 <see cref="InvalidOperationException"/>。</param>
        /// <returns>携带新导航链节点的查询实例；命中既有路径时链尾指向既有节点（幂等）。</returns>
        IIncludableEntityQuery<TEntity, TResult> WithNextNavigation<TEntity, TResult>(LambdaExpression selector)
            where TEntity : class;
    }
}
