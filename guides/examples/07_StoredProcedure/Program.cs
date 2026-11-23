using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Xbc.DbAccess;
using Xbc.DbAccess.SqlServer;

namespace Examples
{
    /// <summary>
    /// 示例 07: 存储过程的使用
    /// 演示如何执行存储过程、使用参数、处理输出参数
    ///
    /// 前置条件：
    /// 1. 确保 SQL Server 已安装并运行
    /// 2. 创建数据库 DbAccessTest
    /// 3. 运行 SetupDatabase.sql 脚本创建测试表和存储过程
    /// </summary>
    public class Program
    {
        // Windows 身份认证连接字符串
        private const string ConnectionString = "Server=.;Database=DbAccessTest;Integrated Security=True;TrustServerCertificate=True;";

        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== 示例 07: 存储过程的使用 ===\n");
            Console.WriteLine("本示例演示 SQL Server 存储过程的使用\n");
            Console.WriteLine($"连接字符串: {ConnectionString}\n");

            try
            {
                // 1. 执行非查询存储过程
                await DemoExecuteNonQueryProcedure();

                // 2. 执行查询存储过程
                await DemoExecuteQueryProcedure();

                // 3. 使用输出参数
                await DemoOutputParameters();

                // 4. 异步执行存储过程
                await DemoExecuteProcedureAsync();

                // 5. 使用 DataReader 执行存储过程
                await DemoExecuteReaderProcedure();

                Console.WriteLine("\n示例运行完成！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n运行出错: {ex.Message}");
                Console.WriteLine("请确保:");
                Console.WriteLine("1. SQL Server 已安装并运行");
                Console.WriteLine("2. 数据库 DbAccessTest 已创建");
                Console.WriteLine("3. 已运行 SetupDatabase.sql 脚本");
            }

            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }

        /// <summary>
        /// 1. 执行非查询存储过程
        /// </summary>
        private static async Task DemoExecuteNonQueryProcedure()
        {
            Console.WriteLine("【1. 执行非查询存储过程】\n");

            // 创建 SQL Server 工厂
            var factory = SqlAccessFactory.CreateSqlServerFactory(ConnectionString);

            using var db = factory.Create();

            try
            {
                // 创建参数（包含 RowsAffected 参数用于获取受影响行数）
                // 注意：返回值参数的名称可以是任意的，这里使用 "RowsAffected" 作为示例
                var parameters = db.NewParameters()
                    .Add("UserName", "测试用户_" + Guid.NewGuid().ToString("N").Substring(0, 8))
                    .Add("Email", "test@example.com")
                    .Add("Age", 25)
                    .AddReturnValue("RowsAffected", DbType.Int32);

                // 执行存储过程
                ProcedureResult result = await db.ExecuteNonQueryProcedureAsync("sp_InsertUser", parameters);

                // 方式1：从 result.RowsAffected 获取
                // 注意：当存储过程使用 SET NOCOUNT ON 时，返回 -1
                Console.WriteLine($"   RowsAffected 属性值: {result.RowsAffected}");

                // 方式2：从 ReturnValue 参数获取（推荐，不受 SET NOCOUNT 影响）
                // 使用 GetValue<T> 获取返回值（参数不存在时会抛出异常）
                var returnValue = result.OutputParameters.GetValue<int>("RowsAffected");
                Console.WriteLine($"   ReturnValue 参数值（实际受影响行数）: {returnValue}");

                // 方式3：使用 TryGetValue（参数不存在时返回 false，不会抛出异常）
                if (result.OutputParameters.TryGetValue("RowsAffected", out int rowsAffected))
                {
                    Console.WriteLine($"   TryGetValue 获取值: {rowsAffected}");
                }

                // 方式4：兼容做法（优先使用 RowsAffected，如为 -1 则尝试从参数获取）
                var rowsAffectedCompatible = result.RowsAffected > 0
                    ? result.RowsAffected
                    : result.OutputParameters.GetValue<int>("RowsAffected");
                Console.WriteLine($"   兼容方式获取值: {rowsAffectedCompatible}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   执行失败: {ex.Message}");
            }

            Console.WriteLine();
        }

