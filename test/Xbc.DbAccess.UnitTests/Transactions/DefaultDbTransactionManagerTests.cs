using Moq;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Transactions
{
    /// <summary>
    /// DefaultDbTransactionManager 的单元测试。
    /// </summary>
    public class DefaultDbTransactionManagerTests : IDisposable
    {
        private readonly Mock<IDbConnectionManager> _mockConnectionManager;
        private readonly MockDbConnection _mockConnection;
        private readonly DefaultDbTransactionManager _manager;

        public DefaultDbTransactionManagerTests()
        {
            _mockConnectionManager = new Mock<IDbConnectionManager>();
            _mockConnection = new MockDbConnection();

            _manager = new DefaultDbTransactionManager(_mockConnectionManager.Object);
        }

        public void Dispose()
        {
            _manager?.Dispose();
            GC.SuppressFinalize(this);
        }

        #region 构造函数测试

        [Fact]
        public void Constructor_NullConnectionManager_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() => new DefaultDbTransactionManager(null!));
        }

        [Fact]
        public void Constructor_ValidParameters_CreatesManager()
        {
            Assert.NotNull(_manager);
            Assert.Null(_manager.CurrentTransaction);
            Assert.False(_manager.HasActiveConnection);
        }

        #endregion

        #region BeginTransaction 测试

        [Fact]
        public async Task BeginTransaction_WhenNotDisposed_CreatesTransaction()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);

            var transaction = await _manager.BeginTransactionAsync(Guid.NewGuid());

            Assert.NotNull(transaction);
            Assert.IsType<DefaultDbTransaction>(transaction);
            _mockConnectionManager.Verify(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task BeginTransaction_WhenDisposed_ThrowsObjectDisposedException()
        {
            _manager.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => _manager.BeginTransactionAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task BeginTransaction_WhenTransactionExists_ThrowsInvalidOperationException()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);
            _ = await _manager.BeginTransactionAsync(Guid.NewGuid());

            await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.BeginTransactionAsync(Guid.NewGuid()));

            _manager.CurrentTransaction?.Dispose();
        }

        [Fact]
        public async Task BeginTransaction_WhenBeginTransactionFails_ReleasesConnection()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);
            _ = _mockConnectionManager.Setup(m => m.Release(_mockConnection));

            // 模拟 BeginTransaction 抛出异常
            _mockConnection.BeginTransactionException = new InvalidOperationException("Test exception");

            await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.BeginTransactionAsync(Guid.NewGuid()));

            _mockConnectionManager.Verify(m => m.Release(_mockConnection), Times.Once);
        }

        [Fact]
        public async Task BeginTransaction_SetsCurrentTransaction()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);

            var transaction = await _manager.BeginTransactionAsync(Guid.NewGuid());

            Assert.Equal(transaction, _manager.CurrentTransaction);
            Assert.True(_manager.HasActiveConnection);
        }

        #endregion

        #region BeginTransaction with connection 测试

        [Fact]
        public async Task BeginTransaction_WithConnection_NullConnection_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _manager.BeginTransactionAsync(null!, Guid.NewGuid()));
        }

        [Fact]
        public async Task BeginTransaction_WithConnection_WhenNotDisposed_CreatesTransaction()
        {
            var transaction = await _manager.BeginTransactionAsync(_mockConnection, Guid.NewGuid());

            Assert.NotNull(transaction);
        }

        [Fact]
        public async Task BeginTransaction_WithConnection_WhenDisposed_ThrowsObjectDisposedException()
        {
            _manager.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => _manager.BeginTransactionAsync(_mockConnection, Guid.NewGuid()));
        }

        [Fact]
        public async Task BeginTransaction_WithConnection_WhenTransactionExists_ThrowsInvalidOperationException()
        {
            _ = await _manager.BeginTransactionAsync(_mockConnection, Guid.NewGuid());

            await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.BeginTransactionAsync(_mockConnection, Guid.NewGuid()));

            _manager.CurrentTransaction?.Dispose();
        }

        #endregion

        #region CurrentTransaction 测试

        [Fact]
        public void CurrentTransaction_WhenDisposed_ThrowsObjectDisposedException()
        {
            _manager.Dispose();

            _ = Assert.Throws<ObjectDisposedException>(() => _manager.CurrentTransaction);
        }

        [Fact]
        public void CurrentTransaction_WhenNoTransaction_ReturnsNull()
        {
            Assert.Null(_manager.CurrentTransaction);
        }

        [Fact]
        public async Task CurrentTransaction_WhenTransactionExists_ReturnsTransaction()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);

            var transaction = await _manager.BeginTransactionAsync(Guid.NewGuid());

            Assert.Equal(transaction, _manager.CurrentTransaction);

            transaction.Dispose();
        }

        #endregion

        #region CurrentConnection 测试

        [Fact]
        public void CurrentConnection_WhenDisposed_ThrowsObjectDisposedException()
        {
            _manager.Dispose();

            _ = Assert.Throws<ObjectDisposedException>(() => _manager.CurrentConnection);
        }

        [Fact]
        public void CurrentConnection_WhenNoTransaction_ReturnsNull()
        {
            Assert.Null(_manager.CurrentConnection);
        }

        [Fact]
        public async Task CurrentConnection_WhenTransactionExists_ReturnsConnection()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);
            _ = await _manager.BeginTransactionAsync(Guid.NewGuid());

            Assert.Equal(_mockConnection, _manager.CurrentConnection);

            _manager.CurrentTransaction?.Dispose();
        }

        #endregion

        #region HasActiveConnection 测试

        [Fact]
        public void HasActiveConnection_WhenDisposed_ThrowsObjectDisposedException()
        {
            _manager.Dispose();

            _ = Assert.Throws<ObjectDisposedException>(() => _manager.HasActiveConnection);
        }

        [Fact]
        public void HasActiveConnection_WhenNoTransaction_ReturnsFalse()
        {
            Assert.False(_manager.HasActiveConnection);
        }

        [Fact]
        public async Task HasActiveConnection_WhenTransactionExists_ReturnsTrue()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);
            _ = await _manager.BeginTransactionAsync(Guid.NewGuid());

            Assert.True(_manager.HasActiveConnection);

            _manager.CurrentTransaction?.Dispose();
        }

        #endregion

        #region Transaction Completed 事件测试

        [Fact]
        public async Task TransactionCompleted_WhenCommitted_ClearsCurrentTransaction()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);

            var transaction = await _manager.BeginTransactionAsync(Guid.NewGuid());
            transaction.Commit();

            Assert.Null(_manager.CurrentTransaction);
            Assert.False(_manager.HasActiveConnection);
        }

        [Fact]
        public async Task TransactionCompleted_WhenRolledBack_ClearsCurrentTransaction()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);

            var transaction = await _manager.BeginTransactionAsync(Guid.NewGuid());
            transaction.Rollback();

            Assert.Null(_manager.CurrentTransaction);
            Assert.False(_manager.HasActiveConnection);
        }

        [Fact]
        public async Task TransactionCompleted_WhenDisposed_ClearsCurrentTransaction()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);

            var transaction = await _manager.BeginTransactionAsync(Guid.NewGuid());
            transaction.Dispose();

            Assert.Null(_manager.CurrentTransaction);
            Assert.False(_manager.HasActiveConnection);
        }

        #endregion

        #region Dispose 测试

        [Fact]
        public async Task Dispose_WhenTransactionExists_RollbacksAndDisposesTransaction()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);

            _ = await _manager.BeginTransactionAsync(Guid.NewGuid());
            _manager.Dispose();

            Assert.Equal(1, _mockConnection.MockTransaction.RollbackCallCount);
        }

        [Fact]
        public void Dispose_MultipleTimes_DoesNotThrow()
        {
            _manager.Dispose();

            Exception exception = Record.Exception(() => _manager.Dispose());

            Assert.Null(exception);
        }

        [Fact]
        public async Task Dispose_WhenCalled_RollbacksTransaction()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);

            _ = await _manager.BeginTransactionAsync(Guid.NewGuid());
            _manager.Dispose();

            // 验证事务被回滚
            Assert.Equal(1, _mockConnection.MockTransaction.RollbackCallCount);
        }

        #endregion
    }
}
