using System.Data;
using Xbc.DbAccess.Common;
using Xbc.DbAccess.IntegrationTests.Infrastructure;
using Xbc.DbAccess.SqlServer;
using Xunit;

namespace Xbc.DbAccess.IntegrationTests
{
    /// <summary>
    /// 存储过程真库测试（SQL Server 专属：执行非查询/查询存储过程、输出参数、输入输出参数、返回值、
    /// 事务结合、TryGetValue、多结果集与错误传播）。迁自 RegressionTests，去掉了原先吞异常的跳过逻辑，
    /// 环境不可用由 <see cref="SqlServerOnlyFactAttribute"/> 统一跳过。
    /// <para>测试表与存储过程均使用本类专属名称（同类内串行执行），每方法前重建过程保证幂等。</para>
    /// </summary>
    public class StoredProcedureTests : IAsyncLifetime
    {
        private IDbAccess DbAccess { get; }

        public StoredProcedureTests()
        {
            var connectionOptions = new DbConnectionOptions
            {
                ConnectionString = RealDbProbe.MsSqlConnectionString,
                PoolOptions = { Enabled = false }
            };
            DbAccess = new SqlAccessFactory(connectionOptions).Create();
        }

        public async Task InitializeAsync()
        {
            await DbAccess.ExecuteNonQueryAsync("DROP TABLE IF EXISTS [SpUsers]");
            await DbAccess.ExecuteNonQueryAsync("CREATE TABLE [SpUsers] ([Id] INT IDENTITY(1,1) PRIMARY KEY, [Name] NVARCHAR(100), [Age] INT)");
        }

        public async Task DisposeAsync()
        {
            // 过程与表逐个清理，对象不存在时忽略（SQL Server 16sp3+/2019 前无 DROP IF EXISTS 对过程的部分版本兼容）
            foreach (var procedureName in new[] { "sp_XbcIt_Insert", "sp_XbcIt_Query", "sp_XbcIt_Output", "sp_XbcIt_InputOutput", "sp_XbcIt_ReturnValue", "sp_XbcIt_Async", "sp_XbcIt_Transaction", "sp_XbcIt_TryGetValue", "sp_XbcIt_MultipleResultSets", "sp_XbcIt_Error" })
            {
                try
                {
                    await DbAccess.ExecuteNonQueryAsync($"IF OBJECT_ID('{procedureName}', 'P') IS NOT NULL DROP PROCEDURE [{procedureName}]");
                }
                catch
                {
                    // 清理失败不影响测试结果
                }
            }

            try
            {
                await DbAccess.ExecuteNonQueryAsync("DROP TABLE IF EXISTS [SpUsers]");
            }
            catch
            {
                // 清理失败不影响测试结果
            }
            DbAccess.Dispose();
        }

