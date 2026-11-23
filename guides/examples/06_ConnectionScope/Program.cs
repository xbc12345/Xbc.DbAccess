using Xbc.DbAccess;
using Xbc.DbAccess.Sqlite;

namespace Examples
{
    /// <summary>
    /// 示例 06: 使用连接作用域
    /// 演示连接作用域的基本用法、嵌套作用域以及与事务的配合
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== 示例 06: 使用连接作用域 ===\n");

            var dbFileName = $"myapp06_{Guid.NewGuid()}.db";
            try
            {
                // 创建数据库访问工厂
                var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    options.ConnectionString = $"Data Source={dbFileName}";
                });

                using var db = factory.Create();

                // 创建测试表
                await CreateTestTable(db);

                // 1. 基本连接作用域
                await DemoBasicConnectionScope(db);

                // 2. 嵌套连接作用域
                await DemoNestedConnectionScope(db);

                // 3. 连接作用域与事务
                await DemoConnectionScopeWithTransaction(db);

                Console.WriteLine("示例运行完成！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n运行出错: {ex}");
            }
            finally
            {
                try { File.Delete(dbFileName); } catch { }
            }

            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }

        /// <summary>
        /// 创建测试表
        /// </summary>
        private static async Task CreateTestTable(IDbAccess db)
        {
            await db.ExecuteNonQueryAsync(@"
                CREATE TABLE TestData (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT,
                    Value INTEGER
                )");

            Console.WriteLine("测试表创建完成\n");
        }

        /// <summary>
        /// 1. 基本连接作用域
        /// </summary>
        private static async Task DemoBasicConnectionScope(IDbAccess db)
        {
            Console.WriteLine("【1. 基本连接作用域】\n");

            using (var scope = await db.BeginConnectionScopeAsync())
            {
                Console.WriteLine($"   作用域 ID: {scope.ScopeId}");

                await db.ExecuteNonQueryAsync("INSERT INTO TestData VALUES (1, 'A', 100)");
                await db.ExecuteNonQueryAsync("INSERT INTO TestData VALUES (2, 'B', 200)");

                var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TestData");
                Console.WriteLine($"   插入后记录数: {count}\n");
            }
        }

        /// <summary>
        /// 2. 嵌套连接作用域
        /// </summary>
        private static async Task DemoNestedConnectionScope(IDbAccess db)
        {
            Console.WriteLine("【2. 嵌套连接作用域】\n");

            using (var outerScope = await db.BeginConnectionScopeAsync())
            {
                Console.WriteLine($"   外部作用域 ID: {outerScope.ScopeId}");

                await db.ExecuteNonQueryAsync("DELETE FROM TestData");
                await db.ExecuteNonQueryAsync("INSERT INTO TestData VALUES (10, 'X', 1000)");

                // 内部作用域 - 复用外部连接
                using (var innerScope = await db.BeginConnectionScopeAsync())
                {
                    Console.WriteLine($"   内部作用域 ID: {innerScope.ScopeId}");

                    await db.ExecuteNonQueryAsync("INSERT INTO TestData VALUES (20, 'Y', 2000)");
                    var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TestData");
                    Console.WriteLine($"   内部作用域记录数: {count}");
                }

                await db.ExecuteNonQueryAsync("INSERT INTO TestData VALUES (30, 'Z', 3000)");
                var finalCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TestData");
                Console.WriteLine($"   外部作用域最终记录数: {finalCount}\n");
            }
        }

        /// <summary>
        /// 3. 连接作用域与事务
        /// </summary>
        private static async Task DemoConnectionScopeWithTransaction(IDbAccess db)
        {
            Console.WriteLine("【3. 连接作用域与事务】\n");

            // 3.1 先开启连接作用域，再开启事务
            Console.WriteLine("3.1 先开启连接作用域，再开启事务");
            using (var scope = await db.BeginConnectionScopeAsync())
            {
                // 非事务操作
                await db.ExecuteNonQueryAsync("INSERT INTO TestData VALUES (100, 'Before', 1)");

                await using (var transaction = await db.BeginTransactionAsync())
                {
                    await db.ExecuteNonQueryAsync("INSERT INTO TestData VALUES (101, 'InTransaction', 2)");
                    await transaction.CommitAsync();
                }

                // 事务结束后继续非事务操作
                await db.ExecuteNonQueryAsync("INSERT INTO TestData VALUES (102, 'After', 3)");

                var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TestData WHERE Id >= 100");
                Console.WriteLine($"   记录数: {count}\n");
            }

            // 3.2 事务中调用内部有连接作用域的方法
            Console.WriteLine("3.2 事务中调用内部有连接作用域的方法");
            await using (var transaction = await db.BeginTransactionAsync())
            {
                await db.ExecuteNonQueryAsync("INSERT INTO TestData VALUES (200, 'Main', 10)");

                // 调用内部有连接作用域的方法
                await InsertWithScope(db, 201, "Method1", 20);
                await InsertWithScope(db, 202, "Method2", 30);

                await transaction.CommitAsync();

                var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TestData WHERE Id >= 200");
                Console.WriteLine($"   事务中插入记录数: {count}\n");
            }
        }

        /// <summary>
        /// 内部开启连接作用域的方法
        /// </summary>
        private static async Task InsertWithScope(IDbAccess db, int id, string name, int value)
        {
            using (var scope = await db.BeginConnectionScopeAsync())
            {
                await db.ExecuteNonQueryAsync(
                    "INSERT INTO TestData VALUES (@Id, @Name, @Value)",
                    new { Id = id, Name = name, Value = value });
            }
        }
    }
}
