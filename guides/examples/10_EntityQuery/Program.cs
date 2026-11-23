using System.IO;
using Xbc.DbAccess;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.Sqlite;

namespace EntityQueryExample
{
    /// <summary>
    /// 10 IEntityQuery 使用示例
    /// 演示如何使用 IEntityQuery 进行实体查询（仅提供异步枚举方法）
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== IEntityQuery 使用示例 ===\n");

            var dbFileName = $"entityquery_{Guid.NewGuid()}.db";
            try
            {
                var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    options.ConnectionString = $"Data Source={dbFileName}";
                });

                using var db = factory.Create();

                // 创建表
                await db.ExecuteNonQueryAsync(@"
                    CREATE TABLE IF NOT EXISTS Users (
                        user_id INTEGER PRIMARY KEY AUTOINCREMENT,
                        user_name TEXT NOT NULL,
                        email TEXT,
                        age INTEGER DEFAULT 0,
                        status INTEGER DEFAULT 1
                    )");

                // 插入测试数据
                await InsertTestDataAsync(db);

                // 示例 1: FirstOrDefaultAsync
                Console.WriteLine("1. FirstOrDefaultAsync - 查询指定邮箱的用户");
                await DemonstrateFirstOrDefaultAsync(db);

                // 示例 2: FirstAsync
                Console.WriteLine("\n2. FirstAsync - 查询年龄大于 20 的第一个用户");
                await DemonstrateFirstAsync(db);

                // 示例 3: SingleAsync
                Console.WriteLine("\n3. SingleAsync - 查询指定 ID 的用户");
                await DemonstrateSingleAsync(db);

                // 示例 4: ToListAsync
                Console.WriteLine("\n4. ToListAsync - 查询所有活跃用户");
                await DemonstrateToListAsync(db);

                // 示例 5: 分页查询（Skip/Take）
                Console.WriteLine("\n5. 分页查询 - Skip/Take 窗口");
                await DemonstratePaginationAsync(db);

                // 示例 6: 带取消令牌的查询
                Console.WriteLine("\n6. 带取消令牌的查询");
                await DemonstrateCancellationTokenAsync(db);

                Console.WriteLine("\n示例完成!");
            }
            finally
            {
                // 清理临时数据库文件
                if (File.Exists(dbFileName))
                {
                    File.Delete(dbFileName);
                }
            }
        }

        private static async Task InsertTestDataAsync(IDbAccess db)
        {
            var users = new[]
            {
                new { Name = "张三", Email = "zhangsan@example.com", Age = 25, Status = 1 },
                new { Name = "李四", Email = "lisi@example.com", Age = 30, Status = 1 },
                new { Name = "王五", Email = "wangwu@example.com", Age = 35, Status = 0 },
                new { Name = "赵六", Email = "zhaoliu@example.com", Age = 28, Status = 1 },
                new { Name = "孙七", Email = "sunqi@example.com", Age = 22, Status = 1 },
                new { Name = "周八", Email = "zhouba@example.com", Age = 26, Status = 1 },
                new { Name = "吴九", Email = "wujiu@example.com", Age = 29, Status = 1 },
                new { Name = "郑十", Email = "zhengshi@example.com", Age = 31, Status = 1 }
            };

            foreach (var u in users)
            {
                await db.ExecuteNonQueryAsync(
                    "INSERT INTO Users (user_name, email, age, status) VALUES (@UserName, @Email, @Age, @Status)",
                    new { UserName = u.Name, Email = u.Email, Age = u.Age, Status = u.Status });
            }
        }

        private static async Task DemonstrateFirstOrDefaultAsync(IDbAccess db)
        {
            var user = await db.FromSql<User>(
                "SELECT * FROM Users WHERE email = @Email",
                new { Email = "zhangsan@example.com" }).FirstOrDefaultAsync();

            if (user != null)
            {
                Console.WriteLine($"   找到用户: {user.UserName}, 年龄: {user.Age}");
            }
            else
            {
                Console.WriteLine("   用户不存在");
            }

            // 查询不存在的用户
            var notFound = await db.FromSql<User>(
                "SELECT * FROM Users WHERE email = @Email",
                new { Email = "notexist@example.com" }).FirstOrDefaultAsync();

            Console.WriteLine($"   查询不存在的用户: {(notFound == null ? "null" : "找到")}");
        }

        private static async Task DemonstrateFirstAsync(IDbAccess db)
        {
            try
            {
                var user = await db.FromSql<User>(
                    "SELECT * FROM Users WHERE age > @Age",
                    new { Age = 20 }).FirstAsync();

                Console.WriteLine($"   第一个年龄大于 20 的用户: {user.UserName}, 年龄: {user.Age}");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"   异常: {ex.Message}");
            }
        }

        private static async Task DemonstrateSingleAsync(IDbAccess db)
        {
            try
            {
                var user = await db.FromSql<User>(
                    "SELECT * FROM Users WHERE user_id = @Id",
                    new { Id = 1 }).SingleAsync();

                Console.WriteLine($"   ID=1 的用户: {user.UserName}");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"   异常: {ex.Message}");
            }
        }

        private static async Task DemonstrateToListAsync(IDbAccess db)
        {
            var users = await db.FromSql<User>(
                "SELECT * FROM Users WHERE status = @Status",
                new { Status = 1 }).ToListAsync();

            Console.WriteLine($"   活跃用户数量: {users.Count}");
            foreach (var u in users.Take(3))
            {
                Console.WriteLine($"   - {u.UserName} ({u.Email})");
            }
            if (users.Count > 3)
            {
                Console.WriteLine($"   ... 还有 {users.Count - 3} 条记录");
            }
        }

        private static async Task DemonstratePaginationAsync(IDbAccess db)
        {
            int pageSize = 3;

            // 第一页（窗口要求先设置查询级排序）
            var page1 = await db.FromSql<User>(
                "SELECT * FROM Users WHERE status = @Status",
                new { Status = 1 })
                .OrderBy(u => u.UserId)
                .Skip(0)
                .Take(pageSize)
                .ToListAsync();

            Console.WriteLine("   第 1 页:");
            foreach (var u in page1)
            {
                Console.WriteLine($"   - {u.UserId}: {u.UserName}");
            }

            // 第二页
            var page2 = await db.FromSql<User>(
                "SELECT * FROM Users WHERE status = @Status",
                new { Status = 1 })
                .OrderBy(u => u.UserId)
                .Skip(pageSize)
                .Take(pageSize)
                .ToListAsync();

            Console.WriteLine("   第 2 页:");
            foreach (var u in page2)
            {
                Console.WriteLine($"   - {u.UserId}: {u.UserName}");
            }
        }

        private static async Task DemonstrateCancellationTokenAsync(IDbAccess db)
        {
            using var cts = new CancellationTokenSource();

            // 正常查询
            var user = await db.FromSql<User>(
                "SELECT * FROM Users WHERE user_id = @Id",
                new { Id = 2 }).FirstOrDefaultAsync(cts.Token);

            Console.WriteLine($"   查询 ID=2 的用户: {user?.UserName}");

            // 取消后查询将抛出 OperationCanceledException
            cts.Cancel();
            try
            {
                await db.FromSql<User>("SELECT * FROM Users").ToListAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("   查询已被取消");
            }
        }
    }

    // 用户实体
    [Xbc.DbAccess.Entity.Table("Users")]
    public class User
    {
        [Xbc.DbAccess.Entity.Column("user_id")]
        public int UserId { get; set; }

        [Xbc.DbAccess.Entity.Column("user_name")]
        public string UserName { get; set; } = string.Empty;

        [Xbc.DbAccess.Entity.Column("email")]
        public string Email { get; set; } = string.Empty;

        [Xbc.DbAccess.Entity.Column("age")]
        public int Age { get; set; }

        [Xbc.DbAccess.Entity.Column("status")]
        public int Status { get; set; }
    }
}
