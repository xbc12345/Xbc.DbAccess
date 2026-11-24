using Xbc.DbAccess.Entity;
using Xbc.DbAccess.IntegrationTests.Infrastructure;
using Xunit;
using static Xbc.DbAccess.IntegrationTests.Infrastructure.RealDbDialect;

namespace Xbc.DbAccess.IntegrationTests
{
    /// <summary>
    /// 实体路径排序与窗口（OrderBy/ThenBy/Skip/Take → ToListAsync/First/Single）的真库回归：
    /// 纯排序与窗口截取（LIMIT/OFFSET、OFFSET/FETCH、ROW_NUMBER 兼容形态）在各方言上行集与行序语义一致。
    /// </summary>
    public abstract class EntityWindowPagingTestsBase : RealDbTestBase
    {
        protected EntityWindowPagingTestsBase(RealDbDialect dialect) : base(dialect)
        {
        }

        protected override Type[] EntityTypes => new[] { typeof(WindowOrder) };

        private async Task<List<WindowOrder>> SeedAsync(EntitySet<WindowOrder> entitySet)
        {
            var orders = new List<WindowOrder>
            {
                new WindowOrder { CustomerName = "Alice", Region = "East", Amount = 100m },
                new WindowOrder { CustomerName = "Bob", Region = "West", Amount = 200m },
                new WindowOrder { CustomerName = "Carol", Region = "East", Amount = 250m },
                new WindowOrder { CustomerName = "Dave", Region = "West", Amount = 300m },
                new WindowOrder { CustomerName = "Eve", Region = "East", Amount = 400m },
                new WindowOrder { CustomerName = "Frank", Region = "West", Amount = 500m }
            };
            await entitySet.InsertRangeAsync(orders);
            return orders;
        }

        [SkippableFact]
        public async Task OrderBy_WithoutWindow_ToList_ReturnsSortedRows()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<WindowOrder>(DbAccess);
            await SeedAsync(entitySet);

            // 纯排序路径：SQL 末尾附加 ORDER BY，无窗口包裹
            var rows = await entitySet.AsQuery().OrderBy(o => o.Amount).ToListAsync();

            Assert.Equal(6, rows.Count);
            Assert.Equal(new[] { "Alice", "Bob", "Carol", "Dave", "Eve", "Frank" }, rows.Select(o => o.CustomerName));
        }

        [SkippableFact]
        public async Task OrderByDescending_ThenBy_MultiKeyMixedDirections()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<WindowOrder>(DbAccess);
            await SeedAsync(entitySet);

            // Region 降序（West 在前）+ 组内 Amount 升序
            var rows = await entitySet.AsQuery().OrderByDescending(o => o.Region).ThenBy(o => o.Amount).ToListAsync();

