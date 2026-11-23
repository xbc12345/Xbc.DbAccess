using System.Data;
using Xbc.DbAccess.UnitTests.Infrastructure;
using Xunit;

namespace Xbc.DbAccess.UnitTests
{
    /// <summary>
    /// DbAccess.ExecuteScalar 的单元测试（使用 SQLite 内存数据库）。
    /// </summary>
    public class DbAccessExecuteScalarTests : SqliteTestBase
    {
        #region ExecuteScalar 同步方法测试

        [Fact]
        public async Task ExecuteScalar_查询整数_返回正确值()
        {
            // Arrange
            const string sql = "SELECT COUNT(*) FROM TestUsers";

            // Act
            var result = await DbAccess.ExecuteScalarAsync(sql);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<long>(result);
            Assert.Equal(3, Convert.ToInt64(result));
        }

        [Fact]
        public async Task ExecuteScalar_查询字符串_返回正确值()
        {
            // Arrange
            const string sql = "SELECT Name FROM TestUsers WHERE Id = 1";

            // Act
            var result = await DbAccess.ExecuteScalarAsync(sql);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Alice", result);
        }

        [Fact]
        public async Task ExecuteScalar_查询无结果_返回DBNull()
        {
            // Arrange
            const string sql = "SELECT Name FROM TestUsers WHERE Id = -1";

            // Act
            var result = await DbAccess.ExecuteScalarAsync(sql);

            // Assert
            // SQLite 返回 null 而不是 DBNull.Value
            Assert.Null(result);
        }

        [Fact]
        public async Task ExecuteScalar_带参数_参数正确设置()
        {
            // Arrange
            const string sql = "SELECT Name FROM TestUsers WHERE Id = @id";
            // 使用 CreateParameter 方法（基类方法会自动处理前缀）
            var param = CreateParameter("id", 1);

            // Act
            var result = await DbAccess.ExecuteScalarAsync(sql, param);

            // Assert
            Assert.Equal("Alice", result);
        }

        [Fact]
        public async Task ExecuteScalar_SQL为空_抛出ArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => DbAccess.ExecuteScalarAsync(null!));
        }

        [Fact]
        public async Task ExecuteScalar_执行异常_抛出异常()
        {
            // Arrange
            const string sql = "INVALID SQL SYNTAX";

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() => DbAccess.ExecuteScalarAsync(sql));
        }

        #endregion

        #region ExecuteScalarAsync 异步方法测试

        [Fact]
        public async Task ExecuteScalarAsync_查询整数_返回正确值()
        {
            // Arrange
            const string sql = "SELECT COUNT(*) FROM TestUsers";

            // Act
            var result = await DbAccess.ExecuteScalarAsync(sql);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<long>(result);
            Assert.Equal(3, Convert.ToInt64(result));
        }

        [Fact]
        public async Task ExecuteScalarAsync_查询字符串_返回正确值()
        {
            // Arrange
            const string sql = "SELECT Name FROM TestUsers WHERE Id = 1";

            // Act
            var result = await DbAccess.ExecuteScalarAsync(sql);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Alice", result);
        }

        [Fact]
        public async Task ExecuteScalarAsync_取消操作_抛出OperationCanceledException()
        {
            // Arrange
            const string sql = "SELECT COUNT(*) FROM TestUsers";
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            // TaskCanceledException 继承自 OperationCanceledException，所以使用 ThrowsAny
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await DbAccess.ExecuteScalarAsync(sql, null, cts.Token));
        }

        [Fact]
        public async Task ExecuteScalarAsync_执行异常_抛出异常()
        {
            // Arrange
            const string sql = "INVALID SQL SYNTAX";

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() => DbAccess.ExecuteScalarAsync(sql));
        }

        #endregion

        #region IDbAccessExtensions 扩展方法测试

        [Fact]
        public async Task ExecuteScalarT_查询整数_返回正确类型()
        {
            // Arrange
            const string sql = "SELECT COUNT(*) FROM TestUsers";

            // Act
            var result = await DbAccess.ExecuteScalarAsync<long>(sql);

            // Assert
            Assert.IsType<long>(result);
            Assert.Equal(3, result);
        }

        [Fact]
        public async Task ExecuteScalarT_查询可空整数_返回正确类型()
        {
            // Arrange
            const string sql = "SELECT Age FROM TestUsers WHERE Id = 1";

            // Act
            var result = await DbAccess.ExecuteScalarAsync<int?>(sql);

            // Assert
            Assert.True(result.HasValue);
            Assert.Equal(25, result.Value);
        }

        [Fact]
        public async Task ExecuteScalarT_查询字符串_返回正确类型()
        {
            // Arrange
            const string sql = "SELECT Name FROM TestUsers WHERE Id = 1";

            // Act
            var result = await DbAccess.ExecuteScalarAsync<string>(sql);

            // Assert
            Assert.IsType<string>(result);
            Assert.Equal("Alice", result);
        }

        [Fact]
        public async Task ExecuteScalarT_查询无结果_返回默认值()
        {
            // Arrange
            const string sql = "SELECT Age FROM TestUsers WHERE Id = -1";

            // Act
            var result = await DbAccess.ExecuteScalarAsync<int?>(sql);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task ExecuteScalarAsyncT_查询整数_返回正确类型()
        {
            // Arrange
            const string sql = "SELECT COUNT(*) FROM TestUsers";

            // Act
            var result = await DbAccess.ExecuteScalarAsync<long>(sql);

            // Assert
            Assert.IsType<long>(result);
            Assert.Equal(3, result);
        }

        [Fact]
        public async Task ExecuteScalarAsyncT_查询字符串_返回正确类型()
        {
            // Arrange
            const string sql = "SELECT Name FROM TestUsers WHERE Id = 1";

            // Act
            var result = await DbAccess.ExecuteScalarAsync<string>(sql);

            // Assert
            Assert.IsType<string>(result);
            Assert.Equal("Alice", result);
        }

        [Fact]
        public async Task ExecuteScalarAsyncT_查询可空整数_返回正确类型()
        {
            // Arrange
            const string sql = "SELECT Age FROM TestUsers WHERE Id = 1";

            // Act
            var result = await DbAccess.ExecuteScalarAsync<int?>(sql);

            // Assert
            Assert.True(result.HasValue);
            Assert.Equal(25, result.Value);
        }

        [Fact]
        public async Task ExecuteScalarAsyncT_查询无结果_返回默认值()
        {
            // Arrange
            const string sql = "SELECT Age FROM TestUsers WHERE Id = -1";

            // Act
            var result = await DbAccess.ExecuteScalarAsync<int?>(sql);

            // Assert
            Assert.Null(result);
        }

        #endregion
    }
}
