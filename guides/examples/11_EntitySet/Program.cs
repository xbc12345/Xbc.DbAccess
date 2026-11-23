using System.IO;
using Xbc.DbAccess;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.Sqlite;
using Xbc.DbAccess.SqlServer;

namespace EntitySetExample
{
    /// <summary>
    /// 11 IEntitySet 使用示例
    /// 演示如何使用 IEntitySet 进行实体增删改查操作（仅提供异步方法）
    /// 同时支持 SQLite 和 SQL Server，验证自增 ID 获取
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== IEntitySet 使用示例 ===\n");

            // SQLite 示例
            await RunSqliteExample();

            // SQL Server 示例
            await RunSqlServerExample();

            Console.WriteLine("\n所有示例完成!");
        }

        #region SQLite 示例

        static async Task RunSqliteExample()
        {
            Console.WriteLine("========================================");
            Console.WriteLine("SQLite 示例");
            Console.WriteLine("========================================\n");

            var dbFileName = $"entityset_sqlite_{Guid.NewGuid()}.db";
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

                var userSet = db.Set<User>();
                await RunAllDemosAsync(db, userSet, "SQLite");
            }
            finally
            {
                if (File.Exists(dbFileName))
                {
                    File.Delete(dbFileName);
                }
            }
        }

        #endregion

        #region SQL Server 示例

        static async Task RunSqlServerExample()
        {
            Console.WriteLine("\n========================================");
            Console.WriteLine("SQL Server 示例");
            Console.WriteLine("========================================\n");

            IDbAccess db = null;
            try
            {
                var factory = SqlAccessFactory.CreateSqlServerFactory(options =>
                {
                    options.ConnectionString = "Server=.;Database=DbAccessTest;Integrated Security=True;TrustServerCertificate=True;";
                });

                db = factory.Create();

                // 创建表（先删除已存在的表）
                await db.ExecuteNonQueryAsync(@"IF EXISTS (SELECT * FROM sysobjects WHERE name='Users' AND xtype='U') DROP TABLE Users");
                await db.ExecuteNonQueryAsync(@"
                    CREATE TABLE Users (
                        user_id INT IDENTITY(1,1) PRIMARY KEY,
                        user_name NVARCHAR(100) NOT NULL,
                        email NVARCHAR(200),
                        age INT DEFAULT 0,
                        status INT DEFAULT 1
                    )");

                var userSet = db.Set<User>();
                await RunAllDemosAsync(db, userSet, "SQL Server");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SQL Server 连接失败（请确保本地 SQL Server 可用）: {ex.Message}");
            }
            finally
            {
                db?.Dispose();
            }
        }

        #endregion

        #region 通用演示方法

        static async Task RunAllDemosAsync(IDbAccess db, IEntitySet<User> userSet, string dbType)
        {
            // 示例 1: 插入操作
            Console.WriteLine($"[{dbType}] 1. InsertAsync - 插入单条记录");
            await DemonstrateInsertAsync(userSet, dbType);

            // 示例 2: 批量插入
            Console.WriteLine($"[{dbType}] 2. InsertRangeAsync - 批量插入");
            await DemonstrateInsertRangeAsync(userSet, dbType);

            // 示例 3: 查询操作
            Console.WriteLine($"[{dbType}] 3. FindAsync - 根据主键查找");
            await DemonstrateFindAsync(db, userSet, dbType);

            // 示例 4: 更新操作
            Console.WriteLine($"[{dbType}] 4. UpdateAsync - 更新记录");
            await DemonstrateUpdateAsync(userSet, dbType);

            // 示例 5: 批量更新
            Console.WriteLine($"[{dbType}] 5. UpdateRangeAsync - 批量更新");
            await DemonstrateUpdateRangeAsync(db, userSet, dbType);

            // 示例 6: 删除操作
            Console.WriteLine($"[{dbType}] 6. DeleteAsync - 删除记录");
            await DemonstrateDeleteAsync(userSet, dbType);

            // 示例 7: 批量删除
            Console.WriteLine($"[{dbType}] 7. DeleteRangeAsync - 批量删除");
            await DemonstrateDeleteRangeAsync(userSet, dbType);

            // 示例 8: Upsert（存在则更新，不存在则插入）
            Console.WriteLine($"[{dbType}] 8. UpsertAsync - 存在则更新，不存在则插入");
            await DemonstrateUpsertAsync(userSet, dbType);

            // 示例 9: 条件更新
            Console.WriteLine($"[{dbType}] 9. UpdateAsync - 条件更新");
            await DemonstrateConditionalUpdateAsync(userSet, dbType);

            // 示例 10: 条件删除
            Console.WriteLine($"[{dbType}] 10. DeleteAsync - 条件删除");
            await DemonstrateConditionalDeleteAsync(userSet, dbType);

            // 示例 11: 高性能批量写入
            Console.WriteLine($"[{dbType}] 11. BulkInsertAsync - 高性能批量写入");
            await DemonstrateBulkInsertAsync(userSet, dbType);
        }

        private static async Task DemonstrateInsertAsync(IEntitySet<User> userSet, string dbType)
        {
            var user = new User
            {
                UserName = "张三",
                Email = "zhangsan@example.com",
                Age = 25,
                Status = 1
            };

            int rows = await userSet.InsertAsync(user);
            Console.WriteLine($"   插入 {rows} 条记录，新用户 ID: {user.UserId}（自增 ID 已自动填充）");
        }

        private static async Task DemonstrateInsertRangeAsync(IEntitySet<User> userSet, string dbType)
        {
            var users = new List<User>
            {
                new User { UserName = "李四", Email = "lisi@example.com", Age = 30, Status = 1 },
                new User { UserName = "王五", Email = "wangwu@example.com", Age = 35, Status = 1 },
                new User { UserName = "赵六", Email = "zhaoliu@example.com", Age = 28, Status = 0 }
            };

            int rows = await userSet.InsertRangeAsync(users);
            Console.WriteLine($"   批量插入 {rows} 条记录");
            foreach (var u in users)
            {
                Console.WriteLine($"   - 用户 ID: {u.UserId}, 姓名: {u.UserName}（自增 ID 已自动填充）");
            }
        }

        private static async Task DemonstrateFindAsync(IDbAccess db, IEntitySet<User> userSet, string dbType)
        {
            // 根据主键查找
            var user = await userSet.FindAsync(1);
            if (user != null)
            {
                Console.WriteLine($"   找到用户: {user.UserName}, 邮箱: {user.Email}, 年龄: {user.Age}");
            }

            // 查找不存在的用户
            var notFound = await userSet.FindAsync(999);
            Console.WriteLine($"   查找不存在的用户: {(notFound == null ? "null" : "找到")}");

            // 使用 Query 进行复杂查询
            var activeUsers = await db.FromSql<User>(
                "SELECT * FROM Users WHERE status = @Status",
                new { Status = 1 }).ToListAsync();
            Console.WriteLine($"   活跃用户数量: {activeUsers.Count}");
        }

        private static async Task DemonstrateUpdateAsync(IEntitySet<User> userSet, string dbType)
        {
            var user = await userSet.FindAsync(1);
            if (user != null)
            {
                user.Email = "zhangsan_new@example.com";
                user.Age = 26;

                int rows = await userSet.UpdateAsync(user);
                Console.WriteLine($"   更新 {rows} 条记录");

                // 验证更新
                var updated = await userSet.FindAsync(1);
                Console.WriteLine($"   更新后邮箱: {updated.Email}, 年龄: {updated.Age}");
            }
        }

        private static async Task DemonstrateUpdateRangeAsync(IDbAccess db, IEntitySet<User> userSet, string dbType)
        {
            var users = await db.FromSql<User>(
                "SELECT * FROM Users WHERE status = @Status",
                new { Status = 0 }).ToListAsync();

            if (users.Count > 0)
            {
                foreach (var u in users)
                {
                    u.Status = 1;
                }

                int rows = await userSet.UpdateRangeAsync(users);
                Console.WriteLine($"   批量更新 {rows} 条记录（状态 0 -> 1）");
            }
            else
            {
                Console.WriteLine("   没有需要更新的记录");
            }
        }

        private static async Task DemonstrateDeleteAsync(IEntitySet<User> userSet, string dbType)
        {
            // 先插入一条用于删除的记录
            var tempUser = new User { UserName = "临时用户", Email = "temp@example.com", Age = 20, Status = 0 };
            await userSet.InsertAsync(tempUser);
            Console.WriteLine($"   插入临时用户，ID: {tempUser.UserId}");

            // 方式一：先查询再删除
            var user = await userSet.FindAsync(tempUser.UserId);
            if (user != null)
            {
                int rows = await userSet.DeleteAsync(user);
                Console.WriteLine($"   删除 {rows} 条记录");
            }

            // 方式二：直接创建实体对象删除（不需要先查询）
            var tempUser2 = new User { UserName = "临时用户2", Email = "temp2@example.com", Age = 21, Status = 0 };
            await userSet.InsertAsync(tempUser2);
            int deleteRows = await userSet.DeleteAsync(new User { UserId = tempUser2.UserId });
            Console.WriteLine($"   直接删除 {deleteRows} 条记录（无需查询）");
        }

        private static async Task DemonstrateDeleteRangeAsync(IEntitySet<User> userSet, string dbType)
        {
            // 先插入几条用于批量删除的记录
            var tempUsers = new List<User>
            {
                new User { UserName = "删除1", Email = "del1@example.com", Age = 20, Status = 9 },
                new User { UserName = "删除2", Email = "del2@example.com", Age = 21, Status = 9 }
            };
            await userSet.InsertRangeAsync(tempUsers);
            Console.WriteLine($"   插入 {tempUsers.Count} 条待删除记录");

            // 批量删除
            int rows = await userSet.DeleteRangeAsync(tempUsers);
            Console.WriteLine($"   批量删除 {rows} 条记录");
        }

        private static async Task DemonstrateUpsertAsync(IEntitySet<User> userSet, string dbType)
        {
            // 已存在的主键 → 更新路径
            var existing = new User { UserId = 1, UserName = "张三Upsert", Email = "upsert@example.com", Age = 27, Status = 1 };
            int rows1 = await userSet.UpsertAsync(existing);
            Console.WriteLine($"   主键 1 已存在 → 更新路径，受影响 {rows1} 行");

            // 验证更新生效
            var verified = await userSet.FindAsync(1);
            Console.WriteLine($"   验证: {verified.UserName}, {verified.Email}");

            // 不存在的主键 → 插入路径（自增 ID 回填）
            var fresh = new User { UserName = "赵六Upsert", Email = "upsert_new@example.com", Age = 33, Status = 1 };
            int rows2 = await userSet.UpsertAsync(fresh);
            Console.WriteLine($"   主键不存在 → 插入路径，受影响 {rows2} 行，新 ID: {fresh.UserId}");
        }

        private static async Task DemonstrateConditionalUpdateAsync(IEntitySet<User> userSet, string dbType)
        {
            // 先插入两条状态为 0 的用户作为待更新数据
            await userSet.InsertRangeAsync(
            [
                new User { UserName = "待激活1", Email = "active1@example.com", Age = 22, Status = 0 },
                new User { UserName = "待激活2", Email = "active2@example.com", Age = 23, Status = 0 }
            ]);

            // 将所有状态为 0 的用户激活（按条件批量更新指定列，不用逐条加载）
            int rows = await userSet.UpdateAsync(
                u => new User { Status = 1 },
                u => u.Status == 0);
            Console.WriteLine($"   将状态为 0 的用户激活: 受影响 {rows} 行");

            // 匿名类型形态
            int rows2 = await userSet.UpdateAsync(
                u => new { Age = 40 },
                u => u.UserName == "王五");
            Console.WriteLine($"   匿名类型赋值: 受影响 {rows2} 行");
        }

        private static async Task DemonstrateConditionalDeleteAsync(IEntitySet<User> userSet, string dbType)
        {
            int rows = await userSet.DeleteAsync(u => u.Age == 40);
            Console.WriteLine($"   删除年龄为 40 的用户: 受影响 {rows} 行");
        }

        private static async Task DemonstrateBulkInsertAsync(IEntitySet<User> userSet, string dbType)
        {
            var bulkUsers = Enumerable.Range(1, 2000)
                .Select(i => new User
                {
                    UserName = $"bulk{i}",
                    Email = $"bulk{i}@example.com",
                    Age = 20 + i % 30,
                    Status = 1
                });

            long inserted = await userSet.BulkInsertAsync(bulkUsers);
            Console.WriteLine($"   Bulk 插入 {inserted} 条记录（大批量数据，自增列被忽略且不回填 ID）");

            long total = await userSet.AsQuery().CountAsync();
            Console.WriteLine($"   当前用户总数: {total}");
        }

        #endregion
    }

    // 用户实体
    [Table("Users")]
    public class User
    {
        [PrimaryKey("user_id", IsIdentity = true)]
        public int UserId { get; set; }

        [Column("user_name")]
        public string UserName { get; set; } = string.Empty;

        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Column("age")]
        public int Age { get; set; }

        [Column("status")]
        public int Status { get; set; }
    }
}
