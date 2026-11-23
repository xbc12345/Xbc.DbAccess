using System;
using System.Data;
using System.Data.Common;
using Moq;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Transactions
{
    /// <summary>
    /// NestedDbTransactionManager 的单元测试。
    /// </summary>
    public class NestedDbTransactionManagerTests : IDisposable
    {
        private readonly Mock<IDbConnectionManager> _mockConnectionManager;
        private readonly MockDbConnection _mockConnection;
        private readonly Mock<ISavepointSqlDialect> _mockSavepointDialect;
        private readonly NestedDbTransactionManager _manager;

        public NestedDbTransactionManagerTests()
        {
            _mockConnectionManager = new Mock<IDbConnectionManager>();
            _mockConnection = new MockDbConnection();
            _mockSavepointDialect = new Mock<ISavepointSqlDialect>();

            _manager = new NestedDbTransactionManager(_mockConnectionManager.Object, _mockSavepointDialect.Object);
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
            _ = Assert.Throws<ArgumentNullException>(() => new NestedDbTransactionManager(null!, _mockSavepointDialect.Object));
        }

        [Fact]
        public void Constructor_NullSavepointStrategy_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() => new NestedDbTransactionManager(_mockConnectionManager.Object, null!));
        }

        [Fact]
        public void Constructor_ValidParameters_CreatesManager()
        {
            Assert.NotNull(_manager);
            Assert.Null(_manager.CurrentTransaction);
            Assert.False(_manager.HasActiveConnection);
        }

        #endregion

        #region BeginTransaction 测试 - 根事务

        [Fact]
        public async Task BeginTransaction_WhenNotDisposed_CreatesRootTransaction()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);

            var transaction = await _manager.BeginTransactionAsync(Guid.NewGuid());

            Assert.NotNull(transaction);
            Assert.IsType<NestedDbTransaction>(transaction);
            _mockConnectionManager.Verify(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task BeginTransaction_WhenDisposed_ThrowsObjectDisposedException()
        {
            _manager.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => _manager.BeginTransactionAsync(Guid.NewGuid()));
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

        #region BeginTransaction 测试 - 子事务

        [Fact]
        public async Task BeginTransaction_WhenTransactionExists_CreatesChildTransaction()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);

            var rootTransaction = (NestedDbTransaction)await _manager.BeginTransactionAsync(Guid.NewGuid());
            var childTransaction = (NestedDbTransaction)await _manager.BeginTransactionAsync(Guid.NewGuid());

            Assert.NotNull(childTransaction);
            Assert.NotEqual(rootTransaction.TransactionId, childTransaction.TransactionId);
            Assert.Equal(childTransaction, _manager.CurrentTransaction);

            childTransaction.Dispose();
            rootTransaction.Dispose();
        }

        [Fact]
        public async Task BeginTransaction_Child_CrossThread_IsAllowed()
        {
            // 跨线程开启子事务是允许的：并发安全由 lock(syncRoot) 与 LIFO 栈顶校验保证，
            // 不再基于线程 ID 做限制（异步环境下 await 后线程会切换，线程比对必然误报）。
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);
            _ = await _manager.BeginTransactionAsync(Guid.NewGuid());

            Exception exception = null!;
            var thread = new System.Threading.Thread(() =>
            {
                try { _manager.BeginTransactionAsync(Guid.NewGuid()).GetAwaiter().GetResult(); }
                catch (Exception ex) { exception = ex; }
            });
            thread.Start();
            thread.Join();

            Assert.Null(exception);
            Assert.True(_manager.CurrentTransaction is NestedDbTransaction childTransaction
                && !childTransaction.IsRootTransaction);

            _manager.CurrentTransaction?.Dispose();
            _manager.CurrentTransaction?.Dispose();
        }

        [Fact]
        public async Task BeginTransaction_Child_SetsIsRootTransactionFalse()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);

            _ = await _manager.BeginTransactionAsync(Guid.NewGuid());
            var childTransaction = (NestedDbTransaction)await _manager.BeginTransactionAsync(Guid.NewGuid());

            Assert.False(childTransaction.IsRootTransaction);

            childTransaction.Dispose();
            _manager.CurrentTransaction?.Dispose();
        }

        [Fact]
        public async Task Dispose_OutOfOrder_DoesNotThrowAndKeepsStackConsistent()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);
            var rootTransaction = (NestedDbTransaction)await _manager.BeginTransactionAsync(Guid.NewGuid());
            var childTransaction = (NestedDbTransaction)await _manager.BeginTransactionAsync(Guid.NewGuid());

            // 先释放根事务（错误的顺序）：不抛出异常（异常会从 Dispose 的完成通知中逃逸，
            // 跳过后续的内部事务与连接释放，导致资源泄漏）；根事务及其栈中之上的子事务
            // （已随根事务的回滚物理失效）一并从事务栈中级联清理，栈恢复为空
            rootTransaction.Dispose();

            Assert.Null(_manager.CurrentTransaction);
        }

        [Fact]
        public async Task Dispose_MiddleTransactionOutOfOrder_CascadesAboveAndKeepsBelow()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);
            var rootTransaction = (NestedDbTransaction)await _manager.BeginTransactionAsync(Guid.NewGuid());
            var middleTransaction = (NestedDbTransaction)await _manager.BeginTransactionAsync(Guid.NewGuid());
            var childTransaction = (NestedDbTransaction)await _manager.BeginTransactionAsync(Guid.NewGuid());

            // 释放中间事务（错误的顺序）：其上更晚开启的子事务（物理上已随回滚失效）级联清理，
            // 其下更早开启的根事务（物理资源的管理者）完整保留，栈顶恢复为根事务
            middleTransaction.Dispose();

            Assert.Same(rootTransaction, _manager.CurrentTransaction);

            rootTransaction.Dispose();
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
        public async Task BeginTransaction_WithConnection_WhenTransactionExists_CreatesChildTransaction()
        {
            _ = await _manager.BeginTransactionAsync(_mockConnection, Guid.NewGuid());
            var childTransaction = await _manager.BeginTransactionAsync(_mockConnection, Guid.NewGuid());

            Assert.NotNull(childTransaction);
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
        public async Task CurrentTransaction_WhenTransactionExists_ReturnsTopTransaction()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);

            var rootTransaction = await _manager.BeginTransactionAsync(Guid.NewGuid());
            var childTransaction = await _manager.BeginTransactionAsync(Guid.NewGuid());

            Assert.Equal(childTransaction, _manager.CurrentTransaction);

            childTransaction.Dispose();
            rootTransaction.Dispose();
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
        public async Task TransactionCompleted_WhenRootCommits_ClearsCurrentTransaction()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);

            var transaction = await _manager.BeginTransactionAsync(Guid.NewGuid());
            transaction.Commit();

            Assert.Null(_manager.CurrentTransaction);
            Assert.False(_manager.HasActiveConnection);
        }

        [Fact]
        public async Task TransactionCompleted_WhenChildCommits_PopsToParent()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);
            _ = _mockSavepointDialect.Setup(s => s.IsSavepointSupported).Returns(true);
            _ = _mockSavepointDialect.Setup(s => s.IsReleaseSavepointSupported).Returns(true);
            _ = _mockSavepointDialect.Setup(s => s.ReleaseSavepoint(It.IsAny<string>())).Returns("RELEASE SAVEPOINT SAVEPOINT_1");

            _ = await _manager.BeginTransactionAsync(Guid.NewGuid());
            var childTransaction = await _manager.BeginTransactionAsync(Guid.NewGuid());

            childTransaction.Commit();

            var topTransaction = (NestedDbTransaction)_manager.CurrentTransaction;
            Assert.True(topTransaction.IsRootTransaction);

            topTransaction.Dispose();
        }

        [Fact]
        public async Task TransactionCompleted_WhenRootRollbacks_ClearsCurrentTransaction()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);

            var transaction = await _manager.BeginTransactionAsync(Guid.NewGuid());
            transaction.Rollback();

            Assert.Null(_manager.CurrentTransaction);
            Assert.False(_manager.HasActiveConnection);
        }

        [Fact]
        public async Task TransactionCompleted_WhenChildRollbacks_PopsToParent()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);
            _ = _mockSavepointDialect.Setup(s => s.IsSavepointSupported).Returns(true);
            _ = _mockSavepointDialect.Setup(s => s.RollbackToSavepoint(It.IsAny<string>())).Returns("ROLLBACK TO SAVEPOINT SAVEPOINT_1");

            _ = await _manager.BeginTransactionAsync(Guid.NewGuid());
            var childTransaction = await _manager.BeginTransactionAsync(Guid.NewGuid());

            childTransaction.Rollback();

            var topTransaction = (NestedDbTransaction)_manager.CurrentTransaction;
            Assert.True(topTransaction.IsRootTransaction);

            topTransaction.Dispose();
        }

        [Fact]
        public async Task TransactionCompleted_WhenNotTopTransaction_RemovesTransactionFromStack()
        {
            _ = _mockConnectionManager.Setup(m => m.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockConnection);

            var rootTransaction = await _manager.BeginTransactionAsync(Guid.NewGuid());
            var childTransaction = await _manager.BeginTransactionAsync(Guid.NewGuid());

            // 非栈顶事务完成（错误的顺序）：不抛出异常（异常会从 Commit/Dispose 的完成通知中
            // 逃逸，跳过后续的资源释放）；该事务及其栈中之上的子事务一并从事务栈中级联清理
            rootTransaction.Commit();

            Assert.Null(_manager.CurrentTransaction);
        }

        [Fact]
        public async Task TransactionCompleted_WhenRootDisposes_ClearsCurrentTransaction()
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
