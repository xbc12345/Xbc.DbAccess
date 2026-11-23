using System.Data;
using System.Data.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Connections
{
    /// <summary>
    /// PoolingDbConnection 补充覆盖测试：激活（快速/慢速路径、健康检查）、挂起事务防护、
    /// 已回收/已释放守卫以及数据库上下文重置。
    /// </summary>
    public class PoolingDbConnectionActivateTests : IDisposable
    {
        private readonly List<PoolingDbConnection> _trackedConnections = new List<PoolingDbConnection>();

        public void Dispose()
        {
            foreach (var connection in _trackedConnections)
            {
                if (!connection.IsDisposed)
                    connection.ActualDispose();
            }

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 创建并打开一个池化连接（内部连接状态 Open，初始数据库 StubDb）。
        /// </summary>
        private PoolingDbConnection CreateOpenedConnection()
        {
            var connection = new PoolingDbConnection(new StubDbConnection());
            connection.Open();
            _trackedConnections.Add(connection);
            return connection;
        }

        private static StubDbConnection InnerOf(PoolingDbConnection connection)
        {
            return (StubDbConnection)connection.UnderlyingConnection;
        }

        #region ActivateAsync - 快速路径

        [Fact]
        public async Task ActivateAsync_WithinSkipIntervalAndOpen_SkipsValidationAndReturnsTrue()
        {
            var connection = CreateOpenedConnection();

            bool activated = await connection.ActivateAsync(TimeSpan.FromSeconds(10), 3, "SELECT 1");

            Assert.True(activated);
            Assert.True(connection.IsActive);
            Assert.Equal(0, InnerOf(connection).CommandStub.ScalarCallCount);// 未执行健康检查
        }

        [Fact]
        public async Task ActivateAsync_WithPendingTransaction_ReturnsFalse()
        {
            var connection = CreateOpenedConnection();
            _ = connection.BeginTransaction();

            bool activated = await connection.ActivateAsync(TimeSpan.FromSeconds(10), 3, "SELECT 1");

            // 挂事务的连接不可激活复用，判定失败由调用方销毁重建（ActivateAsync 不改动连接状态）
            Assert.False(activated);
        }

        [Fact]
        public async Task ActivateAsync_WhenFastPathResetThrows_ReturnsFalse()
        {
            var connection = CreateOpenedConnection();
            var inner = InnerOf(connection);
            connection.ChangeDatabase("OtherDb");// 数据库被切换过，激活时需重置
            inner.ChangeDatabaseException = new InvalidOperationException("重置失败");

            bool activated = await connection.ActivateAsync(TimeSpan.FromSeconds(10), 3, "SELECT 1");

            Assert.False(activated);
        }

        [Fact]
        public async Task ActivateAsync_AfterDatabaseChanged_ResetsToInitialDatabase()
        {
            var connection = CreateOpenedConnection();
            var inner = InnerOf(connection);
            connection.ChangeDatabase("OtherDb");
            connection.Close();// 回池（无管理器订阅回收事件）

            bool activated = await connection.ActivateAsync(TimeSpan.FromSeconds(10), 3, "SELECT 1");

            // 复用时重置回初始数据库，避免残留访问其他数据库
            Assert.True(activated);
            Assert.Equal("StubDb", inner.CurrentDatabase);
        }

        #endregion

        #region ActivateAsync - 慢速路径

        [Fact]
        public async Task ActivateAsync_WhenClosed_OpensAndValidates_ReturnsTrue()
        {
            var connection = CreateOpenedConnection();
            InnerOf(connection).SetState(ConnectionState.Closed);

            bool activated = await connection.ActivateAsync(TimeSpan.Zero, 3, "SELECT 1");

            Assert.True(activated);
            Assert.True(connection.IsActive);
            Assert.Equal(ConnectionState.Open, InnerOf(connection).State);
        }

        [Fact]
        public async Task ActivateAsync_WhenBroken_ClosesAndReopens_ReturnsTrue()
        {
            var connection = CreateOpenedConnection();
            var inner = InnerOf(connection);
            inner.SetState(ConnectionState.Broken);

            bool activated = await connection.ActivateAsync(TimeSpan.Zero, 3, "SELECT 1");

            Assert.True(activated);
            Assert.True(inner.CloseCallCount >= 1);// 先关闭损坏连接
            Assert.Equal(ConnectionState.Open, inner.State);
        }

        [Fact]
        public async Task ActivateAsync_WhenSlowPathResetThrows_ClosesAndReturnsFalse()
        {
            var connection = CreateOpenedConnection();
            var inner = InnerOf(connection);
            inner.SetState(ConnectionState.Closed);
            inner.CurrentDatabase = "OtherDb";
            inner.ChangeDatabaseException = new InvalidOperationException("重置失败");

            bool activated = await connection.ActivateAsync(TimeSpan.Zero, 3, "SELECT 1");

            Assert.False(activated);
            Assert.Equal(1, inner.CloseCallCount);// 重置失败关闭连接
        }

        [Fact]
        public async Task ActivateAsync_WhenValidationResultIsNotOne_ClosesAndReturnsFalse()
        {
            var connection = CreateOpenedConnection();
            var inner = InnerOf(connection);
            inner.SetState(ConnectionState.Closed);
            inner.CommandStub.ScalarResult = 2;// 健康检查返回非 1

            bool activated = await connection.ActivateAsync(TimeSpan.Zero, 3, "SELECT 1");

            Assert.False(activated);
            Assert.Equal(1, inner.CloseCallCount);
        }

        [Fact]
        public async Task ActivateAsync_WhenValidationCommandThrows_ClosesAndReturnsFalse()
        {
            var connection = CreateOpenedConnection();
            var inner = InnerOf(connection);
            inner.SetState(ConnectionState.Closed);
            inner.CommandStub.ExecuteScalarException = new InvalidOperationException("健康检查失败");

            bool activated = await connection.ActivateAsync(TimeSpan.Zero, 3, "SELECT 1");

            Assert.False(activated);
            Assert.Equal(1, inner.CloseCallCount);
        }

        #endregion

        #region ChangeDatabase 守卫测试

        [Fact]
        public void ChangeDatabase_WhenActive_ChangesInnerDatabase()
        {
            var connection = CreateOpenedConnection();

            connection.ChangeDatabase("OtherDb");

            Assert.Equal("OtherDb", InnerOf(connection).CurrentDatabase);
        }

        [Fact]
        public void ChangeDatabase_AfterRecycle_ThrowsDbConnectionRecycledException()
        {
            var connection = CreateOpenedConnection();
            connection.Close();

            _ = Assert.Throws<DbConnectionRecycledException>(() => connection.ChangeDatabase("OtherDb"));
        }

        [Fact]
        public void ChangeDatabase_AfterDispose_ThrowsObjectDisposedException()
        {
            var connection = CreateOpenedConnection();
            connection.ActualDispose();

            _ = Assert.Throws<ObjectDisposedException>(() => connection.ChangeDatabase("OtherDb"));
        }

        #endregion

        #region GetSchema 守卫测试

        [Fact]
        public void GetSchema_AfterRecycle_ThrowsDbConnectionRecycledException()
        {
            var connection = CreateOpenedConnection();
            connection.Close();

            _ = Assert.Throws<DbConnectionRecycledException>(connection.GetSchema);
        }

        [Fact]
        public void GetSchema_Overloads_AfterRecycle_ThrowDbConnectionRecycledException()
        {
            var connection = CreateOpenedConnection();
            connection.Close();

            _ = Assert.Throws<DbConnectionRecycledException>(() => connection.GetSchema("Tables"));
            _ = Assert.Throws<DbConnectionRecycledException>(() => connection.GetSchema("Tables", Array.Empty<string>()));
        }

        [Fact]
        public void GetSchema_Overloads_AfterDispose_ThrowObjectDisposedException()
        {
            var connection = CreateOpenedConnection();
            connection.ActualDispose();

            _ = Assert.Throws<ObjectDisposedException>(connection.GetSchema);
            _ = Assert.Throws<ObjectDisposedException>(() => connection.GetSchema("Tables"));
            _ = Assert.Throws<ObjectDisposedException>(() => connection.GetSchema("Tables", Array.Empty<string>()));
        }

        #endregion

        #region CreateCommand 守卫测试

        [Fact]
        public void CreateCommand_AfterRecycleOrDispose_Throws()
        {
            var connection = CreateOpenedConnection();
            connection.Close();

            _ = Assert.Throws<DbConnectionRecycledException>(connection.CreateCommand);

            connection.ActualDispose();

            _ = Assert.Throws<ObjectDisposedException>(connection.CreateCommand);
        }

        #endregion

        #region BeginTransaction 守卫测试

        [Fact]
        public void BeginTransaction_AfterRecycleOrDispose_Throws()
        {
            var connection = CreateOpenedConnection();
            connection.Close();

            _ = Assert.Throws<DbConnectionRecycledException>(connection.BeginTransaction);

            connection.ActualDispose();

            _ = Assert.Throws<ObjectDisposedException>(connection.BeginTransaction);
        }

        #endregion

        #region 挂起事务归还测试

        [Fact]
        public void Close_WithPendingTransaction_DestroysWithoutRecycleEvent()
        {
            var connection = CreateOpenedConnection();
            bool recycled = false;
            connection.Recycled += (sender, e) => recycled = true;

            _ = connection.BeginTransaction();
            connection.Close();

            // 挂有未完成事务的连接不能回池，直接销毁且不触发回收事件
            Assert.True(connection.IsDisposed);
            Assert.False(recycled);
        }

        #endregion
    }
}
