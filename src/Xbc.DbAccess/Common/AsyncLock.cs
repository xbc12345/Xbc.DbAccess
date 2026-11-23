namespace Xbc.DbAccess.Common
{
    /// <summary>
    /// 异步锁，提供同步和异步的互斥锁定功能。
    /// </summary>
    internal sealed class AsyncLock : DisposableBase
    {
        private readonly SemaphoreSlim semaphore;

        /// <summary>
        /// 初始化异步锁。
        /// </summary>
        public AsyncLock()
        {
            semaphore = new SemaphoreSlim(1, 1);
        }

        /// <summary>
        /// 同步获取锁。
        /// </summary>
        /// <returns>锁释放器，配合 using 语句自动释放锁。</returns>
        public IDisposable Lock()
        {
            ThrowIfDisposed();

            semaphore.Wait();
            return new LockReleaser(semaphore);
        }

        /// <summary>
        /// 同步获取锁，支持取消。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>锁释放器，配合 using 语句自动释放锁。</returns>
        /// <exception cref="OperationCanceledException"></exception>
        public IDisposable Lock(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            semaphore.Wait(cancellationToken);
            return new LockReleaser(semaphore);
        }

        /// <summary>
        /// 同步获取锁，支持超时。
        /// </summary>
        /// <param name="millisecondsTimeout">等待超时时间（毫秒）。</param>
        /// <returns>锁释放器，配合 using 语句自动释放锁。</returns>
        /// <exception cref="TimeoutException"></exception>
        public IDisposable Lock(int millisecondsTimeout)
        {
            ThrowIfDisposed();

            if (semaphore.Wait(millisecondsTimeout))
                return new LockReleaser(semaphore);

            throw new TimeoutException($"等待锁超时，超时时间：{millisecondsTimeout} 毫秒。");
        }

        /// <summary>
        /// 同步获取锁，支持超时和取消。
        /// </summary>
        /// <param name="millisecondsTimeout">等待超时时间（毫秒）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>锁释放器，配合 using 语句自动释放锁。</returns>
        /// <exception cref="TimeoutException"></exception>
        /// <exception cref="OperationCanceledException"></exception>
        public IDisposable Lock(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (semaphore.Wait(millisecondsTimeout, cancellationToken))
                return new LockReleaser(semaphore);

            throw new TimeoutException($"等待锁超时，超时时间：{millisecondsTimeout} 毫秒。");
        }

        /// <summary>
        /// 异步获取锁。
        /// </summary>
        /// <returns>异步任务，结果为锁释放器。</returns>
        public Task<IDisposable> LockAsync()
        {
            ThrowIfDisposed();

            var wait = semaphore.WaitAsync();

            // 检查任务是否成功完成（不是取消也不是错误）
            if (wait.Status == TaskStatus.RanToCompletion)
                return Task.FromResult((IDisposable)new LockReleaser(semaphore));

            return wait.ContinueWith((task, state) =>
                {
                    // 如果任务被取消或出错，抛出异常而不创建 LockReleaser
                    if (task.IsCanceled)
                        throw new OperationCanceledException();

                    if (task.IsFaulted)
                        throw task.Exception.InnerException ?? task.Exception;

                    return (IDisposable)new LockReleaser((SemaphoreSlim)state);
                },
                semaphore,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>
        /// 异步获取锁，支持取消。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务，结果为锁释放器。</returns>
        /// <exception cref="OperationCanceledException"></exception>
        public Task<IDisposable> LockAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            var wait = semaphore.WaitAsync(cancellationToken);

            // 检查任务是否成功完成（不是取消也不是错误）
            if (wait.Status == TaskStatus.RanToCompletion)
                return Task.FromResult((IDisposable)new LockReleaser(semaphore));

            return wait.ContinueWith((task, state) =>
                {
                    // 如果任务被取消或出错，抛出异常而不创建 LockReleaser；取消时携带调用者的令牌
                    if (task.IsCanceled)
                        throw new OperationCanceledException(cancellationToken);

                    if (task.IsFaulted)
                        throw task.Exception.InnerException ?? task.Exception;

                    return (IDisposable)new LockReleaser((SemaphoreSlim)state);
                },
                semaphore,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>
        /// 异步获取锁，支持超时。
        /// </summary>
        /// <param name="millisecondsTimeout">等待超时时间（毫秒）。</param>
        /// <returns>异步任务，结果为锁释放器。</returns>
        /// <exception cref="TimeoutException"></exception>
        public async Task<IDisposable> LockAsync(int millisecondsTimeout)
        {
            ThrowIfDisposed();

            if (await semaphore.WaitAsync(millisecondsTimeout).ConfigureAwait(false))
                return new LockReleaser(semaphore);

            throw new TimeoutException($"等待锁超时，超时时间：{millisecondsTimeout} 毫秒。");
        }

        /// <summary>
        /// 异步获取锁，支持超时和取消。
        /// </summary>
        /// <param name="millisecondsTimeout">等待超时时间（毫秒）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务，结果为锁释放器。</returns>
        /// <exception cref="TimeoutException"></exception>
        /// <exception cref="OperationCanceledException"></exception>
        public async Task<IDisposable> LockAsync(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (await semaphore.WaitAsync(millisecondsTimeout, cancellationToken).ConfigureAwait(false))
                return new LockReleaser(semaphore);

            throw new TimeoutException($"等待锁超时，超时时间：{millisecondsTimeout} 毫秒。");
        }

        /// <summary>
        /// 释放信号量资源。
        /// </summary>
        /// <param name="disposing">是否释放托管资源。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                semaphore.Dispose();
        }

        /// <summary>
        /// 锁释放器，释放时归还信号量。
        /// </summary>
        private sealed class LockReleaser : IDisposable
        {
            private readonly SemaphoreSlim semaphore;
            private int disposeState;

            /// <summary>
            /// 初始化锁释放器。
            /// </summary>
            /// <param name="semaphore">信号量。</param>
            public LockReleaser(SemaphoreSlim semaphore)
            {
                this.semaphore = semaphore;
            }

            /// <summary>
            /// 释放锁，归还信号量。使用 <see cref="Interlocked.CompareExchange(ref int,int,int)"/> 防止重复释放。
            /// </summary>
            public void Dispose()
            {
                // 使用 Interlocked 确保线程安全，防止重复释放
                if (Interlocked.CompareExchange(ref disposeState, 1, 0) == 1)
                    return;

                semaphore.Release();
            }
        }
    }
}
