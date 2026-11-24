using System.Collections.Concurrent;
using System.Diagnostics;
using Xbc.DbAccess.Diagnostics;
using Xbc.DbAccess.UnitTests.Infrastructure;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Diagnostics
{
    /// <summary>
    /// DbAccessActivitySource SQL 执行 span 的产生与 tag 验证（基于真实 SQLite 执行）。
    /// </summary>
    public class DbAccessActivitySourceTests : SqliteTestBase
    {
        /// <summary>
        /// 挂接仅监听 Xbc.DbAccess 的 ActivityListener，采集已停止的 span（using 结束自动移除，不影响其他测试）。
        /// </summary>
        private static (ActivityListener Listener, ConcurrentBag<Activity> Stopped) AttachListener()
        {
            var stopped = new ConcurrentBag<Activity>();
            var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == DbAccessActivitySource.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> options)
                    => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => stopped.Add(activity),
            };
            ActivitySource.AddActivityListener(listener);
            return (listener, stopped);
        }

        [Fact]
        public void 未接入listener_StartExecution返回null_零开销()
        {
            Assert.Null(DbAccessActivitySource.StartExecution("SQLite", "ExecuteNonQueryAsync", "SELECT 1"));
        }

        [Fact]
        public async Task 成功执行_产生携带OTel语义tag的Ok状态span()
        {
            var (listener, stopped) = AttachListener();
            try
            {
                const string sql = "UPDATE TestUsers SET Age = 26 WHERE Name = 'Alice'";
                await DbAccess.ExecuteNonQueryAsync(sql);

                // listener 为进程级，其他并行测试类的 span 也会进入采集袋，按 db.statement 过滤本次执行
                var span = Assert.Single(stopped.Where(a => a.GetTagItem("db.statement") as string == sql));
                Assert.Equal(DbAccessActivitySource.ActivityName, span.OperationName);
                Assert.Equal(ActivityKind.Client, span.Kind);
                Assert.Equal(ActivityStatusCode.Ok, span.Status);
                Assert.Equal("SQLite", span.GetTagItem("db.system"));
                Assert.Equal("ExecuteNonQueryAsync", span.GetTagItem("db.operation"));
                Assert.Equal(sql, span.GetTagItem("db.statement"));
            }
            finally
            {
                listener.Dispose();
            }
        }

        [Fact]
        public async Task 失败执行_产生Error状态span()
        {
            var (listener, stopped) = AttachListener();
            try
            {
                const string sql = "THIS IS NOT A VALID SQL STATEMENT";
                await Assert.ThrowsAnyAsync<Exception>(() => DbAccess.ExecuteNonQueryAsync(sql));

                // listener 为进程级，其他并行测试类的 span 也会进入采集袋，按 db.statement 过滤本次执行
                var span = Assert.Single(stopped.Where(a => a.GetTagItem("db.statement") as string == sql));
                Assert.Equal(DbAccessActivitySource.ActivityName, span.OperationName);
                Assert.Equal(ActivityStatusCode.Error, span.Status);
                Assert.Equal("ExecuteNonQueryAsync", span.GetTagItem("db.operation"));
            }
            finally
            {
                listener.Dispose();
            }
        }

        [Fact]
        public async Task 失败执行_span记录exception事件与errorTypeTag()
        {
            var (listener, stopped) = AttachListener();
            try
            {
                const string sql = "THIS IS ALSO NOT A VALID SQL STATEMENT";
                var thrown = await Assert.ThrowsAnyAsync<Exception>(() => DbAccess.ExecuteNonQueryAsync(sql));

                // listener 为进程级，其他并行测试类的 span 也会进入采集袋，按 db.statement 过滤本次执行
                var span = Assert.Single(stopped.Where(a => a.GetTagItem("db.statement") as string == sql));
                Assert.Equal(ActivityStatusCode.Error, span.Status);

                // OTel 约定：失败 span 携带 exception 事件（exception.type / exception.message）与 error.type tag
                var exceptionEvent = Assert.Single(span.Events, e => e.Name == "exception");
                Assert.Equal(thrown.GetType().FullName, exceptionEvent.Tags.SingleOrDefault(t => t.Key == "exception.type").Value as string);
                Assert.Equal(thrown.Message, exceptionEvent.Tags.SingleOrDefault(t => t.Key == "exception.message").Value as string);
                Assert.Equal(thrown.GetType().FullName, span.GetTagItem("error.type") as string);
            }
            finally
            {
                listener.Dispose();
            }
        }
    }
}
