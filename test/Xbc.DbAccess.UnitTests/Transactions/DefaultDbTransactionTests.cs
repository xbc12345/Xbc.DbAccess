using System;
using System.Data;
using Moq;
using System.Data.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Transactions
{
    /// <summary>
    /// DefaultDbTransaction 的单元测试。
    /// </summary>
    public class DefaultDbTransactionTests : IDisposable
    {
        private readonly Mock<DbTransaction> _mockInnerTransaction;
        private readonly MockDbConnection _mockInnerConnection;
        private DefaultDbTransaction _transaction;

        public DefaultDbTransactionTests()
        {
            _mockInnerTransaction = new Mock<DbTransaction>();
            _mockInnerConnection = new MockDbConnection();

            _transaction = new DefaultDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
        }

        public void Dispose()
        {
            _transaction?.Dispose();
            GC.SuppressFinalize(this);
        }

        #region 构造函数测试

        [Fact]
        public void Constructor_NullInnerTransaction_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() => new DefaultDbTransaction(null!, _mockInnerConnection, true, Guid.NewGuid()));
        }

        [Fact]
        public void Constructor_NullInnerConnection_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() => new DefaultDbTransaction(_mockInnerTransaction.Object, null!, true, Guid.NewGuid()));
        }

        [Fact]
        public void Constructor_ValidParameters_CreatesTransaction()
        {
            Assert.NotNull(_transaction);
            Assert.NotEqual(Guid.Empty, _transaction.TransactionId);
            Assert.False(_transaction.IsDisposed);
            Assert.True(_transaction.ManageConnection);
        }

        [Fact]
        public void Constructor_ManageConnectionFalse_DoesNotManageConnection()
        {
            var transaction = new DefaultDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, false, Guid.NewGuid());

            Assert.False(transaction.ManageConnection);

            transaction.Dispose();
        }

        #endregion

        #region 属性测试

        [Fact]
        public void TransactionId_Always_ReturnsUniqueGuid()
        {
            var transaction1 = new DefaultDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var transaction2 = new DefaultDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());

            Assert.NotEqual(transaction1.TransactionId, transaction2.TransactionId);

            transaction1.Dispose();
            transaction2.Dispose();
        }

        [Fact]
        public void ThreadId_Always_ReturnsCurrentThreadId()
        {
            var expectedThreadId = Environment.CurrentManagedThreadId;

            Assert.Equal(expectedThreadId, _transaction.ThreadId);
        }

        [Fact]
        public void IsDisposed_WhenNotDisposed_ReturnsFalse()
        {
            Assert.False(_transaction.IsDisposed);
        }

        [Fact]
        public void IsDisposed_WhenDisposed_ReturnsTrue()
        {
            _transaction.Dispose();

            Assert.True(_transaction.IsDisposed);
        }

        [Fact]
        public void ManageConnection_WhenTrue_ReturnsTrue()
        {
            Assert.True(_transaction.ManageConnection);
        }

        [Fact]
        public void ManageConnection_WhenFalse_ReturnsFalse()
        {
            var transaction = new DefaultDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, false, Guid.NewGuid());

            Assert.False(transaction.ManageConnection);

            transaction.Dispose();
        }

        #endregion

        #region Commit 测试

        [Fact]
        public void Commit_WhenNotDisposed_CommitsTransaction()
        {
            _mockInnerTransaction.Setup(t => t.Commit());

            _transaction.Commit();

            _mockInnerTransaction.Verify(t => t.Commit(), Times.Once);
        }

        [Fact]
        public void Commit_WhenDisposed_ThrowsObjectDisposedException()
        {
            _transaction.Dispose();

            _ = Assert.Throws<ObjectDisposedException>(() => _transaction.Commit());
        }

        [Fact]
        public void Commit_WhenAlreadyCompleted_ThrowsInvalidOperationException()
        {
            _mockInnerTransaction.Setup(t => t.Commit());
            _transaction.Commit();

            _ = Assert.Throws<InvalidOperationException>(() => _transaction.Commit());
        }

        [Fact]
        public void Commit_WhenNotDisposed_RaisesCompletedEvent()
        {
            _mockInnerTransaction.Setup(t => t.Commit());
            var eventRaised = false;
            DbTransactionCompletedEventArgs eventArgs = null!;

            _transaction.Completed += (sender, e) =>
            {
                eventRaised = true;
                eventArgs = e;
            };

            _transaction.Commit();

            Assert.True(eventRaised);
            Assert.NotNull(eventArgs);
            Assert.Equal(_transaction.TransactionId, eventArgs.TransactionId);
        }

        #endregion

        #region Rollback 测试

        [Fact]
        public void Rollback_WhenNotDisposed_RollsBackTransaction()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());

            _transaction.Rollback();

            _mockInnerTransaction.Verify(t => t.Rollback(), Times.Once);
        }

        [Fact]
        public void Rollback_WhenDisposed_ThrowsObjectDisposedException()
        {
            _transaction.Dispose();

            _ = Assert.Throws<ObjectDisposedException>(() => _transaction.Rollback());
        }

        [Fact]
        public void Rollback_WhenAlreadyCompleted_ThrowsInvalidOperationException()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());
            _transaction.Rollback();

            _ = Assert.Throws<InvalidOperationException>(() => _transaction.Rollback());
        }

        [Fact]
        public void Rollback_WhenNotDisposed_RaisesCompletedEvent()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());
            var eventRaised = false;
            DbTransactionCompletedEventArgs eventArgs = null!;

            _transaction.Completed += (sender, e) =>
            {
                eventRaised = true;
                eventArgs = e;
            };

            _transaction.Rollback();

            Assert.True(eventRaised);
            Assert.NotNull(eventArgs);
        }

        #endregion

        #region Dispose 测试

        [Fact]
        public void Dispose_WhenNotCompleted_RollsBackTransaction()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());

            _transaction.Dispose();

            _mockInnerTransaction.Verify(t => t.Rollback(), Times.Once);
        }

        [Fact]
        public void Dispose_WhenAlreadyCompleted_DoesNotRollbackAgain()
        {
            _mockInnerTransaction.Setup(t => t.Commit());
            _transaction.Commit();

            _transaction.Dispose();

            _mockInnerTransaction.Verify(t => t.Rollback(), Times.Never);
        }

        [Fact]
        public void Dispose_WithManageConnection_DisposesConnection()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());

            _transaction.Dispose();

            Assert.Equal(1, _mockInnerConnection.DisposeCallCount);
        }

        [Fact]
        public void Dispose_WithoutManageConnection_DoesNotDisposeConnection()
        {
            var transaction = new DefaultDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, false, Guid.NewGuid());
            _mockInnerTransaction.Setup(t => t.Rollback());

            transaction.Dispose();

            Assert.Equal(0, _mockInnerConnection.DisposeCallCount);
        }

        [Fact]
        public void Dispose_WhenCalled_DisposesInnerTransaction()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());

            _transaction.Dispose();

            // Dispose 不是虚方法，无法验证，但测试不应抛出异常
            Assert.True(_transaction.IsDisposed);
        }

        [Fact]
        public void Dispose_MultipleTimes_DoesNotThrow()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());

            _transaction.Dispose();

            Exception exception = Record.Exception(() => _transaction.Dispose());

            Assert.Null(exception);
        }

        [Fact]
        public void Dispose_WhenNotCompleted_RaisesCompletedEvent()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());
            var eventRaised = false;
            DbTransactionCompletedEventArgs eventArgs = null!;

            _transaction.Completed += (sender, e) =>
            {
                eventRaised = true;
                eventArgs = e;
            };

            _transaction.Dispose();

            Assert.True(eventRaised);
            Assert.NotNull(eventArgs);
        }

        [Fact]
        public void Dispose_WhenAlreadyCompleted_DoesNotRaiseCompletedEvent()
        {
            _mockInnerTransaction.Setup(t => t.Commit());
            var eventCount = 0;

            _transaction.Completed += (sender, e) => eventCount++;

            _transaction.Commit();
            _transaction.Dispose();

            Assert.Equal(1, eventCount);
        }

        #endregion

        #region Completed 事件测试

        [Fact]
        public void Completed_WhenCommitted_RaisesEventWithTransactionId()
        {
            _mockInnerTransaction.Setup(t => t.Commit());
            var receivedTransactionId = Guid.Empty;

            _transaction.Completed += (sender, e) =>
            {
                receivedTransactionId = e.TransactionId;
            };

            _transaction.Commit();

            Assert.NotEqual(Guid.Empty, receivedTransactionId);
            Assert.Equal(_transaction.TransactionId, receivedTransactionId);
        }

        [Fact]
        public void Completed_WhenRolledBack_RaisesEventWithTransactionId()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());
            var receivedTransactionId = Guid.Empty;

            _transaction.Completed += (sender, e) =>
            {
                receivedTransactionId = e.TransactionId;
            };

            _transaction.Rollback();

            Assert.NotEqual(Guid.Empty, receivedTransactionId);
            Assert.Equal(_transaction.TransactionId, receivedTransactionId);
        }

        [Fact]
        public void Completed_MultipleSubscribers_AllReceiveEvent()
        {
            _mockInnerTransaction.Setup(t => t.Commit());
            var subscriber1Called = false;
            var subscriber2Called = false;

            _transaction.Completed += (sender, e) => subscriber1Called = true;
            _transaction.Completed += (sender, e) => subscriber2Called = true;

            _transaction.Commit();

            Assert.True(subscriber1Called);
            Assert.True(subscriber2Called);
        }

        #endregion
    }
}
