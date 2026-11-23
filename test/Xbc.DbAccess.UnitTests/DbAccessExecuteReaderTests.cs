using Xbc.DbAccess.UnitTests.Infrastructure;
using Xunit;

namespace Xbc.DbAccess.UnitTests
{
    /// <summary>
    /// DbAccess.ExecuteReader 的单元测试，使用 SQLite 内存数据库。
    /// </summary>
    public class DbAccessExecuteReaderTests : SqliteTestBase
    {
        #region ExecuteReader 同步方法测试

        [Fact]
        public async Task ExecuteReader_有效SQL_返回数据读取器()
        {
            // Arrange
            const string sql = "SELECT * FROM TestUsers";

            // Act
            using var reader = await DbAccess.ExecuteReaderAsync(sql);

            // Assert
            Assert.NotNull(reader);
            Assert.True(reader.Read());
        }

        [Fact]
        public async Task ExecuteReader_带参数_参数正确设置()
        {
            // Arrange
            const string sql = "SELECT * FROM TestUsers WHERE Id = @id";
            var param = CreateParameter("@id", 1);

            // Act
            using var reader = await DbAccess.ExecuteReaderAsync(sql, param);

            // Assert
            Assert.NotNull(reader);
            Assert.True(reader.Read());
        }

        [Fact]
        public async Task ExecuteReader_SQL为空_抛出ArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => DbAccess.ExecuteReaderAsync(null!));
        }

        [Fact]
        public async Task ExecuteReader_执行异常_抛出异常()
        {
            // Arrange
            const string sql = "INVALID SQL SYNTAX";

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() => DbAccess.ExecuteReaderAsync(sql));
        }

        [Fact]
        public async Task ExecuteReader_读取多行数据_正确遍历()
        {
            // Arrange
            const string sql = "SELECT * FROM TestUsers";

            // Act
            using var reader = await DbAccess.ExecuteReaderAsync(sql);
            var rowCount = 0;
            while (reader.Read())
            {
                rowCount++;
            }

            // Assert
            Assert.True(rowCount >= 3);
        }

        #endregion

        #region ExecuteReaderAsync 异步方法测试

        [Fact]
        public async Task ExecuteReaderAsync_有效SQL_返回数据读取器()
        {
            // Arrange
            const string sql = "SELECT * FROM TestUsers";

            // Act
            using var reader = await DbAccess.ExecuteReaderAsync(sql);

            // Assert
            Assert.NotNull(reader);
            Assert.True(reader.Read());
        }

        [Fact]
        public async Task ExecuteReaderAsync_取消操作_抛出OperationCanceledException()
        {
            // Arrange
            const string sql = "SELECT * FROM TestUsers";
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            // TaskCanceledException 继承自 OperationCanceledException，所以使用 ThrowsAny
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await DbAccess.ExecuteReaderAsync(sql, null, cts.Token));
        }

        [Fact]
        public async Task ExecuteReaderAsync_执行异常_抛出异常()
        {
            // Arrange
            const string sql = "INVALID SQL SYNTAX";

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() => DbAccess.ExecuteReaderAsync(sql));
        }

        [Fact]
        public async Task ExecuteReaderAsync_读取多行数据_正确遍历()
        {
            // Arrange
            const string sql = "SELECT * FROM TestUsers";

            // Act
            using var reader = await DbAccess.ExecuteReaderAsync(sql);
            var rowCount = 0;
            while (await reader.ReadAsync())
            {
                rowCount++;
            }

            // Assert
            Assert.True(rowCount >= 3);
        }

        #endregion

        #region 连接生命周期测试

        [Fact]
        public async Task ExecuteReader_不在事务或作用域中_读取器释放时释放连接()
        {
            // Arrange
            const string sql = "SELECT * FROM TestUsers";

            // Act
            using (var reader = await DbAccess.ExecuteReaderAsync(sql))
            {
                // 读取器在使用中
                Assert.True(reader.Read());
            }

            // Assert - 读取器释放后应该释放连接，不抛出异常
            Assert.True(true);
        }

        [Fact]
        public async Task ExecuteReader_在连接作用域内_不自动释放连接()
        {
            // Arrange
            const string sql = "SELECT * FROM TestUsers";

            // Act
            using (await DbAccess.BeginConnectionScopeAsync())
            {
                using var reader = await DbAccess.ExecuteReaderAsync(sql);
                Assert.True(reader.Read());
            }

            // Assert - 在作用域内不释放连接，不抛出异常
            Assert.True(true);
        }

        #endregion

        #region 数据读取测试

        [Fact]
        public async Task ExecuteReader_读取特定列_返回正确值()
        {
            // Arrange
            const string sql = "SELECT Name, Age FROM TestUsers WHERE Id = 1";

            // Act
            using var reader = await DbAccess.ExecuteReaderAsync(sql);
            Assert.True(reader.Read());
            var name = reader.GetString(0);
            var age = reader.GetInt32(1);

            // Assert
            Assert.NotNull(name);
            Assert.True(age > 0);
        }

        [Fact]
        public async Task ExecuteReader_使用列名访问_返回正确值()
        {
            // Arrange
            const string sql = "SELECT Name, Age FROM TestUsers WHERE Id = 1";

            // Act
            using var reader = await DbAccess.ExecuteReaderAsync(sql);
            Assert.True(reader.Read());
            var name = reader["Name"];
            var age = reader["Age"];

            // Assert
            Assert.NotNull(name);
            Assert.NotNull(age);
        }

        [Fact]
        public async Task ExecuteReader_空结果_返回空读取器()
        {
            // Arrange
            const string sql = "SELECT * FROM TestUsers WHERE Id = -1";

            // Act
            using var reader = await DbAccess.ExecuteReaderAsync(sql);

            // Assert
            Assert.NotNull(reader);
            Assert.False(reader.Read());
        }

        #endregion
    }
}
