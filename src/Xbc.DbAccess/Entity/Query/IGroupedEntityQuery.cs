using System.Linq.Expressions;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 分组查询接口：GroupBy 之后的链式构建层（分组后过滤、分组结果排序与窗口），终结执行 CountAsync 与 Select 分派。
    /// <para>lambda 参数为 BCL 的 <see cref="IGrouping{TKey, TElement}"/>（用户表达式真实可编译，与 EF Core 心智一致）；
    /// 分组查询不物化组序列，经 <see cref="Select{TResult}"/> 出扁平行（First/Single/ToList 由投影终结面承担）。</para>
    /// <para>排序方法命名与签名与 <see cref="IEntityQuery{T}"/> 一致；窗口作用于分组结果集（须先排序，Skip 必配 Take）。</para>
    /// <para>所有链式方法复制状态新建实例（不可变构建器，原实例不变）；查询对象不可并发执行（与实体查询同约束）。</para>
    /// </summary>
    /// <typeparam name="T">实体类型。</typeparam>
    /// <typeparam name="TKey">分组键类型：单列键为列属性类型，复合键为匿名类型或命名初始化器类型。</typeparam>
    public interface IGroupedEntityQuery<T, TKey> where T : class
    {
        /// <summary>
        /// 分组后过滤（HAVING）：谓词支持聚合比较（<c>g =&gt; g.Count() &gt;= 5</c>）、键列比较（<c>g =&gt; g.Key.CustomerId == 5</c>）
        /// 与逻辑组合（&amp;&amp;/||/!）；常量与闭包值参数化，聚合间运算与条件计数抛 <see cref="NotSupportedException"/>。
        /// <para>多次调用按 AND 合并（与 Where 的多谓词语义一致）；分组接口不暴露 Where——分组后的过滤语义就是 Having。</para>
        /// </summary>
        /// <param name="predicate">分组谓词表达式，参数为 <see cref="IGrouping{TKey, TElement}"/>。</param>
        /// <returns>带分组过滤的新查询实例（不可变构建器，原实例不变）。</returns>
        IGroupedEntityQuery<T, TKey> Having(Expression<Func<IGrouping<TKey, T>, bool>> predicate);

        /// <summary>
        /// 设置分组结果排序（升序），开启一条新的排序链；排序键支持分组键引用（<c>g =&gt; g.Key.CustomerId</c>，复合键 <c>g =&gt; g.Key</c> 展开为全部键列）
        /// 与聚合调用（<c>g =&gt; g.Sum(x =&gt; x.Amount)</c>）。
        /// <para>排序在 SQL 外层以分组 SELECT 列的引用兑现（键列为自然名引用、聚合为生成别名引用），未投影的排序聚合自动注入分组 SELECT 并由投影外层裁剪。</para>
        /// <para>多次调用时后者替换整条排序链（与 LINQ 语义一致）。</para>
        /// </summary>
        /// <typeparam name="TResult">排序键类型。</typeparam>
        /// <param name="keySelector">排序键选择器表达式。</param>
        /// <returns>带排序的新查询实例（不可变构建器，原实例不变）。</returns>
        IGroupedEntityQuery<T, TKey> OrderBy<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> keySelector);

        /// <summary>
        /// 设置分组结果排序（降序），开启一条新的排序链；约束与 <see cref="OrderBy{TResult}"/> 一致。
        /// </summary>
        /// <typeparam name="TResult">排序键类型。</typeparam>
        /// <param name="keySelector">排序键选择器表达式。</param>
        /// <returns>带排序的新查询实例（不可变构建器，原实例不变）。</returns>
        IGroupedEntityQuery<T, TKey> OrderByDescending<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> keySelector);

        /// <summary>
        /// 在既有排序链末尾追加次级排序键（升序），排序内同值组按追加键决出先后。
        /// <para>未先调用 <see cref="OrderBy{TResult}"/>/<see cref="OrderByDescending{TResult}"/> 时抛 <see cref="InvalidOperationException"/>。</para>
        /// </summary>
        /// <typeparam name="TResult">排序键类型。</typeparam>
        /// <param name="keySelector">排序键选择器表达式。</param>
        /// <returns>追加了排序键的新查询实例（不可变构建器，原实例不变）。</returns>
        IGroupedEntityQuery<T, TKey> ThenBy<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> keySelector);

        /// <summary>
        /// 在既有排序链末尾追加次级排序键（降序），排序内同值组按追加键决出先后。
        /// <para>未先调用 <see cref="OrderBy{TResult}"/>/<see cref="OrderByDescending{TResult}"/> 时抛 <see cref="InvalidOperationException"/>。</para>
        /// </summary>
        /// <typeparam name="TResult">排序键类型。</typeparam>
        /// <param name="keySelector">排序键选择器表达式。</param>
        /// <returns>追加了排序键的新查询实例（不可变构建器，原实例不变）。</returns>
        IGroupedEntityQuery<T, TKey> ThenByDescending<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> keySelector);

        /// <summary>
        /// 设置分组结果窗口跳过行数（OFFSET 语义），与 <see cref="Take"/> 组合截取排序后分组结果中的一段连续组。
        /// <para>要求先设置排序（键列或聚合），否则抛 <see cref="InvalidOperationException"/>；且必须配 <see cref="Take"/> 使用。</para>
        /// </summary>
        /// <param name="count">跳过的组数，非负。</param>
        /// <returns>带窗口跳过的新查询实例（不可变构建器，原实例不变）。</returns>
        IGroupedEntityQuery<T, TKey> Skip(int count);

        /// <summary>
        /// 设置分组结果窗口取组数（FETCH/LIMIT 语义），可单独使用（等价 OFFSET 为 0 的窗口）。
        /// <para>要求先设置排序（键列或聚合），否则抛 <see cref="InvalidOperationException"/>。</para>
        /// </summary>
        /// <param name="count">取回的组数，必须为正数（与实体查询 Take 同约定）。</param>
        /// <returns>带窗口截断的新查询实例（不可变构建器，原实例不变）。</returns>
        IGroupedEntityQuery<T, TKey> Take(int count);

        /// <summary>
        /// 分组投影：将分组结果投影为键列与聚合值（外层按 Select 项配对序裁剪分组层超集，位置映射与实体投影一致）。
        /// <para>单列/多列选择器支持键列引用（<c>g =&gt; g.Key.CustomerId</c>；<c>g =&gt; g.Key</c> 复合键整体出列）、
        /// 聚合调用（<c>g =&gt; g.Count()</c>、<c>g =&gt; g.Sum(x =&gt; x.Amount)</c> 等）及其匿名类型/元组（ValueTuple.Create 形态）/
        /// 命名类型带参构造（按编译器实参绑定的构造函数位置映射）/初始化器组合；
        /// 多列选择器内的复合 <c>g.Key</c> 抛 <see cref="NotSupportedException"/>（引导展开为 g.Key.X 逐成员）。</para>
        /// <para>各列值转换与实体赋值一致（转换器列、枚举名列按各自配置转换）；聚合列无别名读取按位置映射，
        /// SUM 建议用 long、AVG 建议用 decimal 承接（引擎返回类型差异见设计文档）。</para>
        /// </summary>
        /// <typeparam name="TResult">投影结果类型：单列为键/聚合值类型，多列为匿名类型、元组（ValueTuple）或命名类型。</typeparam>
        /// <param name="selector">分组投影选择器表达式。</param>
        /// <returns>分组投影查询对象（First/Single/ToList 终结面）。</returns>
        IProjectedEntityQuery<TResult> Select<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> selector);

        /// <summary>
        /// 异步统计分组组数：对分组结果集再聚合（SELECT COUNT(*) FROM (分组 SQL)），HAVING 过滤生效。
        /// <para>已设置窗口（Skip/Take）时统计窗口内组数；未设窗口时忽略排序链（排序与组数无关）。</para>
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>分组组数（HAVING 过滤后、窗口截取内）。</returns>
        Task<long> CountAsync(CancellationToken cancellationToken = default);
    }
}