        /// <summary>删除已有过程并按给定定义重建（每方法前调用，保证幂等与干净状态）。</summary>
        private Task RecreateProcedureAsync(string procedureName, string body)
        {
            return DbAccess.ExecuteNonQueryAsync($@"
                IF OBJECT_ID('{procedureName}', 'P') IS NOT NULL DROP PROCEDURE [{procedureName}];
                EXEC('CREATE PROCEDURE [{procedureName}] " + body + "');");
        }

        [SqlServerOnlyFact]
        public async Task ExecuteNonQueryProcedure_InsertsRow_AndReturnsRowsAffected()
        {
            await RecreateProcedureAsync("sp_XbcIt_Insert", @"
                    @Name NVARCHAR(100),
                    @Age INT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    INSERT INTO [SpUsers] ([Name], [Age]) VALUES (@Name, @Age);
                    RETURN @@ROWCOUNT;
                END");

            var result = await DbAccess.ExecuteNonQueryProcedureAsync("sp_XbcIt_Insert",
                DbAccess.NewParameters()
                    .Add("Name", "张三")
                    .Add("Age", 25)
                    .AddReturnValue("ReturnValue", DbType.Int32)
                    .ToArray());

            // SET NOCOUNT ON 下受影响行数可能为 -1，以 RETURN @@ROWCOUNT 的返回值为准
            Assert.Equal(1, result.OutputParameters.GetValue<int>("ReturnValue"));
            Assert.Equal(1, await DbAccess.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM [SpUsers]"));
        }

        [SqlServerOnlyFact]
        public async Task ExecuteQueryProcedure_ReturnsFilteredRows()
        {
            await RecreateProcedureAsync("sp_XbcIt_Query", @"
                    @MinAge INT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SELECT * FROM [SpUsers] WHERE [Age] >= @MinAge;
                END");

            await DbAccess.ExecuteNonQueryAsync("INSERT INTO [SpUsers] ([Name], [Age]) VALUES ('张三', 25), ('李四', 30), ('王五', 20)");

            var result = await DbAccess.ExecuteQueryProcedureAsync("sp_XbcIt_Query",
                DbAccess.NewParameters()
                    .Add("MinAge", 25)
                    .ToArray());

            Assert.NotNull(result.DataSet);
            Assert.Single(result.DataSet.Tables);
            Assert.Equal(2, result.DataSet.Tables[0].Rows.Count);
        }

        [SqlServerOnlyFact]
        public async Task ExecuteNonQueryProcedure_FillsOutputParameters()
        {
            await RecreateProcedureAsync("sp_XbcIt_Output", @"
                    @InputValue INT,
                    @OutputValue INT OUTPUT,
                    @Message NVARCHAR(100) OUTPUT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET @OutputValue = @InputValue * 2;
                    SET @Message = ''输入值: '' + CAST(@InputValue AS NVARCHAR(10)) + '', 输出值: '' + CAST(@OutputValue AS NVARCHAR(10));
                END");

            var result = await DbAccess.ExecuteNonQueryProcedureAsync("sp_XbcIt_Output",
                DbAccess.NewParameters()
                    .Add("InputValue", 10)
                    .AddOutput("OutputValue", DbType.Int32)
                    .AddOutput("Message", DbType.String)
                    .ToArray());

            Assert.Equal(20, result.OutputParameters.GetValue<int>("OutputValue"));
            var message = result.OutputParameters.GetValue<string>("Message");
            Assert.Contains("10", message);
            Assert.Contains("20", message);
        }

        [SqlServerOnlyFact]
        public async Task ExecuteNonQueryProcedure_FillsInputOutputParameter()
        {
            await RecreateProcedureAsync("sp_XbcIt_InputOutput", @"
                    @Counter INT OUTPUT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET @Counter = @Counter + 10;
                END");

            var result = await DbAccess.ExecuteNonQueryProcedureAsync("sp_XbcIt_InputOutput",
                DbAccess.NewParameters()
                    .Add("Counter", 5, DbType.Int32, ParameterDirection.InputOutput)
                    .ToArray());

            Assert.Equal(15, result.OutputParameters.GetValue<int>("Counter"));
        }

        [SqlServerOnlyFact]
        public async Task ExecuteNonQueryProcedure_FillsReturnValue()
        {
            await RecreateProcedureAsync("sp_XbcIt_ReturnValue", @"
                    @A INT,
                    @B INT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    RETURN @A + @B;
                END");

            var result = await DbAccess.ExecuteNonQueryProcedureAsync("sp_XbcIt_ReturnValue",
                DbAccess.NewParameters()
                    .Add("A", 10)
                    .Add("B", 20)
                    .AddReturnValue("ReturnValue", DbType.Int32)
                    .ToArray());

            Assert.Equal(30, result.OutputParameters.GetValue<int>("ReturnValue"));
        }

        [SqlServerOnlyFact]
        public async Task ExecuteQueryProcedure_ReturnsResultSet_AfterInsert()
        {
            await RecreateProcedureAsync("sp_XbcIt_Async", @"
                    @Name NVARCHAR(100)
                AS
                BEGIN
                    SET NOCOUNT ON;
                    INSERT INTO [SpUsers] ([Name], [Age]) VALUES (@Name, 30);
                    SELECT @@IDENTITY AS NewId;
                END");

            var result = await DbAccess.ExecuteQueryProcedureAsync("sp_XbcIt_Async",
                DbAccess.NewParameters()
                    .Add("Name", "AsyncUser")
                    .ToArray());

            Assert.NotNull(result.DataSet);
            Assert.Equal(1, await DbAccess.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM [SpUsers] WHERE [Name] = 'AsyncUser'"));
        }

        [SqlServerOnlyFact]
        public async Task ExecuteNonQueryProcedure_CommitsInsideTransaction()
        {
            await RecreateProcedureAsync("sp_XbcIt_Transaction", @"
                    @Name NVARCHAR(100)
                AS
                BEGIN
                    SET NOCOUNT ON;
                    INSERT INTO [SpUsers] ([Name], [Age]) VALUES (@Name, 40);
                END");

            using (var transaction = await DbAccess.BeginTransactionAsync())
            {
                await DbAccess.ExecuteNonQueryProcedureAsync("sp_XbcIt_Transaction",
                    DbAccess.NewParameters()
                        .Add("Name", "TransactionUser")
                        .ToArray());

                transaction.Commit();
            }

            Assert.Equal(1, await DbAccess.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM [SpUsers] WHERE [Name] = 'TransactionUser'"));
        }

        [SqlServerOnlyFact]
        public async Task OutputParameters_TryGetValue_DistinguishesMissingKeys()
        {
            await RecreateProcedureAsync("sp_XbcIt_TryGetValue", @"
                    @Output1 INT OUTPUT,
                    @Output2 NVARCHAR(50) OUTPUT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET @Output1 = 100;
                    SET @Output2 = ''TestValue'';
                END");

            var result = await DbAccess.ExecuteNonQueryProcedureAsync("sp_XbcIt_TryGetValue",
                DbAccess.NewParameters()
                    .AddOutput("Output1", DbType.Int32)
                    .AddOutput("Output2", DbType.String)
                    .ToArray());

            Assert.True(result.OutputParameters.TryGetValue<int>("Output1", out var output1));
            Assert.Equal(100, output1);

            Assert.True(result.OutputParameters.TryGetValue<string>("Output2", out var output2));
            Assert.Equal("TestValue", output2);

            Assert.False(result.OutputParameters.TryGetValue<int>("NonExistent", out _));
        }

        [SqlServerOnlyFact]
        public async Task ExecuteQueryProcedure_ReturnsMultipleResultSets()
        {
            await RecreateProcedureAsync("sp_XbcIt_MultipleResultSets", @"
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SELECT 1 AS Id, ''First'' AS Name;
                    SELECT 2 AS Id, ''Second'' AS Name;
                    SELECT 3 AS Id, ''Third'' AS Name;
                END");

            var result = await DbAccess.ExecuteQueryProcedureAsync("sp_XbcIt_MultipleResultSets");

            Assert.NotNull(result.DataSet);
            Assert.Equal(3, result.DataSet.Tables.Count);
            Assert.Equal(1, result.DataSet.Tables[0].Rows.Count);
            Assert.Equal(1, result.DataSet.Tables[1].Rows.Count);
            Assert.Equal(1, result.DataSet.Tables[2].Rows.Count);
        }

        [SqlServerOnlyFact]
        public async Task ExecuteNonQueryProcedure_PropagatesStoredProcedureError()
        {
            await RecreateProcedureAsync("sp_XbcIt_Error", @"
                AS
                BEGIN
                    SET NOCOUNT ON;
                    RAISERROR(''测试错误'', 16, 1);
                END");

            await Assert.ThrowsAnyAsync<Exception>(() => DbAccess.ExecuteNonQueryProcedureAsync("sp_XbcIt_Error"));
        }
    }
}
