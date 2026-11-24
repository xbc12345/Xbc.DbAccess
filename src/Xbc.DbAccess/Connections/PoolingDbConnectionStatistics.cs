namespace Xbc.DbAccess
{
    /// <summary>
    /// 连接池模式下的数据库连接统计信息。
    /// <para>连接可以被复用（命中）而非每次都新建，使用完毕后归还到池中。</para>
    /// <para>
    /// 指标分两类：<b>累计器</b>（Total* 系列）单调递增、只计事件发生的次数；
    /// <b>仪表</b>（<see cref="ActiveConnections"/>/<see cref="IdleConnections"/> 及其派生率）在快照瞬间
    /// 从连接池的活动集合与空闲队列实时测量，任一时刻都与集合真实状态一致。
    /// </para>
    /// <para>
    /// 连接生命周期与仪表（测量式，值随连接的集合归属自动变化）：
    /// <list type="table">
    /// <listheader><term>生命周期事件</term><description>集合归属变化 → 仪表变化</description></listheader>
    /// <item><term>预创建 / 回收</term><description>入空闲队列 → Idle + 1</description></item>
    /// <item><term>命中借出</term><description>出空闲队列、入活动集合 → Idle − 1, Active + 1</description></item>
    /// <item><term>活动连接销毁（如挂起事务的归还）</term><description>出活动集合 → Active − 1</description></item>
    /// <item><term>空闲连接销毁（超时清理 / 激活失败淘汰）</term><description>出空闲队列 → Idle − 1</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 请求流程与指标关系：
    /// <list type="table">
    /// <listheader><term>路径</term><description>描述</description></listheader>
    /// <item><term>A</term><description>直接命中空闲 → TotalRequests+1, TotalHits+1</description></item>
    /// <item><term>B</term><description>排队后命中空闲 → TotalRequests+1, TotalHits+1, Queued+1</description></item>
    /// <item><term>C</term><description>排队后创建新连接 → TotalRequests+1, TotalCreated+1, Queued+1</description></item>
    /// <item><term>D</term><description>排队后耗尽 → TotalRequests+1, TotalExhausted+1, Queued+1</description></item>
    /// <item><term>E</term><description>排队后创建失败 → TotalRequests+1, TotalCreateFailed+1, Queued+1</description></item>
    /// <item><term>F</term><description>排队期间取消 → TotalRequests+1, TotalCancelled+1, Queued+1</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 互斥关系（每个请求恰好一种结局）：<see cref="TotalRequests"/> = <see cref="TotalHits"/> + <see cref="TotalCreated"/> + <see cref="TotalExhausted"/> + <see cref="TotalCreateFailed"/> + <see cref="TotalCancelled"/>
    /// （例外：池释放窗口期的飞行请求可能无结局计数——入口检查与信号量销毁的竞态，恒等式允许个位数偏差）。
    /// </para>
    /// <para>
    /// 交叉关系（<see cref="TotalQueued"/> 是过程指标，与 <see cref="TotalHits"/> 在路径 B 重叠）：
    /// <see cref="TotalQueued"/> = B + C + D + E + F = <see cref="TotalRequests"/> - 直接命中数。
    /// </para>
    /// <para>
    /// 派生指标：
    /// <list type="bullet">
    /// <item>命中率 = <see cref="HitRate"/>（已提供）</item>
    /// <item>排队率 = <see cref="QueueRate"/>（已提供）</item>
    /// <item>即时命中率 = 100 - <see cref="QueueRate"/>（无需排队直接获取的占比）</item>
    /// <item>排队后命中率 = (<see cref="TotalHits"/> + <see cref="TotalQueued"/> - <see cref="TotalRequests"/>) / <see cref="TotalQueued"/> × 100（排队请求中最终命中空闲的占比）</item>
    /// </list>
    /// </para>
    /// </summary>
    public class PoolingDbConnectionStatistics : IDbConnectionStatistics
    {
        /// <inheritdoc/>
        /// <remarks>
        /// 等于 <see cref="TotalHits"/> + <see cref="TotalCreated"/> + <see cref="TotalExhausted"/> + <see cref="TotalCreateFailed"/> + <see cref="TotalCancelled"/>。
        /// </remarks>
        public long TotalRequests { get; internal set; }

        /// <inheritdoc/>
        /// <remarks>
        /// 含连接池初始化时预创建的连接。
        /// </remarks>
        public long TotalCreated { get; internal set; }

        /// <inheritdoc/>
        /// <remarks>
        /// 此指标仅统计创建异常，不包含连接池耗尽等其他失败情况。
        /// </remarks>
        public long TotalCreateFailed { get; internal set; }

        /// <inheritdoc/>
        /// <remarks>
        /// 仅统计创建异常的失败率，不包含连接池耗尽等其他失败情况。
        /// </remarks>
        public double CreateFailureRate => TotalRequests > 0 ? (double)TotalCreateFailed / TotalRequests * 100 : 0;

        /// <inheritdoc/>
        /// <remarks>
        /// 仅统计空闲连接的实际销毁（定时清理、激活失败淘汰），不含归还到池与活动连接销毁；连接池销毁时因事件已退订不计数。
        /// </remarks>
        public long TotalReleased { get; internal set; }

        /// <inheritdoc/>
        /// <remarks>
        /// 等于 <see cref="ActiveConnections"/> + <see cref="IdleConnections"/>（活动连接 + 空闲连接）。
        /// </remarks>
        public long AliveConnections => ActiveConnections + IdleConnections;

        /// <inheritdoc/>
        /// <remarks>
        /// 正在使用的连接数（快照值）；长期接近 <see cref="MaxPoolSize"/> 说明连接池容量不足。
        /// </remarks>
        public int ActiveConnections { get; internal set; }

        #region 连接池专属属性
        /// <summary>
        /// 最小连接池大小（配置值）。
        /// <para>连接池初始化时预创建的连接数，也是空闲连接清理的下限。</para>
        /// </summary>
        public int MinPoolSize { get; internal set; }

        /// <summary>
        /// 最大连接池大小（配置值）。
        /// <para>连接池允许的最大连接数，超过此限制的请求将排队等待。</para>
        /// <para>配合 <see cref="PoolUsageRate"/> 可评估池容量是否充足。</para>
        /// </summary>
        public int MaxPoolSize { get; internal set; }

        /// <summary>
        /// 当前空闲连接数（快照值）。
        /// <para>若空闲数长期为 0 且 <see cref="TotalExhausted"/> 持续增长，说明池容量不足。</para>
        /// </summary>
        public int IdleConnections { get; internal set; }

        /// <summary>
        /// 连接池使用率（百分比）。
        /// <para>即 <see cref="ActiveConnections"/> / <see cref="MaxPoolSize"/> × 100；持续高于 90% 表明池容量不足。</para>
        /// </summary>
        public double PoolUsageRate => MaxPoolSize > 0 ? (double)ActiveConnections / MaxPoolSize * 100 : 0;

        /// <summary>
        /// 池容量利用率（百分比）。
        /// <para>即 <see cref="AliveConnections"/> / <see cref="MaxPoolSize"/> × 100；与 <see cref="PoolUsageRate"/> 不同，此指标包含空闲连接。</para>
        /// </summary>
        public double PoolCapacityRate => MaxPoolSize > 0 ? (double)AliveConnections / MaxPoolSize * 100 : 0;

        /// <summary>
        /// 总命中次数（累计值）。
        /// <para>每次从空闲队列成功获取连接时递增（含直接命中和排队后命中）。</para>
        /// </summary>
        public long TotalHits { get; internal set; }

        /// <summary>
        /// 连接池命中率（百分比）。
        /// <para>即 <see cref="TotalHits"/> / <see cref="TotalRequests"/> × 100；命中率低说明频繁创建新连接，可能是池容量不足或连接生命周期过短。</para>
        /// </summary>
        public double HitRate => TotalRequests > 0 ? (double)TotalHits / TotalRequests * 100 : 0;

        /// <summary>
        /// 连接池耗尽次数（累计值）。
        /// <para>请求连接时池已满且等待超时则递增；任何大于 0 的值都值得关注，配合 <see cref="PoolUsageRate"/> 可判断是偶发还是容量不足。</para>
        /// </summary>
        public long TotalExhausted { get; internal set; }

        /// <summary>
        /// 排队请求数（累计值）。
        /// <para>请求未能直接命中空闲连接、进入等待状态时递增；与 <see cref="TotalHits"/> 在"排队后命中"场景重叠，不是互斥关系。</para>
        /// </summary>
        public long TotalQueued { get; internal set; }

        /// <summary>
        /// 排队率（百分比）。
        /// <para>即 <see cref="TotalQueued"/> / <see cref="TotalRequests"/> × 100；即时命中率 = 100 - 排队率。</para>
        /// </summary>
        public double QueueRate => TotalRequests > 0 ? (double)TotalQueued / TotalRequests * 100 : 0;

        /// <summary>
        /// 回收的连接数（累计值）。
        /// <para>活动连接被归还到空闲队列时递增；与 <see cref="TotalHits"/>（从池中取出）长期应趋于平衡。</para>
        /// </summary>
        public long TotalRecycled { get; internal set; }

        /// <summary>
        /// 排队期间被取消的请求数（累计值）。
        /// <para>排队等待时因取消令牌触发而中止的请求递增；持续大于 0 说明等待时间过长，可结合 <see cref="TotalQueued"/> 评估。</para>
        /// </summary>
        public long TotalCancelled { get; internal set; }

        /// <summary>
        /// 因借用者取消而销毁的连接数（累计值）。
        /// <para>激活阶段借用者取消会销毁健康的空闲连接、创建阶段取消会中断新建连接，两者均计入此指标；
        /// 行为保守安全（销毁重建），此指标用于评估取消频率对连接创建量的影响，与请求结局的 <see cref="TotalCancelled"/> 互不影响。</para>
        /// </summary>
        public long TotalDestroyedByCancellation { get; internal set; }
        #endregion
    }
}
