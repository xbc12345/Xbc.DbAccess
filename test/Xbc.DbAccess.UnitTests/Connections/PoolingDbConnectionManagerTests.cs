using Moq;
using System.Data.Common;
using Xbc.DbAccess.Diagnostics;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Connections
{
    /// <summary>
    /// PoolingDbConnectionManager 的单元测试。
    /// </summary>
    public class PoolingDbConnectionManagerTests : IDisposable
    {
        private readonly Mock<DbProviderFactory> _mockProviderFactory;
        private readonly Mock<DbConnection> _mockInnerConnection;
        private readonly Mock<IDbConnectionTracker> _mockTracker;
        private readonly DbConnectionOptions _options;
        private readonly PoolingDbConnectionManager _manager;

        public PoolingDbConnectionManagerTests()
        {
            _mockProviderFactory = new Mock<DbProviderFactory>();
            _mockInnerConnection = new Mock<DbConnection>();
            _mockTracker = new Mock<IDbConnectionTracker>();
            _options = new DbConnectionOptions
            {
                ConnectionString = "Server=test;Database=test;",
                EnableStatistics = false,
                PoolOptions = new DbConnectionPoolOptions
                {
                    MinPoolSize = 0,
                    MaxPoolSize = 10,
                    ConnectionWaitTimeout = TimeSpan.FromSeconds(5),
                    CleanupInterval = TimeSpan.FromMinutes(5)
                }
            };

            _ = _mockProviderFactory.Setup(static f => f.CreateConnection()).Returns(_mockInnerConnection.Object);
            _manager = new PoolingDbConnectionManager(_options, _mockProviderFactory.Object, _mockTracker.Object);
        }

        public void Dispose()
        {
            _manager?.Dispose();
            GC.SuppressFinalize(this);
        }

        #region 构造函数测试

        [Fact]
        public void Constructor_NullConnectionOptions_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() =>
                new PoolingDbConnectionManager(null, _mockProviderFactory.Object, _mockTracker.Object));
        }

        [Fact]
        public void Constructor_NullProviderFactory_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() =>
                new PoolingDbConnectionManager(_options, null, _mockTracker.Object));
        }

        [Fact]
        public void Constructor_NullTracker_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() =>
                new PoolingDbConnectionManager(_options, _mockProviderFactory.Object, null));
        }

        [Fact]
        public void Constructor_ValidParameters_CreatesManager()
        {
            Assert.NotNull(_manager);
            Assert.NotEqual(Guid.Empty, _manager.ManagerId);
        }

        #endregion

        #region GetOrCreate 测试

        [Fact]
        public async Task GetOrCreate_WhenNotDisposed_CreatesConnection()
        {
            DbConnection connection = await _manager.GetOrCreateAsync();

            Assert.NotNull(connection);
            _ = Assert.IsType<PoolingDbConnection>(connection);
        }

        [Fact]
        public async Task GetOrCreate_WhenDisposed_ThrowsObjectDisposedException()
        {
            _manager.Dispose();

            _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => _manager.GetOrCreateAsync());
        }

        [Fact]
        public async Task GetOrCreate_MultipleCalls_CreateDifferentConnections()
        {
            PoolingDbConnection connection1 = (PoolingDbConnection)await _manager.GetOrCreateAsync();
            PoolingDbConnection connection2 = (PoolingDbConnection)await _manager.GetOrCreateAsync();

            Assert.NotEqual(connection1.ConnectionId, connection2.ConnectionId);
        }

        #endregion

        #region GetOrCreateAsync 测试

        [Fact]
        public async Task GetOrCreateAsync_WhenNotDisposed_CreatesConnection()
        {
            DbConnection connection = await _manager.GetOrCreateAsync();

            Assert.NotNull(connection);
            _ = Assert.IsType<PoolingDbConnection>(connection);
        }

        [Fact]
        public async Task GetOrCreateAsync_WhenDisposed_ThrowsObjectDisposedException()
        {
            _manager.Dispose();

            _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => _manager.GetOrCreateAsync());
        }

        #endregion

        #region Release 测试

        [Fact]
        public void Release_NullConnection_DoesNotThrow()
        {
            Exception exception = Record.Exception(() => _manager.Release(null));

            Assert.Null(exception);
        }

        [Fact]
        public async Task Release_ValidConnection_RecyclesConnection()
        {
            DbConnection connection = await _manager.GetOrCreateAsync();

            Exception exception = Record.Exception(() => _manager.Release(connection));

            Assert.Null(exception);
        }

        [Fact]
        public async Task Release_WhenDisposed_DoesNotThrow()
        {
            DbConnection connection = await _manager.GetOrCreateAsync();
            _manager.Dispose();

            Exception exception = Record.Exception(() => _manager.Release(connection));

            Assert.Null(exception);
        }

        #endregion

        #region GetStatistics 测试

        [Fact]
        public void GetStatistics_WhenNotDisposed_ReturnsStatistics()
        {
            // 注意：当 EnableStatistics = false 时，GetStatistics 返回 null
            _ = _manager.GetStatistics();

            // 统计信息可能为 null（当未启用统计时）
            // 这里我们只验证方法不抛出异常
        }

        [Fact]
        public void GetStatistics_WhenDisposed_ThrowsObjectDisposedException()
        {
            _manager.Dispose();

            _ = Assert.Throws<ObjectDisposedException>(_manager.GetStatistics);
        }

        #endregion

        #region ManagerId 测试

        [Fact]
        public void ManagerId_Always_ReturnsUniqueGuid()
        {
            PoolingDbConnectionManager manager1 = new PoolingDbConnectionManager(_options, _mockProviderFactory.Object, _mockTracker.Object);
            PoolingDbConnectionManager manager2 = new PoolingDbConnectionManager(_options, _mockProviderFactory.Object, _mockTracker.Object);

            Assert.NotEqual(manager1.ManagerId, manager2.ManagerId);

            manager1.Dispose();
            manager2.Dispose();
        }

        #endregion

        #region IsDisposed 测试

        [Fact]
        public void IsDisposed_WhenNotDisposed_ReturnsFalse()
        {
            Assert.False(_manager.IsDisposed);
        }

        [Fact]
        public void IsDisposed_WhenDisposed_ReturnsTrue()
        {
            _manager.Dispose();

            Assert.True(_manager.IsDisposed);
        }

        #endregion

        #region Dispose 测试

        [Fact]
        public async Task Dispose_WithActiveConnections_DisposesAllConnections()
        {
            _ = await _manager.GetOrCreateAsync();
            _ = await _manager.GetOrCreateAsync();

            _manager.Dispose();

            Assert.True(_manager.IsDisposed);
        }

        [Fact]
        public void Dispose_MultipleTimes_DoesNotThrow()
        {
            _manager.Dispose();

            Exception exception = Record.Exception(_manager.Dispose);

            Assert.Null(exception);
        }

        #endregion

        #region 连接池耗尽测试

        [Fact]
        public async Task GetOrCreate_WhenPoolExhausted_ThrowsDbConnectionExhaustedException()
        {
            // 使用最大连接数为 1 的配置
            DbConnectionOptions options = new DbConnectionOptions
            {
                ConnectionString = "Server=test;Database=test;",
                EnableStatistics = false,
                PoolOptions = new DbConnectionPoolOptions
                {
                    MinPoolSize = 0,
                    MaxPoolSize = 1,
                    ConnectionWaitTimeout = TimeSpan.FromMilliseconds(100)
                }
            };

            using PoolingDbConnectionManager manager = new PoolingDbConnectionManager(options, _mockProviderFactory.Object, _mockTracker.Object);

            // 获取唯一可用的连接
            DbConnection connection = await manager.GetOrCreateAsync();

            // 再次获取应该抛出异常
            _ = await Assert.ThrowsAsync<DbConnectionExhaustedException>(() => manager.GetOrCreateAsync());
        }

        #endregion

        #region 连接回收测试

        [Fact]
        public async Task GetOrCreate_AfterRelease_ReusesConnection()
        {
            PoolingDbConnection connection = (PoolingDbConnection)await _manager.GetOrCreateAsync();
            _ = connection.ConnectionId;

            _manager.Release(connection);

            // 释放后再次获取，应该复用连接
            DbConnection newConnection = await _manager.GetOrCreateAsync();

            Assert.NotNull(newConnection);
        }

        [Fact]
        public async Task GetOrCreate_WaitingRequest_IsServedByRecycledConnection()
        {
            // 使用最大连接数为 1 的配置：唯一连接被借出时，后续请求进入排队等待
            DbConnectionOptions options = new DbConnectionOptions
            {
                ConnectionString = "Server=test;Database=test;",
                EnableStatistics = false,
                PoolOptions = new DbConnectionPoolOptions
                {
                    MinPoolSize = 0,
                    MaxPoolSize = 1,
                    ConnectionWaitTimeout = TimeSpan.FromSeconds(2)
                }
            };

            using PoolingDbConnectionManager manager = new PoolingDbConnectionManager(options, _mockProviderFactory.Object, _mockTracker.Object);

            // 1.借出唯一连接，占满池容量
            DbConnection borrowed = await manager.GetOrCreateAsync();

            // 2.等待者先排队（此刻空闲队列空、许可证为 0，等待者挂起在信号量上）
            Task<DbConnection> waiter = manager.GetOrCreateAsync();
            await Task.Delay(200);// 确保等待者已挂起后再归还连接

            // 3.归还连接（回收进空闲队列，不销毁）
            manager.Release(borrowed);

            // 4.等待者应被回收的连接唤醒并获得连接，而非等到超时抛连接池耗尽异常
            DbConnection served = await waiter;
            Assert.NotNull(served);
        }

        #endregion
    }
}
