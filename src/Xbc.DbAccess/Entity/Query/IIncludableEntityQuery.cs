using System.Linq.Expressions;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 可包含导航属性的实体查询接口，作为 Include/ThenInclude 的载体类型，携带上一级导航属性类型 <typeparamref name="TPrevious"/>
    /// 供 ThenInclude 推断下一级表达式；继承 <see cref="IExecutableEntityQuery{T}"/> 可直接执行。
    /// <para><typeparamref name="TPrevious"/> 为协变参数，集合导航后经协变赋值转换解包出元素类型（见 <see cref="IncludableEntityQueryExtensions"/>）。</para>
    /// <para>排序与窗口方法与 <see cref="IEntityQuery{T}"/> 同签，可与 Include/ThenInclude 任意顺序混用。</para>
    /// </summary>
    /// <typeparam name="T">根实体类型。</typeparam>
    /// <typeparam name="TPrevious">上一级导航属性的类型（引用导航为实体类型，集合导航为集合类型如 <see cref="List{T}"/>）。</typeparam>
    public interface IIncludableEntityQuery<T, out TPrevious> : IExecutableEntityQuery<T> where T : class
    {
        /// <summary>
        /// 设置查询级排序（升序），开启一条新的排序链；后续可经 <see cref="ThenBy{TKey}"/> 追加次级排序键。
        /// <para>排序键仅支持根实体的映射列（如 <c>x =&gt; x.Name</c>）；导航属性、嵌套路径、计算表达式与 JSON 列抛 <see cref="NotSupportedException"/>。</para>
        /// <para>多次调用时后者替换整条排序链（与 LINQ 语义一致）；查询 SQL 已含 ORDER BY 时再设置排序抛 <see cref="InvalidOperationException"/>。</para>
        /// </summary>
        /// <typeparam name="TKey">排序键类型。</typeparam>
        /// <param name="keySelector">排序键选择器表达式。</param>
        /// <returns>带排序的新查询实例（不可变构建器，原实例不变，导航链随实例传递）。</returns>
        IIncludableEntityQuery<T, TPrevious> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector);

        /// <summary>
        /// 设置查询级排序（降序），开启一条新的排序链；约束与 <see cref="OrderBy{TKey}"/> 一致。
        /// </summary>
        /// <typeparam name="TKey">排序键类型。</typeparam>
        /// <param name="keySelector">排序键选择器表达式。</param>
        /// <returns>带排序的新查询实例（不可变构建器，原实例不变，导航链随实例传递）。</returns>
        IIncludableEntityQuery<T, TPrevious> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector);

        /// <summary>
        /// 在既有排序链末尾追加次级排序键（升序），排序内同值行按追加键决出先后。
        /// <para>未先调用 <see cref="OrderBy{TKey}"/>/<see cref="OrderByDescending{TKey}"/> 时抛 <see cref="InvalidOperationException"/>。</para>
        /// </summary>
        /// <typeparam name="TKey">排序键类型。</typeparam>
        /// <param name="keySelector">排序键选择器表达式。</param>
        /// <returns>追加了排序键的新查询实例（不可变构建器，原实例不变，导航链随实例传递）。</returns>
        IIncludableEntityQuery<T, TPrevious> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector);

        /// <summary>
        /// 在既有排序链末尾追加次级排序键（降序），排序内同值行按追加键决出先后。
        /// <para>未先调用 <see cref="OrderBy{TKey}"/>/<see cref="OrderByDescending{TKey}"/> 时抛 <see cref="InvalidOperationException"/>。</para>
        /// </summary>
        /// <typeparam name="TKey">排序键类型。</typeparam>
        /// <param name="keySelector">排序键选择器表达式。</param>
        /// <returns>追加了排序键的新查询实例（不可变构建器，原实例不变，导航链随实例传递）。</returns>
        IIncludableEntityQuery<T, TPrevious> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector);

        /// <summary>
        /// 设置窗口跳过行数（OFFSET 语义），与 <see cref="Take"/> 组合截取排序结果中的一段连续行。
        /// <para>要求先设置查询级排序（指经 <see cref="OrderBy{TKey}"/>/<see cref="OrderByDescending{TKey}"/> 设置的框架排序链，
        /// 非原始 SQL 里的 ORDER BY，原始 SQL 带 ORDER BY 时请移除并改用 OrderBy），否则抛 <see cref="InvalidOperationException"/>；
        /// 且必须配 <see cref="Take"/> 使用（单独 OFFSET 部分方言不支持），终结方法执行时校验。</para>
        /// </summary>
        /// <param name="count">跳过的行数，非负。</param>
        /// <returns>带窗口跳过的新查询实例（不可变构建器，原实例不变，导航链随实例传递）。</returns>
        IIncludableEntityQuery<T, TPrevious> Skip(int count);

        /// <summary>
        /// 设置窗口取行数（FETCH/LIMIT 语义），可单独使用（等价 OFFSET 为 0 的窗口）。
        /// <para>要求先设置查询级排序（同 <see cref="Skip"/>），否则抛 <see cref="InvalidOperationException"/>。</para>
        /// </summary>
        /// <param name="count">取回的行数，非负；0 表示空窗口。</param>
        /// <returns>带窗口截断的新查询实例（不可变构建器，原实例不变，导航链随实例传递）。</returns>
        IIncludableEntityQuery<T, TPrevious> Take(int count);

        /// <summary>
        /// 从根实体开始新的导航链，包含另一个导航属性。
        /// <para>重复 Include 同一导航属性时幂等复用既有节点，其后的 ThenInclude 挂载到既有节点（语义与 EF Core 一致）。</para>
        /// </summary>
        /// <typeparam name="TResult">导航属性类型。</typeparam>
        /// <param name="selector">导航属性选择器表达式。</param>
        /// <returns>携带新根导航的查询实例；重复 Include 时链尾切至既有节点。</returns>
        IIncludableEntityQuery<T, TResult> Include<TResult>(Expression<Func<T, TResult>> selector);
    }
}
