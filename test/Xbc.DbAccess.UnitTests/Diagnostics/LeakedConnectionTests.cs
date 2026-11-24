using Xbc.DbAccess.Diagnostics;
using Xbc.DbAccess.Sqlite;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Diagnostics
{
    /// <summary>
    /// 连接泄漏查询（<see cref="DbAccessDiagnostics.GetLeakedConnections"/>）的判定与快照行为验证。
    /// <para>测试工厂启用追踪与统计后会注册到全局连接管理器注册表，与直接操作注册表的测试类串行执行。</para>
    /// </summary>
    [Collection("DbAccessMetricsRegistrySerial")]
    public class LeakedConnectionTests : IDisposable
    {
        /// <summary>可能泄漏阈值，取小值加速判定。</summary>
        private static readonly TimeSpan PossibleThreshold = TimeSpan.FromMilliseconds(20);

        private readonly IDbAccessFactory factory;
        private readonly IDbConnectionTracker tracker;
        private readonly Guid managerId;

        public LeakedConnectionTests()
        {
            // 严重阈值取不可达到的大值，默认工厂只验证可能泄漏级别；严重级别在专属测试中用短阈值工厂验证
            factory = CreateFactory(PossibleThreshold, TimeSpan.FromHours(1));
            var manager = factory.ConnectionManager;
            managerId = manager.ManagerId;
            tracker = manager.ConnectionTracker;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            (factory as IDisposable)?.Dispose();
        }

        [Fact]
        public void 未超过持有时间阈值的连接不出现在查询结果中()
        {
            var id = StartTrackedConnection();

            var leaks = DbAccessDiagnostics.GetLeakedConnections();

            Assert.DoesNotContain(leaks, x => x.TrackingInfo.ConnectionId == id);
        }

        [Fact]
        public void 超过可能阈值的连接以可能级别出现在结果中()
        {
            var id = StartTrackedConnection();
            Thread.Sleep(40);

            var leak = FindLeak(id);

            Assert.False(leak.IsCritical);
            Assert.Equal(managerId, leak.TrackingInfo.ManagerId);
            Assert.True(leak.TrackingInfo.CurrentHoldDuration >= PossibleThreshold);
            Assert.False(string.IsNullOrEmpty(leak.ProviderName));
        }

        [Fact]
        public void 超过严重阈值的连接以严重级别出现在结果中()
        {
            var shortThresholdFactory = CreateFactory(PossibleThreshold, TimeSpan.FromMilliseconds(50));
            try
            {
                var manager = shortThresholdFactory.ConnectionManager;
                var id = Guid.NewGuid();
                manager.ConnectionTracker.StartTracking(id, manager.ManagerId, 0);
                Thread.Sleep(100);

                var leak = FindLeak(id);

                Assert.True(leak.IsCritical);
            }
            finally
            {
                (shortThresholdFactory as IDisposable)?.Dispose();
            }
        }

        [Fact]
        public void 已释放的连接不再出现在查询结果中()
        {
            var id = StartTrackedConnection();
            Thread.Sleep(40);
            tracker.EndTracking(id);

            var leaks = DbAccessDiagnostics.GetLeakedConnections();

            Assert.DoesNotContain(leaks, x => x.TrackingInfo.ConnectionId == id);
        }

        [Fact]
        public void 已回收待复用的连接不算泄漏()
        {
            var id = StartTrackedConnection();
            tracker.MarkRecycled(id);
            Thread.Sleep(40);

            var leaks = DbAccessDiagnostics.GetLeakedConnections();

            Assert.DoesNotContain(leaks, x => x.TrackingInfo.ConnectionId == id);
        }

        [Fact]
        public void 开启堆栈记录时结果包含获取连接时的调用堆栈()
        {
            var id = StartTrackedConnection();
            Thread.Sleep(40);

            var leak = FindLeak(id);

            Assert.NotNull(leak.TrackingInfo.AcquireStackTrace);
            Assert.Contains(nameof(LeakedConnectionTests), leak.TrackingInfo.AcquireStackTrace);
        }

        /// <summary>
        /// 在默认工厂的追踪器中登记一个借出连接并返回其连接ID。
        /// </summary>
        private Guid StartTrackedConnection()
        {
            var id = Guid.NewGuid();
            tracker.StartTracking(id, managerId, 0);
            return id;
        }

        /// <summary>
        /// 按连接ID从泄漏查询结果中查找唯一条目，未找到时断言失败。
        /// </summary>
        private static LeakedConnectionInfo FindLeak(Guid connectionId)
        {
            var leak = DbAccessDiagnostics.GetLeakedConnections()
                .SingleOrDefault(x => x.TrackingInfo.ConnectionId == connectionId);
            Assert.NotNull(leak);
            return leak;
        }

        /// <summary>
        /// 创建启用连接追踪与统计的 SQLite 工厂，泄漏判定阈值可自定义。
        /// </summary>
        private static IDbAccessFactory CreateFactory(TimeSpan possibleThreshold, TimeSpan criticalThreshold)
        {
            return new SqliteAccessFactory(new DbConnectionOptions
            {
                ConnectionString = "Data Source=:memory:",
                EnableStatistics = true,
                PoolOptions = { Enabled = false },
                ConnectionTrackingOptions = { Enabled = true, RecordStackTrace = true },
                PossibleLeakHoldTimeThreshold = possibleThreshold,
                CriticalLeakHoldTimeThreshold = criticalThreshold,
            });
        }
    }
}
