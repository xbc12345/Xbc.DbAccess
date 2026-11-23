using Microsoft.Extensions.Logging;

namespace Xbc.DbAccess.Common
{
    /// <summary>
    /// 框架内部诊断日志，消息格式统一为 "[类名] 消息"，级别由 <see cref="Warn"/> / <see cref="Error"/> 决定。
    /// 调用 <see cref="Configure"/> 接入 <see cref="ILoggerFactory"/> 后路由到结构化日志；未配置时不输出。
    /// </summary>
    internal static class DiagnosticLog
    {
        /// <summary>
        /// ILogger 路由使用的日志类别。
        /// </summary>
        private const string LoggerCategory = "Xbc.DbAccess";

        // 已配置的日志器；null 表示未配置。
        // volatile + 写序（先 logger 后 configured）：读到 configured == true 的线程必定能看到已赋值的 logger
        private static volatile ILogger logger;

        // 是否已配置过（显式配置或容器自动接入均置位）；容器自动接入据此不覆盖显式配置
        private static volatile bool configured;

        /// <summary>
        /// 是否已配置日志路由。
        /// </summary>
        internal static bool IsConfigured => configured;

        /// <summary>
        /// 配置日志工厂，内部 Warn / Error 路由到 ILogger。后配置者生效（含传 null 关闭输出）；已配置过时容器自动接入不覆盖。
        /// </summary>
        /// <param name="loggerFactory">日志工厂；传 null 停止日志输出。</param>
        public static void Configure(ILoggerFactory loggerFactory)
        {
            logger = loggerFactory?.CreateLogger(LoggerCategory);
            configured = true;
        }

        /// <summary>
        /// 重置为未配置状态，仅供测试隔离使用。
        /// </summary>
        internal static void ResetForTest()
        {
            logger = null;
            configured = false;
        }

        /// <summary>
        /// 输出 WARN 级别的日志。
        /// </summary>
        /// <param name="source">来源类名。</param>
        /// <param name="message">消息模板。</param>
        /// <param name="args">模板参数。</param>
        public static void Warn(string source, string message, params object[] args)
        {
            logger?.LogWarning("[{Source}] " + message, PrependSource(source, args));
        }

        /// <summary>
        /// 输出 ERROR 级别的日志。
        /// </summary>
        /// <param name="source">来源类名。</param>
        /// <param name="message">消息模板。</param>
        /// <param name="args">模板参数。</param>
        public static void Error(string source, string message, params object[] args)
        {
            logger?.LogError("[{Source}] " + message, PrependSource(source, args));
        }

        /// <summary>
        /// 将来源类名前置到模板参数数组，与模板首占位符 {Source} 对应。
        /// </summary>
        private static object[] PrependSource(string source, object[] args)
        {
            var merged = new object[args.Length + 1];
            merged[0] = source;
            Array.Copy(args, 0, merged, 1, args.Length);
            return merged;
        }
    }
}
