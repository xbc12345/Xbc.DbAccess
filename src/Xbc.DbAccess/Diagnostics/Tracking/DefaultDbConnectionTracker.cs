using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Diagnostics
{
    /// <summary>
    /// 默认数据库连接追踪器，使用并发字典追踪活动连接。
    /// </summary>
    internal sealed class DefaultDbConnectionTracker : IDbConnectionTracker
    {
        private readonly DbConnectionTrackingOptions trackingOptions;

        // 追踪连接字典：ConnectionId -> ConnectionInfo
        private readonly ConcurrentDictionary<Guid, DbConnectionTrackingInfo> trackedConnections
            = new ConcurrentDictionary<Guid, DbConnectionTrackingInfo>();

        /// <summary>
        /// 追踪选项。
        /// </summary>
        public DbConnectionTrackingOptions TrackingOptions => trackingOptions;

        /// <summary>
        /// 初始化默认连接追踪器。
        /// </summary>
        /// <param name="trackingOptions">连接追踪选项。</param>
        public DefaultDbConnectionTracker(DbConnectionTrackingOptions trackingOptions)
        {
            this.trackingOptions = trackingOptions ?? new DbConnectionTrackingOptions();
        }

        /// <summary>
        /// 开始追踪新的连接（连接首次创建时调用）。
        /// </summary>
        /// <param name="connectionId">连接唯一标识。</param>
        /// <param name="managerId">连接管理器唯一标识。</param>
        /// <param name="acquireDurationMs">获取连接的耗时（毫秒）。</param>
        /// <returns></returns>
        public DbConnectionTrackingInfo StartTracking(Guid connectionId, Guid managerId, double acquireDurationMs)
        {
            var info = new DbConnectionTrackingInfo
            {
                ConnectionId = connectionId,
                ManagerId = managerId,
                CreatedAt = DateTime.Now,
                CreatedTimestamp = StopwatchClock.ElapsedTicks,
                AcquiredAt = DateTime.Now,
                AcquiredTimestamp = StopwatchClock.ElapsedTicks,
                AcquireDurationMs = acquireDurationMs,
                State = DbConnectionTrackingState.Acquired,
                AcquireStackTrace = trackingOptions.RecordStackTrace ? GetStackTrace() : null
            };

            if (!trackedConnections.TryAdd(connectionId, info))
                throw new InvalidOperationException($"连接已被追踪，连接ID: {connectionId}。");

            return info;
        }

        /// <summary>
        /// 标记连接为活动中（执行 SQL 时调用）。
        /// </summary>
        /// <param name="connectionId">连接唯一标识。</param>
        /// <param name="sql">执行的 SQL 语句。</param>
        public void MarkActive(Guid connectionId, string sql = null)
        {
            if (trackedConnections.TryGetValue(connectionId, out var info))
            {
                info.State = DbConnectionTrackingState.Active;
                info.Sql = sql;
            }
            else
            {
                DiagnosticLog.Warn(nameof(DefaultDbConnectionTracker), "尝试标记不存在的连接为 '{State}' 状态，连接ID: {ConnectionId}。", DbConnectionTrackingState.Active, connectionId);
            }
        }

        /// <summary>
        /// 标记连接为已回收（连接回收到连接池时调用）。
        /// </summary>
        /// <param name="connectionId">连接唯一标识。</param>
        public void MarkRecycled(Guid connectionId)
        {
            if (trackedConnections.TryGetValue(connectionId, out var info))
            {
                info.State = DbConnectionTrackingState.Recycled;
                info.RecycledAt = DateTime.Now;
                info.RecycledTimestamp = StopwatchClock.ElapsedTicks;
                info.RecycleStackTrace = trackingOptions.RecordStackTrace ? GetStackTrace() : null;
            }
            else
            {
                DiagnosticLog.Warn(nameof(DefaultDbConnectionTracker), "尝试标记不存在的连接为 '{State}' 状态，连接ID: {ConnectionId}。", DbConnectionTrackingState.Recycled, connectionId);
            }
        }

        /// <summary>
        /// 标记连接为已重新获取（连接从连接池取出复用时调用）。
        /// </summary>
        /// <param name="connectionId">连接唯一标识。</param>
        /// <param name="acquireDurationMs">获取连接的耗时（毫秒）。</param>
        public void MarkReacquired(Guid connectionId, double acquireDurationMs)
        {
            if (trackedConnections.TryGetValue(connectionId, out var info))
            {
                info.State = DbConnectionTrackingState.Acquired;
                info.AcquiredAt = DateTime.Now;
                info.AcquiredTimestamp = StopwatchClock.ElapsedTicks;
                info.AcquireDurationMs = acquireDurationMs;
                info.ReacquireCount++;
            }
            else
            {
                DiagnosticLog.Warn(nameof(DefaultDbConnectionTracker), "尝试标记不存在的连接为 '{State}' 状态，连接ID: {ConnectionId}。", DbConnectionTrackingState.Acquired, connectionId);
            }
        }

        /// <summary>
        /// 结束连接的追踪（连接真正释放时调用）。
        /// </summary>
        /// <param name="connectionId">连接唯一标识。</param>
        public void EndTracking(Guid connectionId)
        {
            // 先写终态再移除：持有引用的观察者（此前的 FindAll 快照、LeakedConnectionInfo）能读到 Released 终态，
            // 且移除前短暂可见的 Released 状态不会被泄漏判定计入（IsInUse 为 false）
            if (trackedConnections.TryGetValue(connectionId, out var info))
            {
                info.State = DbConnectionTrackingState.Released;
                info.ReleasedAt = DateTime.Now;
                info.ReleasedTimestamp = StopwatchClock.ElapsedTicks;
                info.ReleaseStackTrace = trackingOptions.RecordStackTrace ? GetStackTrace() : null;

                trackedConnections.TryRemove(connectionId, out _);
            }
            else
            {
                DiagnosticLog.Warn(nameof(DefaultDbConnectionTracker), "尝试标记不存在的连接为 '{State}' 状态，连接ID: {ConnectionId}。", DbConnectionTrackingState.Released, connectionId);
            }
        }

        /// <summary>
        /// 获取当前堆栈信息，跳过追踪器自身的调用帧，最多记录指定帧数。
        /// </summary>
        /// <param name="skipFrames">跳过的帧数，默认为 2（GetStackTrace + 调用方方法）。</param>
        /// <param name="maxFrames">最大记录帧数，默认为 10。</param>
        /// <returns></returns>
        private string GetStackTrace(int skipFrames = 2, int maxFrames = 10)
        {
            try
            {
                var stackTrace = new StackTrace(skipFrames, true);
                var frames = stackTrace.GetFrames();
                if (frames == null)
                    return null;

                var sb = new StringBuilder();
                for (int i = 0; i < Math.Min(maxFrames, frames.Length); i++)
                {
                    sb.AppendLine(frames[i].ToString());
                }

                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取所有追踪中的连接列表（包括已获取、活动和已回收状态的连接）。
        /// </summary>
        /// <param name="predicate">过滤条件，为 null 时返回所有追踪中的连接。</param>
        /// <returns></returns>
        public IReadOnlyList<DbConnectionTrackingInfo> FindAll(Predicate<DbConnectionTrackingInfo> predicate = null)
        {
            // 按高精度时间戳排序：系统时间调整（NTP 校时）不影响顺序；DateTime 字段仅作展示
            if (predicate == null)
                return trackedConnections.Values.OrderBy(x => x.CreatedTimestamp).ToList();

            return trackedConnections.Values
                .Where(x => predicate(x))
                .OrderBy(x => x.CreatedTimestamp)
                .ToList();
        }
    }
}
