using System;
using System.Threading.Tasks;
using Xbc.DbAccess;
using Xbc.DbAccess.Sqlite;

namespace Examples
{
    /// <summary>
    /// 示例 02: 使用连接池
    /// 演示如何配置和使用连接池
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== 示例 02: 使用连接池 ===\n");

            // 1. 连接池基础配置
            await DemoBasicPoolConfiguration();

            // 2. 连接池高级配置
            await DemoAdvancedPoolConfiguration();

            // 3. 连接池性能对比
            await DemoPoolPerformanceComparison();

            // 4. 连接池调优演示（异步）
            await DemoConnectionPoolTuning();

            Console.WriteLine("示例运行完成！");
            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }

        /// <summary>
        /// 1. 连接池基础配置
        /// </summary>
        private static async Task DemoBasicPoolConfiguration()
        {
            Console.WriteLine("【1. 连接池基础配置】\n");

            var dbFileName = $"myapp02_config_{Guid.NewGuid()}.db";
            try
            {
                // 启用连接池
                var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    options.ConnectionString = $"Data Source={dbFileName}";
                    options.PoolOptions = new DbConnectionPoolOptions
                    {
                        Enabled = true  // 启用连接池
                    };
                });

                using (var db = factory.Create())
                {
                    await db.ExecuteNonQueryAsync("CREATE TABLE IF NOT EXISTS Test (Id INTEGER)");
                    await db.ExecuteNonQueryAsync("INSERT INTO Test VALUES (1)");
                    var count = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM Test");
                    Console.WriteLine($"   连接池已启用，记录数: {count}\n");
                }
            }
            finally
            {
                try { File.Delete(dbFileName); } catch { }
            }
        }

        /// <summary>
        /// 2. 连接池高级配置
        /// </summary>
        private static async Task DemoAdvancedPoolConfiguration()
        {
            Console.WriteLine("【2. 连接池高级配置】\n");

            var dbFileName = $"myapp02_config_{Guid.NewGuid()}.db";
            try
            {
                var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    options.ConnectionString = $"Data Source={dbFileName}";
                    options.PoolOptions = new DbConnectionPoolOptions
                    {
                        Enabled = true,                                     // 启用连接池
                        MinPoolSize = 2,                                    // 最小连接数
                        MaxPoolSize = 10,                                   // 最大连接数
                        ConnectionWaitTimeout = TimeSpan.FromSeconds(30),   // 连接等待超时
                        MaxIdleTime = TimeSpan.FromMinutes(5),              // 最大空闲时间
                        CleanupInterval = TimeSpan.FromMinutes(10)          // 清理间隔
                    };
                });

                using (var db = factory.Create())
                {
                    await db.ExecuteNonQueryAsync("CREATE TABLE IF NOT EXISTS AdvancedTest (Id INTEGER)");
                    await db.ExecuteNonQueryAsync("INSERT INTO AdvancedTest VALUES (1)");
                    var count = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM AdvancedTest");
                    Console.WriteLine($"   高级配置成功，记录数: {count}\n");
                }
            }
            finally
            {
                try { File.Delete(dbFileName); } catch { }
            }
        }

        /// <summary>
        /// 3. 连接池性能对比
        /// </summary>
        private static async Task DemoPoolPerformanceComparison()
        {
            Console.WriteLine("【3. 连接池性能对比】\n");

            var dbFileName = $"myapp02_pool_perf_{Guid.NewGuid()}.db";
            try
            {
                const int iterationCount = 100;

                // 无连接池
                var noPoolFactory = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    options.ConnectionString = $"Data Source={dbFileName}";
                    options.PoolOptions = new DbConnectionPoolOptions { Enabled = false };
                });

                long noPoolElapsedTicks;
                using (var db = noPoolFactory.Create())
                {
                    var noPoolStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < iterationCount; i++)
                    {
                        await db.ExecuteScalarAsync("SELECT 1");
                    }
                    noPoolStopwatch.Stop();
                    noPoolElapsedTicks = noPoolStopwatch.ElapsedTicks;
                    Console.WriteLine($"   无连接池耗时: {noPoolStopwatch.ElapsedMilliseconds} ms");
                }

                // 有连接池
                var poolFactory = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    options.ConnectionString = $"Data Source={dbFileName}";
                    options.PoolOptions = new DbConnectionPoolOptions
                    {
                        Enabled = true,
                        MinPoolSize = 5,
                        MaxPoolSize = 20
                    };
                });

                long poolElapsedTicks;
                using (var db = poolFactory.Create())
                {
                    // 预热
                    for (int i = 0; i < 5; i++)
                    {
                        await db.ExecuteScalarAsync("SELECT 1");
                    }

                    var poolStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < iterationCount; i++)
                    {
                        await db.ExecuteScalarAsync("SELECT 1");
                    }
                    poolStopwatch.Stop();
                    poolElapsedTicks = poolStopwatch.ElapsedTicks;
                    Console.WriteLine($"   有连接池耗时: {poolStopwatch.ElapsedMilliseconds} ms");
                }

                // 3.3 性能对比结果
                Console.WriteLine("3.3 性能对比结果");
                var improvement = noPoolElapsedTicks > 0
                    ? (double)(noPoolElapsedTicks - poolElapsedTicks) / noPoolElapsedTicks * 100
                    : 0;
                Console.WriteLine($"   性能提升: {improvement:F2}%\n");
            }
            finally
            {
                try { File.Delete(dbFileName); } catch { }
            }
        }

        /// <summary>
        /// 4. 连接池调优演示
        /// 演示如何通过模拟高并发场景来发现连接池问题，并根据统计数据进行调优
        /// </summary>
        public static async Task DemoConnectionPoolTuning()
        {
            Console.WriteLine("【4. 连接池调优演示】\n");
            Console.WriteLine("   场景：模拟40个并发任务，观察连接池耗尽情况，然后调整配置解决问题\n");

            // 第1轮：使用较小的连接池，观察问题
            Console.WriteLine("【第1轮】使用 MaxPoolSize=5 模拟高并发...");
            try
            {
                await RunBenchmark(maxPoolSize: 5, taskCount: 40);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 第1轮出现错误（预期内）: {ex.InnerException?.Message ?? ex.Message}");
                Console.WriteLine("   这是正常的，因为 MaxPoolSize=5 无法满足40个并发任务的需求\n");
            }

            Console.WriteLine("\n" + new string('=', 40) + "\n");

            // 第2轮：增加连接池大小，解决问题，但是等待时间过长
            Console.WriteLine("【第2轮】使用 MaxPoolSize=20 优化性能...");
            try
            {
                await RunBenchmark(maxPoolSize: 20, taskCount: 40);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 第2轮出现错误（非预期内）: {ex.InnerException?.Message ?? ex.Message}");
            }

            // 第3轮：继续增加连接池大小，解决问题
            Console.WriteLine("【第3轮】调整 MaxPoolSize=30 优化性能...");
            try
            {
                await RunBenchmark(maxPoolSize: 40, taskCount: 40);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 第3轮出现错误（非预期内）: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        static async Task RunBenchmark(int maxPoolSize, int taskCount)
        {
            // 使用文件数据库，确保多个连接可以共享数据
            var dbFileName = $"myapp02_pool_tuning_{Guid.NewGuid()}.db";

            try
            {
                // 创建工厂
                var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    options.ConnectionString = $"Data Source={dbFileName}";
                    options.PoolOptions = new DbConnectionPoolOptions
                    {
                        Enabled = true,
                        MinPoolSize = 3,
                        MaxPoolSize = maxPoolSize,
                        ConnectionWaitTimeout = TimeSpan.FromSeconds(3)
                    };
                    options.EnableStatistics = true;
                });

                // 初始化数据库
                using (var db = factory.Create())
                {
                    await db.ExecuteNonQueryAsync("CREATE TABLE IF NOT EXISTS DemoTable (Id INTEGER PRIMARY KEY, Data TEXT)");
                }

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // 创建多个并发任务
                var tasks = new Task[taskCount];
                for (int i = 0; i < taskCount; i++)
                {
                    int taskId = i;
                    tasks[i] = Task.Run(async () =>
                    {
                        using (var db = factory.Create())
                        {
                            // 模拟数据库操作
                            for (int j = 0; j < 5; j++)
                            {
                                await db.ExecuteNonQueryAsync(
                                    "INSERT INTO DemoTable (Data) VALUES (@Data)",
                                    new { Data = $"Task{taskId}-Op{j}" });

                                // 模拟业务处理时间
                                await Task.Delay(30);
                            }
                        }
                    });
                }

                // 等待所有任务完成
                await Task.WhenAll(tasks);
                stopwatch.Stop();

                Console.WriteLine($"   完成 {taskCount} 个并发任务，总耗时: {stopwatch.ElapsedMilliseconds}ms");

                // 获取并显示统计信息
                var stats = factory.ConnectionManager.GetStatistics();
                var poolingStats = stats as PoolingDbConnectionStatistics;
                Console.WriteLine("\n   【统计信息】");
                Console.WriteLine($"   - 活动连接数: {stats.ActiveConnections}");

                if (poolingStats != null)
                {
                    Console.WriteLine($"   - 连接池使用率: {poolingStats.PoolUsageRate:F2}%");
                    Console.WriteLine($"   - 连接池耗尽次数: {poolingStats.TotalExhausted}");
                    Console.WriteLine($"   - 排队请求数: {poolingStats.TotalQueued}");

                    // 自动分析并给出建议（根据阈值判断，应根据实际场景调整）
                    Console.WriteLine("\n   【调优分析】");
                    if (poolingStats.TotalExhausted > 0)
                    {
                        Console.WriteLine($"   ⚠️ 连接池耗尽 {poolingStats.TotalExhausted} 次！");
                        Console.WriteLine($"      建议：增加 MaxPoolSize（当前: {maxPoolSize}）");
                    }
                    else if (poolingStats.PoolUsageRate > 80)
                    {
                        Console.WriteLine($"   ℹ️ 连接池使用率较高 ({poolingStats.PoolUsageRate:F2}%)，阈值为 80%");
                        Console.WriteLine($"      建议：监控连接池使用情况，必要时扩容");
                    }
                    else
                    {
                        Console.WriteLine($"   ✓ 连接池工作正常");
                    }
                }
            }
            finally
            {
                // 清理临时文件
                try { File.Delete(dbFileName); } catch { }
            }
        }
    }
}
