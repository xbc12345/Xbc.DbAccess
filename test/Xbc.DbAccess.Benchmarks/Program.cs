using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Xbc.DbAccess.Benchmarks
{
    /// <summary>
    /// Xbc.DbAccess 性能基准入口。
    /// <para>运行示例：</para>
    /// <para>全部基准：dotnet run -c Release -f net8.0 -- --filter *</para>
    /// <para>单组基准：dotnet run -c Release -f net8.0 -- --filter "*MappingBenchmarks*"</para>
    /// <para>快速验证：dotnet run -c Release -f net8.0 -- --filter "*MappingBenchmarks*" --job dry</para>
    /// <para>注：禁用依赖优化验证（SqlSugarCore 官方发布包为非优化程序集，无法满足默认验证策略）。</para>
    /// </summary>
    public static class Program
    {
        public static void Main(string[] args)
        {
            var config = ManualConfig.Create(DefaultConfig.Instance)
                .WithOptions(ConfigOptions.DisableOptimizationsValidator);
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        }
    }
}
