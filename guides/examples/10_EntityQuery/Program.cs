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

                // 示例 6: 表达式谓词查询（Set<T>().Where）
                Console.WriteLine("\n6. 表达式谓词查询 - Set<T>().Where");
                await DemonstrateExpressionQueryAsync(db);

                // 示例 7: keyset 分页
                Console.WriteLine("\n7. keyset 分页 - KeysetAfterAsync");
                await DemonstrateKeysetPagingAsync(db);

                // 示例 8: 投影（Select）
                Console.WriteLine("\n8. 投影 - Select");
                await DemonstrateProjectionAsync(db);

                // 示例 9: 聚合方法
                Console.WriteLine("\n9. 聚合方法 - Count/Sum/Average/Max/Min");
                await DemonstrateAggregatesAsync(db);

                // 示例 10: 分组聚合（GroupBy / Having）
                Console.WriteLine("\n10. 分组聚合 - GroupBy / Having");
                await DemonstrateGroupByAsync(db);

                // 示例 11: 带取消令牌的查询
                Console.WriteLine("\n11. 带取消令牌的查询");
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

        private static async Task DemonstrateExpressionQueryAsync(IDbAccess db)
        {
            // 谓词表达式自动翻译为参数化 WHERE 子句
            var adults = await db.Set<User>().Where(u => u.Age >= 26).ToListAsync();
            Console.WriteLine($"   年龄 >= 26 的用户: {adults.Count} 位");
            foreach (var u in adults.Take(3))
            {
                Console.WriteLine($"   - {u.UserName} ({u.Age} 岁)");
            }

            // 组合谓词 + 字符串匹配
            var matches = await db.Set<User>()
                .Where(u => u.Status == 1 && u.Email.Contains("example.com"))
                .OrderBy(u => u.UserId)
                .ToListAsync();
            Console.WriteLine($"   活跃且邮箱含 example.com 的用户: {matches.Count} 位");
        }

        private static async Task DemonstrateKeysetPagingAsync(IDbAccess db)
        {
            // 键集排序 + 游标翻页，深分页性能稳定
            var query = db.Set<User>().AsQuery().OrderBy(u => u.UserId);

            var firstPage = await query.KeysetAfterAsync(null, 3);
            Console.WriteLine($"   第 1 页（{firstPage.Count} 条）: {string.Join(", ", firstPage.Select(u => u.UserId))}");

            if (firstPage.Count > 0)
            {
                var secondPage = await query.KeysetAfterAsync(firstPage[^1], 3);
                Console.WriteLine($"   第 2 页（{secondPage.Count} 条）: {string.Join(", ", secondPage.Select(u => u.UserId))}");
            }
        }

        private static async Task DemonstrateProjectionAsync(IDbAccess db)
        {
            // 单列投影
            var names = await db.Set<User>().AsQuery().Select(u => u.UserName).ToListAsync();
            Console.WriteLine($"   单列投影（前 3 个）: {string.Join(", ", names.Take(3))}");

            // 多列投影 - 匿名类型（Where 在 Set<T> 上，AsQuery 用于无条件链）
            var briefs = await db.Set<User>()
                .Where(u => u.Age >= 28)
                .Select(u => new { u.UserName, u.Age })
                .ToListAsync();
            Console.WriteLine($"   匿名类型投影（Age >= 28，共 {briefs.Count} 条）:");
            foreach (var b in briefs.Take(3))
            {
                Console.WriteLine($"   - {b.UserName} ({b.Age} 岁)");
            }
        }

        private static async Task DemonstrateAggregatesAsync(IDbAccess db)
        {
            long total = await db.Set<User>().AsQuery().CountAsync();
            Console.WriteLine($"   用户总数: {total}");

            bool hasAdults = await db.Set<User>().Where(u => u.Age >= 18).AnyAsync();
            Console.WriteLine($"   存在成年人: {hasAdults}");

            int? maxAge = await db.Set<User>().AsQuery().MaxAsync(u => u.Age);
            int? minAge = await db.Set<User>().AsQuery().MinAsync(u => u.Age);
            decimal? avgAge = await db.Set<User>().AsQuery().AverageAsync(u => u.Age);
            Console.WriteLine($"   最大年龄: {maxAge}, 最小年龄: {minAge}, 平均年龄: {avgAge:F1}");
        }

        private static async Task DemonstrateGroupByAsync(IDbAccess db)
        {
            // 按状态分组统计
            var byStatus = await db.Set<User>().AsQuery()
                .GroupBy(u => u.Status)
                .Select(g => new { Status = g.Key, Cnt = g.Count() })
                .ToListAsync();
            Console.WriteLine("   按状态分组统计:");
            foreach (var g in byStatus)
            {
                Console.WriteLine($"   - 状态 {g.Status}: {g.Cnt} 位");
            }

            // 按状态分组 + HAVING + 聚合排序
            var statusAggs = await db.Set<User>().AsQuery()
                .GroupBy(u => u.Status)
                .Having(g => g.Count() >= 2)
                .OrderByDescending(g => g.Count())
                .Select(g => new { g.Key, Cnt = g.Count(), TotalAge = g.Sum(x => x.Age) })
                .ToListAsync();
            Console.WriteLine("   组数 >= 2 的状态分组（按组内人数倒序）:");
            foreach (var g in statusAggs)
            {
                Console.WriteLine($"   - 状态 {g.Key}: {g.Cnt} 位, 年龄合计 {g.TotalAge}");
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
        [Xbc.DbAccess.Entity.PrimaryKey("user_id", IsIdentity = true)]
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
