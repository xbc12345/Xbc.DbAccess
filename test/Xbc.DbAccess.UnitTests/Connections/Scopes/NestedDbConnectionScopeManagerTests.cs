using Moq;
using System.Data.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Connections.Scopes
{
    /// <summary>
    /// NestedDbConnectionScopeManager 的单元测试。
    /// </summary>
    public class NestedDbConnectionScopeManagerTests : IDisposable
    {
        private readonly Mock<IDbConnectionManager> _mockConnectionManager;
        private readonly Mock<DbConnection> _mockConnection;
        private readonly NestedDbConnectionScopeManager _manager;

        public NestedDbConnectionScopeManagerTests()
        {
            _mockConnectionManager = new Mock<IDbConnectionManager>();
            _mockConnection = new Mock<DbConnection>();
            _manager = new NestedDbConnectionScopeManager(_mockConnectionManager.Object);
        }

        public void Dispose()
        {
            _manager?.Dispose();
            GC.SuppressFinalize(this);
        }

        // 便捷方法：模拟非 async 入口生成作用域 ID
        private static Task<IDbConnectionScope> CreateScopeAsync(NestedDbConnectionScopeManager manager)
        {
            return manager.CreateScopeAsync(Guid.NewGuid());
        }

        private static Task<IDbConnectionScope> CreateScopeAsync(NestedDbConnectionScopeManager manager, DbConnection connection)
        {
            return manager.CreateScopeAsync(connection, Guid.NewGuid());
        }

        #region 构造函数测试

        [Fact]
        public void Constructor_NullConnectionManager_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new NestedDbConnectionScopeManager(null));
        }

        [Fact]
        public void Constructor_ValidConnectionManager_CreatesManager()
        {
            var manager = new NestedDbConnectionScopeManager(_mockConnectionManager.Object);

            Assert.NotNull(manager);
            Assert.Null(manager.CurrentScope);
            Assert.False(manager.HasActiveConnection);
        }

        #endregion

        #region CreateScope - 根作用域测试

        [Fact]
        public async Task CreateScope_WhenNoActiveScope_CreatesRootScope()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);

            var scope = await _manager.CreateScopeAsync(Guid.NewGuid());

            Assert.NotNull(scope);
            Assert.IsType<NestedDbConnectionScope>(scope);
            Assert.Same(scope, _manager.CurrentScope);
            Assert.True(_manager.HasActiveConnection);
            Assert.True(((NestedDbConnectionScope)scope).IsRootScope);
            Assert.Equal(0, ((NestedDbConnectionScope)scope).NestedLevel);
        }

        [Fact]
        public async Task CreateScope_WhenDisposed_ThrowsObjectDisposedException()
        {
            _manager.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => _manager.CreateScopeAsync(Guid.NewGuid()));
        }

        #endregion

        #region CreateScope - 嵌套作用域测试

        [Fact]
        public async Task CreateScope_WhenHasActiveScope_CreatesChildScope()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            var rootScope = await _manager.CreateScopeAsync(Guid.NewGuid());

            var childScope = await _manager.CreateScopeAsync(Guid.NewGuid());

            Assert.NotNull(childScope);
            Assert.Same(childScope, _manager.CurrentScope);
            Assert.False(((NestedDbConnectionScope)childScope).IsRootScope);
            Assert.Equal(1, ((NestedDbConnectionScope)childScope).NestedLevel);
            Assert.Same(rootScope, ((NestedDbConnectionScope)childScope).ParentScope);
        }

        [Fact]
        public async Task CreateScope_MultipleNesting_CreatesCorrectHierarchy()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            var level0 = await _manager.CreateScopeAsync(Guid.NewGuid());
            var level1 = await _manager.CreateScopeAsync(Guid.NewGuid());
            var level2 = await _manager.CreateScopeAsync(Guid.NewGuid());

            Assert.Equal(0, ((NestedDbConnectionScope)level0).NestedLevel);
            Assert.Equal(1, ((NestedDbConnectionScope)level1).NestedLevel);
            Assert.Equal(2, ((NestedDbConnectionScope)level2).NestedLevel);
            Assert.Same(level1, ((NestedDbConnectionScope)level2).ParentScope);
        }

        #endregion

        #region CreateScopeAsync 测试

        [Fact]
        public async Task CreateScopeAsync_WhenNoActiveScope_CreatesRootScope()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(default))
                .ReturnsAsync(_mockConnection.Object);

            var scope = await _manager.CreateScopeAsync(Guid.NewGuid());

            Assert.NotNull(scope);
            Assert.True(((NestedDbConnectionScope)scope).IsRootScope);
        }

        [Fact]
        public async Task CreateScopeAsync_WhenHasActiveScope_CreatesChildScope()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(default))
                .ReturnsAsync(_mockConnection.Object);
            await _manager.CreateScopeAsync(Guid.NewGuid());

            var childScope = await _manager.CreateScopeAsync(Guid.NewGuid());

            Assert.False(((NestedDbConnectionScope)childScope).IsRootScope);
            Assert.Equal(1, ((NestedDbConnectionScope)childScope).NestedLevel);
        }

        [Fact]
        public async Task CreateScopeAsync_WhenDisposed_ThrowsObjectDisposedException()
        {
            _manager.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => _manager.CreateScopeAsync(Guid.NewGuid()));
        }

        #endregion

        #region CreateScope(DbConnection) 测试

        [Fact]
        public async Task CreateScope_WithConnection_WhenNoActiveScope_CreatesRootScope()
        {
            var scope = await CreateScopeAsync(_manager, _mockConnection.Object);

            Assert.NotNull(scope);
            Assert.True(((NestedDbConnectionScope)scope).IsRootScope);
            Assert.False(scope.ManageConnection);
        }

        [Fact]
        public async Task CreateScope_WithConnection_NullConnection_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _manager.CreateScopeAsync(null, Guid.NewGuid()));
        }

        [Fact]
        public async Task CreateScope_WithConnection_WhenHasActiveScope_WithSameConnection_CreatesChildScope()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            var rootScope = await _manager.CreateScopeAsync(Guid.NewGuid());

            var childScope = await CreateScopeAsync(_manager, _mockConnection.Object);

            Assert.False(((NestedDbConnectionScope)childScope).IsRootScope);
            Assert.Equal(1, ((NestedDbConnectionScope)childScope).NestedLevel);
        }

        [Fact]
        public async Task CreateScope_WithConnection_WhenHasActiveScope_WithDifferentConnection_ThrowsInvalidOperationException()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            await _manager.CreateScopeAsync(Guid.NewGuid());

            var differentConnection = new Mock<DbConnection>().Object;

            await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.CreateScopeAsync(differentConnection, Guid.NewGuid()));
        }

        #endregion

        #region 作用域释放测试 - LIFO 顺序

        [Fact]
        public async Task Scope_DisposeInLifoOrder_WorksCorrectly()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            var rootScope = (NestedDbConnectionScope)await _manager.CreateScopeAsync(Guid.NewGuid());
            var childScope = (NestedDbConnectionScope)await _manager.CreateScopeAsync(Guid.NewGuid());

            // 先释放子作用域
            childScope.Dispose();

            Assert.Same(rootScope, _manager.CurrentScope);

            // 再释放根作用域
            rootScope.Dispose();

            Assert.Null(_manager.CurrentScope);
        }

        [Fact]
        public async Task Scope_DisposeOutOfOrder_RemovesScopeFromStackWithoutThrowing()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            var rootScope = (NestedDbConnectionScope)await _manager.CreateScopeAsync(Guid.NewGuid());
            var childScope = (NestedDbConnectionScope)await _manager.CreateScopeAsync(Guid.NewGuid());

            // 先释放根作用域（错误的顺序）：不抛出异常（异常会从 Dispose 的完成通知中逃逸）；
            // 根作用域及其栈中之上的子作用域（Connection 引用归根作用域管理，已随其释放
            // 成为僵尸）一并从作用域栈中级联清理，栈恢复为空
            rootScope.Dispose();

            Assert.Null(_manager.CurrentScope);
        }

        [Fact]
        public async Task Scope_DisposeMiddleOutOfOrder_CascadesAboveAndKeepsBelow()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            var rootScope = (NestedDbConnectionScope)await _manager.CreateScopeAsync(Guid.NewGuid());
            var middleScope = (NestedDbConnectionScope)await _manager.CreateScopeAsync(Guid.NewGuid());
            var childScope = (NestedDbConnectionScope)await _manager.CreateScopeAsync(Guid.NewGuid());

            // 释放中间作用域（错误的顺序）：其上更晚开启的子作用域级联清理，
            // 其下更早开启的根作用域（连接的管理者）完整保留，栈顶恢复为根作用域
            middleScope.Dispose();

            Assert.Same(rootScope, _manager.CurrentScope);

            rootScope.Dispose();
        }

        #endregion

        #region 属性测试

        [Fact]
        public void CurrentScope_WhenNoScope_ReturnsNull()
        {
            Assert.Null(_manager.CurrentScope);
        }

        [Fact]
        public void CurrentScope_WhenDisposed_ThrowsObjectDisposedException()
        {
            _manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _manager.CurrentScope);
        }

        [Fact]
        public void CurrentConnection_WhenNoScope_ReturnsNull()
        {
            Assert.Null(_manager.CurrentConnection);
        }

        [Fact]
        public async Task CurrentConnection_WhenScopeExists_ReturnsConnection()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            await _manager.CreateScopeAsync(Guid.NewGuid());

            Assert.Same(_mockConnection.Object, _manager.CurrentConnection);
        }

        [Fact]
        public void HasActiveConnection_WhenNoScope_ReturnsFalse()
        {
            Assert.False(_manager.HasActiveConnection);
        }

        [Fact]
        public async Task HasActiveConnection_WhenScopeExists_ReturnsTrue()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            await _manager.CreateScopeAsync(Guid.NewGuid());

            Assert.True(_manager.HasActiveConnection);
        }

        #endregion

        #region Dispose 测试

        [Fact]
        public async Task Dispose_WithNestedScopes_DisposesAllScopes()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            var rootScope = (NestedDbConnectionScope)await _manager.CreateScopeAsync(Guid.NewGuid());
            var childScope1 = (NestedDbConnectionScope)await _manager.CreateScopeAsync(Guid.NewGuid());
            var childScope2 = (NestedDbConnectionScope)await _manager.CreateScopeAsync(Guid.NewGuid());

            _manager.Dispose();

            // Dispose 后访问 CurrentScope 会抛出 ObjectDisposedException
            Assert.Throws<ObjectDisposedException>(() => _manager.CurrentScope);
        }

        [Fact]
        public async Task Dispose_MultipleTimes_DoesNotThrow()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            await _manager.CreateScopeAsync(Guid.NewGuid());

            _manager.Dispose();
            _manager.Dispose(); // 不应抛出异常
        }

        #endregion

        #region LifecycleEnded 事件测试

        [Fact]
        public async Task LifecycleEnded_WhenScopeDisposed_RaisesEvent()
        {
            _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection.Object);
            var scope = await _manager.CreateScopeAsync(Guid.NewGuid());
            var eventRaised = false;

            ((DbConnectionScopeBase)scope).LifecycleEnded += (sender, e) =>
            {
                eventRaised = true;
            };

            scope.Dispose();

            Assert.True(eventRaised);
        }

        #endregion
    }
}
