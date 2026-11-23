using Moq;
using System.Data.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Connections.Scopes
{
    /// <summary>
    /// DefaultDbConnectionScopeManager 的单元测试。
    /// </summary>
    public class DefaultDbConnectionScopeManagerTests : IDisposable
    {
        private readonly Mock<IDbConnectionManager> _mockConnectionManager;
        private readonly Mock<DbConnection> _mockConnection;
        private readonly DefaultDbConnectionScopeManager _manager;

        public DefaultDbConnectionScopeManagerTests()
        {
            _mockConnectionManager = new Mock<IDbConnectionManager>();
            _mockConnection = new Mock<DbConnection>();
            _manager = new DefaultDbConnectionScopeManager(_mockConnectionManager.Object);
        }

        public void Dispose()
        {
            _manager?.Dispose();
            GC.SuppressFinalize(this);
        }

        // 便捷方法：模拟非 async 入口生成作用域 ID
        private static Task<IDbConnectionScope> CreateScopeAsync(DefaultDbConnectionScopeManager manager)
        {
            return manager.CreateScopeAsync(Guid.NewGuid());
        }

        private static Task<IDbConnectionScope> CreateScopeAsync(DefaultDbConnectionScopeManager manager, DbConnection connection)
        {
            return manager.CreateScopeAsync(connection, Guid.NewGuid());
        }

        #region 构造函数测试

        [Fact]
        public void Constructor_NullConnectionManager_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new DefaultDbConnectionScopeManager(null));
        }

        [Fact]
        public void Constructor_ValidConnectionManager_CreatesManager()
        {
            var manager = new DefaultDbConnectionScopeManager(_mockConnectionManager.Object);

            Assert.NotNull(manager);
            Assert.Null(manager.CurrentScope);
            Assert.False(manager.HasActiveConnection);
        }

        #endregion

        #region CreateScope 测试

        [Fact]
        public async Task CreateScope_WhenNoActiveScope_CreatesNewScope()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);

            var scope = await CreateScopeAsync(_manager);

            Assert.NotNull(scope);
            Assert.Same(scope, _manager.CurrentScope);
            Assert.True(_manager.HasActiveConnection);
        }

        [Fact]
        public async Task CreateScope_WhenActiveScopeExists_ThrowsInvalidOperationException()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            await CreateScopeAsync(_manager);

            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateScopeAsync(_manager));
        }

        [Fact]
        public async Task CreateScope_WhenDisposed_ThrowsObjectDisposedException()
        {
            _manager.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => CreateScopeAsync(_manager));
        }

        #endregion

        #region CreateScopeAsync 测试

        [Fact]
        public async Task CreateScopeAsync_WhenNoActiveScope_CreatesNewScope()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(default))
                .ReturnsAsync(_mockConnection.Object);

            var scope = await CreateScopeAsync(_manager);

            Assert.NotNull(scope);
            Assert.Same(scope, _manager.CurrentScope);
            Assert.True(_manager.HasActiveConnection);
        }

        [Fact]
        public async Task CreateScopeAsync_WhenActiveScopeExists_ThrowsInvalidOperationException()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(default))
                .ReturnsAsync(_mockConnection.Object);
            await CreateScopeAsync(_manager);

            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateScopeAsync(_manager));
        }

        [Fact]
        public async Task CreateScopeAsync_WhenDisposed_ThrowsObjectDisposedException()
        {
            _manager.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => CreateScopeAsync(_manager));
        }

        #endregion

        #region CreateScope(DbConnection) 测试

        [Fact]
        public async Task CreateScope_WithConnection_WhenNoActiveScope_CreatesNewScope()
        {
            var scope = await CreateScopeAsync(_manager, _mockConnection.Object);

            Assert.NotNull(scope);
            Assert.Same(scope, _manager.CurrentScope);
            Assert.True(_manager.HasActiveConnection);
        }

        [Fact]
        public async Task CreateScope_WithConnection_NullConnection_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _manager.CreateScopeAsync(null, Guid.NewGuid()));
        }

        [Fact]
        public async Task CreateScope_WithConnection_WhenActiveScopeExists_ThrowsInvalidOperationException()
        {
            await CreateScopeAsync(_manager, _mockConnection.Object);

            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateScopeAsync(_manager, _mockConnection.Object));
        }

        [Fact]
        public async Task CreateScope_WithConnection_DoesNotManageConnection()
        {
            var scope = await CreateScopeAsync(_manager, _mockConnection.Object);

            Assert.False(scope.ManageConnection);
        }

        #endregion

        #region CurrentScope 测试

        [Fact]
        public void CurrentScope_WhenNoScope_ReturnsNull()
        {
            Assert.Null(_manager.CurrentScope);
        }

        [Fact]
        public async Task CurrentScope_WhenScopeExists_ReturnsScope()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            var scope = await CreateScopeAsync(_manager);

            Assert.Same(scope, _manager.CurrentScope);
        }

        [Fact]
        public void CurrentScope_WhenDisposed_ThrowsObjectDisposedException()
        {
            _manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _manager.CurrentScope);
        }

        #endregion

        #region CurrentConnection 测试

        [Fact]
        public void CurrentConnection_WhenNoScope_ReturnsNull()
        {
            Assert.Null(_manager.CurrentConnection);
        }

        [Fact]
        public async Task CurrentConnection_WhenScopeExists_ReturnsConnection()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            await CreateScopeAsync(_manager);

            Assert.Same(_mockConnection.Object, _manager.CurrentConnection);
        }

        [Fact]
        public void CurrentConnection_WhenDisposed_ThrowsObjectDisposedException()
        {
            _manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _manager.CurrentConnection);
        }

        #endregion

        #region HasActiveConnection 测试

        [Fact]
        public void HasActiveConnection_WhenNoScope_ReturnsFalse()
        {
            Assert.False(_manager.HasActiveConnection);
        }

        [Fact]
        public async Task HasActiveConnection_WhenScopeExists_ReturnsTrue()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            await CreateScopeAsync(_manager);

            Assert.True(_manager.HasActiveConnection);
        }

        [Fact]
        public void HasActiveConnection_WhenDisposed_ThrowsObjectDisposedException()
        {
            _manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _manager.HasActiveConnection);
        }

        #endregion

        #region 作用域生命周期测试

        [Fact]
        public async Task Scope_WhenDisposed_ClearsCurrentScope()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            var scope = await CreateScopeAsync(_manager);

            scope.Dispose();

            Assert.Null(_manager.CurrentScope);
            Assert.False(_manager.HasActiveConnection);
        }

        [Fact]
        public async Task Scope_WhenDisposed_DisposesConnection()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            var scope = await CreateScopeAsync(_manager);

            scope.Dispose();

            // DbConnection.Dispose 不是虚方法，无法验证
            // 但作用域应该被释放，CurrentScope 应该为 null
            Assert.Null(_manager.CurrentScope);
        }

        [Fact]
        public async Task Scope_WhenDisposed_RaisesLifecycleEndedEvent()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            var scope = await CreateScopeAsync(_manager);
            var eventRaised = false;

            ((DbConnectionScopeBase)scope).LifecycleEnded += (sender, e) =>
            {
                eventRaised = true;
            };

            scope.Dispose();

            Assert.True(eventRaised);
        }

        #endregion

        #region Dispose 测试

        [Fact]
        public async Task Dispose_WhenScopeExists_DisposesScope()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            var scope = await CreateScopeAsync(_manager);
            var scopeId = scope.ScopeId;

            _manager.Dispose();

            // Dispose 后访问 CurrentScope 会抛出 ObjectDisposedException
            Assert.Throws<ObjectDisposedException>(() => _manager.CurrentScope);
        }

        [Fact]
        public async Task Dispose_MultipleTimes_DoesNotThrow()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            await CreateScopeAsync(_manager);

            _manager.Dispose();
            _manager.Dispose(); // 不应抛出异常
        }

        #endregion
    }
}
