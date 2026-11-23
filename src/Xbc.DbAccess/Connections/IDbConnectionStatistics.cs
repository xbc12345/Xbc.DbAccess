namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库连接统计信息接口，定义连接管理器对外提供的公共统计属性。
    /// <para>两种实现：</para>
    /// <list type="bullet">
    /// <item><see cref="PoolingDbConnectionStatistics"/>：连接池模式</item>
    /// <item><see cref="DefaultDbConnectionStatistics"/>：非连接池模式</item>
    /// </list>
    /// </summary>
    public interface IDbConnectionStatistics
    {
        /// <summary>
        /// 请求总数（累计值）。
        /// <para>每次调用 <see cref="IDbConnectionManager.GetOrCreateAsync(CancellationToken)"/> 时递增，是多数比率指标的分母。</para>
        /// </summary>
        long TotalRequests { get; }

        /// <summary>
        /// 创建的连接总数（累计值）。
        /// <para>每次成功创建新连接时递增。</para>
        /// </summary>
        long TotalCreated { get; }

        /// <summary>
        /// 创建连接失败总数（累计值）。
        /// <para>每次创建连接失败或抛出异常时递增。</para>
        /// </summary>
        long TotalCreateFailed { get; }

        /// <summary>
        /// 创建失败率（百分比）。
        /// <para>即 <see cref="TotalCreateFailed"/> / <see cref="TotalRequests"/> × 100。</para>
        /// </summary>
        double CreateFailureRate { get; }

        /// <summary>
        /// 释放的连接总数（累计值）。
        /// </summary>
        long TotalReleased { get; }

        /// <summary>
        /// 连接存活数（快照值）。
        /// </summary>
        long AliveConnections { get; }

        /// <summary>
        /// 当前活动连接数（快照值）。
        /// </summary>
        int ActiveConnections { get; }

        /// <summary>
        /// 排队等待期间被取消的请求数（累计值）。
        /// <para>非连接池模式不存在排队，恒为 0。</para>
        /// </summary>
        long TotalCancelled { get; }
    }
}
