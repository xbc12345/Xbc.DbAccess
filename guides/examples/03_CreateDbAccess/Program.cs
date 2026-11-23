using Xbc.DbAccess;
using Xbc.DbAccess.Diagnostics;
using Xbc.DbAccess.Sqlite;

namespace Examples
{
    /// <summary>
    /// 示例 03: 创建 IDbAccess 实例
    /// 演示如何使用工厂模式创建数据库访问对象
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== 示例 03: 创建 IDbAccess 实例 ===\n");

            // 1. 工厂模式介绍
            await DemoFactoryPattern();

            // 2. SQLite 连接示例
            await DemoSqliteConnection();

            // 3. 工厂配置选项
            await DemoFactoryOptions();

            // 4. 多数据库配置示例
            await DemoMultiDatabaseConfig();

            Console.WriteLine("示例运行完成！");
            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }

        /// <summary>
        /// 1. 工厂模式介绍
        /// </summary>
        private static async Task DemoFactoryPattern()
        {
            Console.WriteLine("【1. 工厂模式介绍】\n");

            // 1.1 最简配置 - 直接传入连接字符串
            Console.WriteLine("1.1 最简配置");
            var simpleFactory = SqliteAccessFactory.CreateSqliteFactory("Data Source=:memory:");
            using (var db = simpleFactory.Create())
            {
                var result = await db.ExecuteScalarAsync("SELECT 1");
                Console.WriteLine($"   连接成功: {result}\n");
            }

            // 1.2 使用配置选项
            Console.WriteLine("1.2 使用配置选项");
            var optionsFactory = SqliteAccessFactory.CreateSqliteFactory(options =>
            {
                options.ConnectionString = "Data Source=:memory:";
            });
            using (var db = optionsFactory.Create())
            {
                var result = await db.ExecuteScalarAsync("SELECT 2");
                Console.WriteLine($"   连接成功: {result}\n");
            }
        }

        /// <summary>
        /// 2. SQLite 连接示例
        /// </summary>
        private static async Task DemoSqliteConnection()
        {
            Console.WriteLine("【2. SQLite 连接示例】\n");

            // 内存数据库
            var memoryFactory = SqliteAccessFactory.CreateSqliteFactory("Data Source=:memory:");
            using (var db = memoryFactory.Create())
            {
                var result = await db.ExecuteScalarAsync("SELECT 'SQLite Memory'");
                Console.WriteLine($"   内存数据库: {result}");
            }

            // 文件数据库
            var dbFileName = $"myapp03_config_{Guid.NewGuid()}.db";

            try
            {
                var fileFactory = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    options.ConnectionString = $"Data Source={dbFileName}";
                    options.PoolOptions = new DbConnectionPoolOptions { Enabled = true };
                });

                using (var db = fileFactory.Create())
                {
                    await db.ExecuteNonQueryAsync("CREATE TABLE IF NOT EXISTS Test (Id INTEGER)");
                    var result = await db.ExecuteScalarAsync("SELECT 'SQLite File'");
                    Console.WriteLine($"   文件数据库: {result}\n");
                }
            }
            finally
            {
                try { File.Delete(dbFileName); } catch { }
            }
        }

        /// <summary>
        /// 3. 工厂配置选项
        /// </summary>
        private static async Task DemoFactoryOptions()
        {
            Console.WriteLine("【3. 工厂配置选项】\n");

            var dbFileName = $"myapp03_config_{Guid.NewGuid()}.db";
            try
            {
                var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    // 连接字符串
                    options.ConnectionString = $"Data Source={dbFileName}";

                    // 连接池配置
                    options.PoolOptions = new DbConnectionPoolOptions
                    {
                        Enabled = true,
                        MinPoolSize = 5,
                        MaxPoolSize = 20
                    };

                    // 追踪配置
                    options.ConnectionTrackingOptions = new DbConnectionTrackingOptions
                    {
                        Enabled = true,
                        RecordStackTrace = true
                    };

                    // 启用统计
                    options.EnableStatistics = true;
                });

                Console.WriteLine($"   ProviderFactory: {factory.ProviderFactory.GetType().Name}");
                Console.WriteLine($"   ConnectionManager: {factory.ConnectionManager.GetType().Name}");
                Console.WriteLine($"   ServerInfo: {(await factory.GetServerInfoAsync())?.ServerVersion ?? "N/A"}\n");

                using (var db = factory.Create())
                {
                    await db.ExecuteNonQueryAsync("CREATE TABLE OptionsTest (Id INTEGER)");
                    await db.ExecuteNonQueryAsync("INSERT INTO OptionsTest VALUES (1)");
                    var count = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM OptionsTest");
                    Console.WriteLine($"   数据库操作成功，记录数: {count}\n");
                }
            }
            finally
            {
                try { File.Delete(dbFileName); } catch { }
            }
        }

        /// <summary>
        /// 4. 多数据库配置示例
        /// </summary>
        private static async Task DemoMultiDatabaseConfig()
        {
            Console.WriteLine("【4. 多数据库配置示例】\n");

            // 模拟主数据库
            var primaryDbFileName = $"myapp03_primary_{Guid.NewGuid()}.db";
            // 模拟日志数据库
            var logDbFileName = $"myapp03_log_{Guid.NewGuid()}.db";

            try
            {
                // 创建主数据库工厂
                var primaryFactory = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    options.ConnectionString = $"Data Source={primaryDbFileName}";
                    options.PoolOptions = new DbConnectionPoolOptions { Enabled = true };
                });

                // 创建日志数据库工厂
                var logFactory = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    options.ConnectionString = $"Data Source={logDbFileName}";
                    options.PoolOptions = new DbConnectionPoolOptions { Enabled = true };
                });

                // 使用主数据库
                using (var primaryDb = primaryFactory.Create())
                {
                    await primaryDb.ExecuteNonQueryAsync("CREATE TABLE Users (Id INTEGER, Name TEXT)");
                    await primaryDb.ExecuteNonQueryAsync(
                        "INSERT INTO Users VALUES (@Id, @Name)",
                        new { Id = 1, Name = "张三" });
                    var userCount = await primaryDb.ExecuteScalarAsync("SELECT COUNT(*) FROM Users");
                    Console.WriteLine($"   主数据库 - 用户数量: {userCount}");
                }

                // 使用日志数据库
                using (var logDb = logFactory.Create())
                {
                    await logDb.ExecuteNonQueryAsync("CREATE TABLE Logs (Id INTEGER, Message TEXT)");
                    await logDb.ExecuteNonQueryAsync(
                        "INSERT INTO Logs VALUES (@Id, @Message)",
                        new { Id = 1, Message = "用户创建成功" });
                    var logCount = await logDb.ExecuteScalarAsync("SELECT COUNT(*) FROM Logs");
                    Console.WriteLine($"   日志数据库 - 日志数量: {logCount}\n");
                }
            }
            finally
            {
                try { File.Delete(primaryDbFileName); } catch { }
                try { File.Delete(logDbFileName); } catch { }
            }
        }
    }
}
