namespace Xbc.DbAccess
{
    /// <summary>
    /// 可释放状态管理，提供线程安全的释放状态跟踪。
    /// </summary>
    public sealed class DisposableState
    {
        private const int Undisposed = 0;// 未释放
        private const int Disposing = 1;// 正在释放
        private const int Disposed = 2;// 已释放

        private int disposeState;

        /// <summary>
        /// 是否正在释放资源。
        /// </summary>
        public bool IsDisposing => Volatile.Read(ref disposeState) == Disposing;

        /// <summary>
        /// 是否已释放资源。
        /// </summary>
        public bool IsDisposed => Volatile.Read(ref disposeState) == Disposed;

        /// <summary>
        /// 检查资源是否已释放或正在释放，若是则抛出 <see cref="ObjectDisposedException"/> 异常。
        /// <para>正在释放（<see cref="TryDispose"/> 已成功但 <see cref="Complete"/> 未完成）的对象同样不可用，
        /// 否则释放过程中的并发访问会绕过仅检查 <see cref="IsDisposed"/> 的守卫。</para>
        /// </summary>
        /// <param name="objectName">对象名称。</param>
        /// <exception cref="ObjectDisposedException"></exception>
        public void ThrowIfDisposed(string objectName)
        {
            // volatile 读具备获取语义即可（发布语义由 TryDispose/Complete 的原子写保证），无需锁前缀指令
            if (Volatile.Read(ref disposeState) != Undisposed)
                throw new ObjectDisposedException(objectName);
        }

        /// <summary>
        /// 尝试开始释放资源，返回 true 表示当前线程获得了释放权。
        /// </summary>
        /// <returns></returns>
        public bool TryDispose()
        {
            return Interlocked.CompareExchange(ref disposeState, Disposing, Undisposed) == Undisposed;
        }

        /// <summary>
        /// 标记资源已完全释放，应在释放逻辑完成后调用。
        /// </summary>
        public void Complete()
        {
            Interlocked.Exchange(ref disposeState, Disposed);
        }
    }
}
