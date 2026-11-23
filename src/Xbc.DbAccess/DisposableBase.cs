namespace Xbc.DbAccess
{
    /// <summary>
    /// 可释放对象基类，提供线程安全的资源释放功能。
    /// </summary>
    public abstract class DisposableBase : IDisposable
    {
        private readonly DisposableState disposeState = new DisposableState();

        /// <summary>
        /// 是否已释放。
        /// </summary>
        public bool IsDisposed => disposeState.IsDisposed;

        /// <summary>
        /// 是否正在释放（Dispose() 已开始但尚未完成）。
        /// <para>防御性检查应同时判断 <see cref="IsDisposed"/> 与此属性，否则释放过程中的并发访问会绕过守卫。</para>
        /// </summary>
        public bool IsDisposing => disposeState.IsDisposing;

        /// <summary>
        /// 检查资源是否已释放或正在释放，若是则抛出 <see cref="ObjectDisposedException"/> 异常。
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        protected virtual void ThrowIfDisposed()
        {
            disposeState.ThrowIfDisposed(GetType().ToString());
        }

        /// <summary>
        /// 释放资源。
        /// </summary>
        /// <param name="disposing">是否释放托管资源。</param>
        protected abstract void Dispose(bool disposing);

        /// <summary>
        /// 释放资源。
        /// </summary>
        public void Dispose()
        {
            // 尝试开始释放，只有第一个线程能成功，此时状态为正在释放中
            if (!disposeState.TryDispose())
                return;

            try
            {
                Dispose(true);
            }
            finally
            {
                disposeState.Complete();
                GC.SuppressFinalize(this);
            }
        }
    }
}
