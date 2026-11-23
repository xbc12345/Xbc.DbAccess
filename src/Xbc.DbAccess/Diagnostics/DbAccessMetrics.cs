using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics.Metrics;

namespace Xbc.DbAccess.Diagnostics
{
    /// <summary>
    /// DbAccess 指标采集器，基于 <see cref="Meter"/> 发布 SQL 执行、连接获取与连接池状态指标，遵循 OpenTelemetry 语义约定。
    /// 泄漏判定只发布指标数据，告警条件由消费侧定义。
    /// </summary>
    public static class DbAccessMetrics
    {
        /// <summary>
        /// Meter 名称。
        /// </summary>
        public const string MeterName = "Xbc.DbAccess";

        /// <summary>
        /// SQL 执行耗时指标名称（直方图，毫秒）。
        /// </summary>
        public const string SqlDurationMetricName = "xbc.dbaccess.sql.duration";

        /// <summary>
        /// SQL 执行计数指标名称（计数器）。
        /// </summary>
        public const string SqlCountMetricName = "xbc.dbaccess.sql.count";

        /// <summary>
        /// 连接获取耗时指标名称（直方图，毫秒）。
        /// </summary>
        public const string ConnectionAcquireMetricName = "xbc.dbaccess.connection.acquire";

        /// <summary>
        /// 当前活动连接数指标名称（可观测仪表）。
        /// </summary>
        public const string PoolActiveConnectionsMetricName = "xbc.dbaccess.pool.active_connections";

        /// <summary>
        /// 当前空闲连接数指标名称（可观测仪表，仅连接池模式的管理器上报）。
        /// </summary>
        public const string PoolIdleConnectionsMetricName = "xbc.dbaccess.pool.idle_connections";

        /// <summary>
        /// 连接池最大容量指标名称（可观测仪表，仅连接池模式的管理器上报）。
        /// </summary>
        public const string PoolMaxSizeMetricName = "xbc.dbaccess.pool.max_size";

        /// <summary>
        /// 可能泄漏的连接数指标名称（可观测仪表）：持有时间超过可能泄漏阈值（默认 5 分钟）的借出连接数。
        /// </summary>
        public const string PossibleLeakConnectionsMetricName = "xbc.dbaccess.connection.possible_leak";

        /// <summary>
        /// 严重泄漏的连接数指标名称（可观测仪表）：持有时间超过严重泄漏阈值（默认 10 分钟）的借出连接数。
        /// </summary>
        public const string CriticalLeakConnectionsMetricName = "xbc.dbaccess.connection.critical_leak";

        private static readonly Meter meter = new Meter(MeterName);

        /// <summary>
        /// SQL 执行耗时直方图（毫秒），tag：provider / operation / status。
        /// </summary>
        public static Histogram<double> SqlDuration { get; }
            = meter.CreateHistogram<double>(SqlDurationMetricName, "ms", "SQL 执行耗时");

        /// <summary>
        /// SQL 执行计数器，tag：provider / operation / status。
        /// </summary>
        public static Counter<long> SqlCount { get; }
            = meter.CreateCounter<long>(SqlCountMetricName, null, "SQL 执行次数");

        /// <summary>
        /// 连接获取耗时直方图（毫秒），tag：provider / status（ok / error）。
        /// </summary>
        public static Histogram<double> ConnectionAcquire { get; }
            = meter.CreateHistogram<double>(ConnectionAcquireMetricName, "ms", "连接获取耗时");

        /// <summary>
        /// 当前活动连接数仪表，tag：provider。
        /// </summary>
        private static readonly ObservableGauge<int> poolActiveConnectionsGauge = meter.CreateObservableGauge<int>(
            PoolActiveConnectionsMetricName, ObservePoolActiveConnections, null, "当前活动连接数");

        /// <summary>
        /// 当前空闲连接数仪表，tag：provider；仅连接池模式的管理器上报。
        /// </summary>
        private static readonly ObservableGauge<int> poolIdleConnectionsGauge = meter.CreateObservableGauge<int>(
            PoolIdleConnectionsMetricName, ObservePoolIdleConnections, null, "当前空闲连接数");

        /// <summary>
        /// 连接池最大容量仪表，tag：provider；仅连接池模式的管理器上报。
        /// </summary>
        private static readonly ObservableGauge<int> poolMaxSizeGauge = meter.CreateObservableGauge<int>(
            PoolMaxSizeMetricName, ObservePoolMaxSize, null, "连接池最大容量");

