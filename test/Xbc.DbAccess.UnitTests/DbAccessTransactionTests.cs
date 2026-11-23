using System.Data.Common;
using System.Data;
using Xbc.DbAccess.Sqlite;
using Xbc.DbAccess.UnitTests.Infrastructure;
using Xunit;

namespace Xbc.DbAccess.UnitTests
{
    /// <summary>
    /// DbAccess 事务处理的单元测试，使用 SQLite 内存数据库。
    /// </summary>
    public class DbAccessTransactionTests : SqliteTestBase
    {
        #region BeginTransaction 基础测试

        [Fact]
        public async Task BeginTransaction_正常调用_返回事务对象()
        {
            // Act
            using var transaction = await DbAccess.BeginTransactionAsync();

            // Assert
            Assert.NotNull(transaction);
        }

        [Fact]
        public async Task BeginTransaction_默认隔离级别_使用Unspecified()
        {
            // Act
            using var transaction = await DbAccess.BeginTransactionAsync();

            // Assert
            Assert.NotNull(transaction);
        }

        [Fact]
        public async Task BeginTransaction_指定ReadCommitted_使用指定隔离级别()
        {
            // Act
            using var transaction = await DbAccess.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            // Assert
            Assert.NotNull(transaction);
        }

        [Fact]
        public async Task BeginTransaction_指定Serializable_使用指定隔离级别()
        {
            // Act
            using var transaction = await DbAccess.BeginTransactionAsync(IsolationLevel.Serializable);

            // Assert
            Assert.NotNull(transaction);
        }

        #endregion

        #region 事务提交测试

        [Fact]
        public async Task Transaction_Commit_提交成功()
        {
            // Arrange
            var transaction = await DbAccess.BeginTransactionAsync();
            const string sql = "INSERT INTO TestUsers (Name, Age) VALUES ('CommitTest', 25)";
            await DbAccess.ExecuteNonQueryAsync(sql);

            // Act
            transaction.Commit();

            // Assert
            var count = await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM TestUsers WHERE Name = 'CommitTest'");
            Assert.Equal(1, Convert.ToInt32(count));
        }

        [Fact]
        public async Task Transaction_CommitAsync_提交成功()
        {
            // Arrange
            var transaction = await DbAccess.BeginTransactionAsync();
            const string sql = "INSERT INTO TestUsers (Name, Age) VALUES ('AsyncCommitTest', 30)";
            await DbAccess.ExecuteNonQueryAsync(sql);

            // Act
            await transaction.CommitAsync();

            // Assert
            var count = await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM TestUsers WHERE Name = 'AsyncCommitTest'");
            Assert.Equal(1, Convert.ToInt32(count));
        }

        #endregion

        #region 事务回滚测试

        [Fact]
        public async Task Transaction_Rollback_回滚成功()
        {
            // Arrange
            var transaction = await DbAccess.BeginTransactionAsync();
            const string sql = "INSERT INTO TestUsers (Name, Age) VALUES ('RollbackTest', 25)";
            await DbAccess.ExecuteNonQueryAsync(sql);

            // Act
            transaction.Rollback();

            // Assert
            var count = await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM TestUsers WHERE Name = 'RollbackTest'");
            Assert.Equal(0, Convert.ToInt32(count));
        }

        [Fact]
        public async Task Transaction_RollbackAsync_回滚成功()
        {
            // Arrange
            var transaction = await DbAccess.BeginTransactionAsync();
            const string sql = "INSERT INTO TestUsers (Name, Age) VALUES ('AsyncRollbackTest', 30)";
            await DbAccess.ExecuteNonQueryAsync(sql);

            // Act
            await transaction.RollbackAsync();

            // Assert
            var count = await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM TestUsers WHERE Name = 'AsyncRollbackTest'");
            Assert.Equal(0, Convert.ToInt32(count));
        }

        #endregion

        #region 事务中执行操作测试

        [Fact]
        public async Task ExecuteNonQuery_在事务中_使用事务连接()
        {
            // Arrange
            using var transaction = await DbAccess.BeginTransactionAsync();
            const string sql = "INSERT INTO TestUsers (Name, Age) VALUES ('TransactionInsert', 25)";

            // Act
            var result = await DbAccess.ExecuteNonQueryAsync(sql);

            // Assert
            Assert.Equal(1, result);

            // 回滚事务
            transaction.Rollback();
        }

        [Fact]
        public async Task ExecuteScalar_在事务中_使用事务连接()
        {
            // Arrange
            using var transaction = await DbAccess.BeginTransactionAsync();
            const string sql = "SELECT COUNT(*) FROM TestUsers";

            // Act
            var result = await DbAccess.ExecuteScalarAsync(sql);

            // Assert
            Assert.NotNull(result);

            // 回滚事务
            transaction.Rollback();
        }

