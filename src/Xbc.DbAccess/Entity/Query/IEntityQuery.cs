using System.Linq.Expressions;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 实体查询接口，提供基于 SQL 的实体查询功能，支持导航属性加载、链式排序与窗口取数。
    /// </summary>
    /// <typeparam name="T">实体类型。</typeparam>
    public interface IEntityQuery<T> : IExecutableEntityQuery<T> where T : class
    {
        /// <summary>
        /// 设置查询级排序（升序），开启一条新的排序链；后续可经 <see cref="ThenBy{TKey}"/> 追加次级排序键。
        /// <para>排序键仅支持根实体的映射列（如 <c>x =&gt; x.Name</c>）；导航属性、嵌套路径、计算表达式与 JSON 列抛 <see cref="NotSupportedException"/>。</para>
        /// <para>多次调用时后者替换整条排序链（与 LINQ 语义一致）；查询 SQL 已含 ORDER BY 时再设置排序抛 <see cref="InvalidOperationException"/>。</para>
        /// </summary>
        /// <typeparam name="TKey">排序键类型。</typeparam>
        /// <param name="keySelector">排序键选择器表达式。</param>
        /// <returns>带排序的新查询实例（不可变构建器，原实例不变）。</returns>
        IEntityQuery<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector);

        /// <summary>
        /// 设置查询级排序（降序），开启一条新的排序链；约束与 <see cref="OrderBy{TKey}"/> 一致。
        /// </summary>
        /// <typeparam name="TKey">排序键类型。</typeparam>
        /// <param name="keySelector">排序键选择器表达式。</param>
        /// <returns>带排序的新查询实例（不可变构建器，原实例不变）。</returns>
        IEntityQuery<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector);

        /// <summary>
        /// 在既有排序链末尾追加次级排序键（升序），排序内同值行按追加键决出先后。
        /// <para>未先调用 <see cref="OrderBy{TKey}"/>/<see cref="OrderByDescending{TKey}"/> 时抛 <see cref="InvalidOperationException"/>。</para>
        /// </summary>
        /// <typeparam name="TKey">排序键类型。</typeparam>
        /// <param name="keySelector">排序键选择器表达式。</param>
        /// <returns>追加了排序键的新查询实例（不可变构建器，原实例不变）。</returns>
        IEntityQuery<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector);

        /// <summary>
        /// 在既有排序链末尾追加次级排序键（降序），排序内同值行按追加键决出先后。
        /// <para>未先调用 <see cref="OrderBy{TKey}"/>/<see cref="OrderByDescending{TKey}"/> 时抛 <see cref="InvalidOperationException"/>。</para>
        /// </summary>
        /// <typeparam name="TKey">排序键类型。</typeparam>
        /// <param name="keySelector">排序键选择器表达式。</param>
        /// <returns>追加了排序键的新查询实例（不可变构建器，原实例不变）。</returns>
        IEntityQuery<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector);

        /// <summary>
        /// 设置窗口跳过行数（OFFSET 语义），与 <see cref="Take"/> 组合截取排序结果中的一段连续行。
        /// <para>要求先设置查询级排序，否则抛 <see cref="InvalidOperationException"/>；
        /// 且必须配 <see cref="Take"/> 使用（单独 OFFSET 部分方言不支持），终结方法执行时校验。</para>
        /// </summary>
        /// <param name="count">跳过的行数，非负。</param>
        /// <returns>带窗口跳过的新查询实例（不可变构建器，原实例不变）。</returns>
        IEntityQuery<T> Skip(int count);

        /// <summary>
        /// 设置窗口取行数（FETCH/LIMIT 语义），可单独使用（等价 OFFSET 为 0 的窗口）。
        /// <para>要求先设置查询级排序，否则抛 <see cref="InvalidOperationException"/>。</para>
        /// </summary>
        /// <param name="count">取回的行数，非负；0 表示空窗口。</param>
        /// <returns>带窗口截断的新查询实例（不可变构建器，原实例不变）。</returns>
        IEntityQuery<T> Take(int count);

        /// <summary>
        /// 包含导航属性，可多次调用以包含多个导航属性。
        /// <para>导航属性使用分裂查询：先执行用户 SQL 获取主实体，再根据键值查询对方表装配（1:N 集合用本方主键匹配对方外键，1:1 引用用本方外键匹配对方主键），不改写主 SQL。</para>
        /// <para>如需多次查询间的数据一致性，请自行在查询前开启事务。</para>
        /// </summary>
        /// <typeparam name="TResult">导航属性类型。</typeparam>
        /// <param name="selector">导航属性选择器表达式。</param>
        /// <returns>可包含导航属性的查询接口，支持 ThenInclude 多级导航。</returns>
        IIncludableEntityQuery<T, TResult> Include<TResult>(Expression<Func<T, TResult>> selector);

        /// <summary>
        /// 投影：将查询结果投影为映射列的值（SELECT 列组 FROM (查询)），仅支持映射列。
        /// <para>单列选择器限根实体的单级映射列（如 <c>x => x.Age</c>）；多列选择器限匿名类型（如 <c>x => new { x.Name, x.Age }</c>，构造器按位置映射）、
        /// 元组（如 <c>x => ValueTuple.Create(x.Name, x.Age)</c>，按位置映射——元组字面量在表达式树中不可编译（CS8143），须用 Create 调用形态且无元素名）、
        /// 命名类型带参构造（如 <c>x => new Dto(x.Name, x.Age)</c>，按编译器实参绑定的构造函数位置映射，多构造重载由编译器各自消歧）
        /// 或命名类型对象初始化器（如 <c>x => new Dto { Name = x.Name, Age = x.Age }</c>，要求公共无参构造与可写成员），
        /// 各成员为根实体的单级映射列；不支持计算表达式、嵌套成员路径与聚合。</para>
        /// <para>与 Include 编译期互斥：Include 后的查询链类型不提供 Select（投影结果不是实体，无法装配导航属性）；
        /// 各列值转换与实体赋值一致（转换器列、枚举名列、JSON 列按各自配置转换）。</para>
        /// <para>投影是链的最后一步构建操作，之后只能调用终结方法；已设置的排序与 Skip/Take 窗口在执行时生效。</para>
        /// </summary>
        /// <typeparam name="TResult">投影结果类型：单列为属性类型，多列为匿名类型、元组（ValueTuple）或命名类型。</typeparam>
        /// <param name="selector">映射列选择器表达式。</param>
        /// <returns>投影查询对象。</returns>
        IProjectedEntityQuery<TResult> Select<TResult>(Expression<Func<T, TResult>> selector);

        /// <summary>
        /// 分组聚合：按实体列分组，产出服务端聚合 SQL（SELECT 键列与聚合片段 FROM (查询) 别名 GROUP BY ... HAVING ...）。
        /// <para>键选择器仅支持根实体的映射列访问（单列 <c>x => x.CustomerId</c>、匿名类型或对象初始化器
        /// <c>x =&gt; new {{ x.CustomerId, x.Status }}</c>）；计算表达式、常量、导航路径、元组与带参构造形态抛 <see cref="NotSupportedException"/>。</para>
        /// <para>调用前置校验：此前不能设置排序或窗口（OrderBy/Skip/Take）——源行序对分组结果无意义、窗口语义属分组后的结果集，
        /// 分组后的排序与窗口在 <see cref="IGroupedEntityQuery{T,TKey}"/> 上链；分组后的过滤链点是
        /// <see cref="IGroupedEntityQuery{T,TKey}.Having"/>（无 Where，语义即 HAVING）。</para>
        /// <para>分组查询不物化组序列，经 <see cref="IGroupedEntityQuery{T,TKey}.Select{TResult}"/> 出扁平行
        /// 或 <see cref="IGroupedEntityQuery{T,TKey}.CountAsync"/> 统计组数。</para>
        /// </summary>
        /// <typeparam name="TKey">分组键类型：单列键为列属性类型，复合键为匿名类型或命名初始化器类型。</typeparam>
        /// <param name="keySelector">分组键选择器表达式。</param>
        /// <returns>分组查询对象（Having/排序/窗口/Select/CountAsync 链式面）。</returns>
        IGroupedEntityQuery<T, TKey> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector);
    }
}
