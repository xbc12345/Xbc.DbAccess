using System.Data;
using System.Data.Common;
using System.Reflection;
using Moq;
using Xbc.DbAccess.Diagnostics;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Connections
{
    /// <summary>
    /// PoolingDbConnectionManager 补充覆盖测试：池预创建、统计计数器、空闲清理、
    /// 释放窗口期事件与竞态防御等此前未覆盖的行为分支。
    /// </summary>
    public class PoolingDbConnectionManagerCoverageTests
    {
        #region 辅助方法

        /// <summary>
        /// 通过反射调用私有清理方法，避免依赖 10 分钟的定时器间隔。
        /// </summary>
        private static void InvokeCleanupIdleConnections(PoolingDbConnectionManager manager)
        {
            var method = typeof(PoolingDbConnectionManager).GetMethod(
                "CleanupIdleConnections", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(method);
            method.Invoke(manager, null);
        }

        /// <summary>
        /// 通过反射调用私有预热方法，模拟首个借用请求触发的懒预热。
        /// </summary>
        private static void InvokeWarmup(PoolingDbConnectionManager manager)
        {
            var method = typeof(PoolingDbConnectionManager).GetMethod(
                "EnsurePoolWarmedUp", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(method);
            method.Invoke(manager, null);
        }

        /// <summary>
        /// 获取池化连接的内部 stub 连接，用于配置行为（延迟、异常等）。
        /// </summary>
        private static StubDbConnection InnerOf(PoolingDbConnection connection)
        {
            return (StubDbConnection)connection.UnderlyingConnection;
        }

        #endregion

        #region 池预创建测试

        [Fact]
        public void Warmup_WithMinPoolSize_PrecreatesIdleConnectionsAndTracks()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(minPoolSize: 3, maxPoolSize: 5), factory.Object, tracker.Object);

            // 预创建已移出构造函数：构造完成时池尚无连接
            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(0, statistics.IdleConnections);
            Assert.Equal(0, statistics.TotalCreated);
            tracker.Verify(
                t => t.StartTracking(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<double>()), Times.Never);

            // 触发懒预热：MinPoolSize 个连接全部就位并开始追踪
            InvokeWarmup(manager);

            statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(3, statistics.TotalCreated);
            Assert.Equal(3, statistics.IdleConnections);
            Assert.Equal(0, statistics.ActiveConnections);
            Assert.Equal(3, statistics.MinPoolSize);
            Assert.Equal(5, statistics.MaxPoolSize);
            Assert.Equal(3, statistics.AliveConnections);
            tracker.Verify(
                t => t.StartTracking(It.IsAny<Guid>(), It.IsAny<Guid>(), 0d), Times.Exactly(3));
        }

        [Fact]
        public async Task GetOrCreateAsync_FirstBorrow_TriggersLazyWarmup()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(minPoolSize: 3, maxPoolSize: 5), factory.Object, tracker.Object);

            var first = (PoolingDbConnection)await manager.GetOrCreateAsync();

            // 首借自动触发懒预热：预创建 3 条，借用者命中预创建库存取走 1 条
            Assert.False(first.IsDisposed);
            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(3, statistics.TotalCreated);
            Assert.Equal(1, statistics.TotalHits);
            Assert.Equal(2, statistics.IdleConnections);
            Assert.Equal(1, statistics.ActiveConnections);
        }

        [Fact]
        public void Warmup_WhenCreateConnectionThrows_RecordsCreateFailed()
        {
            var factory = new Mock<DbProviderFactory>();
            factory.Setup(static f => f.CreateConnection())
                .Throws(new InvalidOperationException("创建连接失败"));
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(minPoolSize: 2), factory.Object, tracker.Object);

            InvokeWarmup(manager);

            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(2, statistics.TotalCreateFailed);
            Assert.Equal(0, statistics.TotalCreated);
            Assert.Equal(0, statistics.IdleConnections);
        }

        [Fact]
        public void Warmup_WhenStartTrackingThrows_ActualDisposesConnection()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();
            tracker.Setup(static t => t.StartTracking(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<double>()))
                .Throws(new InvalidOperationException("追踪失败"));

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(minPoolSize: 1), factory.Object, tracker.Object);

            InvokeWarmup(manager);

            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();

            // 连接已创建并入队（TotalCreated），追踪失败走兜底销毁并计入创建失败
            Assert.Equal(1, statistics.TotalCreated);
            Assert.Equal(1, statistics.TotalCreateFailed);
            Assert.Equal(1, statistics.IdleConnections);
            tracker.Verify(
                t => t.StartTracking(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<double>()), Times.Once);
        }

        #endregion

        #region 获取连接测试

        [Fact]
        public async Task GetOrCreateAsync_WhenOpenFails_RecordsCreateFailedAndRereleasesLicense()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();
            var options = ConnectionTestHelper.CreatePoolOptions(maxPoolSize: 1);

            using var manager = new PoolingDbConnectionManager(options, factory.Object, tracker.Object);

            factory.Setup(static f => f.CreateConnection())
                .Returns(() => new StubDbConnection { OpenException = new InvalidOperationException("打开失败") });

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.GetOrCreateAsync());

            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(1, statistics.TotalRequests);
            Assert.Equal(1, statistics.TotalCreateFailed);
            Assert.Equal(0, statistics.TotalCreated);

            // 创建失败已归还许可证：恢复工厂后可继续创建，池未被失败请求占满
            factory.Setup(static f => f.CreateConnection()).Returns(() => new StubDbConnection());
            _ = await manager.GetOrCreateAsync();

            statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(2, statistics.TotalRequests);
            Assert.Equal(1, statistics.TotalCreated);
        }

        [Fact]
        public async Task GetOrCreateAsync_AfterRelease_HitsIdleConnection()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(), factory.Object, tracker.Object);

            var first = (PoolingDbConnection)await manager.GetOrCreateAsync();
            manager.Release(first);

            var second = (PoolingDbConnection)await manager.GetOrCreateAsync();

            Assert.Same(first, second);
            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(2, statistics.TotalRequests);
            Assert.Equal(1, statistics.TotalHits);
            Assert.Equal(1, statistics.TotalRecycled);
            Assert.Equal(1, statistics.TotalCreated);
            tracker.Verify(
                t => t.MarkReacquired(first.ConnectionId, It.IsAny<double>()), Times.Once);
        }

        [Fact]
        public async Task GetOrCreateAsync_SkipsDisposedIdleConnection()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(), factory.Object, tracker.Object);

            var first = (PoolingDbConnection)await manager.GetOrCreateAsync();
            manager.Release(first);

            // 模拟归还后连接被竞态销毁：空闲队列中滞留已释放的连接
            first.ActualDispose();

            var second = (PoolingDbConnection)await manager.GetOrCreateAsync();

            Assert.NotSame(first, second);
            Assert.True(first.IsDisposed);
            Assert.False(second.IsDisposed);

            // 已释放连接按空闲销毁计数，不命中空闲队列
            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(2, statistics.TotalRequests);
            Assert.Equal(2, statistics.TotalCreated);
            Assert.Equal(0, statistics.TotalHits);
            Assert.Equal(1, statistics.TotalRecycled);
            Assert.Equal(1, statistics.TotalReleased);
        }

        [Fact]
        public async Task GetOrCreateAsync_WhenPoolExhausted_RecordsQueuedAndExhausted()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();
            var options = ConnectionTestHelper.CreatePoolOptions(maxPoolSize: 1);
            options.PoolOptions.ConnectionWaitTimeout = TimeSpan.FromMilliseconds(100);

            using var manager = new PoolingDbConnectionManager(options, factory.Object, tracker.Object);

            _ = await manager.GetOrCreateAsync();
            _ = await Assert.ThrowsAsync<DbConnectionExhaustedException>(() => manager.GetOrCreateAsync());

            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(2, statistics.TotalRequests);
            Assert.Equal(1, statistics.TotalQueued);
            Assert.Equal(1, statistics.TotalExhausted);
            Assert.Equal(1, statistics.TotalCreated);
        }

        [Fact]
        public async Task GetOrCreateAsync_QueuedRequest_IsServedByRecycledConnection()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();
            var options = ConnectionTestHelper.CreatePoolOptions(maxPoolSize: 1);

            using var manager = new PoolingDbConnectionManager(options, factory.Object, tracker.Object);

            var first = await manager.GetOrCreateAsync();

            // 第二个请求进入排队等待，由归还的连接唤醒
            var waiter = manager.GetOrCreateAsync();
            await Task.Delay(200);
            manager.Release(first);

            var served = await waiter;

            Assert.NotNull(served);
            Assert.Same(first, served);
            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(2, statistics.TotalRequests);
            Assert.Equal(1, statistics.TotalQueued);
            Assert.Equal(1, statistics.TotalHits);
            Assert.Equal(1, statistics.TotalRecycled);
        }

        [Fact]
        public async Task GetOrCreateAsync_QueuedRequest_WhenCancelled_RecordsCancelled()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();
            var options = ConnectionTestHelper.CreatePoolOptions(maxPoolSize: 1);

            using var manager = new PoolingDbConnectionManager(options, factory.Object, tracker.Object);

            var first = await manager.GetOrCreateAsync();
            using var cts = new CancellationTokenSource();

            var waiter = manager.GetOrCreateAsync(cts.Token);
            await Task.Delay(200);
            cts.Cancel();

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
            manager.Release(first);

            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(2, statistics.TotalRequests);
            Assert.Equal(1, statistics.TotalQueued);
            Assert.Equal(1, statistics.TotalCancelled);
            Assert.Equal(0, statistics.TotalHits);
        }

        #endregion

        #region 释放连接测试

        [Fact]
        public void Release_WhenConnectionIsNotPooling_IgnoresConnection()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(), factory.Object, tracker.Object);

            using var foreignConnection = new StubDbConnection();
            foreignConnection.Open();

            manager.Release(foreignConnection);

            // 非池化连接不受管理器管理：不应被关闭或销毁
            Assert.Equal(0, foreignConnection.CloseCallCount);
            Assert.Equal(0, foreignConnection.DisposeCallCount);
        }

        #endregion

        #region 定时清理空闲连接测试

        [Fact]
        public async Task CleanupIdleConnections_DisposesAgedIdleConnection()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(maxIdleTime: TimeSpan.FromMilliseconds(1)),
                factory.Object, tracker.Object);

            var connection = (PoolingDbConnection)await manager.GetOrCreateAsync();
            manager.Release(connection);
            await Task.Delay(100);// 超过 MaxIdleTime

            InvokeCleanupIdleConnections(manager);

            Assert.True(connection.IsDisposed);
            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(0, statistics.IdleConnections);
            Assert.Equal(1, statistics.TotalRecycled);
            Assert.Equal(1, statistics.TotalReleased);
        }

        [Fact]
        public async Task CleanupIdleConnections_KeepsFreshConnectionsAndBreaksAtBoundary()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(), factory.Object, tracker.Object);

            var first = (PoolingDbConnection)await manager.GetOrCreateAsync();
            var second = (PoolingDbConnection)await manager.GetOrCreateAsync();
            manager.Release(first);
            manager.Release(second);

            InvokeCleanupIdleConnections(manager);

            // 连接空闲时长未超过 MaxIdleTime：全部保留
            Assert.False(first.IsDisposed);
            Assert.False(second.IsDisposed);
            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(2, statistics.IdleConnections);
            Assert.Equal(0, statistics.TotalReleased);
        }

        [Fact]
        public async Task CleanupIdleConnections_RespectsMinPoolSizeFloor()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(
                    minPoolSize: 2, maxIdleTime: TimeSpan.FromMilliseconds(1)),
                factory.Object, tracker.Object);

            var first = (PoolingDbConnection)await manager.GetOrCreateAsync();
            var second = (PoolingDbConnection)await manager.GetOrCreateAsync();
            var third = (PoolingDbConnection)await manager.GetOrCreateAsync();
            manager.Release(first);
            manager.Release(second);
            manager.Release(third);
            await Task.Delay(100);

            InvokeCleanupIdleConnections(manager);

            // 3 个超时空闲连接仅清理到 MinPoolSize 下限：队首 1 个销毁，其余 2 个保留
            Assert.True(first.IsDisposed);
            Assert.False(second.IsDisposed);
            Assert.False(third.IsDisposed);
            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(2, statistics.IdleConnections);
            Assert.Equal(1, statistics.TotalReleased);
        }

        [Fact]
        public async Task CleanupIdleConnections_SkipsDisposedIdleConnection()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(), factory.Object, tracker.Object);

            var connection = (PoolingDbConnection)await manager.GetOrCreateAsync();
            manager.Release(connection);
            connection.ActualDispose();

            InvokeCleanupIdleConnections(manager);

            // 已释放连接直接出队跳过：不再计入销毁，也不会重新入队
            Assert.True(connection.IsDisposed);
            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(0, statistics.IdleConnections);
            Assert.Equal(1, statistics.TotalReleased);
        }

        [Fact]
        public async Task CleanupIdleConnections_WhenManagerDisposed_ReturnsImmediately()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(), factory.Object, tracker.Object);

            var connection = (PoolingDbConnection)await manager.GetOrCreateAsync();
            manager.Release(connection);

            manager.Dispose();

            // 已释放管理器上手动触发清理：入口守卫直接返回，不抛异常
            Exception exception = Record.Exception(() => InvokeCleanupIdleConnections(manager));
            Assert.Null(exception);
            Assert.True(connection.IsDisposed);
        }

        [Fact]
        public async Task CleanupIdleConnections_WhenConnectionDisposeThrows_SwallowsException()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(maxIdleTime: TimeSpan.FromMilliseconds(1)),
                factory.Object, tracker.Object);

            var connection = (PoolingDbConnection)await manager.GetOrCreateAsync();
            InnerOf(connection).DisposeException = new InvalidOperationException("销毁失败");
            manager.Release(connection);
            await Task.Delay(100);

            Exception exception = Record.Exception(() => InvokeCleanupIdleConnections(manager));

            // 清理循环吞掉连接销毁异常，仅记录日志
            Assert.Null(exception);
            Assert.True(connection.IsDisposed);
        }

        #endregion

        #region 释放窗口期事件测试

        [Fact]
        public async Task Dispose_WhileDisposing_RecycledEventDisposesConnection()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(maxPoolSize: 5), factory.Object, tracker.Object);

            var first = (PoolingDbConnection)await manager.GetOrCreateAsync();
            var second = (PoolingDbConnection)await manager.GetOrCreateAsync();
            InnerOf(first).DisposeDelay = TimeSpan.FromMilliseconds(300);// 拓宽释放窗口

            var disposeTask = Task.Run(() => manager.Dispose());
            ConnectionTestHelper.WaitForDisposing(manager);

            // 释放窗口期内归还连接：直接销毁而非回收入池
            manager.Release(second);
            await disposeTask;

            Assert.True(second.IsDisposed);
            // 释放窗口期归还走直接销毁路径（生命周期终结至少结束追踪一次；与 Dispose 清理循环的相对时序
            // 决定兜底是否再触发一次，两种先后均合法，故用 AtLeastOnce 断言销毁终结语义）
            tracker.Verify(t => t.EndTracking(second.ConnectionId), Times.AtLeastOnce);
        }

        [Fact]
        public async Task LifecycleEnded_WhileDisposing_EndsTrackingWithoutRelease()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(maxPoolSize: 5), factory.Object, tracker.Object);

            var connection = (PoolingDbConnection)await manager.GetOrCreateAsync();

            // 直接置位释放中状态（不启动 Dispose 循环，避免与循环清理的退订时序竞态，保证断言确定性）
            var disposeStateField = typeof(DisposableBase).GetField("disposeState", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(disposeStateField);
            var disposeState = (DisposableState)disposeStateField.GetValue(manager);
            Assert.True(disposeState.TryDispose());

            // 释放窗口期内连接生命周期结束：不归还名额（信号量销毁竞态），但处理器必须结束追踪，
            // 否则清理回调正处理的连接（已出队、不在任何集合中）的追踪记录永久滞留
            var method = typeof(PoolingDbConnectionManager).GetMethod(
                "Connection_LifecycleEnded", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method.Invoke(manager, new object?[] { connection, new DbResourceLifecycleEndedEventArgs { ResourceId = connection.ConnectionId } });

            tracker.Verify(t => t.EndTracking(connection.ConnectionId), Times.Exactly(1));
        }

        #endregion

        #region 借用者取消统计测试

        [Fact]
        public async Task GetOrCreateAsync_WhenActivationCancelledByBorrower_RecordsDestroyedByCancellation()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(), factory.Object, tracker.Object);

            var first = (PoolingDbConnection)await manager.GetOrCreateAsync();
            manager.Release(first);

            // 已归还的连接处于关闭态且打开时被借用者取消：激活慢路径抛取消异常，
            // 连接按激活失败销毁（保守安全），单独计入取消销毁统计，随后由新建连接补位
            InnerOf(first).SetState(ConnectionState.Closed);
            InnerOf(first).OpenException = new OperationCanceledException("借用者取消");

            var second = (PoolingDbConnection)await manager.GetOrCreateAsync();

            Assert.NotSame(first, second);
            Assert.True(first.IsDisposed);
            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(1, statistics.TotalDestroyedByCancellation);
            Assert.Equal(2, statistics.TotalCreated);
            Assert.Equal(0, statistics.TotalHits);
            Assert.Equal(1, statistics.TotalReleased);
        }

        [Fact]
        public async Task GetOrCreateAsync_WhenCreateCancelledByBorrower_RecordsDestroyedByCancellation()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(), factory.Object, tracker.Object);

            factory.Setup(static f => f.CreateConnection())
                .Returns(() => new StubDbConnection { OpenException = new OperationCanceledException("借用者取消") });

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.GetOrCreateAsync(cts.Token));

            // 创建阶段借用者取消：结局为创建失败，连接销毁单独计入取消销毁统计
            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(1, statistics.TotalRequests);
            Assert.Equal(1, statistics.TotalCreateFailed);
            Assert.Equal(1, statistics.TotalDestroyedByCancellation);
            Assert.Equal(0, statistics.TotalCreated);
        }

        #endregion

        #region 连接最大寿命测试

        [Fact]
        public async Task Release_WhenConnectionLifetimeExpired_DestroysInsteadOfEnqueue()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(maxLifetime: TimeSpan.FromMilliseconds(50)),
                factory.Object, tracker.Object);

            var connection = (PoolingDbConnection)await manager.GetOrCreateAsync();
            await Task.Delay(100);// 超过 MaxLifetime
            manager.Release(connection);

            // 归还时超龄：不入空闲队列直接销毁换新，名额归还为可创建余量
            Assert.True(connection.IsDisposed);
            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(0, statistics.IdleConnections);
            Assert.Equal(0, statistics.TotalRecycled);
            Assert.Equal(1, statistics.TotalReleased);
            Assert.Equal(1, statistics.TotalCreated);

            // 下一个借用者现场重建，不命中已销毁的连接
            var second = (PoolingDbConnection)await manager.GetOrCreateAsync();

            Assert.NotSame(connection, second);
            Assert.False(second.IsDisposed);
            statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(2, statistics.TotalCreated);
            Assert.Equal(2, statistics.TotalRequests);
            Assert.Equal(0, statistics.TotalHits);
        }

        [Fact]
        public async Task GetOrCreateAsync_WhenWarmedConnectionLifetimeExpired_DisposesAndRecreates()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(minPoolSize: 1, maxLifetime: TimeSpan.FromMilliseconds(50)),
                factory.Object, tracker.Object);

            InvokeWarmup(manager);
            await Task.Delay(100);// 预创建库存超龄

            var connection = (PoolingDbConnection)await manager.GetOrCreateAsync();

            // 借用时发现库存超龄：激活前销毁，现场重建，不命中空闲队列
            Assert.False(connection.IsDisposed);
            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(2, statistics.TotalCreated);
            Assert.Equal(0, statistics.TotalHits);
            Assert.Equal(1, statistics.TotalReleased);
            Assert.Equal(0, statistics.IdleConnections);
            Assert.Equal(1, statistics.ActiveConnections);
        }

        [Fact]
        public async Task CleanupIdleConnections_WhenLifetimeExpired_DisposesRegardlessOfMinPoolSizeFloor()
        {
            var factory = ConnectionTestHelper.CreateStubFactory();
            var tracker = new Mock<IDbConnectionTracker>();

            using var manager = new PoolingDbConnectionManager(
                ConnectionTestHelper.CreatePoolOptions(minPoolSize: 2, maxLifetime: TimeSpan.FromMilliseconds(50)),
                factory.Object, tracker.Object);

            var connection = (PoolingDbConnection)await manager.GetOrCreateAsync();
            await Task.Delay(100);// 超过 MaxLifetime（MaxIdleTime 未超时）
            manager.Release(connection);

            InvokeCleanupIdleConnections(manager);

            // 超龄清理不受 MinPoolSize 下限保护：借出连接归还时超龄销毁、预创建库存清理时超龄销毁
            Assert.True(connection.IsDisposed);
            var statistics = (PoolingDbConnectionStatistics)manager.GetStatistics();
            Assert.Equal(0, statistics.IdleConnections);
            Assert.Equal(2, statistics.TotalCreated);
            Assert.Equal(0, statistics.TotalRecycled);
            Assert.Equal(2, statistics.TotalReleased);
        }

        #endregion
    }
}
