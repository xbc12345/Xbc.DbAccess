using System.Data;
using System.Data.Common;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 池化数据库连接，由 <see cref="PoolingDbConnectionManager"/> 管理，释放时回收到连接池而非关闭连接。
    /// </summary>
    internal sealed class PoolingDbConnection : DbConnectionWrapper
    {
        // 连接状态机
        private const int State_Created = 0;     // 已创建（尚未打开）
        private const int State_Active = 1;      // 活动状态（已打开，可使用）
        private const int State_Recycling = 2;   // 回收中（正在返回连接池）
        private const int State_Recycled = 3;    // 已回收（在空闲队列中）
        private const int State_Disposed = 4;    // 已释放
        private int state = State_Created;// 新创建的连接初始状态为已创建
        private string initialDatabase;// 初始数据库名称，用于激活时重置数据库上下文
        private long lastActiveTimestamp = StopwatchClock.ElapsedTicks;// 最后活动时间的共享单调时钟时间戳（创建时刻初始化），使用原子操作读写，避免 32 位平台上 long 赋值撕裂
        private readonly long createdAtTimestamp = StopwatchClock.ElapsedTicks;// 创建时刻的共享单调时钟时间戳，用于最大寿命判定（不受系统时间调整影响）

        /// <summary>
        /// 连接空闲时长（共享单调时钟，不受系统时间调整影响）：最后一次激活（打开或复用）距现在的时长，用于健康检查跳过间隔与空闲清理判定。
        /// </summary>
        internal TimeSpan IdleDuration => StopwatchClock.GetDurationFrom(Interlocked.Read(ref lastActiveTimestamp));

        /// <summary>
        /// 连接是否处于活动状态。
        /// </summary>
        public bool IsActive => Interlocked.CompareExchange(ref state, 0, 0) == State_Active;

        /// <summary>
        /// 连接存活时长（共享单调时钟，不受系统时间调整影响），用于连接池的最大寿命判定。
        /// </summary>
        internal TimeSpan Age => StopwatchClock.GetDurationFrom(createdAtTimestamp);

        /// <summary>
        /// 连接回收事件。
        /// </summary>
        public event EventHandler<DbConnectionRecycledEventArgs> Recycled;

        /// <summary>
        /// 连接是否已回收（含回收中）或已释放。
        /// </summary>
        public bool IsRecycled
        {
            get
            {
                int currentState = Interlocked.CompareExchange(ref state, 0, 0);
                return currentState == State_Recycling || currentState == State_Recycled || currentState == State_Disposed;
            }
        }

        /// <summary>
        /// 是否已释放资源。
        /// </summary>
        public override bool IsDisposed => Interlocked.CompareExchange(ref state, 0, 0) == State_Disposed;

        /// <summary>
        /// 是否正在释放资源。
        /// <para><see cref="ActualDispose"/> 先进入 Disposed 终态再清理，不存在释放窗口期，恒为 false。</para>
        /// </summary>
        public override bool IsDisposing => false;

        /// <summary>
        /// 初始化池化数据库连接。
        /// </summary>
        /// <param name="innerConnection">内部数据库连接。</param>
        public PoolingDbConnection(DbConnection innerConnection) : base(innerConnection)
        {
        }

        /// <summary>
        /// 打开连接。
        /// </summary>
        public override void Open()
        {
            ThrowIfDisposed();
            ThrowIfRecycled();

            innerConnection.Open();
            CaptureInitialDatabase();
            SetConnectionStateActive();
        }

        /// <summary>
        /// 异步打开连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public async override Task OpenAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            ThrowIfRecycled();

            await innerConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
            CaptureInitialDatabase();
            SetConnectionStateActive();
        }

        /// <summary>
        /// 延迟捕获初始数据库名称，仅在连接首次打开时捕获。
        /// </summary>
        private void CaptureInitialDatabase()
        {
            if (string.IsNullOrEmpty(initialDatabase))
                initialDatabase = innerConnection.Database;
        }

        /// <summary>
        /// 设置连接状态为活动状态并更新最后活动时间。
        /// </summary>
        private void SetConnectionStateActive()
        {
            Interlocked.Exchange(ref state, State_Active);// 标记连接为活动状态
            Interlocked.Exchange(ref lastActiveTimestamp, StopwatchClock.ElapsedTicks);// 更新最后活动时间，避免长时间闲置而被连接池清理
        }

        /// <summary>
        /// 关闭连接（实际上将连接返回到连接池）。
        /// </summary>
        public override void Close()
        {
            ReturnToPool();
        }

        /// <summary>
        /// 释放连接（实际上将连接返回到连接池）。
        /// </summary>
        /// <param name="disposing">是否释放托管资源。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                ReturnToPool();// 主动调用 Dispose 方法时，需要将连接返回到连接池中
        }

        /// <summary>
        /// 将连接返回连接池。
        /// </summary>
        private void ReturnToPool()
        {
            // 尝试将状态从 Active 或 Created 改为 Recycling
            // 如果已经是 Recycling、Recycled 或 Disposed，直接返回
            int originalState = Interlocked.CompareExchange(ref state, State_Recycling, State_Active);
            if (originalState != State_Active)
            {
                // 尝试从 Created 状态转换
                originalState = Interlocked.CompareExchange(ref state, State_Recycling, State_Created);
                if (originalState != State_Created)
                    return;
            }

            // 挂有未完成事务的连接不能回池（重借会让新借用者的命令跑进旧事务），直接销毁；
            // 关闭连接时提供程序会回滚挂起事务，状态最终收敛
            if (HasPendingTransaction)
            {
                ActualDispose();
                return;
            }

            // 触发回收事件，由连接池处理回收逻辑
            Recycled?.Invoke(this, new DbConnectionRecycledEventArgs()
            {
                ConnectionId = ConnectionId,
                RecycledAt = DateTime.Now,
            });

            // 成功回收，状态改为 Recycled
            Interlocked.CompareExchange(ref state, State_Recycled, State_Recycling);
        }

        /// <summary>
        /// 实际释放连接资源，关闭连接并触发生命周期结束事件。
        /// </summary>
        internal void ActualDispose()
        {
            // 尝试将状态改为 Disposed，如果已经是 Disposed，直接返回
            int originalState = Interlocked.Exchange(ref state, State_Disposed);
            if (originalState == State_Disposed)
                return;

            // 关闭并释放内部连接
            DisposeInternal();

            // 触发连接生命周期结束事件
            OnLifecycleEnded();
        }

        /// <summary>
        /// 若连接已回收则抛出 <see cref="DbConnectionRecycledException"/>。
        /// </summary>
        /// <exception cref="DbConnectionRecycledException"></exception>
        private void ThrowIfRecycled()
        {
            if (IsRecycled)
                throw new DbConnectionRecycledException(ConnectionId, $"连接已被回收，连接ID: {ConnectionId}。");
        }

        /// <summary>
        /// 激活并测试连接是否可用。验证参数由连接池在调用时提供，连接自身不持有池配置。
        /// </summary>
        /// <param name="validationSkipInterval">跳过健康检查的时间间隔，最近使用时间在此间隔内且连接已打开时跳过验证直接复用。</param>
        /// <param name="validationCommandTimeout">健康检查命令的超时时间（秒）。</param>
        /// <param name="validationCommandText">健康检查命令文本（含方言差异，如 Oracle 的 SELECT 1 FROM DUAL）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        internal async Task<bool> ActivateAsync(
            TimeSpan validationSkipInterval,
            int validationCommandTimeout,
            string validationCommandText,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // 挂事务的连接不可激活复用（正常在回池时已销毁，此处防御竞态窗口），判定失败由连接池销毁重建
            if (HasPendingTransaction)
                return false;

            // 快速路径：连接已打开且空闲时长在跳过间隔内，无需验证直接复用
            if (innerConnection.State == ConnectionState.Open && IdleDuration <= validationSkipInterval)
            {
                try
                {
                    ResetDatabaseContext();// 重置数据库上下文
                    SetConnectionStateActive();// 设置连接状态为 Active
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            // 慢速路径：需要打开或重新打开连接
            switch (innerConnection.State)
            {
                case ConnectionState.Closed:
                    await innerConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case ConnectionState.Broken:// 连接已损坏，需要重新打开
                    innerConnection.Close();
                    await innerConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    break;
            }

            // 重置数据库上下文
            try
            {
                ResetDatabaseContext();
            }
            catch (Exception)
            {
                innerConnection.Close();
                return false;
            }

            // 健康检查失败则关闭连接
            if (!await IsConnectionValidAsync(validationCommandText, validationCommandTimeout, cancellationToken).ConfigureAwait(false))
            {
                innerConnection.Close();
                return false;
            }

            // 设置连接状态为 Active
            SetConnectionStateActive();
            return true;
        }

        /// <summary>
        /// 重置数据库上下文为初始数据库。
        /// </summary>
        private void ResetDatabaseContext()
        {
            // 数据库被修改过则重置回初始数据库，避免残留访问其他数据库
            if (!string.Equals(innerConnection.Database, initialDatabase, StringComparison.OrdinalIgnoreCase))
                innerConnection.ChangeDatabase(initialDatabase);
        }

        /// <summary>
        /// 执行会话状态重置命令（连接归还连接池时由连接池调用，将连接恢复到"干净"会话再入池）。
        /// <para>命令按方言提供（<c>ISqlDialect.SessionStateResetCommand</c>，多语句以分号批合并为单往返），执行失败由调用方将连接按失效销毁。</para>
        /// </summary>
        /// <param name="resetCommand">会话状态重置命令。</param>
        /// <param name="commandTimeout">命令超时时间（秒）。</param>
        internal void ResetSessionState(string resetCommand, int commandTimeout)
        {
            using (var command = innerConnection.CreateCommand())
            {
                command.CommandType = CommandType.Text;
                command.CommandText = resetCommand;
                command.CommandTimeout = commandTimeout;
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 执行健康检查命令（默认 SELECT 1，Oracle 为 SELECT 1 FROM DUAL）测试连接是否可用。
        /// </summary>
        /// <param name="validationCommandText">健康检查命令文本。</param>
        /// <param name="validationCommandTimeout">命令超时时间（秒）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>命令执行成功且返回 1 时为 true，否则 false。</returns>
        private async Task<bool> IsConnectionValidAsync(string validationCommandText, int validationCommandTimeout, CancellationToken cancellationToken)
        {
            try
            {
                using (var command = innerConnection.CreateCommand())
                {
                    command.CommandType = CommandType.Text;
                    command.CommandText = validationCommandText;
                    command.CommandTimeout = validationCommandTimeout;

                    object result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    return result?.ToString() == "1";
                }
            }
            catch
            {
                return false;// 测试失败，连接不可用
            }
        }

        #region 重写父类方法
        /// <summary>
        /// 获取数据库架构。
        /// </summary>
        /// <returns></returns>
        public override DataTable GetSchema()
        {
            ThrowIfDisposed();
            ThrowIfRecycled();
            return innerConnection.GetSchema();
        }

        /// <summary>
        /// 获取数据库架构。
        /// </summary>
        /// <param name="collectionName">架构集合名称。</param>
        /// <returns></returns>
        public override DataTable GetSchema(string collectionName)
        {
            ThrowIfDisposed();
            ThrowIfRecycled();
            return innerConnection.GetSchema(collectionName);
        }

        /// <summary>
        /// 获取数据库架构。
        /// </summary>
        /// <param name="collectionName">架构集合名称。</param>
        /// <param name="restrictionValues">限制值。</param>
        /// <returns></returns>
        public override DataTable GetSchema(string collectionName, string[] restrictionValues)
        {
            ThrowIfDisposed();
            ThrowIfRecycled();
            return innerConnection.GetSchema(collectionName, restrictionValues);
        }

        /// <summary>
        /// 创建数据库命令。
        /// </summary>
        /// <returns></returns>
        protected override DbCommand CreateDbCommand()
        {
            ThrowIfDisposed();
            ThrowIfRecycled();
            return innerConnection.CreateCommand();
        }

        /// <summary>
        /// 开始数据库事务。
        /// </summary>
        /// <param name="isolationLevel">事务隔离级别。</param>
        /// <returns></returns>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            ThrowIfDisposed();
            ThrowIfRecycled();

            // 委托基类实现：透传内部连接并记录挂起事务——回收（ReturnToPool）与复用（ActivateAsync）
            // 的挂事务防护依赖该记录（重写不调基类而直接透传会绕过记录，导致防护失效）
            return base.BeginDbTransaction(isolationLevel);
        }

        /// <summary>
        /// 切换数据库。
        /// </summary>
        /// <param name="databaseName">数据库名称。</param>
        public override void ChangeDatabase(string databaseName)
        {
            ThrowIfDisposed();
            ThrowIfRecycled();
            innerConnection.ChangeDatabase(databaseName);
        }
        #endregion
    }
}
