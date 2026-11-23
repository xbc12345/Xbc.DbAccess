using System.Data;
using System.Data.Common;
using Xbc.DbAccess;
using Xbc.DbAccess.Common;
using Xbc.DbAccess.Sqlite;

namespace Examples
{
    /// <summary>
    /// 示例 04: 执行 SQL
    /// 演示创建参数、DbDataReader、DataSet、非查询 SQL、标量查询和分页查询
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== 示例 04: 执行 SQL ===\n");

            var dbFileName = $"myapp04_{Guid.NewGuid()}.db";
            try
            {
                var factory = SqliteAccessFactory.CreateSqliteFactory($"Data Source={dbFileName}");
                using var db = factory.Create();

                // 创建测试表
                await CreateTestTable(db);

                // 1. 创建参数
                await DemoCreateParameters(db);

                // 2. 获取 DbDataReader
                await DemoExecuteReader(db);

                // 3. 获取 DataSet
                await DemoExecuteDataSet(db);

                // 4. 执行非查询 SQL
                await DemoExecuteNonQuery(db);

                // 5. 执行标量查询
                await DemoExecuteScalar(db);

                // 6. 执行分页查询
                await DemoExecutePagination(db);

                // 7. 异步操作示例
                await DemoExecuteAsync(db);

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
                CREATE TABLE Users (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Email TEXT,
                    Age INTEGER
                )");

            // 插入测试数据 - 使用匿名对象
            for (int i = 1; i <= 20; i++)
            {
                await db.ExecuteNonQueryAsync(
                    "INSERT INTO Users (Id, Name, Email, Age) VALUES (@Id, @Name, @Email, @Age)",
                    new { Id = i, Name = $"用户{i}", Email = $"user{i}@example.com", Age = 20 + i });
            }

            Console.WriteLine("测试表创建完成，插入 20 条测试数据\n");
        }

        /// <summary>
        /// 1. 创建参数
        /// </summary>
        private static async Task DemoCreateParameters(IDbAccess db)
        {
            Console.WriteLine("【1. 创建参数】\n");

            // 1.1 使用匿名对象（推荐）
            Console.WriteLine("1.1 使用匿名对象（推荐）");
            var user = await db.ExecuteScalarAsync<string>("SELECT Name FROM Users WHERE Name = @Name AND Age = @Age",
                new { Name = "用户1", Age = 21 });
            Console.WriteLine($"   查询结果: {user}\n");

            // 1.2 使用字典
            Console.WriteLine("1.2 使用字典");
            var dict = new Dictionary<string, object>
            {
                ["Name"] = "用户2",
                ["Age"] = 22
            };
            var user2 = await db.ExecuteScalarAsync<string>("SELECT Name FROM Users WHERE Name = @Name AND Age = @Age", dict);
            Console.WriteLine($"   查询结果: {user2}\n");

            // 1.3 使用 db.NewParameters 构建器
            Console.WriteLine("1.3 使用 db.NewParameters 构建器");
            var parameters = db.NewParameters()
                .Add("Name", "李四")
                .Add("Age", 30)
                .Add("Email", "lisi@example.com", DbType.String);
            Console.WriteLine($"   创建 {parameters.Count} 个参数（可直接传入方法，无需调用 ToArray()）\n");

            // 1.4 使用 db.CreateParameter 方法
            Console.WriteLine("1.4 使用 db.CreateParameter 方法");
            var param1 = db.CreateParameter("Name", "张三");
            var param2 = db.CreateParameter("Age", 25);
            var param3 = db.CreateParameter("BirthDate", DateTime.Now, DbType.DateTime);
            Console.WriteLine("   创建参数成功\n");

            // 1.5 使用集合（List<DbParameter> 等）
            Console.WriteLine("1.5 使用集合（List<DbParameter> 等）");
            var paramList = new List<DbParameter>
            {
                db.CreateParameter("Name", "王五"),
                db.CreateParameter("Age", 28)
            };
            var user5 = await db.ExecuteScalarAsync<string>("SELECT Name FROM Users WHERE Name = @Name AND Age = @Age", paramList);
            Console.WriteLine($"   查询结果: {user5}\n");
        }

