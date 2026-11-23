using Xbc.DbAccess.Entity;
using Xbc.DbAccess.IntegrationTests.Infrastructure;
using Xunit;
using static Xbc.DbAccess.IntegrationTests.Infrastructure.RealDbDialect;

namespace Xbc.DbAccess.IntegrationTests
{
    /// <summary>
    /// 标量聚合的真库回归：Count/Sum/Min/Max/Any/All（含谓词过滤与可空列）在 PostgreSQL 与 SQL Server 上语义一致。
    /// </summary>
    public abstract class AggregateTestsBase : RealDbTestBase
    {
        protected AggregateTestsBase(RealDbDialect dialect) : base(dialect)
        {
        }

        protected override Type[] EntityTypes => new[] { typeof(MetricRecord) };

        private async Task<List<MetricRecord>> SeedAsync(EntitySet<MetricRecord> entitySet)
        {
            var records = Enumerable.Range(1, 6).Select(i => new MetricRecord
            {
                Name = $"Metric{i}",
                Value = i * 10.5m,
                Hits = i % 2 == 0 ? i : (int?)null,
                RecordedAt = new DateTime(2026, 5, 1, 0, i, 0)
            }).ToList();
            await entitySet.InsertRangeAsync(records);
            return records;
        }

        [SkippableFact]
        public async Task CountAsync_WithAndWithoutPredicate()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<MetricRecord>(DbAccess);
            await SeedAsync(entitySet);

            Assert.Equal(6, await entitySet.AsQuery().CountAsync());
            Assert.Equal(3, await entitySet.Where(r => r.Hits != null).CountAsync());
            // Value = 10.5/21/31.5/42/52.5/63，大于 30 的有 4 个
            Assert.Equal(4, await entitySet.Where(r => r.Value > 30m).CountAsync());
        }

        [SkippableFact]
        public async Task SumAsync_SumsDecimalColumn()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<MetricRecord>(DbAccess);
            await SeedAsync(entitySet);

            // 10.5 + 21 + 31.5 + 42 + 52.5 + 63 = 220.5
            Assert.Equal(220.5m, await entitySet.AsQuery().SumAsync(r => r.Value));
            Assert.Equal(31.5m + 52.5m, await entitySet.Where(r => r.Name == "Metric3" || r.Name == "Metric5").SumAsync(r => r.Value));
        }

        [SkippableFact]
        public async Task MinMaxAsync_ReturnExtremes()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<MetricRecord>(DbAccess);
            await SeedAsync(entitySet);

            Assert.Equal(10.5m, await entitySet.AsQuery().MinAsync(r => r.Value));
            Assert.Equal(63m, await entitySet.AsQuery().MaxAsync(r => r.Value));
            Assert.Equal(new DateTime(2026, 5, 1, 0, 1, 0), await entitySet.AsQuery().MinAsync(r => r.RecordedAt));
            Assert.Equal(new DateTime(2026, 5, 1, 0, 6, 0), await entitySet.AsQuery().MaxAsync(r => r.RecordedAt));

            // 可空 int 列（3 行为 null）：聚合忽略 null 行
            Assert.Equal(2, await entitySet.AsQuery().MinAsync(r => r.Hits));
            Assert.Equal(6, await entitySet.AsQuery().MaxAsync(r => r.Hits));
        }

        [SkippableFact]
        public async Task AnyAllAsync_EvaluatePredicates()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<MetricRecord>(DbAccess);
            await SeedAsync(entitySet);

            Assert.True(await entitySet.Where(r => r.Value > 60m).AnyAsync());
            Assert.False(await entitySet.Where(r => r.Value > 100m).AnyAsync());
            Assert.True(await entitySet.AsQuery().AllAsync(r => r.Value >= 10.5m));
            Assert.False(await entitySet.AsQuery().AllAsync(r => r.Hits != null));
        }

        [SkippableFact]
        public async Task Aggregate_AfterSkipTake_WindowsRows()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<MetricRecord>(DbAccess);
            await SeedAsync(entitySet);

            // 窗口态聚合：窗口行集经无行序变体包裹（COUNT/EXISTS/SUM 的派生表与子查询内不得有裸 ORDER BY），
            // LIMIT/OFFSET 方言（MySQL/PG/SQLite）复用窗口形态；SQL Server 2008/Oracle 11g 走无尾序 ROW_NUMBER 兼容形态
            var window = entitySet.AsQuery().OrderBy(r => r.Id).Skip(2).Take(3);

            // Id 3/4/5 → Value 31.5/42/52.5
            Assert.Equal(3, await window.CountAsync());
            Assert.Equal(126m, await window.SumAsync(r => r.Value));
            Assert.True(await window.AnyAsync());
            Assert.True(await window.AllAsync(r => r.Value > 30m));
            Assert.False(await window.AllAsync(r => r.Value > 40m));
        }

        #region 测试实体

        /// <summary>聚合测试实体（本测试类专属，表名 it_agg_record）。</summary>
        [Table("it_agg_record")]
        public class MetricRecord
        {
            [PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; }

            public decimal Value { get; set; }

            public int? Hits { get; set; }

            public DateTime RecordedAt { get; set; }
        }

        #endregion
    }

    /// <summary>聚合回归的 PostgreSQL 真库用例。</summary>
    public class PgSqlAggregateTests : AggregateTestsBase
    {
        public PgSqlAggregateTests() : base(PgSql)
        {
        }
    }

    /// <summary>聚合回归的 SQL Server 真库用例。</summary>
    public class SqlServerAggregateTests : AggregateTestsBase
    {
        public SqlServerAggregateTests() : base(RealDbDialect.SqlServer)
        {
        }
    }

    /// <summary>聚合回归的 MySQL 真库用例。</summary>
    public class MySqlAggregateTests : AggregateTestsBase
    {
        public MySqlAggregateTests() : base(RealDbDialect.MySql)
        {
        }
    }

    /// <summary>聚合回归的 Oracle 真库用例。</summary>
    public class OracleAggregateTests : AggregateTestsBase
    {
        public OracleAggregateTests() : base(RealDbDialect.Oracle)
        {
        }
    }

    /// <summary>聚合回归的 SQLite 真库用例（本地文件数据库，恒可用）。</summary>
    public class SqliteAggregateTests : AggregateTestsBase
    {
        public SqliteAggregateTests() : base(RealDbDialect.Sqlite)
        {
        }
    }
}