            Assert.Equal(new[] { "Bob", "Dave", "Frank", "Alice", "Carol", "Eve" }, rows.Select(o => o.CustomerName));
        }

        [SkippableFact]
        public async Task OrderBy_SkipTake_ToList_ReturnsWindowRowsInOrder()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<WindowOrder>(DbAccess);
            await SeedAsync(entitySet);

            // 窗口路径：按方言生成 LIMIT/OFFSET 或 OFFSET/FETCH 或 ROW_NUMBER 兼容形态
            var rows = await entitySet.AsQuery().OrderBy(o => o.Amount).Skip(2).Take(3).ToListAsync();

            Assert.Equal(3, rows.Count);
            Assert.Equal(new[] { "Carol", "Dave", "Eve" }, rows.Select(o => o.CustomerName));
        }

        [SkippableFact]
        public async Task SkipTake_UnknownServerVersion_CompatRowNumberSql_ExecutesOnNewServer()
        {
            SkipIfDialectUnavailable();
            // 兼容路径仅存在于版本分支部方言：SQL Server（ROW_NUMBER 包裹）、Oracle（ROWNUM 三层嵌套）；
            // 其余方言恒为 LIMIT/OFFSET，无版本分支可测
            Skip.IfNot(Dialect is RealDbDialect.SqlServer or RealDbDialect.Oracle,
                $"{Dialect} 的窗口 SQL 与服务器版本无关，无兼容路径。");

            // 版本探测固定返回 MajorVersion == 0：与真库的实际新版本无关，构建器必走兼容形态包裹 SQL，
            // 在新版本真库上验证该兼容 SQL 的可执行性与结果正确性（版本探测失效时的回退路径）
            using var legacyDbAccess = CreateLegacyVersionDbAccess();
            var entitySet = new EntitySet<WindowOrder>(legacyDbAccess);
            await SeedAsync(entitySet);

            var rows = await entitySet.AsQuery().OrderBy(o => o.Amount).Skip(2).Take(3).ToListAsync();

            Assert.Equal(3, rows.Count);
            Assert.Equal(new[] { "Carol", "Dave", "Eve" }, rows.Select(o => o.CustomerName));
        }

        [SkippableFact]
        public async Task RawRowNumberOverPartition_ReturnsEachRegionTopAmountRow()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<WindowOrder>(DbAccess);
            await SeedAsync(entitySet);

            // 手写 ROW_NUMBER() OVER (PARTITION BY ...) 标准窗口 SQL（五方言通用；MySQL 8+/SQLite 3.25+ 均支持）：
            // 各分区按 Amount 取 Top1 —— East → Eve(400)、West → Frank(500)
            var sql = $@"SELECT {Qi("CustomerName")} FROM (
                            SELECT {Qi("CustomerName")}, ROW_NUMBER() OVER (PARTITION BY {Qi("Region")} ORDER BY {Qi("Amount")} DESC) AS {Qi("rn")}
                            FROM {Qi("it_win_order")}
                        ) ranked
                        WHERE {Qi("rn")} = 1
                        ORDER BY {Qi("CustomerName")}";

            await using var reader = await DbAccess.ExecuteReaderAsync(sql);
            var names = new List<string>();
            while (await reader.ReadAsync())
                names.Add(reader.GetString(0));

            Assert.Equal(new[] { "Eve", "Frank" }, names);
        }

        [SkippableFact]
        public async Task First_AfterSkip_ReturnsNthRowByOrder()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<WindowOrder>(DbAccess);
            await SeedAsync(entitySet);

            // 单条路径的窗口截取（fetch=1）：Skip 必须配合 Take（窗口状态契约），Take(1) 后 First 取窗口首行
            var fifth = await entitySet.AsQuery().OrderBy(o => o.Amount).Skip(4).Take(1).FirstAsync();

            Assert.Equal("Eve", fifth.CustomerName);
        }

        [SkippableFact]
        public async Task SingleOrDefault_AfterTake1_MultipleMatches_NoThrow()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<WindowOrder>(DbAccess);
            await SeedAsync(entitySet);

            // Take(1) 截断窗口后 Single 不再检测多条：以截断结果为准返回窗口首行
            var first = await entitySet.Where(o => o.Region == "East").OrderBy(o => o.Amount).Take(1).SingleOrDefaultAsync();

            Assert.NotNull(first);
            Assert.Equal("Alice", first.CustomerName);
        }

        [SkippableFact]
        public void Take_Zero_Throws()
        {
            // Take(0) 的空窗口无实用语义，且各数据库对 0 的窗口截断行为不一，构建期即拒绝
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new EntitySet<WindowOrder>(DbAccess).AsQuery().OrderBy(o => o.Id).Take(0));
        }

        #region 测试实体

        /// <summary>窗口分页测试实体（本测试类专属，表名 it_win_order）。</summary>
        [Table("it_win_order")]
        public class WindowOrder
        {
            [PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string CustomerName { get; set; }

            public string Region { get; set; }

            public decimal Amount { get; set; }
        }

        #endregion
    }

    /// <summary>窗口分页回归的 PostgreSQL 真库用例。</summary>
    public class PgSqlEntityWindowPagingTests : EntityWindowPagingTestsBase
    {
        public PgSqlEntityWindowPagingTests() : base(PgSql)
        {
        }
    }

    /// <summary>窗口分页回归的 SQL Server 真库用例。</summary>
    public class SqlServerEntityWindowPagingTests : EntityWindowPagingTestsBase
    {
        public SqlServerEntityWindowPagingTests() : base(RealDbDialect.SqlServer)
        {
        }
    }

    /// <summary>窗口分页回归的 MySQL 真库用例。</summary>
    public class MySqlEntityWindowPagingTests : EntityWindowPagingTestsBase
    {
        public MySqlEntityWindowPagingTests() : base(RealDbDialect.MySql)
        {
        }
    }

    /// <summary>窗口分页回归的 Oracle 真库用例。</summary>
    public class OracleEntityWindowPagingTests : EntityWindowPagingTestsBase
    {
        public OracleEntityWindowPagingTests() : base(RealDbDialect.Oracle)
        {
        }
    }

    /// <summary>窗口分页回归的 SQLite 真库用例（本地文件数据库，恒可用）。</summary>
    public class SqliteEntityWindowPagingTests : EntityWindowPagingTestsBase
    {
        public SqliteEntityWindowPagingTests() : base(RealDbDialect.Sqlite)
        {
        }
    }
}