        /// <summary>
        /// 可能泄漏的连接数仪表，tag：provider。
        /// </summary>
        private static readonly ObservableGauge<int> possibleLeakConnectionsGauge = meter.CreateObservableGauge<int>(
            PossibleLeakConnectionsMetricName, ObservePossibleLeakConnections, null, "可能泄漏的连接数");

        /// <summary>
        /// 严重泄漏的连接数仪表，tag：provider。
        /// </summary>
        private static readonly ObservableGauge<int> criticalLeakConnectionsGauge = meter.CreateObservableGauge<int>(
            CriticalLeakConnectionsMetricName, ObserveCriticalLeakConnections, null, "严重泄漏的连接数");

        // 已注册的连接管理器：管理器 ID -> 注册信息
        private static readonly ConcurrentDictionary<Guid, RegisteredManager> registeredManagers
            = new ConcurrentDictionary<Guid, RegisteredManager>();

        /// <summary>
        /// 从 <see cref="DbProviderFactory"/> 解析提供程序名称：工厂类型名去掉 Factory 后缀（如 MySqlClientFactory → MySqlClient），不以 Factory 结尾时原样返回，工厂为 null 时返回 unknown。
        /// </summary>
        /// <param name="providerFactory">数据库提供程序工厂。</param>
        /// <returns>提供程序名称。</returns>
        internal static string GetProviderName(DbProviderFactory providerFactory)
        {
            var typeName = providerFactory?.GetType().Name;
            if (string.IsNullOrEmpty(typeName))
                return "unknown";

            return typeName.EndsWith("Factory", StringComparison.Ordinal)
                ? typeName.Substring(0, typeName.Length - "Factory".Length)
                : typeName;
        }

        /// <summary>
        /// 记录一次 SQL 执行（执行次数 + 执行耗时）。
        /// </summary>
        /// <param name="provider">提供程序名称。</param>
        /// <param name="operation">操作名称（如 ExecuteNonQuery）。</param>
        /// <param name="status">执行状态（ok / error）。</param>
        /// <param name="durationMs">执行耗时（毫秒）。</param>
        internal static void RecordSqlExecution(string provider, string operation, string status, double durationMs)
        {
            var providerTag = new KeyValuePair<string, object>("provider", provider);
            var operationTag = new KeyValuePair<string, object>("operation", operation);
            var statusTag = new KeyValuePair<string, object>("status", status);

            SqlCount.Add(1, providerTag, operationTag, statusTag);
            SqlDuration.Record(durationMs, providerTag, operationTag, statusTag);
        }

        /// <summary>
        /// 记录一次连接获取耗时。
        /// </summary>
        /// <param name="provider">提供程序名称。</param>
        /// <param name="status">获取状态（ok / error）。</param>
        /// <param name="durationMs">连接获取耗时（毫秒）。</param>
        internal static void RecordConnectionAcquire(string provider, string status, double durationMs)
        {
            ConnectionAcquire.Record(durationMs,
                new KeyValuePair<string, object>("provider", provider),
                new KeyValuePair<string, object>("status", status));
        }

        /// <summary>
        /// 采集当前活动连接数（每个已注册管理器一条测量）。
        /// </summary>
        internal static IEnumerable<Measurement<int>> ObservePoolActiveConnections()
        {
            foreach (var entry in registeredManagers.Values)
            {
                var statistics = entry.Manager.GetStatistics();
                if (statistics != null)
                    yield return new Measurement<int>(statistics.ActiveConnections, entry.ProviderTag);
            }
        }

        /// <summary>
        /// 采集当前空闲连接数（仅连接池模式的管理器上报）。
        /// </summary>
        internal static IEnumerable<Measurement<int>> ObservePoolIdleConnections()
        {
            foreach (var entry in registeredManagers.Values)
            {
                if (entry.Manager.GetStatistics() is PoolingDbConnectionStatistics pooling)
                    yield return new Measurement<int>(pooling.IdleConnections, entry.ProviderTag);
            }
        }

        /// <summary>
        /// 采集连接池最大容量（仅连接池模式的管理器上报）。
        /// </summary>
        internal static IEnumerable<Measurement<int>> ObservePoolMaxSize()
        {
            foreach (var entry in registeredManagers.Values)
            {
                if (entry.Manager.GetStatistics() is PoolingDbConnectionStatistics pooling)
                    yield return new Measurement<int>(pooling.MaxPoolSize, entry.ProviderTag);
            }
        }

