using System.Linq.Expressions;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 实体查询的执行层接口，定义全部终结执行方法。
    /// <para><see cref="IEntityQuery{T}"/> 与 <see cref="IIncludableEntityQuery{T,TPrevious}"/> 均继承此接口，任意形态的查询都可直接调用执行方法。</para>
    /// <para>查询对象不可并发执行：链式派生的实例与原实例共享同一组 <see cref="System.Data.Common.DbParameter"/>，并发提交会引发参数归属冲突；串行复用安全。</para>
    /// <para>聚合方法返回标量，不装配导航属性，链上已构建的 Include/ThenInclude 会被忽略。</para>
    /// </summary>
    /// <typeparam name="T">实体类型。</typeparam>
    public interface IExecutableEntityQuery<T> where T : class
    {
        #region 查询方法
        /// <summary>异步查询第一条数据，已设置的排序与 Skip/Take 窗口按其语义生效。</summary>
        Task<T> FirstAsync(CancellationToken cancellationToken = default);

        /// <summary>异步查询第一条数据，不存在时返回 null。</summary>
        Task<T> FirstOrDefaultAsync(CancellationToken cancellationToken = default);

        /// <summary>异步查询唯一一条数据，不存在或存在多条时抛出异常（已设置 Take 窗口时以窗口截断结果为准）。</summary>
        Task<T> SingleAsync(CancellationToken cancellationToken = default);

        /// <summary>异步查询唯一一条数据，不存在时返回 null，存在多条时抛出异常（已设置 Take 窗口时以窗口截断结果为准）。</summary>
        Task<T> SingleOrDefaultAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步执行查询，返回实体列表，已设置的排序与窗口生效。
        /// <para>分页可组合 <c>.OrderBy(...).Skip(...).Take(...)</c>：Skip 为绝对行号偏移（如第 2 页每页 10 条为 <c>.Skip(10).Take(10)</c>）。</para>
        /// </summary>
        Task<List<T>> ToListAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步 keyset 分页：按键集排序返回游标之后的下一页。键集与方向即查询级排序链（OrderBy/ThenBy），须先设置排序；与 Skip/Take 互斥。
        /// </summary>
        /// <param name="cursor">游标实体（上一页最后一条），null 表示从第一页开始。</param>
        /// <param name="pageSize">页大小，必须为正数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>游标之后的下一页实体列表；无更多行时为空列表。</returns>
        Task<List<T>> KeysetAfterAsync(T cursor, int pageSize, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步 keyset 分页：按键集排序返回游标之前紧邻的一页，结果按键集排序方向输出。键集与方向即查询级排序链；与 Skip/Take 互斥。
        /// </summary>
        /// <param name="cursor">游标实体（下一页第一条），不能为 null。</param>
        /// <param name="pageSize">页大小，必须为正数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>游标之前紧邻的一页实体列表；无更多行时为空列表。</returns>
        Task<List<T>> KeysetBeforeAsync(T cursor, int pageSize, CancellationToken cancellationToken = default);
        #endregion

        #region 聚合方法
        /// <summary>
        /// 异步统计查询结果行数（COUNT 包裹查询）。
        /// <para>已设置窗口（Skip/Take）时统计窗口内行数；基础 SQL 不能包含顶层 ORDER BY，否则抛 <see cref="InvalidOperationException"/>。</para>
        /// </summary>
        Task<long> CountAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步判断查询是否存在结果行（EXISTS 布尔化）。
        /// </summary>
        Task<bool> AnyAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步判断查询结果是否所有行都满足条件（NOT EXISTS 反语义）：空结果集返回 true（SQL 标准的 NOT EXISTS 语义）。
        /// <para>已设置的窗口（Skip/Take）生效，对窗口内行判断；</para>
        /// </summary>
        /// <param name="predicate">条件表达式，如 <c>x => x.Age &gt; 18</c>。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>所有行都满足条件（或无行）时返回 true；存在不满足条件的行时返回 false。</returns>
        Task<bool> AllAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步对查询结果求指定数值列的总和（SUM 包裹查询），已设置的窗口生效。
        /// <para>无匹配行时返回 null；结果统一转换为 <see cref="decimal"/>；基础 SQL 不能包含顶层 ORDER BY（派生表包裹限制，含顶层 ORDER BY 时抛 <see cref="InvalidOperationException"/>）。
        /// 全表求和（无条件）请使用 <c>.AsQuery().SumAsync(...)</c>。</para>
        /// </summary>
        Task<decimal?> SumAsync<TResult>(Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步对查询结果求指定数值列的平均值（AVG 包裹查询），已设置的窗口生效。
        /// <para>无匹配行时返回 null；NULL 值行不参与平均；结果统一转换为 <see cref="decimal"/>；基础 SQL 不能包含顶层 ORDER BY（派生表包裹限制，含顶层 ORDER BY 时抛 <see cref="InvalidOperationException"/>）。
        /// 全表求均值（无条件）请使用 <c>.AsQuery().AverageAsync(...)</c>。</para>
        /// </summary>
        Task<decimal?> AverageAsync<TResult>(Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步求查询结果中指定列的最大值（MAX 包裹查询）。选择器仅支持单级映射列访问。
        /// <para>无匹配行时返回 null；NULL 值行不参与比较；基础 SQL 不能包含顶层 ORDER BY（派生表包裹限制，含顶层 ORDER BY 时抛 <see cref="InvalidOperationException"/>）。</para>
        /// </summary>
        Task<TResult?> MaxAsync<TResult>(Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default) where TResult : struct;

        /// <summary>
        /// 异步求查询结果中指定可空列的最大值。可空列重载：选择器返回类型为 <typeparamref name="TResult"/> 的可空形式。
        /// </summary>
        Task<TResult?> MaxAsync<TResult>(Expression<Func<T, TResult?>> selector, CancellationToken cancellationToken = default) where TResult : struct;

        /// <summary>
        /// 异步求查询结果中指定列的最小值（MIN 包裹查询）。选择器仅支持单级映射列访问。
        /// <para>无匹配行时返回 null；NULL 值行不参与比较；基础 SQL 不能包含顶层 ORDER BY（派生表包裹限制，含顶层 ORDER BY 时抛 <see cref="InvalidOperationException"/>）。</para>
        /// </summary>
        Task<TResult?> MinAsync<TResult>(Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default) where TResult : struct;

        /// <summary>
        /// 异步求查询结果中指定可空列的最小值。可空列重载：选择器返回类型为 <typeparamref name="TResult"/> 的可空形式。
        /// </summary>
        Task<TResult?> MinAsync<TResult>(Expression<Func<T, TResult?>> selector, CancellationToken cancellationToken = default) where TResult : struct;
        #endregion
    }
}
