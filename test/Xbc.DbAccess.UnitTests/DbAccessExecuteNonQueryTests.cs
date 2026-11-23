using System.Data;
using System.Data.Common;
using Xbc.DbAccess.UnitTests.Infrastructure;
using Xunit;

namespace Xbc.DbAccess.UnitTests
{
    /// <summary>
    /// DbAccess.ExecuteNonQuery 的单元测试，使用 SQLite 内存数据库。
    /// </summary>
    public class DbAccessExecuteNonQueryTests : SqliteTestBase
    {
        #region ExecuteNonQuery 同步方法测试

        [Fact]
        public async Task ExecuteNonQuery_有效SQL_执行成功()
        {
            // Arrange
            const string sql = "INSERT INTO TestUsers (Name, Age) VALUES ('TestUser', 25)";

            // Act
            var result = await DbAccess.ExecuteNonQueryAsync(sql);

            // Assert
            Assert.Equal(1, result);
        }

        [Fact]
        public async Task ExecuteNonQuery_带参数_参数正确设置()
        {
            // Arrange
            const string sql = "INSERT INTO TestUsers (Name, Age) VALUES (@Name, @Age)";
            var parameters = new[]
            {
                CreateParameter("@Name", "TestUser"),
                CreateParameter("@Age", 25)
            };

            // Act
            var result = await DbAccess.ExecuteNonQueryAsync(sql, parameters);

            // Assert
            Assert.Equal(1, result);
        }

        [Fact]
        public async Task ExecuteNonQuery_SQL为空_抛出ArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => DbAccess.ExecuteNonQueryAsync(null!));
        }

        [Fact]
        public async Task ExecuteNonQuery_SQL为空字符串_抛出ArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => DbAccess.ExecuteNonQueryAsync(string.Empty));
        }

        [Fact]
        public async Task ExecuteNonQuery_多行受影响_返回正确行数()
        {
            // Arrange
            const string sql = "UPDATE TestUsers SET Age = Age + 1 WHERE Age > 0";

            // Act
            var result = await DbAccess.ExecuteNonQueryAsync(sql);

            // Assert
            Assert.True(result >= 3); // 初始数据有3个用户
        }

        [Fact]
        public async Task ExecuteNonQuery_零行受影响_返回零()
        {
            // Arrange
            const string sql = "UPDATE TestUsers SET Age = 100 WHERE Id = -1";

            // Act
            var result = await DbAccess.ExecuteNonQueryAsync(sql);

            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public async Task ExecuteNonQuery_执行异常_抛出异常()
        {
            // Arrange
            const string sql = "INVALID SQL SYNTAX";

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() => DbAccess.ExecuteNonQueryAsync(sql));
        }

        #endregion

        #region ExecuteNonQueryAsync 异步方法测试

        [Fact]
        public async Task ExecuteNonQueryAsync_有效SQL_执行成功()
        {
            // Arrange
            const string sql = "INSERT INTO TestUsers (Name, Age) VALUES ('AsyncTestUser', 30)";

            // Act
            var result = await DbAccess.ExecuteNonQueryAsync(sql);

            // Assert
            Assert.Equal(1, result);
        }

        [Fact]
        public async Task ExecuteNonQueryAsync_带参数_参数正确设置()
        {
            // Arrange
            const string sql = "UPDATE TestUsers SET Name = @Name WHERE Id = @Id";
            var parameters = new[]
            {
                CreateParameter("@Name", "UpdatedName"),
                CreateParameter("@Id", 1)
            };

            // Act
            var result = await DbAccess.ExecuteNonQueryAsync(sql, parameters, default);

            // Assert
            Assert.Equal(1, result);
        }

        [Fact]
        public async Task ExecuteNonQueryAsync_取消操作_抛出OperationCanceledException()
        {
            // Arrange
            const string sql = "INSERT INTO TestUsers (Name, Age) VALUES ('Test', 25)";
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            // TaskCanceledException 继承自 OperationCanceledException，所以使用 ThrowsAny
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await DbAccess.ExecuteNonQueryAsync(sql, null, cts.Token));
        }

        [Fact]
        public async Task ExecuteNonQueryAsync_SQL为空_抛出ArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => DbAccess.ExecuteNonQueryAsync(null!));
        }

        #endregion

        #region 事务中的 ExecuteNonQuery 测试

        [Fact]
        public async Task ExecuteNonQuery_在事务中_使用事务连接()
        {
            // Arrange
            const string sql = "INSERT INTO TestUsers (Name, Age) VALUES ('TransactionTest', 25)";
            using var transaction = await DbAccess.BeginTransactionAsync();

            // Act
            var result = await DbAccess.ExecuteNonQueryAsync(sql);

            // Assert
            Assert.Equal(1, result);

            // 回滚事务
            transaction.Rollback();

            // 验证数据未提交
            var count = await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM TestUsers WHERE Name = 'TransactionTest'");
            Assert.Equal(0, Convert.ToInt32(count));
        }

        [Fact]
        public async Task ExecuteNonQueryAsync_在事务中_使用事务连接()
        {
            // Arrange
            const string sql = "INSERT INTO TestUsers (Name, Age) VALUES ('AsyncTransactionTest', 30)";
            using var transaction = await DbAccess.BeginTransactionAsync();

            // Act
            var result = await DbAccess.ExecuteNonQueryAsync(sql);

            // Assert
            Assert.Equal(1, result);

            // 回滚事务
            transaction.Rollback();

            // 验证数据未提交
            var count = await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM TestUsers WHERE Name = 'AsyncTransactionTest'");
            Assert.Equal(0, Convert.ToInt32(count));
        }

        [Fact]
        public async Task ExecuteNonQuery_事务提交后_数据持久化()
        {
            // Arrange
            const string sql = "INSERT INTO TestUsers (Name, Age) VALUES ('CommittedUser', 35)";
            using (var transaction = await DbAccess.BeginTransactionAsync())
            {
                // Act
                await DbAccess.ExecuteNonQueryAsync(sql);
                transaction.Commit();
            }

            // Assert
            var count = await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM TestUsers WHERE Name = 'CommittedUser'");
            Assert.Equal(1, Convert.ToInt32(count));
        }

        #endregion

        #region 复杂参数测试

        [Fact]
        public async Task ExecuteNonQuery_多种参数类型_正确处理()
        {
            // Arrange
            const string sql = @"
                INSERT INTO TestUsers (Name, Age, Email, CreatedAt) 
                VALUES (@Name, @Age, @Email, @CreatedAt)";
            var parameters = new[]
            {
                CreateParameter("@Name", "ComplexUser"),
                CreateParameter("@Age", 28),
                CreateParameter("@Email", "complex@example.com"),
                CreateParameter("@CreatedAt", DateTime.Now)
            };

            // Act
            var result = await DbAccess.ExecuteNonQueryAsync(sql, parameters);

            // Assert
            Assert.Equal(1, result);
        }

        [Fact]
        public async Task ExecuteNonQuery_空参数数组_执行成功()
        {
            // Arrange
            const string sql = "DELETE FROM TestOrders WHERE Id = -1";
            var parameters = Array.Empty<DbParameter>();

            // Act
            var result = await DbAccess.ExecuteNonQueryAsync(sql, parameters);

            // Assert
            Assert.Equal(0, result);
        }

        #endregion
    }
}
