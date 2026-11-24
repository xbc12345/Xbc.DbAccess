using Xbc.DbAccess.Entity;
using Xbc.DbAccess.IntegrationTests.Infrastructure;
using Xunit;
using static Xbc.DbAccess.IntegrationTests.Infrastructure.RealDbDialect;

namespace Xbc.DbAccess.IntegrationTests
{
    /// <summary>
    /// 投影（Select）的真库回归：单列/匿名多列投影与排序、窗口状态的 SQL 组合
    /// （窗口行集经无行序变体被投影派生表包裹）在各方言上形态合法且语义正确。
    /// </summary>
    public abstract class SelectProjectionTestsBase : RealDbTestBase
    {
        protected SelectProjectionTestsBase(RealDbDialect dialect) : base(dialect)
        {
        }

        protected override Type[] EntityTypes => new[] { typeof(SalesOrder) };

        private async Task<List<SalesOrder>> SeedAsync(EntitySet<SalesOrder> entitySet)
        {
            var orders = Enumerable.Range(1, 6).Select(i => new SalesOrder
            {
                CustomerName = $"Customer{i}",
                Amount = i * 100m,
                OrderedAt = new DateTime(2026, 6, 1, 0, i, 0)
            }).ToList();
            await entitySet.InsertRangeAsync(orders);
            return orders;
        }

        [SkippableFact]
        public async Task Select_MultiColumns_ToList()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<SalesOrder>(DbAccess);
            await SeedAsync(entitySet);

            var rows = await entitySet.AsQuery().OrderBy(o => o.Id).Select(o => new { o.CustomerName, o.Amount }).ToListAsync();

            Assert.Equal(6, rows.Count);
            Assert.Equal("Customer1", rows[0].CustomerName);
            Assert.Equal(100m, rows[0].Amount);
            Assert.Equal("Customer6", rows[5].CustomerName);
            Assert.Equal(600m, rows[5].Amount);
        }

        [SkippableFact]
        public async Task Select_ValueTuple_ReturnsPositionalValues()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<SalesOrder>(DbAccess);
            await SeedAsync(entitySet);

            // 元组投影：ValueTuple.Create 形态按位置映射，各方言 SQL 合法且语义正确
            var rows = await entitySet.AsQuery().OrderBy(o => o.Id).Select(o => ValueTuple.Create(o.CustomerName, o.Amount)).ToListAsync();

            Assert.Equal(6, rows.Count);
            Assert.Equal("Customer1", rows[0].Item1);
            Assert.Equal(100m, rows[0].Item2);
            Assert.Equal("Customer6", rows[5].Item1);
        }

        [SkippableFact]
        public async Task Select_MultiColumns_AfterSkipTake_ReturnsWindowRows()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<SalesOrder>(DbAccess);
            await SeedAsync(entitySet);

            // 窗口态投影：窗口行集（无行序变体）被投影派生表包裹，行序由投影外层 ORDER BY 保证
            var rows = await entitySet.AsQuery()
                .OrderByDescending(o => o.Amount)
                .Skip(1)
                .Take(3)
                .Select(o => new { o.CustomerName, o.Amount })
                .ToListAsync();

            Assert.Equal(3, rows.Count);
            Assert.Equal(500m, rows[0].Amount);// 降序窗口跳过最高行 600
            Assert.Equal(400m, rows[1].Amount);
            Assert.Equal(300m, rows[2].Amount);
            Assert.Equal("Customer5", rows[0].CustomerName);
        }

        [SkippableFact]
        public async Task Select_First_AfterSkipTake_ReturnsWindowFirstRow()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<SalesOrder>(DbAccess);
            await SeedAsync(entitySet);

            // 单行终结方法的窗口截断（fetch=1，不越过查询级 Take 上限）：窗口 SQL 同样经无行序变体被投影包裹
            var first = await entitySet.AsQuery()
                .OrderBy(o => o.Id)
                .Skip(2)
                .Take(3)
                .Select(o => new { o.CustomerName })
                .FirstAsync();

            Assert.Equal("Customer3", first.CustomerName);
        }

        #region 测试实体

        /// <summary>投影测试实体（本测试类专属，表名 it_proj_order）。</summary>
        [Table("it_proj_order")]
        public class SalesOrder
        {
            [PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string CustomerName { get; set; }

            public decimal Amount { get; set; }

            public DateTime OrderedAt { get; set; }
        }

        #endregion
    }

    /// <summary>投影回归的 PostgreSQL 真库用例。</summary>
    public class PgSqlSelectProjectionTests : SelectProjectionTestsBase
    {
        public PgSqlSelectProjectionTests() : base(PgSql)
        {
        }
    }

    /// <summary>投影回归的 SQL Server 真库用例。</summary>
    public class SqlServerSelectProjectionTests : SelectProjectionTestsBase
    {
        public SqlServerSelectProjectionTests() : base(RealDbDialect.SqlServer)
        {
        }
    }

    /// <summary>投影回归的 MySQL 真库用例。</summary>
    public class MySqlSelectProjectionTests : SelectProjectionTestsBase
    {
        public MySqlSelectProjectionTests() : base(RealDbDialect.MySql)
        {
        }
    }

    /// <summary>投影回归的 Oracle 真库用例。</summary>
    public class OracleSelectProjectionTests : SelectProjectionTestsBase
    {
        public OracleSelectProjectionTests() : base(RealDbDialect.Oracle)
        {
        }
    }

    /// <summary>投影回归的 SQLite 真库用例（本地文件数据库，恒可用）。</summary>
    public class SqliteSelectProjectionTests : SelectProjectionTestsBase
    {
        public SqliteSelectProjectionTests() : base(RealDbDialect.Sqlite)
        {
        }
    }
}
