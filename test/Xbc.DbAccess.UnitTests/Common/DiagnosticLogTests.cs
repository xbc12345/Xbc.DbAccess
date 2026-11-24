using Microsoft.Extensions.Logging;
using Xbc.DbAccess.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Common
{
    /// <summary>
    /// 诊断日志路由是全局静态状态，与其他使用 DiagnosticLog 的测试类并行会互相污染，需串行执行。
    /// </summary>
    [CollectionDefinition("DiagnosticLogSerial", DisableParallelization = true)]
    public class DiagnosticLogSerialCollection
    {
    }

    /// <summary>
    /// DiagnosticLog 的 ILogger 路由验证。
    /// </summary>
    [Collection("DiagnosticLogSerial")]
    public class DiagnosticLogTests : IDisposable
    {
        private readonly CapturingLogger logger = new CapturingLogger();

        public DiagnosticLogTests()
        {
            DiagnosticLog.Configure(new CapturingLoggerFactory(logger));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // 恢复全局默认路由，避免影响其他测试
            DiagnosticLog.Configure(null);
        }

        [Fact]
        public void Error_路由到ILogger并渲染结构化模板()
        {
            var ex = new InvalidOperationException("测试异常");

            DiagnosticLog.Error(nameof(DiagnosticLogTests), "单元测试异常消息：{Exception}。", ex);

            var entry = Assert.Single(logger.Entries.Where(x => x.Message.Contains("单元测试异常消息")));
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Contains("[DiagnosticLogTests]", entry.Message);
            Assert.Contains("单元测试异常消息：System.InvalidOperationException: 测试异常", entry.Message);
        }

        [Fact]
        public void Warn_路由到ILogger的Warning级别并渲染模板参数()
        {
            DiagnosticLog.Warn(nameof(DiagnosticLogTests), "单元测试警告 {Count}：{Name}。", 42, "Xbc");

            var entry = Assert.Single(logger.Entries.Where(x => x.Message.Contains("单元测试警告")));
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Contains("[DiagnosticLogTests]", entry.Message);
            Assert.Contains("单元测试警告 42：Xbc", entry.Message);
        }

        [Fact]
        public void 传null停止日志输出_不再进入ILogger()
        {
            DiagnosticLog.Configure(null);

            var before = logger.Entries.Count;
            DiagnosticLog.Warn(nameof(DiagnosticLogTests), "未配置时不输出日志");
            DiagnosticLog.Error(nameof(DiagnosticLogTests), "未配置时不输出日志");

            Assert.Equal(before, logger.Entries.Count);
        }

        /// <summary>
        /// 捕获日志条目的测试日志器。
        /// </summary>
        private sealed class CapturingLogger : ILogger
        {
            public List<(LogLevel Level, string Message)> Entries { get; }
                = new List<(LogLevel, string)>();

            public IDisposable? BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                Entries.Add((logLevel, state.ToString() ?? string.Empty));
            }
        }

        /// <summary>
        /// 返回固定日志器的测试日志工厂。
        /// </summary>
        private sealed class CapturingLoggerFactory : ILoggerFactory
        {
            private readonly ILogger logger;

            public CapturingLoggerFactory(ILogger logger)
            {
                this.logger = logger;
            }

            public void AddProvider(ILoggerProvider provider)
            {
            }

            public ILogger CreateLogger(string categoryName) => logger;

            public void Dispose()
            {
            }
        }
    }
}
