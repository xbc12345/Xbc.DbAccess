using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Diagnostics
{
    /// <summary>
    /// DbAccess 诊断配置入口：日志路由（<see cref="ConfigureLogging"/>）、慢查询告警（<see cref="SlowQueryThreshold"/>）与泄漏连接查询（<see cref="GetLeakedConnections"/>）。
    /// <para>未配置日志时不输出；经 <c>AddDbAccess</c> 注册时容器中的 <see cref="ILoggerFactory"/> 自动接入，显式配置优先（见 <see cref="ApplyFromContainer"/>）。</para>
    /// </summary>
    public static class DbAccessDiagnostics
    {
        /// <summary>
        /// 慢查询告警阈值；null（默认）禁用。计时口径与 <c>xbc.dbaccess.sql.duration</c> 指标一致，不含连接获取时间。
        /// </summary>
        public static TimeSpan? SlowQueryThreshold { get; set; }

        /// <summary>
        /// 配置日志工厂，内部 Warn / Error 与慢查询告警路由到 ILogger（日志类别 "Xbc.DbAccess"）；传 null 停止输出。
        /// </summary>
        /// <param name="loggerFactory">日志工厂。</param>
        public static void ConfigureLogging(ILoggerFactory loggerFactory)
        {
            DiagnosticLog.Configure(loggerFactory);
        }

        /// <summary>
        /// 从 DI 容器自动应用诊断配置，由 <c>AddDbAccess</c> 的解析钩子在首次解析数据库工厂时调用，幂等。
        /// <para>日志仅在从未配置过时接入，显式配置优先；慢查询阈值经 <c>AddDbAccessDiagnostics</c> 配置且非 null 时应用。</para>
        /// </summary>
        /// <param name="serviceProvider">DI 容器服务提供器。</param>
        internal static void ApplyFromContainer(IServiceProvider serviceProvider)
        {
            // 日志自动接入仅填补“从未配置”的空窗，不覆盖显式配置
            if (!DiagnosticLog.IsConfigured)
            {
                var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                if (loggerFactory != null)
                    DiagnosticLog.Configure(loggerFactory);
            }

            // 慢查询阈值：options 未配置（无 IOptions 服务）或阈值为 null（禁用）时不覆盖当前静态值
            var options = serviceProvider.GetService<IOptions<DbAccessDiagnosticsOptions>>()?.Value;
            if (options?.SlowQueryThreshold != null)
                SlowQueryThreshold = options.SlowQueryThreshold;
        }

        /// <summary>
        /// 耗时达到阈值时输出 Warn 日志（操作名、SQL 文本与耗时）；阈值未设置时不操作。
        /// </summary>
        /// <param name="operation">操作名称（如 ExecuteNonQueryAsync）。</param>
        /// <param name="sql">SQL 命令文本（参数化占位符形式，不含参数值）。</param>
        /// <param name="duration">执行耗时。</param>
        internal static void WarnIfSlowQuery(string operation, string sql, TimeSpan duration)
        {
            var threshold = SlowQueryThreshold;
            if (threshold.HasValue && duration >= threshold.Value)
                DiagnosticLog.Warn(nameof(DbAccessDiagnostics), "SQL 执行耗时 {Duration}ms，达到慢查询阈值 {Threshold}ms：{Operation} {Sql}",
                    duration.TotalMilliseconds, threshold.Value.TotalMilliseconds, operation, sql);
        }

        /// <summary>
        /// 获取所有工厂当前泄漏的连接快照（无状态查询）。达到严重阈值（默认 10 分钟）的 <see cref="LeakedConnectionInfo.IsCritical"/> 为 true，可能阈值默认 5 分钟。
        /// <para>仅统计已启用连接追踪与统计（<c>ConnectionTrackingOptions.Enabled</c> 与 <c>EnableStatistics</c>）的工厂；何时调用与如何告警由消费侧决定。</para>
        /// </summary>
        public static IReadOnlyList<LeakedConnectionInfo> GetLeakedConnections()
        {
            return DbAccessMetrics.FindAllLeaks();
        }
    }
}
