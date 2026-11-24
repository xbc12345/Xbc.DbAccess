using Xbc.DbAccess.UnitTests.Infrastructure;
using Xunit;

namespace Xbc.DbAccess.UnitTests
{
    /// <summary>
    /// DbAccess.ExecuteDataSet 的单元测试，使用 SQLite 内存数据库。
    /// </summary>
    public class DbAccessExecuteDataSetTests : SqliteTestBase
    {
        #region ExecuteDataSet 同步方法测试

        [Fact]
        public async Task ExecuteDataSet_有效SQL_返回数据集()
        {
            // Arrange
            const string sql = "SELECT * FROM TestUsers";

            // Act
            var result = await DbAccess.ExecuteDataSetAsync(sql);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Tables);
            Assert.True(result.Tables[0].Rows.Count >= 3);
        }

        [Fact]
        public async Task ExecuteDataSet_带参数_参数正确设置()
        {
            // Arrange
            const string sql = "SELECT * FROM TestUsers WHERE Id = @id";
            var param = CreateParameter("@id", 1);

            // Act
            var result = await DbAccess.ExecuteDataSetAsync(sql, param);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Tables);
            Assert.Single(result.Tables[0].Rows);
        }

        [Fact]
        public async Task ExecuteDataSet_SQL为空_抛出ArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => DbAccess.ExecuteDataSetAsync(null!));
        }

        [Fact]
        public async Task ExecuteDataSet_执行异常_抛出异常()
        {
            // Arrange
            const string sql = "INVALID SQL SYNTAX";

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() => DbAccess.ExecuteDataSetAsync(sql));
        }

        [Fact]
        public async Task ExecuteDataSet_多表查询_返回多个数据表()
        {
            // Arrange
            const string sql = @"
                SELECT * FROM TestUsers WHERE Id = 1;
                SELECT * FROM TestOrders WHERE UserId = 1;
            ";

            // Act
            var result = await DbAccess.ExecuteDataSetAsync(sql);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Tables.Count);
        }

        [Fact]
        public async Task ExecuteDataSet_空结果_返回空数据集()
        {
            // Arrange
            const string sql = "SELECT * FROM TestUsers WHERE Id = -1";

            // Act
            var result = await DbAccess.ExecuteDataSetAsync(sql);

            // Assert
            Assert.NotNull(result);
            // 即使查询没有结果，也会返回一个包含空表的 DataSet
            Assert.Single(result.Tables);
            Assert.Empty(result.Tables[0].Rows);
        }

        #endregion

        #region ExecuteDataSetAsync 异步方法测试

        [Fact]
        public async Task ExecuteDataSetAsync_有效SQL_返回数据集()
        {
            // Arrange
            const string sql = "SELECT * FROM TestUsers";

            // Act
            var result = await DbAccess.ExecuteDataSetAsync(sql);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Tables);
        }

        [Fact]
        public async Task ExecuteDataSetAsync_取消操作_抛出OperationCanceledException()
        {
            // Arrange
            const string sql = "SELECT * FROM TestUsers";
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            // TaskCanceledException 继承自 OperationCanceledException，所以使用 ThrowsAny
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await DbAccess.ExecuteDataSetAsync(sql, null, cts.Token));
        }

        [Fact]
        public async Task ExecuteDataSetAsync_执行异常_抛出异常()
        {
            // Arrange
            const string sql = "INVALID SQL SYNTAX";

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() => DbAccess.ExecuteDataSetAsync(sql));
        }

        [Fact]
        public async Task ExecuteDataSetAsync_多表查询_返回多个数据表()
        {
            // Arrange
            const string sql = @"
                SELECT * FROM TestUsers WHERE Id = 1;
                SELECT * FROM TestOrders WHERE UserId = 1;
            ";

            // Act
            var result = await DbAccess.ExecuteDataSetAsync(sql);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Tables.Count);
        }

        #endregion

        #region 复杂查询测试

        [Fact]
        public async Task ExecuteDataSet_聚合查询_返回正确结果()
        {
            // Arrange
            const string sql = @"
                SELECT COUNT(*) as UserCount FROM TestUsers;
                SELECT SUM(Amount) as TotalAmount FROM TestOrders;
            ";

            // Act
            var result = await DbAccess.ExecuteDataSetAsync(sql);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Tables.Count);
            Assert.True(result.Tables[0].Rows.Count > 0);
            Assert.True(result.Tables[1].Rows.Count > 0);
        }

        [Fact]
        public async Task ExecuteDataSet_连接查询_返回正确结果()
        {
            // Arrange
            const string sql = @"
                SELECT u.Name, o.ProductName, o.Amount 
                FROM TestUsers u 
                INNER JOIN TestOrders o ON u.Id = o.UserId
            ";

            // Act
            var result = await DbAccess.ExecuteDataSetAsync(sql);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Tables);
            Assert.True(result.Tables[0].Rows.Count > 0);
        }

        #endregion
    }
}
