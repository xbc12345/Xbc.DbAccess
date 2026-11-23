using Moq;
using System.Data.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Connections
{
    /// <summary>
    /// PoolingDbConnection 的单元测试。
    /// </summary>
    public class PoolingDbConnectionTests : IDisposable
    {
        private readonly Mock<DbConnection> _mockInnerConnection;
        private readonly PoolingDbConnection _connection;

        public PoolingDbConnectionTests()
        {
            _mockInnerConnection = new Mock<DbConnection>();
            _ = _mockInnerConnection.Setup(static c => c.Database).Returns("TestDb");
            _connection = new PoolingDbConnection(_mockInnerConnection.Object);
        }

        public void Dispose()
        {
            // 确保连接被实际释放
            if (!_connection.IsDisposed)
            {
                _connection.ActualDispose();
            }

            GC.SuppressFinalize(this);
        }

        #region 构造函数测试

        [Fact]
        public void Constructor_NullInnerConnection_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() =>
                new PoolingDbConnection(null));
        }

        [Fact]
        public void Constructor_ValidParameters_CreatesConnection()
        {
            Assert.NotNull(_connection);
            Assert.NotEqual(Guid.Empty, _connection.ConnectionId);
            Assert.False(_connection.IsDisposed);
            Assert.False(_connection.IsRecycled);
        }

        #endregion

        #region IsActive 测试

        [Fact]
        public void IsActive_WhenCreated_ReturnsFalse()
        {
            Assert.False(_connection.IsActive);
        }

        [Fact]
        public void IsActive_AfterOpen_ReturnsTrue()
        {
            _ = _mockInnerConnection.Setup(static c => c.Open());

            _connection.Open();

            Assert.True(_connection.IsActive);
        }

        [Fact]
        public void IsActive_AfterClose_ReturnsFalse()
        {
            _ = _mockInnerConnection.Setup(static c => c.Open());
            _connection.Open();

            _connection.Close();

            Assert.False(_connection.IsActive);
        }

        #endregion

        #region IsRecycled 测试

        [Fact]
        public void IsRecycled_WhenCreated_ReturnsFalse()
        {
            Assert.False(_connection.IsRecycled);
        }

        [Fact]
        public void IsRecycled_AfterClose_ReturnsTrue()
        {
            _ = _mockInnerConnection.Setup(static c => c.Open());
            _connection.Open();

            _connection.Close();

            Assert.True(_connection.IsRecycled);
        }

        [Fact]
        public void IsRecycled_AfterDispose_ReturnsTrue()
        {
            _connection.Dispose();

            Assert.True(_connection.IsRecycled);
        }

        [Fact]
        public void IsRecycled_AfterActualDispose_ReturnsTrue()
        {
            _connection.ActualDispose();

            Assert.True(_connection.IsRecycled);
            Assert.True(_connection.IsDisposed);
        }

        #endregion

        #region IsDisposed 测试

        [Fact]
        public void IsDisposed_WhenCreated_ReturnsFalse()
        {
            Assert.False(_connection.IsDisposed);
        }

        [Fact]
        public void IsDisposed_AfterActualDispose_ReturnsTrue()
        {
            _connection.ActualDispose();

            Assert.True(_connection.IsDisposed);
        }

        [Fact]
        public void IsDisposed_AfterClose_ReturnsFalse()
        {
            _ = _mockInnerConnection.Setup(static c => c.Open());
            _connection.Open();

            _connection.Close();

            Assert.False(_connection.IsDisposed);
        }

        #endregion

        #region Open 测试

        [Fact]
        public void Open_WhenNotDisposed_OpensInnerConnection()
        {
            _ = _mockInnerConnection.Setup(static c => c.Open());

            _connection.Open();

            _mockInnerConnection.Verify(static c => c.Open(), Times.Once);
        }

        [Fact]
        public void Open_WhenDisposed_ThrowsObjectDisposedException()
        {
            _connection.ActualDispose();

            _ = Assert.Throws<ObjectDisposedException>(_connection.Open);
        }

        [Fact]
        public void Open_WhenRecycled_ThrowsDbConnectionRecycledException()
        {
            _ = _mockInnerConnection.Setup(static c => c.Open());
            _connection.Open();
            _connection.Close();

            _ = Assert.Throws<DbConnectionRecycledException>(_connection.Open);
        }

        #endregion

        #region OpenAsync 测试

        [Fact]
        public async Task OpenAsync_WhenNotDisposed_OpensInnerConnection()
        {
            _ = _mockInnerConnection.Setup(static c => c.OpenAsync(default)).Returns(Task.CompletedTask);

            await _connection.OpenAsync();

            _mockInnerConnection.Verify(static c => c.OpenAsync(default), Times.Once);
        }

        [Fact]
        public async Task OpenAsync_WhenDisposed_ThrowsObjectDisposedException()
        {
            _connection.ActualDispose();

            _ = await Assert.ThrowsAsync<ObjectDisposedException>(_connection.OpenAsync);
        }

        #endregion

        #region Close 测试

        [Fact]
        public void Close_WhenActive_TriggersRecycledEvent()
        {
            _ = _mockInnerConnection.Setup(c => c.Open());
            _connection.Open();

            bool eventRaised = false;
            _connection.Recycled += (sender, e) =>
            {
                eventRaised = true;
            };

            _connection.Close();

            Assert.True(eventRaised);
        }

        [Fact]
        public void Close_WhenNotActive_DoesNotThrow()
        {
            Exception exception = Record.Exception(_connection.Close);

            Assert.Null(exception);
        }

        #endregion

        #region Dispose 测试

        [Fact]
        public void Dispose_TriggersRecycledEvent()
        {
            bool eventRaised = false;
            _connection.Recycled += (sender, e) =>
            {
                eventRaised = true;
            };

            _connection.Dispose();

            Assert.True(eventRaised);
        }

        [Fact]
        public void Dispose_MultipleTimes_DoesNotThrow()
        {
            _connection.Dispose();

            Exception exception = Record.Exception(_connection.Dispose);

            Assert.Null(exception);
        }

        #endregion

        #region ActualDispose 测试

        [Fact]
        public void ActualDispose_DisposesInnerConnection()
        {
            _connection.ActualDispose();

            Assert.True(_connection.IsDisposed);
        }

        [Fact]
        public void ActualDispose_TriggersLifecycleEndedEvent()
        {
            bool eventRaised = false;
            _connection.LifecycleEnded += (sender, e) =>
            {
                eventRaised = true;
            };

            _connection.ActualDispose();

            Assert.True(eventRaised);
        }

        [Fact]
        public void ActualDispose_MultipleTimes_DoesNotThrow()
        {
            _connection.ActualDispose();

            Exception exception = Record.Exception(_connection.ActualDispose);

            Assert.Null(exception);
        }

        #endregion

        #region Recycled 事件测试

        [Fact]
        public void Recycled_WhenClosed_RaisesEventWithCorrectArgs()
        {
            _ = _mockInnerConnection.Setup(c => c.Open());
            _connection.Open();

            DbConnectionRecycledEventArgs eventArgs = null;
            _connection.Recycled += (sender, e) =>
            {
                eventArgs = e;
            };

            _connection.Close();

            Assert.NotNull(eventArgs);
            Assert.Equal(_connection.ConnectionId, eventArgs.ConnectionId);
            Assert.True(eventArgs.RecycledAt > DateTime.MinValue);
        }

        #endregion

        #region LifecycleEnded 事件测试

        [Fact]
        public void LifecycleEnded_WhenActualDisposed_RaisesEvent()
        {
            bool eventRaised = false;
            DbResourceLifecycleEndedEventArgs eventArgs = null;

            _connection.LifecycleEnded += (sender, e) =>
            {
                eventRaised = true;
                eventArgs = e;
            };

            _connection.ActualDispose();

            Assert.True(eventRaised);
            Assert.NotNull(eventArgs);
            Assert.Equal(_connection.ConnectionId, eventArgs.ResourceId);
            Assert.Equal(DbResourceType.Connection, eventArgs.ResourceType);
        }

        #endregion

        #region LastActiveAt 测试

        [Fact]
        public void LastActiveAt_WhenCreated_IsMinValue()
        {
            Assert.Equal(DateTime.MinValue, _connection.LastActiveAt);
        }

        [Fact]
        public void LastActiveAt_AfterOpen_IsUpdated()
        {
            _ = _mockInnerConnection.Setup(static c => c.Open());
            DateTime beforeOpen = DateTime.Now;

            _connection.Open();

            Assert.True(_connection.LastActiveAt >= beforeOpen);
        }

        #endregion
    }
}
