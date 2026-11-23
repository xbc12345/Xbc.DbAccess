using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xbc.DbAccess.Common;
using Xbc.DbAccess.Diagnostics;
using Xbc.DbAccess.UnitTests.Infrastructure;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Diagnostics
{
    /// <summary>
    /// 诊断配置是全局静态状态，与其他使用 DiagnosticLog 的测试类并行会互相污染，需串行执行。
    /// </summary>
    [CollectionDefinition("DbAccessDiagnosticsSerial", DisableParallelization = true)]
    public class DbAccessDiagnosticsSerialCollection
    {
    }

    /// <summary>
    /// DbAccessDiagnostics 的日志路由与慢查询告警验证。
    /// </summary>
    [Collection("DbAccessDiagnosticsSerial")]
    public class DbAccessDiagnosticsTests : SqliteTestBase
    {
        private readonly CapturingLogger logger = new CapturingLogger();

        public DbAccessDiagnosticsTests()
        {
            DbAccessDiagnostics.ConfigureLogging(new CapturingLoggerFactory(logger));
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            // 恢复全局默认状态（含“已配置”标志，保证自动接入类测试从干净状态开始），避免影响其他测试
            DbAccessDiagnostics.SlowQueryThreshold = null;
            DiagnosticLog.ResetForTest();
            base.Dispose();
        }

        [Fact]
        public void 阈值未设置时不输出慢查询告警()
        {
            DbAccessDiagnostics.WarnIfSlowQuery("ExecuteNonQueryAsync", "SELECT 1", TimeSpan.FromMinutes(1));

            Assert.Empty(logger.Entries);
        }

        [Fact]
        public void 耗时低于阈值时不输出慢查询告警()
        {
            DbAccessDiagnostics.SlowQueryThreshold = TimeSpan.FromSeconds(1);

            DbAccessDiagnostics.WarnIfSlowQuery("ExecuteNonQueryAsync", "SELECT 1", TimeSpan.FromMilliseconds(100));

            Assert.Empty(logger.Entries);
        }

        [Fact]
        public void 耗时达到阈值时输出含操作名SQL与耗时的告警()
        {
            DbAccessDiagnostics.SlowQueryThreshold = TimeSpan.FromSeconds(1);

            DbAccessDiagnostics.WarnIfSlowQuery("ExecuteScalarAsync", "SELECT COUNT(*) FROM TestUsers", TimeSpan.FromSeconds(2));

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Contains("[DbAccessDiagnostics]", entry.Message);
            Assert.Contains("ExecuteScalarAsync", entry.Message);
            Assert.Contains("SELECT COUNT(*) FROM TestUsers", entry.Message);
            Assert.Contains("2000", entry.Message);
        }

        [Fact]
        public async Task 执行SQL耗时达到阈值时经ILogger输出慢查询告警()
        {
            DbAccessDiagnostics.SlowQueryThreshold = TimeSpan.Zero;// 阈值为零，所有执行均告警

            await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM TestUsers");

            var entry = Assert.Single(logger.Entries.Where(x => x.Message.Contains("ExecuteScalarAsync")));
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Contains("SELECT COUNT(*) FROM TestUsers", entry.Message);
        }

        [Fact]
        public void AddDbAccess解析工厂时自动接入容器日志()
        {
            DiagnosticLog.ResetForTest();

            var services = new ServiceCollection()
                .AddSingleton<ILoggerFactory>(new CapturingLoggerFactory(logger))
                .AddDbAccess(Factory);
            using (var provider = services.BuildServiceProvider())
            {
                provider.GetRequiredService<IDbAccessFactory>();
            }

            DiagnosticLog.Warn(nameof(DbAccessDiagnosticsTests), "自动接入验证");

            Assert.Contains(logger.Entries, x => x.Message.Contains("自动接入验证"));
        }

        [Fact]
        public void 容器无日志工厂时保持未配置()
        {
            DiagnosticLog.ResetForTest();

            var services = new ServiceCollection().AddDbAccess(Factory);
            using (var provider = services.BuildServiceProvider())
            {
                provider.GetRequiredService<IDbAccessFactory>();
            }

            Assert.False(DiagnosticLog.IsConfigured);
        }

        [Fact]
        public void 显式配置的日志优先于容器自动接入()
        {
            // 构造函数已显式接入本实例的 logger，容器中的另一日志工厂不应覆盖
            var containerLogger = new CapturingLogger();
            var services = new ServiceCollection()
                .AddSingleton<ILoggerFactory>(new CapturingLoggerFactory(containerLogger))
                .AddDbAccess(Factory);
            using (var provider = services.BuildServiceProvider())
            {
                provider.GetRequiredService<IDbAccessFactory>();
            }

            DiagnosticLog.Warn(nameof(DbAccessDiagnosticsTests), "显式优先验证");

            Assert.Contains(logger.Entries, x => x.Message.Contains("显式优先验证"));
            Assert.Empty(containerLogger.Entries);
        }

        [Fact]
        public void AddDbAccessDiagnostics解析工厂时应用慢查询阈值()
        {
            DbAccessDiagnostics.SlowQueryThreshold = null;

            var services = new ServiceCollection()
                .AddDbAccessDiagnostics(o => o.SlowQueryThreshold = TimeSpan.FromMilliseconds(500))
                .AddDbAccess(Factory);
            using (var provider = services.BuildServiceProvider())
            {
                provider.GetRequiredService<IDbAccessFactory>();
            }

            Assert.Equal(TimeSpan.FromMilliseconds(500), DbAccessDiagnostics.SlowQueryThreshold);
        }

        [Fact]
        public void 命名注册解析工厂提供器时自动接入容器日志()
        {
            DiagnosticLog.ResetForTest();

            var services = new ServiceCollection()
                .AddSingleton<ILoggerFactory>(new CapturingLoggerFactory(logger))
                .AddDbAccess("sqlite-test", Factory);
            using (var provider = services.BuildServiceProvider())
            {
                var factoryProvider = provider.GetRequiredService<IDbAccessFactoryProvider>();
                Assert.Equal("sqlite-test", Assert.Single(factoryProvider.Names));
            }

            DiagnosticLog.Warn(nameof(DbAccessDiagnosticsTests), "命名注册自动接入验证");

            Assert.Contains(logger.Entries, x => x.Message.Contains("命名注册自动接入验证"));
        }

        /// <summary>
        /// 捕获日志条目的测试日志器。
        /// </summary>
        private sealed class CapturingLogger : ILogger
        {
            public List<(LogLevel Level, string Message)> Entries { get; }
                = new List<(LogLevel, string)>();

            IDisposable? ILogger.BeginScope<TState>(TState state) => null;

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
