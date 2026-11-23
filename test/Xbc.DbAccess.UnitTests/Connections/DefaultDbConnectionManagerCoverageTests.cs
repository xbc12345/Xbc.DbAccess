using System.Data.Common;
using Moq;
using Xbc.DbAccess.Diagnostics;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Connections
{
    /// <summary>
    /// DefaultDbConnectionManager 补充覆盖测试：非本管理器连接的释放守卫、幂等释放、
    /// 释放异常吞并、释放窗口期事件与统计计数器。
    /// </summary>
    public class DefaultDbConnectionManagerCoverageTests
    {
        #region Release 测试

        [Fact]
        public void Release_WhenConnectionNotCreatedByManager_IgnoresConnection()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();
            var options = ConnectionTestHelper.CreatePoolOptions(enableStatistics: false);

            using var manager = new DefaultDbConnectionManager(options, factory.Object, tracker.Object);

            using var foreignConnection = new StubDbConnection();
            foreignConnection.Open();

            manager.Release(foreignConnection);

            // 非本管理器创建的连接不受管理器管理：不应被关闭或销毁
            Assert.Equal(0, foreignConnection.CloseCallCount);
            Assert.Equal(0, foreignConnection.DisposeCallCount);
        }

        [Fact]
        public async Task Release_Twice_SecondReleaseIsIdempotent()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();
            var options = ConnectionTestHelper.CreatePoolOptions(enableStatistics: false);

            using var manager = new DefaultDbConnectionManager(options, factory.Object, tracker.Object);

            var connection = (DefaultDbConnection)await manager.GetOrCreateAsync();
            var inner = (StubDbConnection)connection.UnderlyingConnection;

            manager.Release(connection);
            manager.Release(connection);

            // 第二次释放时连接已不在活动集合：幂等忽略，内部连接仅释放一次
            Assert.Equal(1, inner.DisposeCallCount);
        }

        [Fact]
        public async Task Release_WhenCloseThrows_SwallowsException()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();
            var options = ConnectionTestHelper.CreatePoolOptions(enableStatistics: false);

            using var manager = new DefaultDbConnectionManager(options, factory.Object, tracker.Object);

            var connection = (DefaultDbConnection)await manager.GetOrCreateAsync();
            var inner = (StubDbConnection)connection.UnderlyingConnection;
            inner.CloseException = new InvalidOperationException("关闭失败");

            // 释放路径不应向调用方传播异常，仅记录日志
            Exception exception = Record.Exception(() => manager.Release(connection));

            Assert.Null(exception);
            Assert.Equal(0, inner.DisposeCallCount);
        }

        #endregion

        #region Dispose 测试

        [Fact]
        public async Task Dispose_WhenConnectionDisposeThrows_SwallowsException()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();
            var options = ConnectionTestHelper.CreatePoolOptions(enableStatistics: false);

            var manager = new DefaultDbConnectionManager(options, factory.Object, tracker.Object);

            var connection = (DefaultDbConnection)await manager.GetOrCreateAsync();
            var inner = (StubDbConnection)connection.UnderlyingConnection;
            inner.DisposeException = new InvalidOperationException("释放失败");

            Exception exception = Record.Exception(manager.Dispose);

            Assert.Null(exception);
            Assert.True(manager.IsDisposed);
        }

        [Fact]
        public async Task Dispose_WhileDisposing_LifecycleEndedEventIsSkipped()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();
            var options = ConnectionTestHelper.CreatePoolOptions(enableStatistics: false);

            var manager = new DefaultDbConnectionManager(options, factory.Object, tracker.Object);

            var first = (DefaultDbConnection)await manager.GetOrCreateAsync();
            var second = (DefaultDbConnection)await manager.GetOrCreateAsync();
            ((StubDbConnection)first.UnderlyingConnection).DisposeDelay = TimeSpan.FromMilliseconds(300);// 拓宽释放窗口

            var disposeTask = Task.Run(() => manager.Dispose());
            ConnectionTestHelper.WaitForDisposing(manager);

            // 释放窗口期内连接生命周期结束：事件被跳过，由 Dispose 循环统一兜底
            second.Dispose();
            await disposeTask;

            Assert.True(manager.IsDisposed);
            tracker.Verify(t => t.EndTracking(second.ConnectionId), Times.Once);
        }

        #endregion

        #region 统计信息测试

        [Fact]
        public async Task Statistics_CountsRequestCreatedAndReleased()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();
            var options = ConnectionTestHelper.CreatePoolOptions();

            using var manager = new DefaultDbConnectionManager(options, factory.Object, tracker.Object);

            var connection = await manager.GetOrCreateAsync();
            manager.Release(connection);

            var statistics = (DefaultDbConnectionStatistics)manager.GetStatistics();

            Assert.Equal(1, statistics.TotalRequests);
            Assert.Equal(1, statistics.TotalCreated);
            Assert.Equal(1, statistics.TotalReleased);
            Assert.Equal(0, statistics.ActiveConnections);
        }

        [Fact]
        public async Task Statistics_WhenCreateFails_RecordsCreateFailed()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();
            var options = ConnectionTestHelper.CreatePoolOptions();

            using var manager = new DefaultDbConnectionManager(options, factory.Object, tracker.Object);

            factory.Setup(static f => f.CreateConnection())
                .Returns(() => new StubDbConnection { OpenException = new InvalidOperationException("打开失败") });

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.GetOrCreateAsync());

            var statistics = (DefaultDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(1, statistics.TotalRequests);
            Assert.Equal(1, statistics.TotalCreateFailed);
            Assert.Equal(0, statistics.TotalCreated);
        }

        #endregion
    }
}
