using System.IO;
using Xbc.DbAccess;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.Sqlite;

namespace EntityMapperExample
{
    /// <summary>
    /// 09 EntityMapper 使用示例
    /// EntityMapper 为框架内部的映射核心（internal），应用代码通过
    /// Query/QueryScalar/QueryMultiple 等公开扩展方法使用其映射能力。
    /// 本示例演示这些公开入口的实体映射用法。
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== EntityMapper 使用示例 ===\n");

            var dbFileName = $"entitymapper_{Guid.NewGuid()}.db";
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
                        age INTEGER DEFAULT 0
                    )");

                // 插入测试数据
                await db.ExecuteNonQueryAsync(
                    "INSERT INTO Users (user_name, email, age) VALUES (@UserName, @Email, @Age)",
                    new { UserName = "张三", Email = "zhangsan@example.com", Age = 25 });

                await db.ExecuteNonQueryAsync(
                    "INSERT INTO Users (user_name, email, age) VALUES (@UserName, @Email, @Age)",
                    new { UserName = "李四", Email = "lisi@example.com", Age = 30 });

                // 示例 1: 映射单条数据（FromSql + FirstOrDefaultAsync，内部由 EntityMapper 完成映射）
                Console.WriteLine("1. 映射单条数据:");
                var user = await db.FromSql<User>(
                    "SELECT * FROM Users WHERE user_name = @UserName",
                    new { UserName = "张三" }).FirstOrDefaultAsync();
                Console.WriteLine($"   用户: {user?.UserName}, 邮箱: {user?.Email}");

                // 示例 2: 映射多条数据（FromSql + ToListAsync）
                Console.WriteLine("\n2. 映射多条数据:");
                var users = await db.FromSql<User>("SELECT * FROM Users").ToListAsync();
                foreach (var u in users)
                {
                    Console.WriteLine($"   用户: {u.UserName}, 年龄: {u.Age}");
                }

                // 示例 3: 标量值转换（Select 单列投影，利用实体的类型转换配置）
                Console.WriteLine("\n3. 标量值转换:");
                var names = await db.FromSql<User>("SELECT * FROM Users")
                    .Select(u => u.UserName)
                    .ToListAsync();
                Console.WriteLine($"   所有用户名: {string.Join(", ", names)}");

                var ages = await db.FromSql<User>(
                    "SELECT * FROM Users WHERE user_name = @UserName",
                    new { UserName = "李四" })
                    .Select(u => u.Age)
                    .ToListAsync();
                Console.WriteLine($"   李四的年龄: {string.Join(", ", ages)}");

                // 示例 4: 多结果集（ExecuteMultipleAsync + EntityMultiReader）
                Console.WriteLine("\n4. 多结果集:");
                using (var multiResult = await db.ExecuteMultipleAsync(
                    "SELECT * FROM Users WHERE age > @Age; SELECT * FROM Users",
                    new { Age = 26 }))
                {
                    var olderUsers = await multiResult.ReadAsync<User>();
                    var allUsers = await multiResult.ReadAsync<User>();
                    Console.WriteLine($"   年龄大于 26 的用户: {olderUsers.Count} 个");
                    Console.WriteLine($"   全部用户: {allUsers.Count} 个");
                }

                // 示例 5: 非泛型读取（EntityMultiReader.ReadAsync(Type)，返回 List<object>）
                Console.WriteLine("\n5. 非泛型版本:");
                using (var multiResult = await db.ExecuteMultipleAsync("SELECT * FROM Users"))
                {
                    List<object> userObjs = await multiResult.ReadAsync(typeof(User));
                    foreach (var userObj in userObjs.Cast<User>())
                    {
                        Console.WriteLine($"   非泛型读取: {userObj.UserName}");
                    }
                }

                // 示例 6: 手动 Reader 读取（了解 EntityMapper 自动完成的动作）
                Console.WriteLine("\n6. 手动 Reader 读取:");
                await using (var reader = await db.ExecuteReaderAsync("SELECT user_name, email FROM Users"))
                {
                    while (await reader.ReadAsync())
                    {
                        Console.WriteLine($"   手动读取: {reader.GetString(0)}, {reader.GetString(1)}");
                    }
                }

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
    }
}