        /// <summary>
        /// 2. 执行查询存储过程
        /// </summary>
        private static async Task DemoExecuteQueryProcedure()
        {
            Console.WriteLine("【2. 执行查询存储过程】\n");

            var factory = SqlAccessFactory.CreateSqlServerFactory(ConnectionString);

            using var db = factory.Create();

            try
            {
                // 创建参数
                var parameters = db.NewParameters()
                    .Add("UserId", 1);

                // 执行查询存储过程
                ProcedureResult result = await db.ExecuteQueryProcedureAsync("sp_GetUserById", parameters);

                // 获取数据集
                if (result.DataSet != null && result.DataSet.Tables.Count > 0 && result.DataSet.Tables[0].Rows.Count > 0)
                {
                    DataRow row = result.DataSet.Tables[0].Rows[0];
                    Console.WriteLine($"   用户ID: {row["Id"]}");
                    Console.WriteLine($"   用户名: {row["UserName"]}");
                    Console.WriteLine($"   邮箱: {row["Email"]}");
                    Console.WriteLine($"   年龄: {row["Age"]}");
                    Console.WriteLine($"   状态: {row["Status"]}");
                }
                else
                {
                    Console.WriteLine("   未找到用户");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   执行失败: {ex.Message}");
            }

            Console.WriteLine();
        }

        /// <summary>
        /// 3. 使用输出参数
        /// </summary>
        private static async Task DemoOutputParameters()
        {
            Console.WriteLine("【3. 使用输出参数】\n");

            var factory = SqlAccessFactory.CreateSqlServerFactory(ConnectionString);

            using var db = factory.Create();

            try
            {
                // 创建参数（包含输出参数）
                var parameters = db.NewParameters()
                    .Add("UserName", "输出参数测试_" + Guid.NewGuid().ToString("N").Substring(0, 8))
                    .AddOutput("NewUserId", DbType.Int32)
                    .AddOutput("CreatedTime", DbType.DateTime);

                // 执行存储过程
                ProcedureResult result = await db.ExecuteNonQueryProcedureAsync("sp_InsertUserWithOutput", parameters);

                // 从 result.OutputParameters 中读取输出参数值
                int newUserId = result.OutputParameters.GetValue<int>("NewUserId");
                DateTime createdTime = result.OutputParameters.GetValue<DateTime>("CreatedTime");

                Console.WriteLine($"   新用户ID: {newUserId}");
                Console.WriteLine($"   创建时间: {createdTime:yyyy-MM-dd HH:mm:ss}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   执行失败: {ex.Message}");
            }

            // 演示 ReturnValue 参数
            await DemoReturnValueParameter(db);

            Console.WriteLine();
        }

        /// <summary>
        /// 演示 ReturnValue 参数
        /// </summary>
        private static async Task DemoReturnValueParameter(IDbAccess db)
        {
            Console.WriteLine("   【ReturnValue 参数演示】");

            try
            {
                // 创建参数（包含返回值参数）
                var parameters = db.NewParameters()
                    .Add("UserName", "返回值测试_" + Guid.NewGuid().ToString("N").Substring(0, 8))
                    .AddReturnValue("ReturnValue", DbType.Int32);

                // 执行存储过程
                ProcedureResult result = await db.ExecuteNonQueryProcedureAsync("sp_InsertUserWithReturnValue", parameters);

                // 从 OutputParameters 中获取返回值
                if (result.OutputParameters.Count > 0)
                {
                    var returnValue = result.OutputParameters.GetValue<int>("ReturnValue");
                    Console.WriteLine($"      存储过程返回值 (新用户ID): {returnValue}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      执行失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 4. 异步执行存储过程
        /// </summary>
        private static async Task DemoExecuteProcedureAsync()
        {
            Console.WriteLine("【4. 异步执行存储过程】\n");

            var factory = SqlAccessFactory.CreateSqlServerFactory(ConnectionString);

            using var db = factory.Create();

            try
            {
                // 创建参数
                var parameters = db.NewParameters()
                    .Add("Status", "Active");

                // 异步执行查询存储过程
                ProcedureResult result = await db.ExecuteQueryProcedureAsync("sp_GetUsersByStatus", parameters);

                int rowCount = result.DataSet?.Tables[0]?.Rows.Count ?? 0;
                Console.WriteLine($"   异步查询完成，返回 {rowCount} 条记录");

                // 显示前3条记录
                if (rowCount > 0 && result.DataSet != null)
                {
                    var table = result.DataSet.Tables[0];
                    int displayCount = Math.Min(3, table.Rows.Count);
                    for (int i = 0; i < displayCount; i++)
                    {
                        var row = table.Rows[i];
                        Console.WriteLine($"      [{row["Id"]}] {row["UserName"]} - {row["Email"]}");
                    }
                }

                // 异步执行非查询存储过程
                var updateParams = db.NewParameters()
                    .Add("UserId", 1)
                    .Add("NewStatus", "Inactive");

                ProcedureResult updateResult = await db.ExecuteNonQueryProcedureAsync("sp_UpdateUserStatus", updateParams);
                Console.WriteLine($"   异步更新完成，影响行数: {updateResult.RowsAffected}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   执行失败: {ex.Message}");
            }

            Console.WriteLine();
        }

        /// <summary>
        /// 5. 使用 DataReader 执行存储过程
        /// </summary>
        private static async Task DemoExecuteReaderProcedure()
        {
            Console.WriteLine("【5. 使用 DataReader 执行存储过程】\n");

            var factory = SqlAccessFactory.CreateSqlServerFactory(ConnectionString);

            using var db = factory.Create();

            try
            {
                // 创建参数
                var parameters = db.NewParameters()
                    .Add("MinId", 0);

                // 使用 ExecuteReader 执行存储过程
                await using (var reader = await db.ExecuteReaderAsync("sp_GetAllUsers", CommandType.StoredProcedure, parameters))
                {
                    Console.WriteLine($"   使用 DataReader 读取数据，返回列数: {reader.FieldCount}");

                    int count = 0;
                    while (await reader.ReadAsync())
                    {
                        count++;
                        if (count <= 3)
                        {
                            Console.WriteLine($"      [{reader["Id"]}] {reader["UserName"]} - {reader["Email"]}");
                        }
                    }
                    Console.WriteLine($"   总共 {count} 条记录");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   执行失败: {ex.Message}");
            }

            Console.WriteLine();
        }
    }
}
