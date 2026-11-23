namespace Xbc.DbAccess.Diagnostics
{
    /// <summary>
    /// 泄漏连接查询结果，包装连接追踪信息与泄漏级别，由 <see cref="DbAccessDiagnostics.GetLeakedConnections"/> 返回。
    /// </summary>
    public sealed class LeakedConnectionInfo
    {
        /// <summary>
        /// 连接追踪信息（含连接ID、持有时长、获取堆栈等）。
        /// </summary>
        public DbConnectionTrackingInfo TrackingInfo { get; }

        /// <summary>
        /// 提供程序名称（如 Microsoft.Data.Sqlite）。
        /// </summary>
        public string ProviderName { get; }

        /// <summary>
        /// 是否达到严重泄漏阈值；false 表示仅达到可能泄漏阈值。
        /// </summary>
        public bool IsCritical { get; }

        /// <summary>
        /// 初始化泄漏连接查询结果。
        /// </summary>
        /// <param name="trackingInfo">连接追踪信息。</param>
        /// <param name="providerName">提供程序名称。</param>
        /// <param name="isCritical">是否达到严重泄漏阈值。</param>
        public LeakedConnectionInfo(DbConnectionTrackingInfo trackingInfo, string providerName, bool isCritical)
        {
            TrackingInfo = trackingInfo ?? throw new ArgumentNullException(nameof(trackingInfo));
            ProviderName = providerName;
            IsCritical = isCritical;
        }
    }
}
