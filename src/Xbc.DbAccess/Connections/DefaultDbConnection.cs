using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 默认数据库连接，由 <see cref="DefaultDbConnectionManager"/> 管理，每次释放时直接关闭连接。
    /// </summary>
    internal sealed class DefaultDbConnection : DbConnectionWrapper
    {
        private readonly DisposableState disposableState = new DisposableState();

        /// <summary>
        /// 是否已释放资源。
        /// </summary>
        public override bool IsDisposed => disposableState.IsDisposed;

        /// <summary>
        /// 是否正在释放资源。
        /// </summary>
        public override bool IsDisposing => disposableState.IsDisposing;

        /// <summary>
        /// 初始化默认数据库连接。
        /// </summary>
        /// <param name="innerConnection">内部数据库连接。</param>
        public DefaultDbConnection(DbConnection innerConnection) : base(innerConnection)
        {
        }

        /// <summary>
        /// 释放内部连接资源，并触发连接生命周期结束事件。
        /// </summary>
        /// <param name="disposing">是否释放托管资源。</param>
        protected override void Dispose(bool disposing)
        {
            if (!disposableState.TryDispose())
                return;

            try
            {
                if (disposing)
                {
                    // 关闭并释放内部连接
                    DisposeInternal();

                    // 触发连接生命周期结束事件
                    OnLifecycleEnded();
                }
            }
            finally
            {
                disposableState.Complete();
            }
        }
    }
}
