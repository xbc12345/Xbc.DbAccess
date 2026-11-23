namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 投影查询接口：Select 之后的终结执行层（单列为列值类型，多列为匿名类型实例）。
    /// <para>构建已结束，仅提供取数方法；Select 前已设置的排序与 Skip/Take 窗口在执行时生效。</para>
    /// </summary>
    /// <typeparam name="TResult">投影结果类型：单列为属性类型，多列为匿名类型。</typeparam>
    public interface IProjectedEntityQuery<TResult>
    {
        /// <summary>异步取投影的第一行，无行时抛 <see cref="InvalidOperationException"/>。</summary>
        Task<TResult> FirstAsync(CancellationToken cancellationToken = default);

        /// <summary>异步取投影的第一行，无行时返回 default。</summary>
        Task<TResult> FirstOrDefaultAsync(CancellationToken cancellationToken = default);

        /// <summary>异步取投影的唯一一行，无行或存在多行时抛 <see cref="InvalidOperationException"/>（窗口已截断时以截断结果为准）。</summary>
        Task<TResult> SingleAsync(CancellationToken cancellationToken = default);

        /// <summary>异步取投影的唯一一行，无行时返回 default，存在多行时抛 <see cref="InvalidOperationException"/>。</summary>
        Task<TResult> SingleOrDefaultAsync(CancellationToken cancellationToken = default);

        /// <summary>异步执行投影查询，返回投影实例列表（各列值转换与实体赋值一致）。</summary>
        Task<List<TResult>> ToListAsync(CancellationToken cancellationToken = default);
    }
}
