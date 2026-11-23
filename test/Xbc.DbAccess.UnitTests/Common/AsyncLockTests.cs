using Xbc.DbAccess.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Common
{
    /// <summary>
    /// AsyncLock 的单元测试。
    /// </summary>
    public class AsyncLockTests : IDisposable
    {
        private readonly AsyncLock _asyncLock;

        public AsyncLockTests()
        {
            _asyncLock = new AsyncLock();
        }

        public void Dispose()
        {
            _asyncLock?.Dispose();
            GC.SuppressFinalize(this);
        }

        #region Lock 测试

        [Fact]
        public void Lock_AcquiresLock_ReturnsReleaser()
        {
            using var releaser = _asyncLock.Lock();
            Assert.NotNull(releaser);
        }

        [Fact]
        public void Lock_Twice_SecondWaitsForFirst()
        {
            using var releaser1 = _asyncLock.Lock();

            var task = Task.Run(() =>
            {
                using var releaser2 = _asyncLock.Lock(100);
                return true;
            });

            Assert.False(task.IsCompleted);
            releaser1.Dispose();
            Assert.True(task.Wait(200));
            Assert.True(task.Result);
        }

        [Fact]
        public void Lock_WithCancellationToken_ThrowsWhenCancelled()
        {
            using var cts = new CancellationTokenSource();
            using var releaser = _asyncLock.Lock();

            cts.CancelAfter(50);

            Assert.Throws<OperationCanceledException>(() =>
            {
                using var releaser2 = _asyncLock.Lock(cts.Token);
            });
        }

        [Fact]
        public void Lock_WithTimeout_ThrowsTimeoutException()
        {
            using var releaser = _asyncLock.Lock();

            Assert.Throws<TimeoutException>(() =>
            {
                using var releaser2 = _asyncLock.Lock(50);
            });
        }

        [Fact]
        public void Lock_WithTimeoutAndCancellation_ThrowsWhenCancelled()
        {
            using var cts = new CancellationTokenSource();
            using var releaser = _asyncLock.Lock();

            cts.CancelAfter(50);

            Assert.Throws<OperationCanceledException>(() =>
            {
                using var releaser2 = _asyncLock.Lock(5000, cts.Token);
            });
        }

        [Fact]
        public void Lock_WithZeroTimeout_ThrowsTimeoutException()
        {
            using var releaser = _asyncLock.Lock();

            Assert.Throws<TimeoutException>(() =>
            {
                using var releaser2 = _asyncLock.Lock(0);
            });
        }

        #endregion

        #region LockAsync 测试

        [Fact]
        public async Task LockAsync_AcquiresLock_ReturnsReleaser()
        {
            var releaser = await _asyncLock.LockAsync();
            Assert.NotNull(releaser);
            releaser.Dispose();
        }

        [Fact]
        public async Task LockAsync_WithCancellationToken_ThrowsWhenCancelled()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // 当取消令牌已取消时，LockAsync 应该抛出 OperationCanceledException
            var ex = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                var releaser = await _asyncLock.LockAsync(cts.Token);
                // 注意：这里不使用 using，因为如果 LockAsync 抛出异常，releaser 不会被赋值
                releaser.Dispose();
            });
        }

        [Fact]
        public async Task LockAsync_SecondCallWaitsForFirst()
        {
            var releaser1 = await _asyncLock.LockAsync();

            var task = _asyncLock.LockAsync();
            Assert.False(task.IsCompleted);

            releaser1.Dispose();
            var releaser2 = await task;
            Assert.NotNull(releaser2);
            releaser2.Dispose();
        }

        #endregion

        #region Dispose 测试

        [Fact]
        public void Dispose_AfterLock_ThrowsObjectDisposedException()
        {
            _asyncLock.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _asyncLock.Lock());
        }

        [Fact]
        public async Task Dispose_AfterLockAsync_ThrowsObjectDisposedException()
        {
            _asyncLock.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await _asyncLock.LockAsync());
        }

        [Fact]
        public void Dispose_Twice_DoesNotThrow()
        {
            _asyncLock.Dispose();
            _asyncLock.Dispose(); // Should not throw
        }

        #endregion

        #region 超时与 Dispose 守卫补测

        [Fact]
        public void Lock_WithTimeoutAndToken_Unavailable_ThrowsTimeoutException()
        {
            using var releaser = _asyncLock.Lock();

            var ex = Assert.Throws<TimeoutException>(() =>
            {
                using var releaser2 = _asyncLock.Lock(50, CancellationToken.None);
            });

            // 异常消息携带超时时间，便于定位等待配置
            Assert.Contains("50", ex.Message);
        }

        [Fact]
        public async Task LockAsync_WithTimeout_Available_ReturnsReleaser()
        {
            var releaser = await _asyncLock.LockAsync(1000);

            Assert.NotNull(releaser);
            releaser.Dispose();
        }

        [Fact]
        public async Task LockAsync_WithTimeout_Unavailable_ThrowsTimeoutException()
        {
            using var releaser = _asyncLock.Lock();

            var ex = await Assert.ThrowsAsync<TimeoutException>(() => _asyncLock.LockAsync(50));

            Assert.Contains("50", ex.Message);
        }

        [Fact]
        public async Task LockAsync_WithTimeout_AfterDispose_ThrowsObjectDisposedException()
        {
            _asyncLock.Dispose();

            // Dispose 守卫先于信号量等待触发：对象名应为 AsyncLock 而非 SemaphoreSlim
            var ex = await Assert.ThrowsAsync<ObjectDisposedException>(() => _asyncLock.LockAsync(1000));
            Assert.Contains("AsyncLock", ex.ObjectName);
        }

        [Fact]
        public async Task LockAsync_WithTimeoutAndToken_Available_ReturnsReleaser()
        {
            var releaser = await _asyncLock.LockAsync(1000, CancellationToken.None);

            Assert.NotNull(releaser);
            releaser.Dispose();
        }

        [Fact]
        public async Task LockAsync_WithTimeoutAndToken_Unavailable_ThrowsTimeoutException()
        {
            using var releaser = _asyncLock.Lock();

            var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
                _asyncLock.LockAsync(50, CancellationToken.None));

            Assert.Contains("50", ex.Message);
        }

        [Fact]
        public async Task LockAsync_WithTimeoutAndToken_AfterDispose_ThrowsObjectDisposedException()
        {
            _asyncLock.Dispose();

            // Dispose 守卫先于信号量等待触发：对象名应为 AsyncLock 而非 SemaphoreSlim
            var ex = await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                _asyncLock.LockAsync(1000, CancellationToken.None));
            Assert.Contains("AsyncLock", ex.ObjectName);
        }

        // 说明：LockAsync() 无参重载（无取消令牌）的 WaitAsync 任务既不可能取消也不可能出错，
        // 其 ContinueWith 中的 IsCanceled / IsFaulted 分支（OperationCanceledException 与
        // task.Exception 解包）属于运行时不可达的防御代码，无法以行为测试触发，不做覆盖。

        #endregion

        #region 并发测试

        [Fact]
        public async Task LockAsync_ConcurrentAccess_OnlyOneThreadAtATime()
        {
            int counter = 0;
            var tasks = new Task[10];

            for (int i = 0; i < 10; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    using var releaser = await _asyncLock.LockAsync();
                    int current = counter;
                    await Task.Delay(10); // Simulate work
                    counter = current + 1;
                });
            }

            await Task.WhenAll(tasks);
            Assert.Equal(10, counter);
        }

        [Fact]
        public void Lock_ConcurrentAccess_OnlyOneThreadAtATime()
        {
            int counter = 0;
            var tasks = new Task[10];

            for (int i = 0; i < 10; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    using var releaser = _asyncLock.Lock();
                    int current = counter;
                    Thread.Sleep(10); // Simulate work
                    counter = current + 1;
                });
            }

            Task.WaitAll(tasks);
            Assert.Equal(10, counter);
        }

        #endregion
    }
}
