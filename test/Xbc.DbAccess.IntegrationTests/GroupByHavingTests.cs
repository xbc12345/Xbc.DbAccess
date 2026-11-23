using Xbc.DbAccess.Entity;
using Xbc.DbAccess.IntegrationTests.Infrastructure;
using Xunit;
using static Xbc.DbAccess.IntegrationTests.Infrastructure.RealDbDialect;

namespace Xbc.DbAccess.IntegrationTests
{
    /// <summary>
    /// 分组查询（GroupBy + 聚合投影 + HAVING + 分组排序/窗口）的真库回归：
    /// 五方言上分组层 SQL（键列暴露 + 聚合别名 + GROUP BY/HAVING + 恒裁剪包裹 + 窗口包裹）
    /// 形态合法且语义正确；HAVING 数值比较统一使用 int 列（SQLite decimal 参数按 TEXT 绑定属提供商既有行为）。
    /// </summary>
    public abstract class GroupByHavingTestsBase : RealDbTestBase
    {
        protected GroupByHavingTestsBase(RealDbDialect dialect) : base(dialect)
        {
        }

        protected override Type[] EntityTypes => new[] { typeof(GroupStatRow) };

        /// <summary>
        /// 种子数据（6 行）与预期分组（5 组）：
        /// (1,East) Qty=10+20=30 Cnt=2 AvgQty=15 Amount=300；
        /// (2,West) Qty=50 Cnt=1 Amount=500；
        /// (3,East) Qty=40 Cnt=1 Amount=400；
        /// (3,West) Qty=5 Cnt=1 Amount=50；
        /// (4,West) Qty=10 Cnt=1 Amount=100。
        /// </summary>
        private async Task SeedAsync(EntitySet<GroupStatRow> entitySet)
        {
            var rows = new List<GroupStatRow>
            {
                new GroupStatRow { CustomerId = 1, Region = "East", Amount = 100m, Qty = 10 },
                new GroupStatRow { CustomerId = 1, Region = "East", Amount = 200m, Qty = 20 },
                new GroupStatRow { CustomerId = 2, Region = "West", Amount = 500m, Qty = 50 },
                new GroupStatRow { CustomerId = 3, Region = "East", Amount = 400m, Qty = 40 },
                new GroupStatRow { CustomerId = 3, Region = "West", Amount = 50m, Qty = 5 },
                new GroupStatRow { CustomerId = 4, Region = "West", Amount = 100m, Qty = 10 }
            };
            await entitySet.InsertRangeAsync(rows);
        }

        [SkippableFact]
        public async Task GroupBy_CompositeKey_AggregateSelect_ReturnsAllGroups()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .Select(g => new { g.Key.CustomerId, g.Key.Region, Cnt = g.Count(), TotalQty = g.Sum(x => x.Qty), TotalAmount = g.Sum(x => x.Amount) })
                .ToListAsync();

            Assert.Equal(5, rows.Count);

            var east1 = rows.Single(r => r.CustomerId == 1 && r.Region == "East");
            Assert.Equal(2L, east1.Cnt);
            Assert.Equal(30, east1.TotalQty);
            Assert.Equal(300m, east1.TotalAmount);

            var west3 = rows.Single(r => r.CustomerId == 3 && r.Region == "West");
            Assert.Equal(1L, west3.Cnt);
            Assert.Equal(5, west3.TotalQty);
            Assert.Equal(50m, west3.TotalAmount);
        }

        [SkippableFact]
        public async Task GroupBy_SingleKey_Select_ReturnsKeyGroups()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            var rows = await entitySet.AsQuery()
                .GroupBy(u => u.CustomerId)
                .Select(g => new { g.Key, Cnt = g.Count() })
                .ToListAsync();