        /// <summary>
        /// 2. 获取 DbDataReader
        /// </summary>
        private static async Task DemoExecuteReader(IDbAccess db)
        {
            Console.WriteLine("【2. 获取 DbDataReader】\n");

            // 2.1 基本读取 - 使用匿名对象
            Console.WriteLine("2.1 基本读取（使用匿名对象）");
            await using (var reader = await db.ExecuteReaderAsync("SELECT * FROM Users WHERE Age > @Age ORDER BY Id LIMIT 5",
                new { Age = 25 }))
            {
                Console.WriteLine("   年龄大于 25 的前 5 个用户:");
                while (reader.Read())
                {
                    Console.WriteLine($"     ID: {reader["Id"]}, Name: {reader["Name"]}, Age: {reader["Age"]}");
                }
            }
            Console.WriteLine();

            // 2.2 多结果集读取
            Console.WriteLine("2.2 多结果集读取");
            await using (var reader = await db.ExecuteReaderAsync("SELECT * FROM Users WHERE Id <= 3; SELECT * FROM Users WHERE Id > 17"))
            {
                Console.WriteLine("   第一个结果集:");
                while (reader.Read())
                {
                    Console.WriteLine($"     ID: {reader["Id"]}, Name: {reader["Name"]}");
                }

                await reader.NextResultAsync();
                Console.WriteLine("   第二个结果集:");
                while (reader.Read())
                {
                    Console.WriteLine($"     ID: {reader["Id"]}, Name: {reader["Name"]}");
                }
            }
            Console.WriteLine();
        }

