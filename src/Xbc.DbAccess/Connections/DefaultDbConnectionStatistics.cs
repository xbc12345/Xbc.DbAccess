namespace Xbc.DbAccess
{
    /// <summary>
    /// 默认的数据库连接统计信息。
    /// <para>每次请求都需要新建连接，使用完毕后直接释放。</para>
    /// <para>
    /// 指标分两类：<b>累计器</b>（Total* 系列）单调递增、只计事件发生的次数；
    /// <b>仪表</b>（<see cref="ActiveConnections"/> 及其派生值）在快照瞬间从活动集合实时测量。
    /// 生命周期与仪表：创建成功 → 入活动集合（Active + 1）；连接释放/销毁 → 出活动集合（Active − 1）。
    /// </para>
    /// <para>
    /// 请求流程与指标关系：
    /// <list type="table">
    /// <listheader><term>路径</term><description>描述</description></listheader>
    /// <item><term>A</term><description>创建成功 → TotalRequests+1, TotalCreated+1</description></item>
    /// <item><term>B</term><description>创建失败 → TotalRequests+1, TotalCreateFailed+1</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 互斥关系（每个请求恰好一种结局）：<see cref="TotalRequests"/> = <see cref="TotalCreated"/> + <see cref="TotalCreateFailed"/>。
    /// </para>
    /// <para>
    /// 生命周期关系（结构保证的恒等式）：<see cref="ActiveConnections"/> = <see cref="TotalCreated"/> − <see cref="TotalReleased"/>
    /// （活动集合条目的移除与 <see cref="TotalReleased"/> 递增总是成对发生，管理器销毁路径两者同时静止）；
    /// 若此差值持续增长且不随业务量下降而减少，说明存在连接未正确释放（可能泄漏）。
    /// </para>
    /// <para>
    /// 派生指标：
    /// <list type="bullet">
    /// <item>创建失败率 = <see cref="CreateFailureRate"/>（已提供）</item>
    /// <item>创建成功率 = 100 - <see cref="CreateFailureRate"/></item>
    /// <item>未释放率 = (<see cref="TotalCreated"/> - <see cref="TotalReleased"/>) / <see cref="TotalCreated"/> × 100（已创建但未释放的占比）</item>
    /// </list>
    /// </para>
    /// </summary>
    public class DefaultDbConnectionStatistics : IDbConnectionStatistics
    {
        /// <inheritdoc/>
        /// <remarks>
        /// 等于 <see cref="TotalCreated"/> + <see cref="TotalCreateFailed"/>。
        /// </remarks>
        public long TotalRequests { get; internal set; }

        /// <inheritdoc/>
        /// <remarks>
        /// 每次请求都需要新建连接，新建连接占比 = 1 - <see cref="CreateFailureRate"/>。
        /// </remarks>
        public long TotalCreated { get; internal set; }

        /// <inheritdoc/>
        public long TotalCreateFailed { get; internal set; }

        /// <inheritdoc/>
        /// <remarks>
        /// 等于 <see cref="TotalCreateFailed"/> / <see cref="TotalRequests"/> × 100。
        /// </remarks>
        public double CreateFailureRate => TotalRequests > 0 ? (double)TotalCreateFailed / TotalRequests * 100 : 0;

        /// <inheritdoc/>
        /// <remarks>
        /// 连接释放/销毁时递增（与活动集合条目的移除成对发生），与 <see cref="TotalCreated"/> 的关系见类摘要的恒等式。
        /// </remarks>
        public long TotalReleased { get; internal set; }

        /// <inheritdoc/>
        /// <remarks>
        /// 等于 <see cref="ActiveConnections"/>；若此值持续增长且不随业务量下降而减少，可能存在连接泄漏。
        /// </remarks>
        public long AliveConnections => ActiveConnections;

        /// <inheritdoc/>
        /// <remarks>
        /// 正在使用的连接数（快照值）；若持续增长且不随业务量下降而减少，可能存在连接泄漏。
        /// </remarks>
        public int ActiveConnections { get; internal set; }

        /// <inheritdoc/>
        /// <remarks>
        /// 非连接池模式不存在排队等待，恒为 0。
        /// </remarks>
        public long TotalCancelled => 0;
    }
}
