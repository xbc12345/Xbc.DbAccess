using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Diagnostics
{
    /// <summary>
    /// 数据库连接追踪信息，记录单个连接从创建到释放的完整生命周期。
    /// <para>状态字段无锁同步：追踪器写入、查询方（FindAll / 指标采集）并发读取，观察者最坏读到滞后一帧的状态；泄漏判定与指标统计容忍该抖动，不加锁以避免热路径开销。</para>
    /// </summary>
    public class DbConnectionTrackingInfo
    {
        /// <summary>
        /// 连接唯一标识。
        /// </summary>
        public Guid ConnectionId { get; internal set; }

        /// <summary>
        /// 连接管理器唯一标识。
        /// </summary>
        public Guid ManagerId { get; internal set; }

        /// <summary>
        /// 连接创建时间。
        /// </summary>
        public DateTime CreatedAt { get; internal set; }

        /// <summary>
        /// 连接创建时的高精度时间戳，仅供 <see cref="TotalLifetime"/> 计算时长使用，不对应实际时间点。
        /// </summary>
        internal long CreatedTimestamp { get; set; }

        /// <summary>
        /// 最近一次获取连接的时间（首次创建或连接池复用时更新）。
        /// </summary>
        public DateTime AcquiredAt { get; internal set; }

        /// <summary>
        /// 最近一次获取连接时的高精度时间戳，仅供 <see cref="CurrentHoldDuration"/> 和 <see cref="ActiveHoldDuration"/> 计算时长使用，不对应实际时间点。
        /// </summary>
        internal long AcquiredTimestamp { get; set; }

        /// <summary>
        /// 最近一次获取连接的耗时（毫秒）；连接池模式下含等待空闲连接与创建新连接的时间。
        /// </summary>
        public double AcquireDurationMs { get; internal set; }

        /// <summary>
        /// 连接复用次数（仅连接池模式下有意义，表示连接被重新获取的次数）。
        /// </summary>
        public int ReacquireCount { get; internal set; }

        /// <summary>
        /// 最近一次回收时间（仅连接池模式，连接被归还到池中的时间）。
        /// </summary>
        public DateTime? RecycledAt { get; internal set; }

        /// <summary>
        /// 最近一次回收时的高精度时间戳，仅供 <see cref="CurrentHoldDuration"/> 计算时长使用，不对应实际时间点。
        /// </summary>
        internal long? RecycledTimestamp { get; set; }

        /// <summary>
        /// 连接释放时间（连接被彻底关闭，不再归还连接池）。
        /// </summary>
        public DateTime? ReleasedAt { get; internal set; }

        /// <summary>
        /// 连接释放时的高精度时间戳，仅供 <see cref="TotalLifetime"/> 和 <see cref="CurrentHoldDuration"/> 计算时长使用，不对应实际时间点。
        /// </summary>
        internal long? ReleasedTimestamp { get; set; }

        /// <summary>
        /// 连接当前追踪状态。
        /// </summary>
        public DbConnectionTrackingState State { get; internal set; }

        /// <summary>
        /// 获取连接时的调用堆栈（需开启堆栈记录选项）。
        /// </summary>
        public string AcquireStackTrace { get; internal set; }

        /// <summary>
        /// 回收连接时的调用堆栈（仅连接池模式，需开启堆栈记录选项）。
        /// </summary>
        public string RecycleStackTrace { get; internal set; }

        /// <summary>
        /// 释放连接时的调用堆栈（需开启堆栈记录选项）。
        /// </summary>
        public string ReleaseStackTrace { get; internal set; }

        /// <summary>
        /// 当前正在执行的 SQL 语句。
        /// </summary>
        public string Sql { get; internal set; }

        /// <summary>
        /// 连接是否已释放（不再归还连接池）。
        /// </summary>
        public bool IsReleased => State == DbConnectionTrackingState.Released && ReleasedAt.HasValue;

        /// <summary>
        /// 连接的总生命周期时长：已释放时为创建到释放，否则为创建到当前。
        /// </summary>
        public TimeSpan TotalLifetime
        {
            get
            {
                if (ReleasedTimestamp.HasValue)
                    return StopwatchClock.GetDuration(CreatedTimestamp, ReleasedTimestamp.Value);
                else
                    return StopwatchClock.GetDurationFrom(CreatedTimestamp);
            }
        }

        /// <summary>
        /// 从最近一次获取到当前的时长（泄漏检测用）；连接不在使用中时返回 <see cref="TimeSpan.Zero"/>。
        /// </summary>
        internal TimeSpan ActiveHoldDuration => IsInUse ? StopwatchClock.GetDurationFrom(AcquiredTimestamp) : TimeSpan.Zero;

        /// <summary>
        /// 当前持有连接的时长：已释放取获取到释放，已回收取获取到回收，否则取获取到当前。
        /// </summary>
        public TimeSpan CurrentHoldDuration
        {
            get
            {
                // 所有时长计算基于 StopwatchClock 全局时钟，不受系统时间调整影响，精度统一
                if (State == DbConnectionTrackingState.Released && ReleasedTimestamp.HasValue)
                    return StopwatchClock.GetDuration(AcquiredTimestamp, ReleasedTimestamp.Value);
                else if (State == DbConnectionTrackingState.Recycled && RecycledTimestamp.HasValue)
                    return StopwatchClock.GetDuration(AcquiredTimestamp, RecycledTimestamp.Value);
                else if (IsInUse)
                    return StopwatchClock.GetDurationFrom(AcquiredTimestamp);
                else
                    return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// 连接是否正在使用中（处于创建、获取或活动状态）。
        /// </summary>
        public bool IsInUse
        {
            get
            {
                return State == DbConnectionTrackingState.Created ||
                    State == DbConnectionTrackingState.Acquired ||
                    State == DbConnectionTrackingState.Active;
            }
        }

        /// <summary>
        /// 检查连接是否泄漏（正在使用且持有时间超过指定阈值）。
        /// </summary>
        /// <param name="threshold">泄漏检测阈值。</param>
        /// <returns></returns>
        public bool IsLeaked(TimeSpan threshold)
        {
            return IsInUse && ActiveHoldDuration > threshold;
        }
    }
}
