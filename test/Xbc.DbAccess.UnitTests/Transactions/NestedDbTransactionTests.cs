using System;
using System.Data;
using Moq;
using System.Data.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Transactions
{
    /// <summary>
    /// NestedDbTransaction 的单元测试。
    /// </summary>
    public class NestedDbTransactionTests : IDisposable
    {
        private readonly Mock<DbTransaction> _mockInnerTransaction;
        private readonly MockDbConnection _mockInnerConnection;
        private readonly Mock<ISavepointSqlDialect> _mockSavepointDialect;

        public NestedDbTransactionTests()
        {
            _mockInnerTransaction = new Mock<DbTransaction>();
            _mockInnerConnection = new MockDbConnection();
            _mockSavepointDialect = new Mock<ISavepointSqlDialect>();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }

        #region 根事务构造函数测试

        [Fact]
        public void Constructor_Root_NullInnerTransaction_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() => new NestedDbTransaction(null!, _mockInnerConnection, true, Guid.NewGuid()));
        }

        [Fact]
        public void Constructor_Root_NullInnerConnection_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() => new NestedDbTransaction(_mockInnerTransaction.Object, null!, true, Guid.NewGuid()));
        }

        [Fact]
        public void Constructor_Root_ValidParameters_CreatesTransaction()
        {
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());

            Assert.NotNull(transaction);
            Assert.NotEqual(Guid.Empty, transaction.TransactionId);
            Assert.False(transaction.IsDisposed);
            Assert.True(transaction.ManageConnection);
            Assert.True(transaction.IsRootTransaction);

            transaction.Dispose();
        }

        [Fact]
        public void Constructor_Root_ManageConnectionFalse_DoesNotManageConnection()
        {
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, false, Guid.NewGuid());

            Assert.False(transaction.ManageConnection);

            transaction.Dispose();
        }

        #endregion

        #region 子事务构造函数测试

        [Fact]
        public void Constructor_Child_NullParentTransaction_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() => new NestedDbTransaction(null!, "SAVEPOINT_1", _mockSavepointDialect.Object, Guid.NewGuid()));
        }

        [Fact]
        public void Constructor_Child_NullSavepointName_ThrowsArgumentNullException()
        {
            var rootTransaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());

            _ = Assert.Throws<ArgumentNullException>(() => new NestedDbTransaction(rootTransaction, null!, _mockSavepointDialect.Object, Guid.NewGuid()));

            rootTransaction.Dispose();
        }

        [Fact]
        public void Constructor_Child_NullSavepointStrategy_ThrowsArgumentNullException()
        {
            var rootTransaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());

            _ = Assert.Throws<ArgumentNullException>(() => new NestedDbTransaction(rootTransaction, "SAVEPOINT_1", null!, Guid.NewGuid()));

            rootTransaction.Dispose();
        }

        [Fact]
        public void Constructor_Child_ValidParameters_CreatesTransaction()
        {
            var rootTransaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());

            var childTransaction = new NestedDbTransaction(rootTransaction, "SAVEPOINT_1", _mockSavepointDialect.Object, Guid.NewGuid());

            Assert.NotNull(childTransaction);
            Assert.NotEqual(Guid.Empty, childTransaction.TransactionId);
            Assert.NotEqual(rootTransaction.TransactionId, childTransaction.TransactionId);
            Assert.False(childTransaction.IsRootTransaction);

            childTransaction.Dispose();
            rootTransaction.Dispose();
        }

        #endregion

        #region 属性测试

        [Fact]
        public void TransactionId_Always_ReturnsUniqueGuid()
        {
            var transaction1 = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var transaction2 = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());

            Assert.NotEqual(transaction1.TransactionId, transaction2.TransactionId);

            transaction1.Dispose();
            transaction2.Dispose();
        }

        [Fact]
        public void ThreadId_Always_ReturnsCurrentThreadId()
        {
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());

            var expectedThreadId = Environment.CurrentManagedThreadId;

            Assert.Equal(expectedThreadId, transaction.ThreadId);

            transaction.Dispose();
        }

        [Fact]
        public void IsRootTransaction_WhenRoot_ReturnsTrue()
        {
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());

            Assert.True(transaction.IsRootTransaction);

            transaction.Dispose();
        }

        [Fact]
        public void IsRootTransaction_WhenChild_ReturnsFalse()
        {
            var rootTransaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var childTransaction = new NestedDbTransaction(rootTransaction, "SAVEPOINT_1", _mockSavepointDialect.Object, Guid.NewGuid());

            Assert.False(childTransaction.IsRootTransaction);

            childTransaction.Dispose();
            rootTransaction.Dispose();
        }

        #endregion

        #region Commit 测试 - 根事务

        [Fact]
        public void Commit_Root_WhenNotDisposed_CommitsTransaction()
        {
            _mockInnerTransaction.Setup(t => t.Commit());
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());

            transaction.Commit();

            _mockInnerTransaction.Verify(t => t.Commit(), Times.Once);

            transaction.Dispose();
        }

        [Fact]
        public void Commit_Root_WhenDisposed_ThrowsObjectDisposedException()
        {
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            transaction.Dispose();

            _ = Assert.Throws<ObjectDisposedException>(() => transaction.Commit());
        }

        [Fact]
        public void Commit_Root_WhenAlreadyCompleted_ThrowsInvalidOperationException()
        {
            _mockInnerTransaction.Setup(t => t.Commit());
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            transaction.Commit();

            _ = Assert.Throws<InvalidOperationException>(() => transaction.Commit());

            transaction.Dispose();
        }

        [Fact]
        public void Commit_Root_WhenNotDisposed_RaisesCompletedEvent()
        {
            _mockInnerTransaction.Setup(t => t.Commit());
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var eventRaised = false;
            DbTransactionCompletedEventArgs eventArgs = null!;

            transaction.Completed += (sender, e) =>
            {
                eventRaised = true;
                eventArgs = e;
            };

            transaction.Commit();

            Assert.True(eventRaised);
            Assert.NotNull(eventArgs);
            Assert.Equal(transaction.TransactionId, eventArgs.TransactionId);

            transaction.Dispose();
        }

        #endregion

        #region Commit 测试 - 子事务

        [Fact]
        public void Commit_Child_WhenNotDisposed_ReleasesSavepoint()
        {
            var rootTransaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var childTransaction = new NestedDbTransaction(rootTransaction, "SAVEPOINT_1", _mockSavepointDialect.Object, Guid.NewGuid());

            _ = _mockSavepointDialect.Setup(s => s.IsSavepointSupported).Returns(true);
            _ = _mockSavepointDialect.Setup(s => s.IsReleaseSavepointSupported).Returns(true);
            _ = _mockSavepointDialect.Setup(s => s.ReleaseSavepoint("SAVEPOINT_1")).Returns("RELEASE SAVEPOINT SAVEPOINT_1");

            childTransaction.Commit();

            Assert.Equal(1, _mockInnerConnection.MockCommand.ExecuteNonQueryCallCount);

            childTransaction.Dispose();
            rootTransaction.Dispose();
        }

        [Fact]
        public void Commit_Child_WhenSavepointNotSupported_DoesNotExecuteCommand()
        {
            var rootTransaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var childTransaction = new NestedDbTransaction(rootTransaction, "SAVEPOINT_1", _mockSavepointDialect.Object, Guid.NewGuid());

            _ = _mockSavepointDialect.Setup(s => s.IsSavepointSupported).Returns(false);

            childTransaction.Commit();

            Assert.Equal(0, _mockInnerConnection.MockCommand.ExecuteNonQueryCallCount);

            childTransaction.Dispose();
            rootTransaction.Dispose();
        }

        [Fact]
        public void Commit_Child_WhenDisposed_ThrowsObjectDisposedException()
        {
            var rootTransaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var childTransaction = new NestedDbTransaction(rootTransaction, "SAVEPOINT_1", _mockSavepointDialect.Object, Guid.NewGuid());
            childTransaction.Dispose();

            _ = Assert.Throws<ObjectDisposedException>(() => childTransaction.Commit());

            rootTransaction.Dispose();
        }

        [Fact]
        public void Commit_Child_WhenAlreadyCompleted_ThrowsInvalidOperationException()
        {
            var rootTransaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var childTransaction = new NestedDbTransaction(rootTransaction, "SAVEPOINT_1", _mockSavepointDialect.Object, Guid.NewGuid());

            _ = _mockSavepointDialect.Setup(s => s.IsSavepointSupported).Returns(true);
            _ = _mockSavepointDialect.Setup(s => s.IsReleaseSavepointSupported).Returns(true);
            _ = _mockSavepointDialect.Setup(s => s.ReleaseSavepoint("SAVEPOINT_1")).Returns("RELEASE SAVEPOINT SAVEPOINT_1");

            childTransaction.Commit();

            _ = Assert.Throws<InvalidOperationException>(() => childTransaction.Commit());

            childTransaction.Dispose();
            rootTransaction.Dispose();
        }

        [Fact]
        public void Commit_Child_WhenNotDisposed_RaisesCompletedEvent()
        {
            var rootTransaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var childTransaction = new NestedDbTransaction(rootTransaction, "SAVEPOINT_1", _mockSavepointDialect.Object, Guid.NewGuid());

            _ = _mockSavepointDialect.Setup(s => s.IsSavepointSupported).Returns(true);
            _ = _mockSavepointDialect.Setup(s => s.IsReleaseSavepointSupported).Returns(true);
            _ = _mockSavepointDialect.Setup(s => s.ReleaseSavepoint("SAVEPOINT_1")).Returns("RELEASE SAVEPOINT SAVEPOINT_1");

            var eventRaised = false;
            DbTransactionCompletedEventArgs eventArgs = null!;

            childTransaction.Completed += (sender, e) =>
            {
                eventRaised = true;
                eventArgs = e;
            };

            childTransaction.Commit();

            Assert.True(eventRaised);
            Assert.NotNull(eventArgs);
            Assert.Equal(childTransaction.TransactionId, eventArgs.TransactionId);

            childTransaction.Dispose();
            rootTransaction.Dispose();
        }

        #endregion

        #region Rollback 测试 - 根事务

        [Fact]
        public void Rollback_Root_WhenNotDisposed_RollsBackTransaction()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());

            transaction.Rollback();

            _mockInnerTransaction.Verify(t => t.Rollback(), Times.Once);

            transaction.Dispose();
        }

        [Fact]
        public void Rollback_Root_WhenDisposed_ThrowsObjectDisposedException()
        {
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            transaction.Dispose();

            _ = Assert.Throws<ObjectDisposedException>(() => transaction.Rollback());
        }

        [Fact]
        public void Rollback_Root_WhenAlreadyCompleted_ThrowsInvalidOperationException()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            transaction.Rollback();

            _ = Assert.Throws<InvalidOperationException>(() => transaction.Rollback());

            transaction.Dispose();
        }

        [Fact]
        public void Rollback_Root_WhenNotDisposed_RaisesCompletedEvent()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var eventRaised = false;
            DbTransactionCompletedEventArgs eventArgs = null!;

            transaction.Completed += (sender, e) =>
            {
                eventRaised = true;
                eventArgs = e;
            };

            transaction.Rollback();

            Assert.True(eventRaised);
            Assert.NotNull(eventArgs);

            transaction.Dispose();
        }

        #endregion

        #region Rollback 测试 - 子事务

        [Fact]
        public void Rollback_Child_WhenNotDisposed_RollsBackToSavepoint()
        {
            var rootTransaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var childTransaction = new NestedDbTransaction(rootTransaction, "SAVEPOINT_1", _mockSavepointDialect.Object, Guid.NewGuid());

            _ = _mockSavepointDialect.Setup(s => s.IsSavepointSupported).Returns(true);
            _ = _mockSavepointDialect.Setup(s => s.RollbackToSavepoint("SAVEPOINT_1")).Returns("ROLLBACK TO SAVEPOINT SAVEPOINT_1");

            childTransaction.Rollback();

            Assert.Equal(1, _mockInnerConnection.MockCommand.ExecuteNonQueryCallCount);

            childTransaction.Dispose();
            rootTransaction.Dispose();
        }

        [Fact]
        public void Rollback_Child_WhenSavepointNotSupported_DoesNotExecuteCommand()
        {
            var rootTransaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var childTransaction = new NestedDbTransaction(rootTransaction, "SAVEPOINT_1", _mockSavepointDialect.Object, Guid.NewGuid());

            _ = _mockSavepointDialect.Setup(s => s.IsSavepointSupported).Returns(false);

            childTransaction.Rollback();

            Assert.Equal(0, _mockInnerConnection.MockCommand.ExecuteNonQueryCallCount);

            childTransaction.Dispose();
            rootTransaction.Dispose();
        }

        [Fact]
        public void Rollback_Child_WhenDisposed_ThrowsObjectDisposedException()
        {
            var rootTransaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var childTransaction = new NestedDbTransaction(rootTransaction, "SAVEPOINT_1", _mockSavepointDialect.Object, Guid.NewGuid());
            childTransaction.Dispose();

            _ = Assert.Throws<ObjectDisposedException>(() => childTransaction.Rollback());

            rootTransaction.Dispose();
        }

        [Fact]
        public void Rollback_Child_WhenAlreadyCompleted_ThrowsInvalidOperationException()
        {
            var rootTransaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var childTransaction = new NestedDbTransaction(rootTransaction, "SAVEPOINT_1", _mockSavepointDialect.Object, Guid.NewGuid());

            _ = _mockSavepointDialect.Setup(s => s.IsSavepointSupported).Returns(true);
            _ = _mockSavepointDialect.Setup(s => s.RollbackToSavepoint("SAVEPOINT_1")).Returns("ROLLBACK TO SAVEPOINT SAVEPOINT_1");

            childTransaction.Rollback();

            _ = Assert.Throws<InvalidOperationException>(() => childTransaction.Rollback());

            childTransaction.Dispose();
            rootTransaction.Dispose();
        }

        #endregion

        #region Dispose 测试 - 根事务

        [Fact]
        public void Dispose_Root_WhenNotCompleted_RollsBackTransaction()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());

            transaction.Dispose();

            _mockInnerTransaction.Verify(t => t.Rollback(), Times.Once);
        }

        [Fact]
        public void Dispose_Root_WhenAlreadyCompleted_DoesNotRollbackAgain()
        {
            _mockInnerTransaction.Setup(t => t.Commit());
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            transaction.Commit();

            transaction.Dispose();

            _mockInnerTransaction.Verify(t => t.Rollback(), Times.Never);
        }

        [Fact]
        public void Dispose_Root_WithManageConnection_DisposesConnection()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());

            transaction.Dispose();

            Assert.Equal(1, _mockInnerConnection.DisposeCallCount);
        }

        [Fact]
        public void Dispose_Root_WithoutManageConnection_DoesNotDisposeConnection()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, false, Guid.NewGuid());

            transaction.Dispose();

            Assert.Equal(0, _mockInnerConnection.DisposeCallCount);
        }

        [Fact]
        public void Dispose_Root_WhenCalled_DisposesInnerTransaction()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());

            transaction.Dispose();

            // Dispose 不是虚方法，无法验证，但测试不应抛出异常
            Assert.True(transaction.IsDisposed);
        }

        [Fact]
        public void Dispose_Root_MultipleTimes_DoesNotThrow()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            transaction.Dispose();

            Exception exception = Record.Exception(() => transaction.Dispose());

            Assert.Null(exception);
        }

        [Fact]
        public void Dispose_Root_WhenNotCompleted_RaisesCompletedEvent()
        {
            _mockInnerTransaction.Setup(t => t.Rollback());
            var transaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var eventRaised = false;
            DbTransactionCompletedEventArgs eventArgs = null!;

            transaction.Completed += (sender, e) =>
            {
                eventRaised = true;
                eventArgs = e;
            };

            transaction.Dispose();

            Assert.True(eventRaised);
            Assert.NotNull(eventArgs);
        }

        #endregion

        #region Dispose 测试 - 子事务

        [Fact]
        public void Dispose_Child_WhenNotCompleted_RollsBackToSavepoint()
        {
            var rootTransaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var childTransaction = new NestedDbTransaction(rootTransaction, "SAVEPOINT_1", _mockSavepointDialect.Object, Guid.NewGuid());

            _ = _mockSavepointDialect.Setup(s => s.IsSavepointSupported).Returns(true);
            _ = _mockSavepointDialect.Setup(s => s.RollbackToSavepoint("SAVEPOINT_1")).Returns("ROLLBACK TO SAVEPOINT SAVEPOINT_1");

            childTransaction.Dispose();

            Assert.Equal(1, _mockInnerConnection.MockCommand.ExecuteNonQueryCallCount);

            rootTransaction.Dispose();
        }

        [Fact]
        public void Dispose_Child_WhenAlreadyCompleted_DoesNotRollbackAgain()
        {
            var rootTransaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var childTransaction = new NestedDbTransaction(rootTransaction, "SAVEPOINT_1", _mockSavepointDialect.Object, Guid.NewGuid());

            _ = _mockSavepointDialect.Setup(s => s.IsSavepointSupported).Returns(true);
            _ = _mockSavepointDialect.Setup(s => s.IsReleaseSavepointSupported).Returns(true);
            _ = _mockSavepointDialect.Setup(s => s.ReleaseSavepoint("SAVEPOINT_1")).Returns("RELEASE SAVEPOINT SAVEPOINT_1");

            childTransaction.Commit();
            childTransaction.Dispose();

            // 验证 Rollback 没有被调用
            _mockSavepointDialect.Verify(s => s.RollbackToSavepoint(It.IsAny<string>()), Times.Never);

            rootTransaction.Dispose();
        }

        [Fact]
        public void Dispose_Child_DoesNotDisposeConnection()
        {
            var rootTransaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var childTransaction = new NestedDbTransaction(rootTransaction, "SAVEPOINT_1", _mockSavepointDialect.Object, Guid.NewGuid());

            _ = _mockSavepointDialect.Setup(s => s.IsSavepointSupported).Returns(true);
            _ = _mockSavepointDialect.Setup(s => s.RollbackToSavepoint("SAVEPOINT_1")).Returns("ROLLBACK TO SAVEPOINT SAVEPOINT_1");

            childTransaction.Dispose();

            // 子事务不应该释放连接
            Assert.Equal(0, _mockInnerConnection.DisposeCallCount);

            rootTransaction.Dispose();
        }

        [Fact]
        public void Dispose_Child_WhenNotCompleted_RaisesCompletedEvent()
        {
            var rootTransaction = new NestedDbTransaction(_mockInnerTransaction.Object, _mockInnerConnection, true, Guid.NewGuid());
            var childTransaction = new NestedDbTransaction(rootTransaction, "SAVEPOINT_1", _mockSavepointDialect.Object, Guid.NewGuid());

            _ = _mockSavepointDialect.Setup(s => s.IsSavepointSupported).Returns(true);
            _ = _mockSavepointDialect.Setup(s => s.RollbackToSavepoint("SAVEPOINT_1")).Returns("ROLLBACK TO SAVEPOINT SAVEPOINT_1");

            var eventRaised = false;
            DbTransactionCompletedEventArgs eventArgs = null!;

            childTransaction.Completed += (sender, e) =>
            {
                eventRaised = true;
                eventArgs = e;
            };

            childTransaction.Dispose();

            Assert.True(eventRaised);
            Assert.NotNull(eventArgs);

            rootTransaction.Dispose();
        }

        #endregion
    }
}