            Assert.Equal(4, rows.Count);
            var group1 = rows.Single(r => r.Key == 1);
            Assert.Equal(2L, group1.Cnt);
        }

        [SkippableFact]
        public async Task GroupBy_SelectWholeKey_ReturnsKeyPairs()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .OrderBy(g => g.Key.CustomerId)
                .ThenBy(g => g.Key.Region)
                .Select(g => g.Key)
                .ToListAsync();

            Assert.Equal(5, rows.Count);
            Assert.Equal(new[] { 1L, 2L, 3L, 3L, 4L }, rows.Select(r => r.CustomerId));
            Assert.Equal(new[] { "East", "West", "East", "West", "West" }, rows.Select(r => r.Region));
        }

        [SkippableFact]
        public async Task GroupBy_AverageAggregate_ReturnsFractionalMean()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .Select(g => new { g.Key.CustomerId, g.Key.Region, AvgQty = g.Average(x => x.Qty) })
                .ToListAsync();

            Assert.Equal(5, rows.Count);
            Assert.Equal(15.0, rows.Single(r => r.CustomerId == 1 && r.Region == "East").AvgQty);
        }

        [SkippableFact]
        public async Task GroupBy_HavingCount_GeFilter_ReturnsQualifiedGroups()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .Having(g => g.Count() >= 2)
                .Select(g => new { g.Key.CustomerId, g.Key.Region, Cnt = g.Count() })
                .ToListAsync();

            var row = Assert.Single(rows);
            Assert.Equal(1L, row.CustomerId);
            Assert.Equal("East", row.Region);
            Assert.Equal(2L, row.Cnt);
        }

        [SkippableFact]
        public async Task GroupBy_HavingAggregateValue_FiltersGroups()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .Having(g => g.Sum(x => x.Qty) >= 40)
                .Select(g => new { g.Key.CustomerId, g.Key.Region, TotalQty = g.Sum(x => x.Qty) })
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, r => r.CustomerId == 2 && r.Region == "West" && r.TotalQty == 50);
            Assert.Contains(rows, r => r.CustomerId == 3 && r.Region == "East" && r.TotalQty == 40);
        }

        [SkippableFact]
        public async Task GroupBy_HavingMultiplePredicates_AreAndCombined()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .Having(g => g.Count() >= 1)
                .Having(g => g.Sum(x => x.Qty) >= 50)
                .Select(g => new { g.Key.CustomerId, g.Key.Region })
                .ToListAsync();

            // 两个 HAVING 谓词 AND 合并：Qty>=50 的组为 (2,West)=50 与 (1,East)=30? 否——仅 (2,West)=50
            var row = Assert.Single(rows);
            Assert.Equal(2L, row.CustomerId);
            Assert.Equal("West", row.Region);
        }

        [SkippableFact]
        public async Task GroupBy_HavingKeyCondition_FiltersByKey()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .Having(g => g.Key.CustomerId == 1)
                .Select(g => new { g.Key.CustomerId, g.Key.Region })
                .ToListAsync();

            var row = Assert.Single(rows);
            Assert.Equal(1L, row.CustomerId);
            Assert.Equal("East", row.Region);
        }

        [SkippableFact]
        public async Task GroupBy_OrderByAggregateDescending_SkipTake_ReturnsWindow()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            // 聚合降序：500(2,West)、40(3,East)、30(1,East)、10(4,West)、5(3,West)，窗口 [1,3) = (3,East)、(1,East)
            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .OrderByDescending(g => g.Sum(x => x.Qty))
                .Skip(1)
                .Take(2)
                .Select(g => new { g.Key.CustomerId, g.Key.Region, TotalQty = g.Sum(x => x.Qty) })
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Equal(3L, rows[0].CustomerId);
            Assert.Equal("East", rows[0].Region);
            Assert.Equal(40, rows[0].TotalQty);
            Assert.Equal(1L, rows[1].CustomerId);
            Assert.Equal("East", rows[1].Region);
            Assert.Equal(30, rows[1].TotalQty);
        }

        [SkippableFact]
        public async Task GroupBy_OrderByKeyColumns_ThenBy_ReturnsFullOrder()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .OrderBy(g => g.Key.CustomerId)
                .ThenBy(g => g.Key.Region)
                .Select(g => new { g.Key.CustomerId, g.Key.Region })
                .ToListAsync();

            Assert.Equal(new[] { 1L, 2L, 3L, 3L, 4L }, rows.Select(r => r.CustomerId));
            Assert.Equal(new[] { "East", "West", "East", "West", "West" }, rows.Select(r => r.Region));
        }

        [SkippableFact]
        public async Task GroupBy_OrderByKeyDescending_SkipTake_ReturnsWindow()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .OrderByDescending(g => g.Key.CustomerId)
                .Skip(1)
                .Take(2)
                .Select(g => new { g.Key.CustomerId, g.Key.Region })
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Equal(3L, rows[0].CustomerId);
            Assert.Equal(3L, rows[1].CustomerId);
        }

        [SkippableFact]
        public async Task GroupBy_ProjectionFirst_ReturnsFirstGroupByAggregate()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            var first = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .OrderBy(g => g.Sum(x => x.Qty))
                .Select(g => new { g.Key.CustomerId, g.Key.Region, TotalQty = g.Sum(x => x.Qty) })
                .FirstAsync();

            Assert.Equal(3L, first.CustomerId);
            Assert.Equal("West", first.Region);
            Assert.Equal(5, first.TotalQty);
        }

        [SkippableFact]
        public async Task GroupBy_CountAsync_ReturnsGroupCount()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            var totalCount = await entitySet.AsQuery().GroupBy(u => new { u.CustomerId, u.Region }).CountAsync();
            var filteredCount = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .Having(g => g.Count() >= 2)
                .CountAsync();

            Assert.Equal(5L, totalCount);
            Assert.Equal(1L, filteredCount);
        }

        [SkippableFact]
        public async Task GroupBy_HavingCountAsync_IgnoresOrderingWindow()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            // COUNT(*) 统计全部组数（COUNT 语义不受排序/窗口影响，与设计文档 §4.2 一致）
            var count = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .OrderBy(g => g.Key.CustomerId)
                .CountAsync();

            Assert.Equal(5L, count);
        }

        #region HAVING 组合形态与窗口边界（代码审查补充）

        [SkippableFact]
        public async Task GroupBy_WithWhere_Having_AggregatesFilteredRows()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            // Where 源过滤（Qty >= 10 剔除 (3,West) 行）与 HAVING 组过滤并存，源参数与 HAVING 参数共享名域
            // （Set 桥接：IEntitySet.Where 一跳生成 SQL 后经 GroupBy 获得分组能力）
            var rows = await entitySet
                .Where(u => u.Qty >= 10)
                .GroupBy(u => new { u.CustomerId, u.Region })
                .Having(g => g.Sum(x => x.Qty) >= 30)
                .Select(g => new { g.Key.CustomerId, g.Key.Region, TotalQty = g.Sum(x => x.Qty) })
                .ToListAsync();

            Assert.Equal(3, rows.Count);
            Assert.Equal(30, rows.Single(r => r.CustomerId == 1 && r.Region == "East").TotalQty);
            Assert.Equal(50, rows.Single(r => r.CustomerId == 2 && r.Region == "West").TotalQty);
            Assert.Equal(40, rows.Single(r => r.CustomerId == 3 && r.Region == "East").TotalQty);
        }

        [SkippableFact]
        public async Task GroupBy_HavingOrComposition_FiltersByEitherBranch()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            // OR 组合：Qty 和 >= 40 或键 == 4——(2,West)50✓、(3,East)40✓、(4,West)键命中✓；(1,East)30 被滤、(3,West)5 被滤
            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .Having(g => g.Sum(x => x.Qty) >= 40 || g.Key.CustomerId == 4)
                .Select(g => new { g.Key.CustomerId, g.Key.Region })
                .ToListAsync();

            Assert.Equal(3, rows.Count);
            Assert.Contains(rows, r => r.CustomerId == 2 && r.Region == "West");
            Assert.Contains(rows, r => r.CustomerId == 3 && r.Region == "East");
            Assert.Contains(rows, r => r.CustomerId == 4 && r.Region == "West");
        }

        [SkippableFact]
        public async Task GroupBy_HavingAggregateVersusKeyColumn_DirectSplice()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            // 聚合与键列比较（COUNT(*) >= CustomerId）：仅 (1,East) 满足 2 >= 1
            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .Having(g => g.Count() >= g.Key.CustomerId)
                .Select(g => new { g.Key.CustomerId, g.Key.Region })
                .ToListAsync();

            var row = Assert.Single(rows);
            Assert.Equal(1L, row.CustomerId);
            Assert.Equal("East", row.Region);
        }

        [SkippableFact]
        public async Task GroupBy_HavingClosureValue_IsParameterized()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            // 闭包值参数化（永不内联）；复合键下 (3,East) 与 (3,West) 是两个组，Qty 和分别为 40 与 5
            var threshold = 35;
            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .Having(g => g.Sum(x => x.Qty) > threshold)
                .Select(g => new { g.Key.CustomerId, g.Key.Region, TotalQty = g.Sum(x => x.Qty) })
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Equal(50, rows.Single(r => r.CustomerId == 2 && r.Region == "West").TotalQty);
            Assert.Equal(40, rows.Single(r => r.CustomerId == 3 && r.Region == "East").TotalQty);
        }

        [SkippableFact]
        public async Task GroupBy_HavingNegation_ExcludesMatchedGroups()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            // NOT 包裹：!(Qty 和 < 30)——(4,West)10 与 (3,West)5 被排除
            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .Having(g => !(g.Sum(x => x.Qty) < 30))
                .Select(g => new { g.Key.CustomerId, g.Key.Region })
                .ToListAsync();

            Assert.Equal(3, rows.Count);
            Assert.Contains(rows, r => r.CustomerId == 1 && r.Region == "East");
            Assert.Contains(rows, r => r.CustomerId == 2 && r.Region == "West");
            Assert.Contains(rows, r => r.CustomerId == 3 && r.Region == "East");
        }

        [SkippableFact]
        public async Task GroupBy_SelectDuplicateFragment_SharesAlias()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            // 同一聚合片段重复出现在投影中：别名去重共用，两列读取同值
            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .OrderBy(g => g.Key.CustomerId)
                .ThenBy(g => g.Key.Region)
                .Select(g => new { A = g.Sum(x => x.Qty), B = g.Sum(x => x.Qty) })
                .ToListAsync();

            Assert.Equal(5, rows.Count);
            Assert.All(rows, r => Assert.Equal(r.A, r.B));
            Assert.Equal(30, rows[0].A);
            Assert.Equal(50, rows[1].A);
            Assert.Equal(40, rows[2].A);
            Assert.Equal(5, rows[3].A);
            Assert.Equal(10, rows[4].A);
        }

        [SkippableFact]
        public async Task GroupBy_TakeAlone_ReturnsFirstGroups()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            // Take 单独使用（等价 OFFSET 0 的窗口）
            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .OrderBy(g => g.Key.CustomerId)
                .ThenBy(g => g.Key.Region)
                .Take(2)
                .Select(g => new { g.Key.CustomerId, g.Key.Region })
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Equal(1L, rows[0].CustomerId);
            Assert.Equal("East", rows[0].Region);
            Assert.Equal(2L, rows[1].CustomerId);
            Assert.Equal("West", rows[1].Region);
        }

        #endregion

        #region 业务场景（代码审查补充）

        [SkippableFact]
        public async Task GroupBy_HavingThenWindow_CombinedFlow()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            // 业务组合流：HAVING 过滤（Sum(Qty) >= 10 剔除 (3,West)5，其余 4 组保留）→ 排序 → 窗口 [1,3)
            // （HAVING 参数与窗口 SQL 在同一条语句中共存）
            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .Having(g => g.Sum(x => x.Qty) >= 10)
                .OrderBy(g => g.Key.CustomerId)
                .ThenBy(g => g.Key.Region)
                .Skip(1)
                .Take(2)
                .Select(g => new { g.Key.CustomerId, g.Key.Region, TotalQty = g.Sum(x => x.Qty) })
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Equal(2L, rows[0].CustomerId);
            Assert.Equal("West", rows[0].Region);
            Assert.Equal(50, rows[0].TotalQty);
            Assert.Equal(3L, rows[1].CustomerId);
            Assert.Equal("East", rows[1].Region);
            Assert.Equal(40, rows[1].TotalQty);
        }

        [SkippableFact]
        public async Task GroupBy_TopNGroupsByAmountSum()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            // 业务场景：金额合计 Top-N 客户组（decimal SUM 排序，业务报表金额排名）
            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .OrderByDescending(g => g.Sum(x => x.Amount))
                .Take(3)
                .Select(g => new { g.Key.CustomerId, g.Key.Region, TotalAmount = g.Sum(x => x.Amount) })
                .ToListAsync();

            Assert.Equal(3, rows.Count);
            Assert.Equal(500m, rows[0].TotalAmount);
            Assert.Equal(2L, rows[0].CustomerId);
            Assert.Equal(400m, rows[1].TotalAmount);
            Assert.Equal(3L, rows[1].CustomerId);
            Assert.Equal(300m, rows[2].TotalAmount);
            Assert.Equal(1L, rows[2].CustomerId);
        }

        [SkippableFact]
        public async Task GroupBy_FullAggregateReportRow()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            // 业务场景：单行全聚合报表（金额与数量的合计/计数/均值/最大/最小一次产出）
            var rows = await entitySet.AsQuery()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .OrderBy(g => g.Key.CustomerId)
                .ThenBy(g => g.Key.Region)
                .Select(g => new
                {
                    g.Key.CustomerId,
                    g.Key.Region,
                    TotalAmount = g.Sum(x => x.Amount),
                    TotalQty = g.Sum(x => x.Qty),
                    Cnt = g.Count(),
                    AvgQty = g.Average(x => x.Qty),
                    MaxQty = g.Max(x => x.Qty),
                    MinQty = g.Min(x => x.Qty)
                })
                .ToListAsync();

            var east1 = rows.Single(r => r.CustomerId == 1 && r.Region == "East");
            Assert.Equal(300m, east1.TotalAmount);
            Assert.Equal(30, east1.TotalQty);
            Assert.Equal(2L, east1.Cnt);
            Assert.Equal(15.0, east1.AvgQty);
            Assert.Equal(20, east1.MaxQty);
            Assert.Equal(10, east1.MinQty);

            var west3 = rows.Single(r => r.CustomerId == 3 && r.Region == "West");
            Assert.Equal(50m, west3.TotalAmount);
            Assert.Equal(5, west3.TotalQty);
            Assert.Equal(1L, west3.Cnt);
            Assert.Equal(5.0, west3.AvgQty);
            Assert.Equal(5, west3.MaxQty);
            Assert.Equal(5, west3.MinQty);
        }

        [SkippableFact]
        public async Task GroupBy_EmptySource_NoGroupsOnAllTerminations()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            // 业务场景：源过滤后无数据（空报表页）——各终结面一致的空语义
            var query = entitySet
                .Where(u => u.CustomerId == 999)
                .GroupBy(u => new { u.CustomerId, u.Region });

            var rows = await query.Select(g => new { g.Key.CustomerId, Total = g.Sum(x => x.Amount) }).ToListAsync();
            Assert.Empty(rows);

            Assert.Equal(0L, await query.CountAsync());

            var firstOrDefault = await query.Select(g => new { g.Key.CustomerId }).FirstOrDefaultAsync();
            Assert.Null(firstOrDefault);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => query.Select(g => new { g.Key.CustomerId }).FirstAsync());
        }

        [SkippableFact]
        public async Task GroupBy_SingleAsync_MultipleGroups_Throws()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            // 单组语义：多组命中时 SingleAsync 抛出
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => entitySet.AsQuery().GroupBy(u => new { u.CustomerId, u.Region })
                    .Select(g => new { g.Key.CustomerId, Total = g.Sum(x => x.Amount) }).SingleAsync());
        }

        [SkippableFact]
        public async Task GroupBy_SingleAsync_SingleGroup_ReturnsRow()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<GroupStatRow>(DbAccess);
            await SeedAsync(entitySet);

            // 单组语义：Where 源过滤收敛到单组（(1,East)）时命中返回
            var row = await entitySet
                .Where(u => u.CustomerId == 1)
                .GroupBy(u => new { u.CustomerId, u.Region })
                .Select(g => new { g.Key.CustomerId, g.Key.Region, Total = g.Sum(x => x.Amount) })
                .SingleAsync();

            Assert.Equal(1L, row.CustomerId);
            Assert.Equal("East", row.Region);
            Assert.Equal(300m, row.Total);
        }

        #endregion

        #region 测试实体

        /// <summary>分组测试实体（本测试类专属，表名 it_gb_stat）。</summary>
        [Table("it_gb_stat")]
        public class GroupStatRow
        {
            [PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public long CustomerId { get; set; }

            public string Region { get; set; }

            public decimal Amount { get; set; }

            public int Qty { get; set; }
        }

        #endregion
    }

    /// <summary>分组回归的 PostgreSQL 真库用例。</summary>
    public class PgSqlGroupByHavingTests : GroupByHavingTestsBase
    {
        public PgSqlGroupByHavingTests() : base(PgSql)
        {
        }
    }

    /// <summary>分组回归的 SQL Server 真库用例。</summary>
    public class SqlServerGroupByHavingTests : GroupByHavingTestsBase
    {
        public SqlServerGroupByHavingTests() : base(RealDbDialect.SqlServer)
        {
        }
    }

    /// <summary>分组回归的 MySQL 真库用例。</summary>
    public class MySqlGroupByHavingTests : GroupByHavingTestsBase
    {
        public MySqlGroupByHavingTests() : base(RealDbDialect.MySql)
        {
        }
    }

    /// <summary>分组回归的 Oracle 真库用例。</summary>
    public class OracleGroupByHavingTests : GroupByHavingTestsBase
    {
        public OracleGroupByHavingTests() : base(RealDbDialect.Oracle)
        {
        }
    }

    /// <summary>分组回归的 SQLite 真库用例（本地文件数据库，恒可用）。</summary>
    public class SqliteGroupByHavingTests : GroupByHavingTestsBase
    {
        public SqliteGroupByHavingTests() : base(RealDbDialect.Sqlite)
        {
        }
    }
}
