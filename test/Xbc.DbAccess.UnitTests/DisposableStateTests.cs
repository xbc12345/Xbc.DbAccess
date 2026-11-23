using System;
using System.Threading.Tasks;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Common
{
    public class DisposableStateTests
    {
        [Fact]
        public void Constructor_初始状态_应为未释放()
        {
            var state = new DisposableState();

            Assert.False(state.IsDisposing);
            Assert.False(state.IsDisposed);
        }

        [Fact]
        public void TryDispose_首次调用_应返回true并设置正在释放状态()
        {
            var state = new DisposableState();

            var result = state.TryDispose();

            Assert.True(result);
            Assert.True(state.IsDisposing);
            Assert.False(state.IsDisposed);
        }

        [Fact]
        public void TryDispose_重复调用_应返回false()
        {
            var state = new DisposableState();
            state.TryDispose();

            var result = state.TryDispose();

            Assert.False(result);
        }

        [Fact]
        public void IsDisposing_TryDispose后_应返回true()
        {
            var state = new DisposableState();

            state.TryDispose();

            Assert.True(state.IsDisposing);
        }

        [Fact]
        public void Complete_调用后_应设置已释放状态()
        {
            var state = new DisposableState();
            state.TryDispose();

            state.Complete();

            Assert.True(state.IsDisposed);
            Assert.False(state.IsDisposing);
        }

        [Fact]
        public void IsDisposed_Complete后_应返回true()
        {
            var state = new DisposableState();
            state.TryDispose();
            state.Complete();

            var result = state.IsDisposed;

            Assert.True(result);
        }

        [Fact]
        public void ThrowIfDisposed_未释放时_不应抛出异常()
        {
            var state = new DisposableState();

            var exception = Record.Exception(() => state.ThrowIfDisposed("TestObject"));

            Assert.Null(exception);
        }

        [Fact]
        public void ThrowIfDisposed_已释放时_应抛出ObjectDisposedException()
        {
            var state = new DisposableState();
            state.TryDispose();
            state.Complete();

            var exception = Assert.Throws<ObjectDisposedException>(() => state.ThrowIfDisposed("TestObject"));

            Assert.Equal("TestObject", exception.ObjectName);
        }

        [Fact]
        public void TryDispose_已释放后调用_应返回false()
        {
            var state = new DisposableState();
            state.TryDispose();
            state.Complete();

            var result = state.TryDispose();

            Assert.False(result);
        }

        [Fact]
        public void Complete_未调用TryDispose直接调用_应设置已释放状态()
        {
            var state = new DisposableState();

            state.Complete();

            Assert.True(state.IsDisposed);
        }

        [Fact]
        public async Task TryDispose_多线程并发调用_只有一个应返回true()
        {
            var state = new DisposableState();
            var successCount = 0;
            var tasks = new Task[10];

            for (int i = 0; i < 10; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    if (state.TryDispose())
                        Interlocked.Increment(ref successCount);
                });
            }

            await Task.WhenAll(tasks);

            Assert.Equal(1, successCount);
        }

        [Fact]
        public void IsDisposing_已释放后_应返回false()
        {
            var state = new DisposableState();
            state.TryDispose();
            state.Complete();

            Assert.False(state.IsDisposing);
        }

        [Fact]
        public void ThrowIfDisposed_正在释放中_应抛出ObjectDisposedException()
        {
            var state = new DisposableState();
            state.TryDispose();

            var exception = Assert.Throws<ObjectDisposedException>(() => state.ThrowIfDisposed("TestObject"));

            Assert.Equal("TestObject", exception.ObjectName);
        }
    }
}
