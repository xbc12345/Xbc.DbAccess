using System.Data;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Extensions
{
    /// <summary>
    /// DbAccessExecuteExtensions（object 参数便捷重载）的单元测试：匿名对象/字典参数、
    /// CommandType 重载、标量泛型转换与参数构建辅助方法。
    /// </summary>
    public class DbAccessExecuteExtensionsTests : Infrastructure.SqliteTestBase
    {
        [Fact]
        public async Task ExecuteNonQueryAsync_WithAnonymousObjectParam()
        {
            var affected = await DbAccess.ExecuteNonQueryAsync(
                "INSERT INTO TestUsers (Name, Age, Email) VALUES (@Name, @Age, @Email)",
                new { Name = "Dave", Age = 40, Email = (string)null });

            Assert.Equal(1, affected);

            var count = await DbAccess.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM TestUsers WHERE Name = @Name",
                new Dictionary<string, object> { { "Name", "Dave" } });
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task ExecuteNonQueryAsync_WithCommandType_ExecutesText()
        {
            var affected = await DbAccess.ExecuteNonQueryAsync(
                "DELETE FROM TestUsers WHERE Name = @Name",
                CommandType.Text,
                new { Name = "Nonexistent" });

            Assert.Equal(0, affected);
        }

        [Fact]
        public async Task ExecuteScalarAsync_Generic_ConvertsScalar()
        {
            Assert.Equal(3, await DbAccess.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TestUsers"));
            Assert.Equal("Alice", await DbAccess.ExecuteScalarAsync<string>("SELECT Name FROM TestUsers WHERE Id = @Id", new { Id = 1 }));
        }

        [Fact]
        public async Task ExecuteScalarAsync_WithCommandType_ReturnsScalar()
        {
            var result = await DbAccess.ExecuteScalarAsync("SELECT Age FROM TestUsers WHERE Id = @Id", CommandType.Text, new { Id = 1 });

            Assert.Equal(25L, result);
        }

        [Fact]
        public async Task ExecuteReaderAsync_WithObjectParam_ReadsRow()
        {
            using var reader = await DbAccess.ExecuteReaderAsync(
                "SELECT Name FROM TestUsers WHERE Id = @Id",
                new { Id = 2 });

            Assert.True(await reader.ReadAsync());
            Assert.Equal("Bob", reader.GetString(0));
        }

        [Fact]
        public async Task ExecuteReaderAsync_WithCommandType_ReturnsReader()
        {
            using var reader = await DbAccess.ExecuteReaderAsync(
                "SELECT Name FROM TestUsers WHERE Id = @Id",
                CommandType.Text,
                new { Id = 3 });

            Assert.True(await reader.ReadAsync());
            Assert.Equal("Charlie", reader.GetString(0));
        }

        [Fact]
        public async Task ExecuteDataSetAsync_WithObjectParam_ReturnsTable()
        {
            var dataSet = await DbAccess.ExecuteDataSetAsync(
                "SELECT Id, Name FROM TestUsers WHERE Age >= @Age",
                new { Age = 25 });

            Assert.Single(dataSet.Tables);
            Assert.Equal(3, dataSet.Tables[0].Rows.Count);
        }

        [Fact]
        public async Task ExecuteDataSetAsync_WithCommandType_ReturnsTable()
        {
            var dataSet = await DbAccess.ExecuteDataSetAsync(
                "SELECT Id, Name FROM TestUsers WHERE Age >= @Age",
                CommandType.Text,
                new { Age = 30 });

            Assert.Equal(2, dataSet.Tables[0].Rows.Count);
        }

        [Fact]
        public async Task NullDbAccess_ThrowsArgumentNullException()
        {
            IDbAccess dbAccess = null;
            string sql = "SELECT 1";

            await Assert.ThrowsAsync<ArgumentNullException>(() => DbAccessExecuteExtensions.ExecuteNonQueryAsync(dbAccess, sql));
            await Assert.ThrowsAsync<ArgumentNullException>(() => DbAccessExecuteExtensions.ExecuteScalarAsync<int>(dbAccess, sql));
            await Assert.ThrowsAsync<ArgumentNullException>(() => DbAccessExecuteExtensions.ExecuteReaderAsync(dbAccess, sql));
            await Assert.ThrowsAsync<ArgumentNullException>(() => DbAccessExecuteExtensions.ExecuteDataSetAsync(dbAccess, sql));
            Assert.Throws<ArgumentNullException>(() => DbAccessExecuteExtensions.NewParameters(dbAccess));
            Assert.Throws<ArgumentNullException>(() => DbAccessExecuteExtensions.CreateParameter(dbAccess, "Id", 1));
            Assert.Throws<ArgumentNullException>(() =>
                DbAccessExecuteExtensions.CreateParameter(dbAccess, "Id", 1, DbType.Int32, ParameterDirection.Output));
            Assert.Throws<ArgumentNullException>(() => DbAccessExecuteExtensions.CreateOutputParameter(dbAccess, "Id", DbType.Int32));
        }

        [Fact]
        public void NewParameters_And_CreateParameter_BuildUsableParameters()
        {
            var collection = DbAccess.NewParameters();
            collection.Add("Name", "Tom");

            Assert.Equal("@Name", collection.Cast<System.Data.Common.DbParameter>().Single().ParameterName);

            // 输出方向参数在 SQLite 上不受支持，由 DbParameterCollectionTests 的 mock 构建器覆盖
            var parameter = DbAccess.CreateParameter("Age", 20, DbType.Int32);
            Assert.Equal("@Age", parameter.ParameterName);
            Assert.Equal(20, parameter.Value);
            Assert.Equal(DbType.Int32, parameter.DbType);
        }
    }
}
