using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Xbc.DbAccess.Diagnostics.Tests
{
    /// <summary>
    /// DefaultDbConnectionTracker 的单元测试。
    /// </summary>
    public class DefaultDbConnectionTrackerTests
    {
        [Fact]
        public void Constructor_正确初始化_创建成功()
        {
            // Arrange
            var options = new DbConnectionTrackingOptions();

            // Act
            var tracker = new DefaultDbConnectionTracker(options);

            // Assert
            Assert.NotNull(tracker);
            Assert.NotNull(tracker.TrackingOptions);
        }

        [Fact]
        public void Constructor_选项为空_使用默认选项()
        {
            // Act
            var tracker = new DefaultDbConnectionTracker(null);

            // Assert
            Assert.NotNull(tracker);
            Assert.NotNull(tracker.TrackingOptions);
        }

        [Fact]
        public void StartTracking_开始追踪_返回追踪信息()
        {
            // Arrange
            var tracker = new DefaultDbConnectionTracker(null);
            var connectionId = Guid.NewGuid();
            var managerId = Guid.NewGuid();

            // Act
            var info = tracker.StartTracking(connectionId, managerId, 0);

            // Assert
            Assert.NotNull(info);
            Assert.Equal(connectionId, info.ConnectionId);
            Assert.Equal(managerId, info.ManagerId);
            Assert.Equal(DbConnectionTrackingState.Acquired, info.State);
            Assert.True(info.CreatedAt > DateTime.MinValue);
        }

        [Fact]
        public void StartTracking_重复追踪同一连接_抛出InvalidOperationException()
        {
            // Arrange
            var tracker = new DefaultDbConnectionTracker(null);
            var connectionId = Guid.NewGuid();
            var managerId = Guid.NewGuid();
            tracker.StartTracking(connectionId, managerId, 0);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => tracker.StartTracking(connectionId, managerId, 0));
        }

        [Fact]
        public void MarkActive_标记连接活动_状态变为Active()
        {
            // Arrange
            var tracker = new DefaultDbConnectionTracker(null);
            var connectionId = Guid.NewGuid();
            var managerId = Guid.NewGuid();
            var info = tracker.StartTracking(connectionId, managerId, 0);

            // Act
            tracker.MarkActive(connectionId, "SELECT * FROM Users");

            // Assert
            Assert.Equal(DbConnectionTrackingState.Active, info.State);
            Assert.Equal("SELECT * FROM Users", info.Sql);
        }

        [Fact]
        public void MarkActive_不存在的连接_不抛出异常()
        {
            // Arrange
            var tracker = new DefaultDbConnectionTracker(null);
            var connectionId = Guid.NewGuid();

            // Act & Assert
            var exception = Record.Exception(() => tracker.MarkActive(connectionId, "SELECT 1"));
            Assert.Null(exception);
        }

        [Fact]
        public void MarkRecycled_标记连接回收_状态变为Recycled()
        {
            // Arrange
            var tracker = new DefaultDbConnectionTracker(null);
            var connectionId = Guid.NewGuid();
            var managerId = Guid.NewGuid();
            var info = tracker.StartTracking(connectionId, managerId, 0);

            // Act
            tracker.MarkRecycled(connectionId);

            // Assert
            Assert.Equal(DbConnectionTrackingState.Recycled, info.State);
            Assert.True(info.RecycledAt > DateTime.MinValue);
        }

        [Fact]
        public void MarkRecycled_不存在的连接_不抛出异常()
        {
            // Arrange
            var tracker = new DefaultDbConnectionTracker(null);
            var connectionId = Guid.NewGuid();

            // Act & Assert
            var exception = Record.Exception(() => tracker.MarkRecycled(connectionId));
            Assert.Null(exception);
        }

        [Fact]
        public void MarkReacquired_标记连接重新获取_状态变为Acquired()
        {
            // Arrange
            var tracker = new DefaultDbConnectionTracker(null);
            var connectionId = Guid.NewGuid();
            var managerId = Guid.NewGuid();
            var info = tracker.StartTracking(connectionId, managerId, 0);
            tracker.MarkRecycled(connectionId);

            // Act
            tracker.MarkReacquired(connectionId, 0);

            // Assert
            Assert.Equal(DbConnectionTrackingState.Acquired, info.State);
            Assert.Equal(1, info.ReacquireCount);
        }

        [Fact]
        public void MarkReacquired_不存在的连接_不抛出异常()
        {
            // Arrange
            var tracker = new DefaultDbConnectionTracker(null);
            var connectionId = Guid.NewGuid();

            // Act & Assert
            var exception = Record.Exception(() => tracker.MarkReacquired(connectionId, 0));
            Assert.Null(exception);
        }

        [Fact]
        public void EndTracking_结束追踪_连接被移除()
        {
            // Arrange
            var tracker = new DefaultDbConnectionTracker(null);
            var connectionId = Guid.NewGuid();
            var managerId = Guid.NewGuid();
            var info = tracker.StartTracking(connectionId, managerId, 0);

            // Act
            tracker.EndTracking(connectionId);

            // Assert
            Assert.Equal(DbConnectionTrackingState.Released, info.State);
            Assert.True(info.ReleasedAt > DateTime.MinValue);
            Assert.DoesNotContain(tracker.FindAll(), c => c.ConnectionId == connectionId);
        }

        [Fact]
        public void EndTracking_不存在的连接_不抛出异常()
        {
            // Arrange
            var tracker = new DefaultDbConnectionTracker(null);
            var connectionId = Guid.NewGuid();

            // Act & Assert
            var exception = Record.Exception(() => tracker.EndTracking(connectionId));
            Assert.Null(exception);
        }

        [Fact]
        public void FindActive_获取活动连接列表_返回活动连接()
        {
            // Arrange
            var tracker = new DefaultDbConnectionTracker(null);
            var connectionId1 = Guid.NewGuid();
            var connectionId2 = Guid.NewGuid();
            var managerId = Guid.NewGuid();
            tracker.StartTracking(connectionId1, managerId, 0);
            tracker.StartTracking(connectionId2, managerId, 0);

            // Act
            var activeConnections = tracker.FindAll(x => x.IsInUse);

            // Assert
            Assert.Equal(2, activeConnections.Count);
            Assert.Contains(activeConnections, c => c.ConnectionId == connectionId1);
            Assert.Contains(activeConnections, c => c.ConnectionId == connectionId2);
        }

        [Fact]
        public void FindActive_释放后_不再包含已释放连接()
        {
            // Arrange
            var tracker = new DefaultDbConnectionTracker(null);
            var connectionId1 = Guid.NewGuid();
            var connectionId2 = Guid.NewGuid();
            var managerId = Guid.NewGuid();
            tracker.StartTracking(connectionId1, managerId, 0);
            tracker.StartTracking(connectionId2, managerId, 0);
            tracker.EndTracking(connectionId1);

            // Act
            var activeConnections = tracker.FindAll(x => x.IsInUse);

            // Assert
            Assert.Single(activeConnections);
            Assert.DoesNotContain(activeConnections, c => c.ConnectionId == connectionId1);
            Assert.Contains(activeConnections, c => c.ConnectionId == connectionId2);
        }

        [Fact]
        public void FindAll_获取所有追踪连接_返回所有连接()
        {
            // Arrange
            var tracker = new DefaultDbConnectionTracker(null);
            var managerId = Guid.NewGuid();
            var connectionId1 = Guid.NewGuid();
            var connectionId2 = Guid.NewGuid();
            tracker.StartTracking(connectionId1, managerId, 0);
            tracker.StartTracking(connectionId2, managerId, 0);

            // Act
            var trackedConnections = tracker.FindAll();

            // Assert
            Assert.Equal(2, trackedConnections.Count);
            Assert.Contains(trackedConnections, c => c.ConnectionId == connectionId1);
            Assert.Contains(trackedConnections, c => c.ConnectionId == connectionId2);
        }
    }
}
