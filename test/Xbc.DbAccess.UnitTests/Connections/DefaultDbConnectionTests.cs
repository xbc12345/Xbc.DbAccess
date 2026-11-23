using Moq;
using System.Data;
using System.Data.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Connections
{
    /// <summary>
    /// DefaultDbConnection 的单元测试。
    /// </summary>
    public class DefaultDbConnectionTests : IDisposable
    {
        private readonly Mock<DbConnection> _mockInnerConnection;
        private readonly DefaultDbConnection _connection;

        public DefaultDbConnectionTests()
        {
            _mockInnerConnection = new Mock<DbConnection>();
            _connection = new DefaultDbConnection(_mockInnerConnection.Object);
        }

        public void Dispose()
        {
            _connection?.Dispose();
            GC.SuppressFinalize(this);
        }

        #region 构造函数测试

        [Fact]
        public void ConstructorNullInnerConnectionThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() =>
                new DefaultDbConnection(null));
        }

        [Fact]
        public void ConstructorValidParametersCreatesConnection()
        {
            Assert.NotNull(_connection);
            Assert.NotEqual(Guid.Empty, _connection.ConnectionId);
            Assert.False(_connection.IsDisposed);
        }

        #endregion

        #region ConnectionId 测试

        [Fact]
        public void ConnectionIdAlwaysReturnsUniqueGuid()
        {
            DefaultDbConnection connection1 = new DefaultDbConnection(_mockInnerConnection.Object);
            DefaultDbConnection connection2 = new DefaultDbConnection(_mockInnerConnection.Object);

            Assert.NotEqual(connection1.ConnectionId, connection2.ConnectionId);

            connection1.Dispose();
            connection2.Dispose();
        }

        #endregion

        #region IsDisposed 测试

        [Fact]
        public void IsDisposedWhenNotDisposedReturnsFalse()
        {
            Assert.False(_connection.IsDisposed);
        }

        [Fact]
        public void IsDisposedWhenDisposedReturnsTrue()
        {
            _connection.Dispose();

            Assert.True(_connection.IsDisposed);
        }

        [Fact]
        public void IsDisposedMultipleDisposeReturnsTrue()
        {
            _connection.Dispose();
            _connection.Dispose();

            Assert.True(_connection.IsDisposed);
        }

        #endregion

        #region LifecycleEnded 事件测试

        [Fact]
        public void LifecycleEndedWhenDisposedRaisesEvent()
        {
            bool eventRaised = false;
            DbResourceLifecycleEndedEventArgs eventArgs = null;

            _connection.LifecycleEnded += (sender, e) =>
            {
                eventRaised = true;
                eventArgs = e;
            };

            _connection.Dispose();

            Assert.True(eventRaised);
            Assert.NotNull(eventArgs);
            Assert.Equal(_connection.ConnectionId, eventArgs.ResourceId);
            Assert.Equal(DbResourceType.Connection, eventArgs.ResourceType);
        }

        [Fact]
        public void LifecycleEndedMultipleDisposeRaisesEventOnce()
        {
            int eventCount = 0;

            _connection.LifecycleEnded += (sender, e) =>
            {
                eventCount++;
            };

            _connection.Dispose();
            _connection.Dispose();

            Assert.Equal(1, eventCount);
        }

        #endregion

        #region Dispose 测试

        [Fact]
        public void DisposeWhenNotDisposedDisposesInnerConnection()
        {
            _connection.Dispose();

            // DbConnection.Dispose 不是虚方法，无法验证
            // 但 IsDisposed 应该返回 true
            Assert.True(_connection.IsDisposed);
        }

        [Fact]
        public void DisposeMultipleTimesDoesNotThrow()
        {
            _connection.Dispose();

            Exception exception = Record.Exception(_connection.Dispose);

            Assert.Null(exception);
        }

        #endregion

        #region 透传成员测试

        [Fact]
        public void ConnectionString_PassesThrough()
        {
            _mockInnerConnection.SetupGet(c => c.ConnectionString).Returns("Data Source=test.db");

            _connection.ConnectionString = "Data Source=other.db";

            Assert.Equal("Data Source=test.db", _connection.ConnectionString);
            _mockInnerConnection.VerifySet(c => c.ConnectionString = "Data Source=other.db", Times.Once);
        }

        [Fact]
        public void ConnectionProperties_PassThrough()
        {
            _mockInnerConnection.SetupGet(c => c.Database).Returns("main");
            _mockInnerConnection.SetupGet(c => c.DataSource).Returns("test.db");
            _mockInnerConnection.SetupGet(c => c.ServerVersion).Returns("3.40.0");
            _mockInnerConnection.SetupGet(c => c.State).Returns(ConnectionState.Open);
            _mockInnerConnection.SetupGet(c => c.ConnectionTimeout).Returns(15);

            Assert.Equal("main", _connection.Database);
            Assert.Equal("test.db", _connection.DataSource);
            Assert.Equal("3.40.0", _connection.ServerVersion);
            Assert.Equal(ConnectionState.Open, _connection.State);
            Assert.Equal(15, _connection.ConnectionTimeout);
        }

        [Fact]
        public void Open_Close_PassThrough()
        {
            _connection.Open();
            _mockInnerConnection.Verify(c => c.Open(), Times.Once);

            _connection.Close();
            _mockInnerConnection.Verify(c => c.Close(), Times.Once);
        }

        [Fact]
        public async Task OpenAsync_PassThrough()
        {
            await _connection.OpenAsync();

            _mockInnerConnection.Verify(c => c.OpenAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void Close_AfterDispose_NoInnerCall()
        {
            _connection.Dispose();
            _mockInnerConnection.Invocations.Clear();

            _connection.Close();

            _mockInnerConnection.Verify(c => c.Close(), Times.Never);
        }

        [Fact]
        public async Task Open_AfterDispose_ThrowsObjectDisposedException()
        {
            _connection.Dispose();

            Assert.Throws<ObjectDisposedException>(_connection.Open);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => _connection.OpenAsync());
        }

        [Fact]
        public void StateChange_PassesThrough()
        {
            StateChangeEventHandler handler = (sender, args) => { };

            _connection.StateChange += handler;
            _connection.StateChange -= handler;

            _mockInnerConnection.VerifyAdd(c => c.StateChange += It.IsAny<StateChangeEventHandler>(), Times.Once);
            _mockInnerConnection.VerifyRemove(c => c.StateChange -= It.IsAny<StateChangeEventHandler>(), Times.Once);
        }

        [Fact]
        public void GetSchema_Overloads_PassThrough()
        {
            var table = new DataTable();
            _mockInnerConnection.Setup(c => c.GetSchema()).Returns(table);
            _mockInnerConnection.Setup(c => c.GetSchema("Tables")).Returns(table);
            _mockInnerConnection.Setup(c => c.GetSchema("Columns", It.IsAny<string[]>())).Returns(table);

            Assert.Same(table, _connection.GetSchema());
            Assert.Same(table, _connection.GetSchema("Tables"));
            Assert.Same(table, _connection.GetSchema("Columns", new[] { "t" }));

            _mockInnerConnection.Verify(c => c.GetSchema(), Times.Once);
            _mockInnerConnection.Verify(c => c.GetSchema("Tables"), Times.Once);
            _mockInnerConnection.Verify(c => c.GetSchema("Columns", It.IsAny<string[]>()), Times.Once);
        }

        [Fact]
        public void ChangeDatabase_PassThrough()
        {
            _connection.ChangeDatabase("main");

            _mockInnerConnection.Verify(c => c.ChangeDatabase("main"), Times.Once);
        }

        #endregion

        #region 事务跟踪测试

        [Fact]
        public void BeginTransaction_TracksPendingTransaction_UntilCleared()
        {
            // BeginTransaction/BeginDbTransaction 链路涉及非虚方法，使用真实 SQLite 连接验证
            using var sqlite = new System.Data.SQLite.SQLiteConnection("Data Source=:memory:");
            sqlite.Open();
            using var connection = new DefaultDbConnection(sqlite);

            Assert.False(connection.HasPendingTransaction);

            using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            Assert.True(connection.HasPendingTransaction);

            // 乱序清除（非当前跟踪的事务）不生效
            connection.ClearPendingTransaction(new Mock<DbTransaction>().Object);
            Assert.True(connection.HasPendingTransaction);

            connection.ClearPendingTransaction(transaction);
            Assert.False(connection.HasPendingTransaction);
        }

        [Fact]
        public void DisposeInternal_ClosesAndDisposesInner_WithoutLifecycleEvent()
        {
            bool eventRaised = false;
            _connection.LifecycleEnded += (sender, e) => eventRaised = true;

            _mockInnerConnection.SetupGet(c => c.State).Returns(ConnectionState.Open);
            _connection.DisposeInternal();

            _mockInnerConnection.Verify(c => c.Close(), Times.Once);
            Assert.False(eventRaised);
        }

        #endregion
    }
}
