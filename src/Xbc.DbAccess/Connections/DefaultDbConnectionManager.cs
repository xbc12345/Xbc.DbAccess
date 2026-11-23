using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Xbc.DbAccess.Common;
using Xbc.DbAccess.Diagnostics;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 默认数据库连接管理器。
    /// <para>每次请求都需要新建连接，使用完毕后直接释放。</para>
    /// </summary>
    internal sealed class DefaultDbConnectionManager : DbConnectionManagerBase
    {
        // 活动的数据库连接字典：ConnectionId -> DefaultDbConnection
        private readonly ConcurrentDictionary<Guid, DefaultDbConnection> activeConnections
            = new ConcurrentDictionary<Guid, DefaultDbConnection>();
        private readonly Statistics statistics;

        /// <summary>
        /// 初始化默认连接管理器。
        /// </summary>
        /// <param name="connectionOptions">连接选项。</param>
        /// <param name="providerFactory">数据库提供程序工厂。</param>
        /// <param name="connectionTracker">连接追踪器。</param>
        public DefaultDbConnectionManager(DbConnectionOptions connectionOptions, DbProviderFactory providerFactory, IDbConnectionTracker connectionTracker)
            : base(connectionOptions, providerFactory, connectionTracker)
        {
            statistics = new Statistics(connectionOptions.EnableStatistics);
        }

        /// <summary>
        /// 异步获取或创建数据库连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns></returns>
        public override async Task<DbConnection> GetOrCreateAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            statistics.RecordRequest();

            // 与 Diagnostics 组件共享同一时钟源，追踪耗时口径一致
            var startTimestamp = StopwatchClock.ElapsedTicks;
            DefaultDbConnection newConnection;

            try
            {
                newConnection = await CreateActiveConnectionAsync(cancellationToken).ConfigureAwait(false) as DefaultDbConnection;
            }
            catch
            {
                statistics.RecordCreateFailed();
                throw;
            }

            // 登记与记账环节不进 catch：吞掉异常会在 Created 与 CreateFailed 各计一次，破坏互斥恒等式
            activeConnections.TryAdd(newConnection.ConnectionId, newConnection);
            statistics.RecordCreated();

            connectionTracker.StartTracking(newConnection.ConnectionId, ManagerId, StopwatchClock.GetDurationFrom(startTimestamp).TotalMilliseconds);
            return newConnection;
        }

        /// <summary>
        /// 创建 <see cref="DefaultDbConnection"/> 包装器实例。
        /// </summary>
        /// <returns></returns>
        protected override DbConnectionWrapper CreateDbConnectionWrapper()
        {
            var innerConnection = providerFactory.CreateConnection();
            var defaultConnection = new DefaultDbConnection(innerConnection)
            {
                ConnectionString = connectionOptions.ConnectionString
            };

            defaultConnection.LifecycleEnded += Connection_LifecycleEnded;
            return defaultConnection;
        }

        /// <summary>
        /// 连接生命周期结束事件处理。
        /// </summary>
        private void Connection_LifecycleEnded(object sender, DbResourceLifecycleEndedEventArgs e)
        {
            if (sender is not DefaultDbConnection connection || connection == null)
                return;

            // 已释放时跳过：Dispose 流程会兜底清理，飞行事件无需处理
            if (IsDisposed || IsDisposing)
                return;

            if (activeConnections.TryRemove(connection.ConnectionId, out _))
            {
                connectionTracker.EndTracking(connection.ConnectionId);
                statistics.RecordReleased();
            }
        }

        /// <summary>
        /// 释放指定的数据库连接。
        /// </summary>
        /// <param name="connection">要释放的数据库连接。</param>
        public override void Release(DbConnection connection)
        {
            if (connection == null)
                return;

            if (IsDisposed)
                return;

            // 只释放由本管理器创建的连接，防止误传的外部连接被强制释放
            if (connection is not DefaultDbConnection wrapper)
            {
                DiagnosticLog.Warn(nameof(DefaultDbConnectionManager), "释放的连接不是当前连接管理器创建的连接，已忽略：{ConnectionType}。", connection.GetType().ToString());
                return;
            }

            // 已从活动集合中移除（如生命周期结束事件已处理的重复释放），幂等忽略
            if (!activeConnections.ContainsKey(wrapper.ConnectionId))
                return;

            try
            {
                if (connection.State != ConnectionState.Closed)
                    connection.Close();

                connection.Dispose();// 这会触发生命周期结束事件，增加释放计数
            }
            catch (ObjectDisposedException)
            {
                // 忽略连接已释放的异常
            }
            catch (Exception ex)
            {
                // 释放路径不应向调用方传播异常（如连接已断裂时 Close 可能失败），仅记录日志
                DiagnosticLog.Error(nameof(DefaultDbConnectionManager), "释放连接时发生异常：{Exception}。", ex);
            }
        }

        /// <summary>
        /// 获取当前连接管理器的统计信息。
        /// </summary>
        /// <returns></returns>
        public override IDbConnectionStatistics GetStatistics()
        {
            ThrowIfDisposed();

            // 活动数快照时从活动集合测量
            return statistics.GetSnapshot(activeConnections.Count);
        }

        /// <summary>
        /// 释放资源。
        /// </summary>
        /// <param name="disposing">是否释放托管资源。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 使用中释放属调用方误用（.NET 惯例为未定义行为），入口守卫已拒绝新请求，此处仅做单次排空
                foreach (var connection in activeConnections.Values)
                {
                    DisposeConnection(connection);
                }
            }
        }

        /// <summary>
        /// 释放单个连接：退订事件、结束追踪并关闭释放资源。
        /// </summary>
        /// <param name="connection">要释放的连接。</param>
        private void DisposeConnection(DefaultDbConnection connection)
        {
            try
            {
                // 取消订阅事件，避免调用 Dispose 方法时触发事件
                connection.LifecycleEnded -= Connection_LifecycleEnded;

                // 事件已退订，需显式结束追踪，否则追踪记录会永久滞留在追踪器中
                connectionTracker.EndTracking(connection.ConnectionId);

                if (connection.State != ConnectionState.Closed)
                    connection.Close();

                connection.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // 忽略连接已释放的异常
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error(nameof(DefaultDbConnectionManager), "释放连接时发生异常：{Exception}。", ex);
            }
        }

        /// <summary>
        /// 统计信息。只维护单调递增的事件累计器，活动数由 GetSnapshot 快照时从活动集合测量传入。
        /// </summary>
        private class Statistics
        {
            private readonly bool isEnabled;

            public Statistics(bool isEnabled)
            {
                this.isEnabled = isEnabled;
            }

            // 通用累计器（单调递增，只计事件）
            private long totalRequests;
            private long totalCreated;
            private long totalCreateFailed;
            private long totalReleased;

            /// <summary>
            /// 记录连接请求。
            /// </summary>
            public void RecordRequest()
            {
                if (!isEnabled)
                    return;

                Interlocked.Increment(ref totalRequests);
            }

            /// <summary>
            /// 记录连接创建成功。
            /// </summary>
            public void RecordCreated()
            {
                if (!isEnabled)
                    return;

                Interlocked.Increment(ref totalCreated);
            }

            /// <summary>
            /// 记录连接创建失败。
            /// </summary>
            public void RecordCreateFailed()
            {
                if (!isEnabled)
                    return;

                Interlocked.Increment(ref totalCreateFailed);
            }

            /// <summary>
            /// 记录连接释放。
            /// </summary>
            public void RecordReleased()
            {
                if (!isEnabled)
                    return;

                Interlocked.Increment(ref totalReleased);
            }

            /// <summary>
            /// 获取统计信息快照。
            /// </summary>
            /// <param name="activeCount">活动集合实时计数（调用方测量传入）。</param>
            /// <returns></returns>
            public IDbConnectionStatistics GetSnapshot(int activeCount)
            {
                if (!isEnabled)
                    return null;

                return new DefaultDbConnectionStatistics
                {
                    TotalRequests = Interlocked.Read(ref totalRequests),
                    TotalCreated = Interlocked.Read(ref totalCreated),
                    TotalCreateFailed = Interlocked.Read(ref totalCreateFailed),
                    TotalReleased = Interlocked.Read(ref totalReleased),
                    ActiveConnections = activeCount,
                };
            }
        }
    }
}