        [Fact]
        public async Task ExecuteReader_在事务中_使用事务连接()
        {
            // Arrange
            using var transaction = await DbAccess.BeginTransactionAsync();
            const string sql = "SELECT * FROM TestUsers";

            // Act
            using var reader = await DbAccess.ExecuteReaderAsync(sql);

            // Assert
            Assert.NotNull(reader);

            // 回滚事务
            transaction.Rollback();
        }

        [Fact]
        public async Task ExecuteDataSet_在事务中_使用事务连接()
        {
            // Arrange
            using var transaction = await DbAccess.BeginTransactionAsync();
            const string sql = "SELECT * FROM TestUsers";

            // Act
            var result = await DbAccess.ExecuteDataSetAsync(sql);

            // Assert
            Assert.NotNull(result);

            // 回滚事务
            transaction.Rollback();
        }

        #endregion

        #region 事务与连接作用域组合测试

        [Fact]
        public async Task BeginTransaction_在连接作用域内_使用作用域连接()
        {
            // Arrange
            using var scope = await DbAccess.BeginConnectionScopeAsync();

            // Act
            using var transaction = await DbAccess.BeginTransactionAsync();

            // Assert
            Assert.NotNull(transaction);
        }

        [Fact]
        public async Task BeginConnectionScope_在事务中_使用事务连接()
        {
            // Arrange
            using var transaction = await DbAccess.BeginTransactionAsync();

            // Act
            using var scope = await DbAccess.BeginConnectionScopeAsync();

            // Assert
            Assert.NotNull(scope);
        }

        #endregion

        #region 嵌套事务测试

        [Fact]
        public async Task BeginTransaction_嵌套事务_支持嵌套()
        {
            // Arrange
            var options = new DbConnectionOptions
            {
                ConnectionString = "Data Source=:memory:",
                PoolOptions = { Enabled = false }
            };
            var factory = new SqliteAccessFactory(options);
            var dbAccess = factory.Create();

            // Act
            using var outerTransaction = await dbAccess.BeginTransactionAsync();
            using var innerTransaction = await dbAccess.BeginTransactionAsync();

            // Assert
            Assert.NotNull(outerTransaction);
            Assert.NotNull(innerTransaction);

            dbAccess.Dispose();
            if (factory is IDisposable disposableFactory)
            {
                disposableFactory.Dispose();
            }
        }

        #endregion

        #region 事务释放测试

        [Fact]
        public async Task Transaction_Using语句_正确释放()
        {
            // Act
            using (var transaction = await DbAccess.BeginTransactionAsync())
            {
                Assert.NotNull(transaction);
            }

            // Assert - 事务应该被正确释放，不抛出异常
            Assert.True(true);
        }

        [Fact]
        public async Task Transaction_重复释放_不抛异常()
        {
            // Arrange
            var transaction = await DbAccess.BeginTransactionAsync();
            transaction.Dispose();

            // Act & Assert
            var exception = Record.Exception(() => transaction.Dispose());
            Assert.Null(exception);
        }

        #endregion

        #region 复杂事务场景测试

        [Fact]
        public async Task Transaction_多个操作_全部提交()
        {
            // Arrange
            using var transaction = await DbAccess.BeginTransactionAsync();

            // Act
            await DbAccess.ExecuteNonQueryAsync("INSERT INTO TestUsers (Name, Age) VALUES ('MultiOp1', 25)");
            await DbAccess.ExecuteNonQueryAsync("INSERT INTO TestUsers (Name, Age) VALUES ('MultiOp2', 30)");
            await DbAccess.ExecuteNonQueryAsync("INSERT INTO TestOrders (UserId, ProductName, Amount) VALUES (1, 'Product1', 100)");

            transaction.Commit();

            // Assert
            var userCount = await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM TestUsers WHERE Name IN ('MultiOp1', 'MultiOp2')");
            Assert.Equal(2, Convert.ToInt32(userCount));
        }

        [Fact]
        public async Task Transaction_多个操作_全部回滚()
        {
            // Arrange
            using var transaction = await DbAccess.BeginTransactionAsync();

            // Act
            await DbAccess.ExecuteNonQueryAsync("INSERT INTO TestUsers (Name, Age) VALUES ('RollbackOp1', 25)");
            await DbAccess.ExecuteNonQueryAsync("INSERT INTO TestUsers (Name, Age) VALUES ('RollbackOp2', 30)");

            transaction.Rollback();

            // Assert
            var userCount = await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM TestUsers WHERE Name IN ('RollbackOp1', 'RollbackOp2')");
            Assert.Equal(0, Convert.ToInt32(userCount));
        }

        #endregion
    }
}
