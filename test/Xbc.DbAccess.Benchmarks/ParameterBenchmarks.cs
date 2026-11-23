using System.Data.Common;
using BenchmarkDotNet.Attributes;
using Dapper;
using Xbc.DbAccess.Sqlite;

namespace Xbc.DbAccess.Benchmarks
{
    /// <summary>
    /// 参数构建性能基准（对象 → DbParameter）。
    /// <para>三方对照：Xbc（匿名对象反射转 DbParameter[]，即时物化）、Dapper（DynamicParameters，惰性表示）、
    /// 手写（SQLiteFactory.CreateParameter，即时物化理论下限）。</para>
    /// <para>语义差异说明：Dapper 的 DynamicParameters 仅构建惰性表示，真正物化延迟到执行期
    /// （该成本计入 ExecutionBenchmarks 的端到端对比）；Xbc 与手写在本环节即完成物化。</para>
    /// <para>使用 SQLite 提供程序（与执行基准一致），全程不建立任何连接。</para>
    /// </summary>
    [MemoryDiagnoser]
    public class ParameterBenchmarks
    {
        private const string SqlText = "UPDATE BenchTable SET Value = @P0 WHERE Id = @P1";

        private IDbAccess dbAccess;
        private DbProviderFactory sqliteFactory;
        private string[] manualNames;
        private object[] manualValues;

        [GlobalSetup]
        public void Setup()
        {
            dbAccess = SqliteAccessFactory.CreateSqliteFactory("Data Source=:memory:").Create();
            sqliteFactory = System.Data.SQLite.SQLiteFactory.Instance;

            // 预创建手写对照的参数名与值（50 组），避免测量字符串拼接
            manualNames = new string[50];
            manualValues = new object[50];
            for (int i = 0; i < 50; i++)
            {
                manualNames[i] = "@P" + i;
                manualValues[i] = i % 3 == 0 ? (object)i : i % 3 == 1 ? (object)("值" + i) : (object)(i + 0.5);
            }
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            dbAccess.Dispose();
        }

        #region Xbc（即时物化）
        [Benchmark(Description = "Xbc 参数构建x1")]
        public DbParameter[] Xbc_Params_1()
        {
            string sql = SqlText;
            return dbAccess.ConvertToDbParameters(BenchmarkParams.Params1, ref sql);
        }

        [Benchmark(Description = "Xbc 参数构建x10")]
        public DbParameter[] Xbc_Params_10()
        {
            string sql = SqlText;
            return dbAccess.ConvertToDbParameters(BenchmarkParams.Params10, ref sql);
        }

        [Benchmark(Description = "Xbc 参数构建x50")]
        public DbParameter[] Xbc_Params_50()
        {
            string sql = SqlText;
            return dbAccess.ConvertToDbParameters(BenchmarkParams.Params50, ref sql);
        }
        #endregion

        #region Dapper（惰性表示）
        [Benchmark(Description = "Dapper DynamicParametersx1")]
        public DynamicParameters Dapper_Params_1()
            => new DynamicParameters(BenchmarkParams.Params1);

        [Benchmark(Description = "Dapper DynamicParametersx10")]
        public DynamicParameters Dapper_Params_10()
            => new DynamicParameters(BenchmarkParams.Params10);

        [Benchmark(Description = "Dapper DynamicParametersx50")]
        public DynamicParameters Dapper_Params_50()
            => new DynamicParameters(BenchmarkParams.Params50);
        #endregion

        #region 手写（即时物化，理论下限）
        [Benchmark(Description = "手写参数构建x1")]
        public DbParameter[] Manual_Params_1() => ManualParams(1);

        [Benchmark(Description = "手写参数构建x10")]
        public DbParameter[] Manual_Params_10() => ManualParams(10);

        [Benchmark(Description = "手写参数构建x50")]
        public DbParameter[] Manual_Params_50() => ManualParams(50);

        private DbParameter[] ManualParams(int count)
        {
            var parameters = new DbParameter[count];
            for (int i = 0; i < count; i++)
            {
                var parameter = sqliteFactory.CreateParameter();
                parameter.ParameterName = manualNames[i];
                parameter.Value = manualValues[i];
                parameters[i] = parameter;
            }

            return parameters;
        }
        #endregion
    }
}