        /// <summary>
        /// 3. 获取 DataSet
        /// </summary>
        private static async Task DemoExecuteDataSet(IDbAccess db)
        {
            Console.WriteLine("【3. 获取 DataSet】\n");

            // 3.1 单表查询 - 使用匿名对象
            Console.WriteLine("3.1 单表查询（使用匿名对象）");
            var dataSet = await db.ExecuteDataSetAsync("SELECT * FROM Users WHERE Age > @Age",
                new { Age = 28 });
            Console.WriteLine($"   返回表数: {dataSet.Tables.Count}");
            Console.WriteLine($"   第一表行数: {dataSet.Tables[0].Rows.Count}\n");

            // 3.2 多表查询
            Console.WriteLine("3.2 多表查询");
            var multiTableSet = await db.ExecuteDataSetAsync(@"
                SELECT * FROM Users WHERE Age <= 25;
                SELECT * FROM Users WHERE Age > 25");
            Console.WriteLine($"   返回表数: {multiTableSet.Tables.Count}");
            Console.WriteLine($"   表 1 行数: {multiTableSet.Tables[0].Rows.Count} (年龄 <= 25)");
            Console.WriteLine($"   表 2 行数: {multiTableSet.Tables[1].Rows.Count} (年龄 > 25)\n");
        }

        /// <summary>
        /// 4. 执行非查询 SQL
        /// </summary>
        private static async Task DemoExecuteNonQuery(IDbAccess db)
        {
            Console.WriteLine("【4. 执行非查询 SQL】\n");

            // 4.1 插入数据 - 使用匿名对象
            Console.WriteLine("4.1 插入数据（使用匿名对象）");
            var insertCount = await db.ExecuteNonQueryAsync(
                "INSERT INTO Users (Id, Name, Email, Age) VALUES (@Id, @Name, @Email, @Age)",
                new { Id = 21, Name = "张三", Email = "zhangsan@example.com", Age = 25 });
            Console.WriteLine($"   插入成功，影响行数: {insertCount}\n");

            // 4.2 更新数据 - 使用匿名对象
            Console.WriteLine("4.2 更新数据（使用匿名对象）");
            var updateCount = await db.ExecuteNonQueryAsync(
                "UPDATE Users SET Age = @Age WHERE Id = @Id",
                new { Age = 30, Id = 21 });
            Console.WriteLine($"   更新成功，影响行数: {updateCount}\n");

            // 4.3 删除数据 - 使用匿名对象
            Console.WriteLine("4.3 删除数据（使用匿名对象）");
            var deleteCount = await db.ExecuteNonQueryAsync(
                "DELETE FROM Users WHERE Id = @Id",
                new { Id = 21 });
            Console.WriteLine($"   删除成功，影响行数: {deleteCount}\n");
        }

        /// <summary>
        /// 5. 执行标量查询
        /// </summary>
        private static async Task DemoExecuteScalar(IDbAccess db)
        {
            Console.WriteLine("【5. 执行标量查询】\n");

            // 5.1 非泛型
            Console.WriteLine("5.1 非泛型");
            var count = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM Users");
            Console.WriteLine($"   用户总数 (object): {count}");

            // 若查询结果为空，返回 DBNull.Value
            var nullResult = await db.ExecuteScalarAsync("SELECT Name FROM Users WHERE Id = 999");
            Console.WriteLine($"   查询不存在的数据: {(nullResult == DBNull.Value ? "DBNull" : nullResult)}\n");

            // 5.2 泛型
            Console.WriteLine("5.2 泛型");

            // 基本类型
            var userCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users");
            var avgAge = await db.ExecuteScalarAsync<double>("SELECT AVG(Age) FROM Users");
            Console.WriteLine($"   用户总数 (int): {userCount}");
            Console.WriteLine($"   平均年龄 (double): {avgAge:F2}");

            // 可空类型 - 当查询结果可能为 NULL 时特别有用
            int? nullableMaxAge = await db.ExecuteScalarAsync<int?>("SELECT MAX(Age) FROM Users WHERE Age > 100");
            Console.WriteLine($"   最大年龄 (int?): {(nullableMaxAge.HasValue ? nullableMaxAge.ToString() : "NULL")}");

            // string 类型 - 可以为 null
            var userName = await db.ExecuteScalarAsync<string>("SELECT Name FROM Users WHERE Id = 1");
            Console.WriteLine($"   用户名 (string): {userName}");

            // DateTime 类型
            var createTime = await db.ExecuteScalarAsync<DateTime>("SELECT datetime('now')");
            Console.WriteLine($"   当前时间 (DateTime): {createTime}");

            // decimal 类型（适合货币计算）
            var avgAgeDecimal = await db.ExecuteScalarAsync<decimal>("SELECT AVG(Age) FROM Users");
            Console.WriteLine($"   平均年龄 (decimal): {avgAgeDecimal:F2}\n");

            // 5.3 DbValueConvert
            Console.WriteLine("5.3 DbValueConvert");

            // 基本类型转换
            var value = await db.ExecuteScalarAsync("SELECT Age FROM Users WHERE Id = 5");
            var convertedAge = DbValueConvert.ChangeType<int>(value);
            var convertedDouble = DbValueConvert.ChangeType<double>(value);
            Console.WriteLine($"   基本转换: 原始值={value}, int={convertedAge}, double={convertedDouble:F2}");

            // 可空类型转换 - null 值
            int? nullableAge = DbValueConvert.ChangeType<int?>(null);
            Console.WriteLine($"   null 转 int?: {(nullableAge.HasValue ? nullableAge.ToString() : "null")}");

            // DBNull 值转换
            var dbNullResult = DbValueConvert.ChangeType<int?>(DBNull.Value);
            Console.WriteLine($"   DBNull 转 int?: {(dbNullResult.HasValue ? dbNullResult.ToString() : "null")}");

            // 日期时间类型转换
            DateTime dateResult = DbValueConvert.ChangeType<DateTime>("2024-01-15");
            Console.WriteLine($"   字符串转 DateTime: {dateResult:yyyy-MM-dd}\n");
        }

        /// <summary>
        /// 6. 执行分页查询
        /// </summary>
        private static async Task DemoExecutePagination(IDbAccess db)
        {
            Console.WriteLine("【6. 执行分页查询】\n");

            int pageSize = 5;
            int totalCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users");
            int totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            Console.WriteLine($"   总记录数: {totalCount}, 每页 {pageSize} 条, 共 {totalPages} 页\n");

            // 6.1 使用 ExecuteReaderWithPaginationAsync
            Console.WriteLine("6.1 使用 ExecuteReaderWithPaginationAsync");
            for (int pageIndex = 1; pageIndex <= 3; pageIndex++)
            {
                Console.WriteLine($"   第 {pageIndex} 页:");
                await using (var reader = await db.ExecuteReaderWithPaginationAsync(
                    "SELECT * FROM Users",
                    pageIndex,
                    pageSize,
                    "Id ASC"))
                {
                    while (reader.Read())
                    {
                        Console.WriteLine($"     ID: {reader["Id"]}, Name: {reader["Name"]}, Age: {reader["Age"]}");
                    }
                }
                Console.WriteLine();
            }

            // 6.2 使用 ExecuteDataSetWithPaginationAsync
            Console.WriteLine("6.2 使用 ExecuteDataSetWithPaginationAsync");
            var pageDataSet = await db.ExecuteDataSetWithPaginationAsync(
                "SELECT * FROM Users",
                pageIndex: 2,
                pageSize: pageSize,
                orderBy: "Id ASC");
            Console.WriteLine("   第 2 页数据 (DataSet):");
            foreach (DataRow row in pageDataSet.Tables[0].Rows)
            {
                Console.WriteLine($"     ID: {row["Id"]}, Name: {row["Name"]}, Age: {row["Age"]}");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// 7. 异步操作示例
        /// </summary>
        private static async Task DemoExecuteAsync(IDbAccess db)
        {
            Console.WriteLine("【7. 异步操作示例】\n");

            // 异步执行标量查询
            var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users");
            Console.WriteLine($"   异步查询用户总数: {count}");

            // 异步执行非查询 - 使用匿名对象
            var insertCount = await db.ExecuteNonQueryAsync(
                "INSERT INTO Users (Id, Name, Email, Age) VALUES (@Id, @Name, @Email, @Age)",
                new { Id = 100, Name = "异步用户", Email = "async@example.com", Age = 25 });
            Console.WriteLine($"   异步插入行数: {insertCount}");

            // 异步使用 DbDataReader - 使用匿名对象
            await using (var reader = await db.ExecuteReaderAsync("SELECT * FROM Users WHERE Id = @Id",
                new { Id = 100 }))
            {
                while (await reader.ReadAsync())
                {
                    Console.WriteLine($"   异步读取: ID={reader["Id"]}, Name={reader["Name"]}");
                }
            }

            // 异步获取 DataSet - 使用匿名对象
            var dataSet = await db.ExecuteDataSetAsync("SELECT * FROM Users WHERE Id = @Id",
                new { Id = 100 });
            Console.WriteLine($"   异步 DataSet 行数: {dataSet.Tables[0].Rows.Count}");

            // 异步分页查询
            await using (var reader = await db.ExecuteReaderWithPaginationAsync(
                "SELECT * FROM Users",
                pageIndex: 1,
                pageSize: 3,
                orderBy: "Id ASC"))
            {
                Console.WriteLine("   异步分页查询结果:");
                while (await reader.ReadAsync())
                {
                    Console.WriteLine($"     ID: {reader["Id"]}, Name: {reader["Name"]}");
                }
            }

            // 清理测试数据
            await db.ExecuteNonQueryAsync("DELETE FROM Users WHERE Id = 100");
            Console.WriteLine("   异步操作示例完成\n");
        }
    }
}