        #region 泄漏连接检测
        /// <summary>
        /// 采集可能泄漏的连接数。
        /// </summary>
        internal static IEnumerable<Measurement<int>> ObservePossibleLeakConnections()
        {
            foreach (var entry in registeredManagers.Values)
            {
                var leakCount = CountLeaks(entry.Manager, entry.PossibleLeakHoldTimeThreshold);
                yield return new Measurement<int>(leakCount, entry.ProviderTag);
            }
        }

        /// <summary>
        /// 采集严重泄漏的连接数。
        /// </summary>
        internal static IEnumerable<Measurement<int>> ObserveCriticalLeakConnections()
        {
            foreach (var entry in registeredManagers.Values)
            {
                var leakCount = CountLeaks(entry.Manager, entry.CriticalLeakHoldTimeThreshold);
                yield return new Measurement<int>(leakCount, entry.ProviderTag);
            }
        }

        /// <summary>
        /// 按持有时间阈值统计借出未还的泄漏连接数。
        /// </summary>
        private static int CountLeaks(IDbConnectionManager manager, TimeSpan threshold)
        {
            // FindAll 已返回物化列表，直接取 Count，不再额外 ToList 分配
            return manager.ConnectionTracker.FindAll(x => x.IsLeaked(threshold)).Count;
        }

        /// <summary>
        /// 遍历所有注册的连接管理器，返回当前泄漏的连接快照（达到可能阈值即列入，达到严重阈值标记为严重级别）。
        /// </summary>
        internal static List<LeakedConnectionInfo> FindAllLeaks()
        {
            var leaks = new List<LeakedConnectionInfo>();
            foreach (var entry in registeredManagers.Values)
            {
                foreach (var info in entry.Manager.ConnectionTracker.FindAll())
                {
                    if (info.IsLeaked(entry.CriticalLeakHoldTimeThreshold))
                        leaks.Add(new LeakedConnectionInfo(info, entry.ProviderName, true));
                    else if (info.IsLeaked(entry.PossibleLeakHoldTimeThreshold))
                        leaks.Add(new LeakedConnectionInfo(info, entry.ProviderName, false));
                }
            }

            return leaks;
        }
        #endregion

        #region 连接管理器管理
        /// <summary>
        /// 注册连接管理器，由工厂创建管理器后调用；仪表采集时读取其统计信息。
        /// </summary>
        /// <param name="manager">连接管理器。</param>
        /// <param name="provider">提供程序名称，作为指标 tag。</param>
        /// <param name="possibleLeakHoldTimeThreshold">可能泄漏的持有时间阈值。</param>
        /// <param name="criticalLeakHoldTimeThreshold">严重泄漏的持有时间阈值。</param>
        internal static void RegisterManager(
            IDbConnectionManager manager,
            string provider,
            TimeSpan possibleLeakHoldTimeThreshold,
            TimeSpan criticalLeakHoldTimeThreshold)
        {
            Guard.ThrowIfNull(manager, nameof(manager));

            registeredManagers[manager.ManagerId] = new RegisteredManager
            {
                Manager = manager,
                ProviderTag = new KeyValuePair<string, object>("provider", provider ?? "unknown"),
                ProviderName = provider ?? "unknown",
                PossibleLeakHoldTimeThreshold = possibleLeakHoldTimeThreshold,
                CriticalLeakHoldTimeThreshold = criticalLeakHoldTimeThreshold,
            };
        }

        /// <summary>
        /// 注销连接管理器，由工厂释放管理器前调用。
        /// </summary>
        /// <param name="managerId">连接管理器唯一标识。</param>
        internal static void UnregisterManager(Guid managerId)
        {
            registeredManagers.TryRemove(managerId, out _);
        }

        /// <summary>
        /// 注册的连接管理器及其泄漏判定阈值。
        /// </summary>
        private sealed class RegisteredManager
        {
            /// <summary>连接管理器。</summary>
            public IDbConnectionManager Manager { get; set; }

            /// <summary>提供程序名称 tag。</summary>
            public KeyValuePair<string, object> ProviderTag { get; set; }

            /// <summary>提供程序名称。</summary>
            public string ProviderName { get; set; }

            /// <summary>可能泄漏的持有时间阈值。</summary>
            public TimeSpan PossibleLeakHoldTimeThreshold { get; set; }

            /// <summary>严重泄漏的持有时间阈值。</summary>
            public TimeSpan CriticalLeakHoldTimeThreshold { get; set; }
        }
        #endregion
    }
}
