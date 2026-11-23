using Xbc.DbAccess.Diagnostics;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库连接选项。
    /// </summary>
    public sealed class DbConnectionOptions
    {
        /// <summary>
        /// 连接字符串。
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// 连接池选项。
        /// <para>此处为 DbAccess 内建的进程内连接池（借用/归还，归还时连接保持打开），与 ADO.NET 提供程序的原生连接池（连接串 Pooling 开关）是相互独立的两层。
        /// 同时启用会导致物理连接数放大，应二选一：启用内建池时在连接串中关闭原生池（如 SqlClient 的 <c>Pooling=false</c>、Npgsql 的 <c>No Pooling=true</c>）。</para>
        /// </summary>
        public DbConnectionPoolOptions PoolOptions { get; set; } = new DbConnectionPoolOptions();

        /// <summary>
        /// 连接追踪选项。
        /// <para>通过 <see cref="DbConnectionTrackingOptions.Enabled"/> 控制是否启用连接追踪。</para>
        /// </summary>
        public DbConnectionTrackingOptions ConnectionTrackingOptions { get; set; } = new DbConnectionTrackingOptions();

        /// <summary>
        /// 是否启用连接统计，默认值为 false。
        /// </summary>
        public bool EnableStatistics { get; set; }

        /// <summary>
        /// 可能连接泄漏的持有时间阈值，用于 OTel 泄漏指标（possible_leak）判定，默认值为 5 分钟；告警条件由消费侧基于指标定义。
        /// </summary>
        public TimeSpan PossibleLeakHoldTimeThreshold { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 严重连接泄漏的持有时间阈值，用于 OTel 泄漏指标（critical_leak）判定，默认值为 10 分钟。
        /// <para>此值应大于 <see cref="PossibleLeakHoldTimeThreshold"/>。</para>
        /// </summary>
        public TimeSpan CriticalLeakHoldTimeThreshold { get; set; } = TimeSpan.FromMinutes(10);
    }
}
