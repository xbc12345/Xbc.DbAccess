using System.IO;
using Microsoft.Extensions.Logging;
using Xbc.DbAccess;
using Xbc.DbAccess.Diagnostics;
using Xbc.DbAccess.Sqlite;

namespace DiagnosticsExample
{
    /// <summary>
    /// 12 可观测性诊断使用示例
    /// 演示日志路由、慢查询告警、连接统计与连接泄漏追踪
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== 可观测性诊断使用示例 ===\n");

            // 示例 1: 日志路由 + 慢查询告警
            Console.WriteLine("1. 日志路由与慢查询告警");
            await DemonstrateLoggingAndSlowQueryAsync();

            // 示例 2: 连接统计
            Console.WriteLine("\n2. 连接统计");
            await DemonstrateConnectionStatisticsAsync();

            // 示例 3: 连接泄漏追踪
            Console.WriteLine("\n3. 连接泄漏追踪");
            await DemonstrateLeakTrackingAsync();

            Console.WriteLine("\n所有示例完成!");
        }

        private static async Task DemonstrateLoggingAndSlowQueryAsync()
        {
            // 配置日志工厂（类别 "Xbc.DbAccess"）；DI 场景下 AddDbAccess 会自动接入容器中的 ILoggerFactory
            var loggerFactory = LoggerFactory.Create(builder => builder
                .AddConsole()
                .SetMinimumLevel(LogLevel.Warning));
            DbAccessDiagnostics.ConfigureLogging(loggerFactory);

            // 慢查询告警阈值（DI 场景经 AddDbAccessDiagnostics 配置）
            DbAccessDiagnostics.SlowQueryThreshold = TimeSpan.FromMilliseconds(10);

            var dbFileName = $"diagnostics_logging_{Guid.NewGuid()}.db";
            try
            {
                var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    options.ConnectionString = $"Data Source={dbFileName}";
                });

                using var db = factory.Create();
                await db.ExecuteNonQueryAsync("CREATE TABLE IF NOT EXISTS Users (user_id INTEGER PRIMARY KEY, user_name TEXT)");

                // 正常执行（远低于阈值，无告警）
                await db.ExecuteNonQueryAsync("INSERT INTO Users (user_name) VALUES ('张三')");

                // 用递归 CTE 压合延迟模拟慢查询，触发慢查询告警（Warn 日志，含操作名与 SQL 文本）
                await db.ExecuteScalarAsync<long>(@"
                    WITH RECURSIVE cnt(x) AS
                    (
                        SELECT 1
                        UNION ALL
                        SELECT x + 1 FROM cnt WHERE x < 3000000
                    )
                    SELECT COUNT(*) FROM cnt");

                Console.WriteLine("   （上方控制台已输出 Xbc.DbAccess 类别的慢查询告警 Warn 日志）");
            }
            finally
            {
                if (File.Exists(dbFileName))
                {
                    File.Delete(dbFileName);
                }
            }
        }

        private static async Task DemonstrateConnectionStatisticsAsync()
        {
            var dbFileName = $"diagnostics_stats_{Guid.NewGuid()}.db";
            try
            {
                var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    options.ConnectionString = $"Data Source={dbFileName}";
                    options.EnableStatistics = true;   // 启用连接统计
                });

                using var db = factory.Create();

                // 执行几次查询产生统计数据
                await db.ExecuteNonQueryAsync("CREATE TABLE IF NOT EXISTS Users (user_id INTEGER PRIMARY KEY)");
                for (int i = 0; i < 3; i++)
                {
                    await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users");
                }

                var stats = factory.ConnectionManager.GetStatistics();
                Console.WriteLine($"   总请求: {stats.TotalRequests}");
                Console.WriteLine($"   总创建: {stats.TotalCreated}");
                Console.WriteLine($"   创建失败: {stats.TotalCreateFailed}");
                Console.WriteLine($"   总归还: {stats.TotalReleased}");
                Console.WriteLine($"   活动连接: {stats.AliveConnections}");
                Console.WriteLine($"   总取消: {stats.TotalCancelled}");
            }
            finally
            {
                if (File.Exists(dbFileName))
                {
                    File.Delete(dbFileName);
                }
            }
        }

        private static async Task DemonstrateLeakTrackingAsync()
        {
            var dbFileName = $"diagnostics_leak_{Guid.NewGuid()}.db";
            try
            {
                var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    options.ConnectionString = $"Data Source={dbFileName}";

                    // 泄漏追踪前提：启用连接追踪 + 统计
                    options.EnableStatistics = true;
                    options.ConnectionTrackingOptions = new DbConnectionTrackingOptions
                    {
                        Enabled = true,
                        RecordStackTrace = true   // 记录借出堆栈，定位泄漏源头
                    };

                    // 阈值调小便于演示（生产环境保持默认 5/10 分钟）
                    options.PossibleLeakHoldTimeThreshold = TimeSpan.FromSeconds(1);
                    options.CriticalLeakHoldTimeThreshold = TimeSpan.FromSeconds(2);
                });

                using var db = factory.Create();

                // 借出一个连接后不归还（模拟泄漏）
                var leakedScope = await db.BeginConnectionScopeAsync();
                Console.WriteLine($"   已借出连接（作用域 {leakedScope.ScopeId}）并持有不归还");

                // 等待超过"严重泄漏"阈值
                await Task.Delay(TimeSpan.FromSeconds(2.5));

                // 查询全部工厂的泄漏快照（无状态查询，可由 HostedService 定时巡检接入告警）
                IReadOnlyList<LeakedConnectionInfo> leaks = DbAccessDiagnostics.GetLeakedConnections();
                Console.WriteLine($"   泄漏连接快照: {leaks.Count} 条");
                foreach (var leak in leaks)
                {
                    Console.WriteLine($"   [{(leak.IsCritical ? "严重" : "可能")}] {leak.ProviderName}" +
                        $" 连接 {leak.TrackingInfo.ConnectionId}" +
                        $" 持有 {(DateTime.Now - leak.TrackingInfo.AcquiredAt).TotalSeconds:F1}s");
                    Console.WriteLine($"   获取堆栈（首帧）: {leak.TrackingInfo.AcquireStackTrace.Split('\n').FirstOrDefault()}");
                }

                // 清理：归还借出的连接，避免进程退出告警
                leakedScope.Dispose();
            }
            finally
            {
                if (File.Exists(dbFileName))
                {
                    File.Delete(dbFileName);
                }
            }
        }
    }
}
