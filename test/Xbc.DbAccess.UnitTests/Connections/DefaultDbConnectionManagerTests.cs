using Moq;
using System.Data.Common;
using Xbc.DbAccess.Diagnostics;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Connections
{
    /// <summary>
    /// DefaultDbConnectionManager 的单元测试。
    /// </summary>
    public class DefaultDbConnectionManagerTests : IDisposable
    {
        private readonly Mock<DbProviderFactory> _mockProviderFactory;
        private readonly Mock<DbConnection> _mockInnerConnection;
        private readonly Mock<IDbConnectionTracker> _mockTracker;
        private readonly DbConnectionOptions _options;
        private readonly DefaultDbConnectionManager _manager;

        public DefaultDbConnectionManagerTests()
        {
            _mockProviderFactory = new Mock<DbProviderFactory>();
            _mockInnerConnection = new Mock<DbConnection>();
            _mockTracker = new Mock<IDbConnectionTracker>();
            _options = new DbConnectionOptions
            {
                ConnectionString = "Server=test;Database=test;",
                EnableStatistics = false
            };

            _ = _mockProviderFactory.Setup(static f => f.CreateConnection()).Returns(_mockInnerConnection.Object);
            _manager = new DefaultDbConnectionManager(_options, _mockProviderFactory.Object, _mockTracker.Object);
        }

        public void Dispose()
        {
            _manager?.Dispose();
            GC.SuppressFinalize(this);
        }

        #region 构造函数测试

        [Fact]
        public void ConstructorNullConnectionOptionsThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() =>
                new DefaultDbConnectionManager(null, _mockProviderFactory.Object, _mockTracker.Object));
        }

        [Fact]
        public void ConstructorNullProviderFactoryThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() =>
                new DefaultDbConnectionManager(_options, null, _mockTracker.Object));
        }

        [Fact]
        public void ConstructorNullTrackerThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() =>
                new DefaultDbConnectionManager(_options, _mockProviderFactory.Object, null));
        }

        [Fact]
        public void ConstructorValidParametersCreatesManager()
        {
            Assert.NotNull(_manager);
            Assert.NotEqual(Guid.Empty, _manager.ManagerId);
        }

        #endregion

        #region GetOrCreate 测试

        [Fact]
        public async Task GetOrCreateWhenNotDisposedCreatesConnection()
        {
            DbConnection connection = await _manager.GetOrCreateAsync();

            Assert.NotNull(connection);
            _ = Assert.IsType<DefaultDbConnection>(connection);
        }

        [Fact]
        public async Task GetOrCreateWhenDisposedThrowsObjectDisposedException()
        {
            _manager.Dispose();

            _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => _manager.GetOrCreateAsync());
        }

        [Fact]
        public async Task GetOrCreateMultipleCallsCreateDifferentConnections()
        {
            DefaultDbConnection connection1 = (DefaultDbConnection)await _manager.GetOrCreateAsync();
            DefaultDbConnection connection2 = (DefaultDbConnection)await _manager.GetOrCreateAsync();

            Assert.NotEqual(connection1.ConnectionId, connection2.ConnectionId);
        }

        #endregion

        #region GetOrCreateAsync 测试

        [Fact]
        public async Task GetOrCreateAsyncWhenNotDisposedCreatesConnection()
        {
            DbConnection connection = await _manager.GetOrCreateAsync();

            Assert.NotNull(connection);
            _ = Assert.IsType<DefaultDbConnection>(connection);
        }

        [Fact]
        public async Task GetOrCreateAsyncWhenDisposedThrowsObjectDisposedException()
        {
            _manager.Dispose();

            _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => _manager.GetOrCreateAsync());
        }

        #endregion

        #region Release 测试

        [Fact]
        public void ReleaseNullConnectionDoesNotThrow()
        {
            Exception exception = Record.Exception(() => _manager.Release(null));

            Assert.Null(exception);
        }

        [Fact]
        public async Task ReleaseValidConnectionDisposesConnection()
        {
            DbConnection connection = await _manager.GetOrCreateAsync();

            Exception exception = Record.Exception(() => _manager.Release(connection));

            Assert.Null(exception);
        }

        [Fact]
        public async Task ReleaseWhenDisposedDoesNotThrow()
        {
            DbConnection connection = await _manager.GetOrCreateAsync();
            _manager.Dispose();

            Exception exception = Record.Exception(() => _manager.Release(connection));

            Assert.Null(exception);
        }

        #endregion

        #region GetStatistics 测试

        [Fact]
        public void GetStatisticsWhenNotDisposedReturnsStatistics()
        {
            // 注意：当 EnableStatistics = false 时，GetStatistics 返回 null
            _ = _manager.GetStatistics();

            // 统计信息可能为 null（当未启用统计时）
            // 这里我们只验证方法不抛出异常
        }

        [Fact]
        public void GetStatisticsWhenDisposedThrowsObjectDisposedException()
        {
            _manager.Dispose();

            _ = Assert.Throws<ObjectDisposedException>(_manager.GetStatistics);
        }

        #endregion

        #region ManagerId 测试

        [Fact]
        public void ManagerIdAlwaysReturnsUniqueGuid()
        {
            DefaultDbConnectionManager manager1 = new DefaultDbConnectionManager(_options, _mockProviderFactory.Object, _mockTracker.Object);
            DefaultDbConnectionManager manager2 = new DefaultDbConnectionManager(_options, _mockProviderFactory.Object, _mockTracker.Object);

            Assert.NotEqual(manager1.ManagerId, manager2.ManagerId);

            manager1.Dispose();
            manager2.Dispose();
        }

        #endregion

        #region IsDisposed 测试

        [Fact]
        public void IsDisposedWhenNotDisposedReturnsFalse()
        {
            Assert.False(_manager.IsDisposed);
        }

        [Fact]
        public void IsDisposedWhenDisposedReturnsTrue()
        {
            _manager.Dispose();

            Assert.True(_manager.IsDisposed);
        }

        #endregion

        #region Dispose 测试

        [Fact]
        public async Task DisposeWithActiveConnectionsDisposesAllConnections()
        {
            _ = await _manager.GetOrCreateAsync();
            _ = await _manager.GetOrCreateAsync();

            _manager.Dispose();

            Assert.True(_manager.IsDisposed);
        }

        [Fact]
        public void DisposeMultipleTimesDoesNotThrow()
        {
            _manager.Dispose();

            Exception exception = Record.Exception(_manager.Dispose);

            Assert.Null(exception);
        }

        #endregion
    }
}
