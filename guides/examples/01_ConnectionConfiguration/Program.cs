using Xbc.DbAccess;
using Xbc.DbAccess.Diagnostics;
using Xbc.DbAccess.Sqlite;

namespace Examples
{
    /// <summary>
    /// 示例 01: IDbAccessFactory 和连接配置
    /// 演示如何配置连接字符串、连接选项
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== 示例 01: IDbAccessFactory 和连接配置 ===\n");

            // 1. 内存数据库连接字符串
            await DemoMemoryDatabase();

            // 2. 文件数据库连接字符串
            await DemoFileDatabase();

            // 3. 连接配置选项
            await DemoConnectionOptions();

            // 4. 完整示例
            await DemoCompleteExample();

            Console.WriteLine("示例运行完成！");
            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }

        /// <summary>
        /// 1. 内存数据库连接字符串
        /// </summary>
        private static async Task DemoMemoryDatabase()
        {
            Console.WriteLine("【1. 内存数据库连接字符串】\n");

            // 最简配置
            var factory = SqliteAccessFactory.CreateSqliteFactory("Data Source=:memory:");

            using (var db = factory.Create())
            {
                var result = await db.ExecuteScalarAsync("SELECT 1");
                Console.WriteLine($"   连接成功，测试查询结果: {result}\n");
            }
        }

        /// <summary>
        /// 2. 文件数据库连接字符串
        /// </summary>
        private static async Task DemoFileDatabase()
        {
            Console.WriteLine("【2. 文件数据库连接字符串】\n");

            var dbFileName = $"myapp01_config_{Guid.NewGuid()}.db";
            try
            {
                var factory = SqliteAccessFactory.CreateSqliteFactory($"Data Source={dbFileName}");

                using (var db = factory.Create())
                {
                    await db.ExecuteNonQueryAsync("CREATE TABLE Test (Id INTEGER)");
                    await db.ExecuteNonQueryAsync("INSERT INTO Test VALUES (1)");
                    var count = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM Test");
                    Console.WriteLine($"   文件数据库连接成功，记录数: {count}\n");
                }
            }
            finally
            {
                try { System.IO.File.Delete(dbFileName); } catch { }
            }
        }

        /// <summary>
        /// 3. 连接配置选项
        /// </summary>
        private static async Task DemoConnectionOptions()
        {
            Console.WriteLine("【3. 连接配置选项】\n");

            // 3.1 基础连接选项
            Console.WriteLine("3.1 基础连接选项");
            var factory1 = SqliteAccessFactory.CreateSqliteFactory(options =>
            {
                options.ConnectionString = "Data Source=:memory:";
            });

            using (var db = factory1.Create())
            {
                await db.ExecuteNonQueryAsync("SELECT 1");
                Console.WriteLine("   基础连接选项配置成功\n");
            }

            // 3.2 连接池配置选项
            Console.WriteLine("3.2 连接池配置选项");
            var dbFileName = $"myapp01_config_{Guid.NewGuid()}.db";

            try
            {
                var factory2 = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    options.ConnectionString = $"Data Source={dbFileName}";
                    options.PoolOptions = new DbConnectionPoolOptions
                    {
                        Enabled = true,
                        MinPoolSize = 2,
                        MaxPoolSize = 10,
                        ConnectionWaitTimeout = TimeSpan.FromSeconds(30),
                        MaxIdleTime = TimeSpan.FromMinutes(5),
                        CleanupInterval = TimeSpan.FromMinutes(10)
                    };
                });

                using (var db = factory2.Create())
                {
                    await db.ExecuteNonQueryAsync("CREATE TABLE PoolConfigTest (Id INTEGER)");
                    await db.ExecuteNonQueryAsync("INSERT INTO PoolConfigTest VALUES (1)");
                    var count = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM PoolConfigTest");
                    Console.WriteLine($"   连接池配置成功，记录数: {count}\n");
                }
            }
            finally
            {
                try { System.IO.File.Delete(dbFileName); } catch { }
            }

            // 3.3 追踪选项配置
            Console.WriteLine("3.3 追踪选项配置");
            var factory3 = SqliteAccessFactory.CreateSqliteFactory(options =>
            {
                options.ConnectionString = "Data Source=:memory:";
                options.ConnectionTrackingOptions = new DbConnectionTrackingOptions
                {
                    Enabled = true,
                    RecordStackTrace = true,
                };
                options.EnableStatistics = true;
            });

            using (var db = factory3.Create())
            {
                await db.ExecuteNonQueryAsync("SELECT 1");
                Console.WriteLine("   追踪选项配置成功\n");
            }
        }

        /// <summary>
        /// 4. 完整示例
        /// </summary>
        private static async Task DemoCompleteExample()
        {
            Console.WriteLine("【4. 完整示例】\n");

            var dbFileName = $"myapp01_{Guid.NewGuid()}.db";
            try
            {
                // 创建工厂
                var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    // 连接字符串
                    options.ConnectionString = $"Data Source={dbFileName}";

                    // 连接池配置（不启用）
                    options.PoolOptions = new DbConnectionPoolOptions
                    {
                        Enabled = false,
                        MinPoolSize = 5,
                        MaxPoolSize = 100,
                        ConnectionWaitTimeout = TimeSpan.FromSeconds(3),
                        MaxIdleTime = TimeSpan.FromMinutes(15),
                        CleanupInterval = TimeSpan.FromMinutes(5)
                    };

                    // 启用连接跟踪（开发环境建议启用）
                    options.ConnectionTrackingOptions = new DbConnectionTrackingOptions
                    {
                        Enabled = true,
                        RecordStackTrace = true
                    };

                    // 启用连接统计
                    options.EnableStatistics = true;
                });

                // 使用工厂创建 IDbAccess 实例
                using (var db = factory.Create())
                {
                    // 创建表
                    await db.ExecuteNonQueryAsync(@"
                    CREATE TABLE IF NOT EXISTS Users (
                        Id INTEGER PRIMARY KEY,
                        Name TEXT NOT NULL,
                        Email TEXT,
                        Age INTEGER
                    )");
                    Console.WriteLine("   创建 Users 表成功");

                    // 插入数据
                    await db.ExecuteNonQueryAsync(
                        "INSERT INTO Users (Id, Name, Email, Age) VALUES (@Id, @Name, @Email, @Age)",
                        new { Id = 1, Name = "张三", Email = "zhangsan@example.com", Age = 25 });
                    Console.WriteLine("   插入数据成功");

                    // 查询数据
                    var count = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM Users");
                    Console.WriteLine($"   查询成功，用户数量: {count}");

                    // 输出连接统计信息
                    var statistics = factory.ConnectionManager.GetStatistics();
                    Console.WriteLine($"   总请求数: {statistics.TotalRequests}");// 预期值：3
                    Console.WriteLine($"   创建连接数: {statistics.TotalCreated}");// 预期值：3
                    Console.WriteLine($"   释放连接数: {statistics.TotalReleased}");// 预期值：3
                }
            }
            finally
            {
                try { File.Delete(dbFileName); } catch { }
            }
        }
    }
}
