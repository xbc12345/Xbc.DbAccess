using Moq;
using Xbc.DbAccess.Common;
using Xbc.DbAccess.Diagnostics;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Diagnostics
{
    /// <summary>
    /// 连接管理器注册表为全局静态状态，直接操作注册表的测试类需串行执行（注册/注销并行会互相污染）。
    /// </summary>
    [CollectionDefinition("DbAccessMetricsRegistrySerial", DisableParallelization = true)]
    public class DbAccessMetricsRegistrySerialCollection
    {
    }

    /// <summary>
    /// DbAccessMetrics 可观测仪表（池账本与泄漏指标）的采集测试。
    /// </summary>
    [Collection("DbAccessMetricsRegistrySerial")]
    public class DbAccessMetricsGaugeTests : IDisposable
    {
        private readonly Guid managerId = Guid.NewGuid();
        private readonly Mock<IDbConnectionManager> mockManager = new Mock<IDbConnectionManager>();
        private readonly Mock<IDbConnectionTracker> mockTracker = new Mock<IDbConnectionTracker>();

        public DbAccessMetricsGaugeTests()
        {
            mockManager.Setup(m => m.ManagerId).Returns(managerId);
            mockManager.Setup(m => m.ConnectionTracker).Returns(mockTracker.Object);
            mockTracker.Setup(t => t.FindAll(It.IsAny<Predicate<DbConnectionTrackingInfo>>()))
                .Returns((Predicate<DbConnectionTrackingInfo> predicate) => new List<DbConnectionTrackingInfo>());
        }

        /// <inheritdoc />
        public void Dispose()
        {
            DbAccessMetrics.UnregisterManager(managerId);
        }

        /// <summary>
        /// 注册管理器（辅助方法）。
        /// </summary>
        private void RegisterManager(TimeSpan possibleLeakThreshold, TimeSpan criticalLeakThreshold)
        {
            DbAccessMetrics.RegisterManager(mockManager.Object, "SqliteClient", possibleLeakThreshold, criticalLeakThreshold);
        }

        /// <summary>
        /// 创建连接追踪信息（辅助方法），通过负时间戳模拟已持有的时长。
        /// </summary>
        private static DbConnectionTrackingInfo CreateTrackingInfo(TimeSpan? holdDuration = null)
        {
            // AcquiredTimestamp 是 Stopwatch 刻度而非 TimeSpan 刻度（两者频率不同，Linux 上 Stopwatch.Frequency 通常为 1e9/s），
            // 需按频率换算回溯时长：当前刻度 - hold 时长对应的 Stopwatch 刻度
            var holdStopwatchTicks = holdDuration.HasValue
                ? (long)(holdDuration.Value.Ticks * (double)StopwatchClock.Frequency / TimeSpan.TicksPerSecond)
                : 0;
            return new DbConnectionTrackingInfo
            {
                ConnectionId = Guid.NewGuid(),
                CreatedAt = DateTime.Now,
                AcquiredAt = holdDuration.HasValue ? DateTime.Now.Add(-holdDuration.Value) : DateTime.Now,
                AcquiredTimestamp = StopwatchClock.ElapsedTicks - holdStopwatchTicks,
                State = DbConnectionTrackingState.Active
            };
        }

        [Fact]
        public void 未注册管理器_采集返回空()
        {
            Assert.Empty(DbAccessMetrics.ObservePoolActiveConnections());
            Assert.Empty(DbAccessMetrics.ObservePoolIdleConnections());
            Assert.Empty(DbAccessMetrics.ObservePoolMaxSize());
            Assert.Empty(DbAccessMetrics.ObservePossibleLeakConnections());
            Assert.Empty(DbAccessMetrics.ObserveCriticalLeakConnections());
        }

        [Fact]
        public void 池活动连接数_读取统计账本并携带provider标签()
        {
            mockManager.Setup(m => m.GetStatistics()).Returns(new PoolingDbConnectionStatistics
            {
                ActiveConnections = 3,
                IdleConnections = 2,
                MaxPoolSize = 10
            });
            RegisterManager(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));

            var measurement = DbAccessMetrics.ObservePoolActiveConnections().Single();
            Assert.Equal(3, measurement.Value);

            object provider = null;
            foreach (var tag in measurement.Tags)
            {
                if (tag.Key == "provider")
                    provider = tag.Value;
            }
            Assert.Equal("SqliteClient", provider);
        }

        [Fact]
        public void 池空闲数与最大容量_池模式上报()
        {
            mockManager.Setup(m => m.GetStatistics()).Returns(new PoolingDbConnectionStatistics
            {
                ActiveConnections = 3,
                IdleConnections = 2,
                MaxPoolSize = 10
            });
            RegisterManager(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));

            Assert.Equal(2, DbAccessMetrics.ObservePoolIdleConnections().Single().Value);
            Assert.Equal(10, DbAccessMetrics.ObservePoolMaxSize().Single().Value);
        }

        [Fact]
        public void 非池模式_池专属指标不上报()
        {
            mockManager.Setup(m => m.GetStatistics()).Returns(new DefaultDbConnectionStatistics());
            RegisterManager(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));

            Assert.Empty(DbAccessMetrics.ObservePoolIdleConnections());
            Assert.Empty(DbAccessMetrics.ObservePoolMaxSize());
        }

        [Fact]
        public void 泄漏指标_按持有时间阈值分级计数()
        {
            var connections = new List<DbConnectionTrackingInfo>
            {
                CreateTrackingInfo(TimeSpan.FromMinutes(6)),// 可能泄漏（超过 5 分钟）
                CreateTrackingInfo(TimeSpan.FromMinutes(16)),// 严重泄漏（超过 10 分钟，同时计入可能泄漏）
                CreateTrackingInfo(TimeSpan.FromMilliseconds(100))// 正常持有
            };
            mockTracker.Setup(t => t.FindAll(It.IsAny<Predicate<DbConnectionTrackingInfo>>()))
                .Returns((Predicate<DbConnectionTrackingInfo> predicate) => connections.Where(x => predicate(x)).ToList());
            RegisterManager(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));

            Assert.Equal(2, DbAccessMetrics.ObservePossibleLeakConnections().Single().Value);
            Assert.Equal(1, DbAccessMetrics.ObserveCriticalLeakConnections().Single().Value);
        }

        [Fact]
        public void 泄漏指标_未超过阈值计为零()
        {
            mockTracker.Setup(t => t.FindAll(It.IsAny<Predicate<DbConnectionTrackingInfo>>()))
                .Returns((Predicate<DbConnectionTrackingInfo> predicate) =>
                    new List<DbConnectionTrackingInfo> { CreateTrackingInfo(TimeSpan.FromMinutes(1)) }
                        .Where(x => predicate(x)).ToList());
            RegisterManager(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));

            Assert.Equal(0, DbAccessMetrics.ObservePossibleLeakConnections().Single().Value);
            Assert.Equal(0, DbAccessMetrics.ObserveCriticalLeakConnections().Single().Value);
        }

        [Fact]
        public void 注销后_不再产生测量()
        {
            mockManager.Setup(m => m.GetStatistics()).Returns(new PoolingDbConnectionStatistics
            {
                ActiveConnections = 3
            });
            RegisterManager(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));

            DbAccessMetrics.UnregisterManager(managerId);

            Assert.Empty(DbAccessMetrics.ObservePoolActiveConnections());
            Assert.Empty(DbAccessMetrics.ObservePossibleLeakConnections());
        }
    }
}
