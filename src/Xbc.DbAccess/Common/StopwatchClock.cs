using System.Diagnostics;

namespace Xbc.DbAccess.Common
{
    /// <summary>
    /// 高精度全局时钟，基于 <see cref="Stopwatch"/> 提供时间戳，不受系统时间调整影响。
    /// 库内各组件共享同一时钟源，确保时间戳可比较。
    /// </summary>
    internal static class StopwatchClock
    {
        private static readonly Stopwatch stopwatch = Stopwatch.StartNew();

        /// <summary>
        /// 获取当前已流逝的时钟刻度数。
        /// </summary>
        public static long ElapsedTicks => stopwatch.ElapsedTicks;

        /// <summary>
        /// 获取时钟频率（每秒刻度数）。
        /// </summary>
        public static long Frequency => Stopwatch.Frequency;

        /// <summary>
        /// 根据起止刻度计算时长。Stopwatch 刻度频率不一定等于 <see cref="TimeSpan.TicksPerSecond"/>，需按比例转换。
        /// </summary>
        /// <param name="startTicks">起始刻度。</param>
        /// <param name="endTicks">结束刻度。</param>
        /// <returns></returns>
        public static TimeSpan GetDuration(long startTicks, long endTicks)
        {
            var delta = endTicks - startTicks;
            var ticks = (long)(delta * (double)TimeSpan.TicksPerSecond / Frequency);
            return TimeSpan.FromTicks(ticks);
        }

        /// <summary>
        /// 计算从指定起始刻度到当前的时长。
        /// </summary>
        /// <param name="startTicks">起始刻度。</param>
        /// <returns></returns>
        public static TimeSpan GetDurationFrom(long startTicks)
        {
            return GetDuration(startTicks, stopwatch.ElapsedTicks);
        }
    }
}
