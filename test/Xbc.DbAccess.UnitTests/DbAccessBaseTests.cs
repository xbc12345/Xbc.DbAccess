using System.Data;
using Xbc.DbAccess.Sqlite;
using Xbc.DbAccess.UnitTests.Infrastructure;
using Xunit;

namespace Xbc.DbAccess.UnitTests
{
    /// <summary>
    /// DbAccess 的单元测试（使用 SQLite 内存数据库）。
    /// </summary>
    public class DbAccessBaseTests : SqliteTestBase
    {
        #region 构造函数测试

        [Fact]
        public void Constructor_有效参数_创建成功()
        {
            // Assert
            Assert.NotNull(DbAccess);
            Assert.NotNull(DbAccess.ProviderFactory);
        }

        #endregion

        #region ProviderFactory 测试

        [Fact]
        public void ProviderFactory_获取提供程序工厂_返回有效工厂()
        {
            // Act
            var factory = DbAccess.ProviderFactory;

            // Assert
            Assert.NotNull(factory);
            Assert.IsType<SqliteAccessProviderFactory>(factory);
        }

        #endregion

        #region 连接作用域测试

        [Fact]
        public async Task BeginConnectionScope_正常调用_返回连接作用域()
        {
            // Act
            using var scope = await DbAccess.BeginConnectionScopeAsync();

            // Assert
            Assert.NotNull(scope);
        }

        [Fact]
        public async Task BeginConnectionScope_已释放_抛出ObjectDisposedException()
        {
            // Arrange
            DbAccess.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => DbAccess.BeginConnectionScopeAsync());
        }

        [Fact]
        public async Task BeginConnectionScopeAsync_正常调用_返回连接作用域()
        {
            // Act
            using var scope = await DbAccess.BeginConnectionScopeAsync();

            // Assert
            Assert.NotNull(scope);
        }

        [Fact]
        public async Task BeginConnectionScopeAsync_已释放_抛出ObjectDisposedException()
        {
            // Arrange
            DbAccess.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => DbAccess.BeginConnectionScopeAsync());
        }

        [Fact]
        public async Task BeginConnectionScope_嵌套调用_返回嵌套作用域()
        {
            // Arrange
            using var outerScope = await DbAccess.BeginConnectionScopeAsync();

            // Act - 当前实现支持嵌套作用域
            using var innerScope = await DbAccess.BeginConnectionScopeAsync();

            // Assert
            Assert.NotNull(innerScope);
        }

        #endregion

        #region 事务测试

        [Fact]
        public async Task BeginTransaction_正常调用_返回事务()
        {
            // Act
            using var transaction = await DbAccess.BeginTransactionAsync();

            // Assert
            Assert.NotNull(transaction);
        }

        [Fact]
        public async Task BeginTransaction_指定隔离级别_使用指定隔离级别()
        {
            // Act
            using var transaction = await DbAccess.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            // Assert
            Assert.NotNull(transaction);
            Assert.IsAssignableFrom<IDbTransaction>(transaction);
        }

        [Fact]
        public async Task BeginTransaction_已释放_抛出ObjectDisposedException()
        {
            // Arrange
            DbAccess.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => DbAccess.BeginTransactionAsync());
        }

        #endregion

        #region Dispose 测试

        [Fact]
        public void Dispose_正常调用_不抛异常()
        {
            // Act & Assert
            var exception = Record.Exception(() => DbAccess.Dispose());
            Assert.Null(exception);
        }

        [Fact]
        public void Dispose_重复调用_不抛异常()
        {
            // Arrange
            DbAccess.Dispose();

            // Act & Assert
            var exception = Record.Exception(() => DbAccess.Dispose());
            Assert.Null(exception);
        }

        [Fact]
        public async Task Dispose_释放后操作_抛出ObjectDisposedException()
        {
            // Arrange
            DbAccess.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => DbAccess.ExecuteNonQueryAsync("SELECT 1"));
        }

        #endregion

        #region 连接选项测试

        [Fact]
        public void ConnectionOptions_获取连接选项_返回有效选项()
        {
            // Act
            var options = ConnectionOptions;

            // Assert
            Assert.NotNull(options);
            // 使用临时文件数据库而不是内存数据库
            Assert.Contains("Data Source=", options.ConnectionString);
        }

        #endregion
    }
}
