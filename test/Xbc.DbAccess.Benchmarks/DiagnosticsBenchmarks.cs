using BenchmarkDotNet.Attributes;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Xbc.DbAccess.Benchmarks.Fakes;
using Xbc.DbAccess.Diagnostics;

namespace Xbc.DbAccess.Benchmarks
{
    /// <summary>
    /// 诊断开销基准：ActivitySource span 与 Meter 指标在开/关状态下的单次执行开销。
    /// <para>使用 Fake 管道（无任何数据库 I/O）执行 ExecuteNonQueryAsync，测得的差值即诊断自身开销：</para>
    /// <para>off（无 listener，框架承诺的零开销路径）→ activity（仅 span）→ metrics（仅指标）→ full（全开）。</para>
    /// <para>listener 回调为空实现，测得的是派发与 Activity 对象构建的下限开销（OTel SDK 真实聚合会更重，属消费侧成本）。</para>
    /// </summary>
    [MemoryDiagnoser]
    public class DiagnosticsBenchmarks
    {
        private const string SqlText = "UPDATE BenchTable SET Value = 1 WHERE Id = 1";

        /// <summary>
        /// 诊断模式：off（无 listener）、activity（仅 span）、metrics（仅指标）、full（全开）。
        /// </summary>
        [Params("off", "activity", "metrics", "full")]
        public string Mode { get; set; }

        private IDbAccess dbAccess;
        private ActivityListener activityListener;
        private MeterListener meterListener;

        [GlobalSetup]
        public void Setup()
        {
            dbAccess = new FakeDbAccessFactory().Create();

            if (Mode == "activity" || Mode == "full")
            {
                activityListener = new ActivityListener
                {
                    ShouldListenTo = source => source.Name == DbAccessActivitySource.Name,
                    Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                    SampleUsingParentId = (ref ActivityCreationOptions<string> options) => ActivitySamplingResult.AllDataAndRecorded,
                    ActivityStarted = activity => { },
                    ActivityStopped = activity => { }
                };
                ActivitySource.AddActivityListener(activityListener);
            }

            if (Mode == "metrics" || Mode == "full")
            {
                meterListener = new MeterListener
                {
                    InstrumentPublished = (instrument, listener) =>
                    {
                        if (instrument.Meter.Name == DbAccessMetrics.MeterName)
                            listener.EnableMeasurementEvents(instrument);
                    }
                };
                meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) => { });
                meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => { });
                meterListener.SetMeasurementEventCallback<int>((instrument, value, tags, state) => { });
                meterListener.Start();
            }

            // 预热一次，让异步上下文与命令创建路径就绪
            dbAccess.ExecuteNonQueryAsync(SqlText).GetAwaiter().GetResult();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            if (activityListener != null)
                activityListener.Dispose();

            if (meterListener != null)
                meterListener.Dispose();

            dbAccess.Dispose();
        }

        /// <summary>
        /// 单次 ExecuteNonQueryAsync 全管道开销（Fake 连接，无 I/O）。
        /// </summary>
        [Benchmark(Description = "执行管道（含连接管理+诊断）")]
        public Task<int> ExecuteUpdate()
            => dbAccess.ExecuteNonQueryAsync(SqlText);
    }
}
