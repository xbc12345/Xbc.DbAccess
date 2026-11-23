using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 查询链 GroupBy/Having 的 SQLite 真库端到端测试：
    /// 复合键/单成员键分组、聚合投影与别名机制（排序引用未投影聚合）、HAVING 过滤（聚合与键列条件）、
    /// 窗口与排序组合、枚举键/转换器列、CountAsync 终结、Set 桥接与拒绝清单的链式形态。
    /// </summary>
    public class EntityQueryGroupByTests : EntityTestBase
    {
        #region 测试实体

        [XbcDbEntity.Table("GroupByUsers")]
        public class GroupUserEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public long CustomerId { get; set; }

            public string Region { get; set; }

            public decimal Amount { get; set; }

            public int Qty { get; set; }

            // 带自定义转换器的列：聚合源列的读取值应与实体赋值一致（存储值 enc: 前缀被剥掉）
            [XbcDbEntity.Column(ConverterType = typeof(GroupTokenConverter))]
            public string Token { get; set; }

            // 枚举名称字符串列：键/聚合按语义值还原
            [XbcDbEntity.Column(EnumAsString = true)]
            public GroupUserStatus Status { get; set; }
        }

        /// <summary>名称字典序（Nine &lt; One）与数值序相反的枚举，验证按名称还原而非数值。</summary>
        public enum GroupUserStatus
        {
            One = 1,
            Nine = 9
        }

        /// <summary>Token 列的模拟转换器：存储值带 enc: 前缀。</summary>
        private sealed class GroupTokenConverter : Xbc.DbAccess.Entity.IDbValueConverter<string>
        {
            public string ConvertFrom(object dbValue) => dbValue == null ? null! : dbValue.ToString()!.Substring(4);
            public object ConvertTo(string value) => value == null ? null! : "enc:" + value;
        }

        /// <summary>命名复合键载体。</summary>
        public sealed class GroupUserKey
        {
            public long CustomerId { get; set; }

            public string Region { get; set; }
        }

        /// <summary>命名投影结果载体。</summary>
        public sealed class GroupStat
        {
            public long CustomerId { get; set; }

            public string Region { get; set; }

            public decimal Total { get; set; }

            public int Cnt { get; set; }
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE GroupByUsers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CustomerId INTEGER NOT NULL,
                    Region TEXT NULL,
                    Amount DECIMAL NOT NULL,
                    Qty INTEGER NOT NULL,
                    Token TEXT NULL,
                    Status TEXT NOT NULL
                );
                INSERT INTO GroupByUsers (CustomerId, Region, Amount, Qty, Token, Status) VALUES (1, 'East', 100, 2, 'enc:a', 'One');
                INSERT INTO GroupByUsers (CustomerId, Region, Amount, Qty, Token, Status) VALUES (1, 'East', 50, 1, 'enc:b', 'Nine');
                INSERT INTO GroupByUsers (CustomerId, Region, Amount, Qty, Token, Status) VALUES (2, 'West', 30, 4, 'enc:c', 'One');
                INSERT INTO GroupByUsers (CustomerId, Region, Amount, Qty, Token, Status) VALUES (2, NULL, 20, 3, 'enc:d', 'Nine');
                INSERT INTO GroupByUsers (CustomerId, Region, Amount, Qty, Token, Status) VALUES (3, 'East', 10, 5, 'enc:e', 'One');
            ");
        }

        private IEntityQuery<GroupUserEntity> Query()
        {
            return DbAccess.Set<GroupUserEntity>().AsQuery();
        }

        #region 分组聚合投影

        [Fact]
        public async Task GroupBy_CompositeKey_Aggregates_ReturnsGroupedRows()
        {
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .Select(g => new
                {
                    g.Key.CustomerId,
                    g.Key.Region,
                    Total = g.Sum(x => x.Amount),
                    Cnt = g.Count(),
                })
                .ToListAsync();

            Assert.Equal(4, rows.Count);
            var east = rows.Single(r => r.CustomerId == 1);
            Assert.Equal(150m, east.Total);
            Assert.Equal(2, east.Cnt);
            Assert.Null(rows.Single(r => r.CustomerId == 2 && r.Total == 20m).Region);// NULL 键自成分组
            Assert.Equal(30m, rows.Single(r => r.CustomerId == 2 && r.Region == "West").Total);
            Assert.Equal(10m, rows.Single(r => r.CustomerId == 3).Total);
        }

        [Fact]
        public async Task GroupBy_TupleOutput_SelectsKeyAndAggregates()
        {
            // 元组投影：键列引用与聚合调用按位置装配（元组为 ValueTuple.Create 形态，无元素名）
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId })
                .Select(g => ValueTuple.Create(g.Key.CustomerId, g.Count(), g.Sum(x => x.Amount)))
                .ToListAsync();

            Assert.Equal(3, rows.Count);
            var first = rows.Single(r => r.Item1 == 1);
            Assert.Equal(2, first.Item2);
            Assert.Equal(150m, first.Item3);
        }

        [Fact]
        public async Task GroupBy_OrderingByUnprojectedAggregate_OrdersByAliasReference()
        {
            // 排序引用未投影的聚合：注入分组 SELECT 并由外层裁剪，位置映射契约不受影响
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId })
                .OrderByDescending(g => g.Sum(x => x.Amount))
                .Select(g => new { g.Key.CustomerId, Cnt = g.Count() })
                .ToListAsync();

            Assert.Equal(3, rows.Count);
            Assert.Equal(1L, rows[0].CustomerId);
            Assert.Equal(2L, rows[1].CustomerId);
            Assert.Equal(3L, rows[2].CustomerId);
        }

        [Fact]
        public async Task GroupBy_ThenBy_KeyColumn_AppendsSecondaryOrdering()
        {
            // 主序 = Status 计数（One 3 行 / Nine 2 行），次序 = CustomerId 升序；相同计数组内按次序排
            var rows = await Query()
                .GroupBy(u => new { u.Status })
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key.Status)
                .Select(g => new { Key = g.Key.Status, Cnt = g.Count() })
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Equal(GroupUserStatus.One, rows[0].Key);
            Assert.Equal(3, rows[0].Cnt);
            Assert.Equal(GroupUserStatus.Nine, rows[1].Key);
            Assert.Equal(2, rows[1].Cnt);
        }

        [Fact]
        public async Task GroupBy_SingleMemberKey_SelectKeyAndCount()
        {
            var rows = await Query()
                .GroupBy(u => u.Region)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => new { Key = g.Key, Cnt = g.Count() })
                .ToListAsync();

            // East 组 3 行、West/NULL 组各 1 行；NULL 组键为 null（SQL 标准 NULL 分组语义）
            Assert.Equal(3, rows.Count);
            Assert.Equal("East", rows[0].Key);
            Assert.Equal(3, rows[0].Cnt);
            Assert.Equal(1, rows[1].Cnt);
            Assert.Equal(1, rows[2].Cnt);
            Assert.Contains(rows[1].Key, new[] { "West", (string)null });
            Assert.Contains(rows[2].Key, new[] { "West", (string)null });
        }

        [Fact]
        public async Task GroupBy_CompositeKey_SelectWholeKey_ExpandsToKeyColumns()
        {
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .OrderByDescending(g => g.Key)
                .Select(g => g.Key)
                .ToListAsync();

            // 匿名键整体出列：位置构造映射；按复合键降序（CustomerId, Region）
            Assert.Equal(4, rows.Count);
            Assert.Equal(3L, rows[0].CustomerId);
            Assert.Equal(2L, rows[1].CustomerId);
            Assert.Equal("West", rows[1].Region);
            Assert.Equal(2L, rows[2].CustomerId);
            Assert.Null(rows[2].Region);
            Assert.Equal(1L, rows[3].CustomerId);
        }

        [Fact]
        public async Task GroupBy_NamedKey_SelectWholeKey_BindsByKeyMembers()
        {
            var rows = await Query()
                .GroupBy(u => new GroupUserKey { CustomerId = u.CustomerId, Region = u.Region })
                .OrderByDescending(g => g.Key.CustomerId)
                .Select(g => g.Key)
                .ToListAsync();

            Assert.Equal(4, rows.Count);
            Assert.Equal(1L, rows[3].CustomerId);
            Assert.Equal("East", rows[3].Region);
        }

        [Fact]
        public async Task GroupBy_NamedInitializer_Select_BindsByMemberNames()
        {
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .OrderByDescending(g => g.Sum(x => x.Amount))
                .Select(g => new GroupStat { CustomerId = g.Key.CustomerId, Region = g.Key.Region, Total = g.Sum(x => x.Amount), Cnt = g.Count() })
                .ToListAsync();

            // 组序按组金额降序：客户 1（150，2 行）居首
            Assert.Equal(2, rows[0].Cnt);
            Assert.Equal(150m, rows[0].Total);
            Assert.Equal(1L, rows[0].CustomerId);
            Assert.Equal("East", rows[0].Region);
        }

        [Fact]
        public async Task GroupBy_ScalarAggregateOnly_SelectCount()
        {
            // 单列标量投影：整表分组组数（位置映射单列形态）
            var count = await Query()
                .GroupBy(u => new { u.CustomerId })
                .Select(g => g.Count())
                .FirstAsync();

            Assert.Equal(2, count);
        }

        #endregion

        #region HAVING 过滤

        [Fact]
        public async Task GroupBy_HavingAggregateCondition_FiltersGroups()
        {
            // 按 CustomerId 分组：客户 1（2 行）、客户 2（2 行）通过 Count >= 2，客户 3（1 行）被过滤；
            // Count >= 3 无组通过
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId })
                .Having(g => g.Count() >= 2)
                .Select(g => new { g.Key.CustomerId, Cnt = g.Count() })
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, r => r.CustomerId == 1L && r.Cnt == 2);
            Assert.Contains(rows, r => r.CustomerId == 2L && r.Cnt == 2);

            var empty = await Query()
                .GroupBy(u => new { u.CustomerId })
                .Having(g => g.Count() >= 3)
                .Select(g => new { g.Key.CustomerId })
                .ToListAsync();

            Assert.Empty(empty);
        }

        [Fact]
        public async Task GroupBy_HavingSumCondition_IsParameterizedAndCorrect()
        {
            // 闭包值参数化：SUM(Qty) > 3 过滤出 (2,West) 4 与 (3,East) 5
            // （数值比较用 int 列——System.Data.SQLite 将 decimal 参数绑定为 TEXT，属 SQLite 提供商既有行为，不属分组功能范畴）
            var threshold = 3;
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId, u.Region })
                .Having(g => g.Sum(x => x.Qty) > threshold)
                .OrderByDescending(g => g.Sum(x => x.Qty))
                .Select(g => new { g.Key.CustomerId, g.Key.Region, TotalQty = g.Sum(x => x.Qty) })
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Equal(3L, rows[0].CustomerId);
            Assert.Equal(5, rows[0].TotalQty);
            Assert.Equal(2L, rows[1].CustomerId);
            Assert.Equal("West", rows[1].Region);
            Assert.Equal(4, rows[1].TotalQty);
        }

        [Fact]
        public async Task GroupBy_HavingKeyCondition_FiltersByGroupKey()
        {
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId })
                .Having(g => g.Key.CustomerId != 2L)
                .Select(g => new { g.Key.CustomerId, Cnt = g.Count() })
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.NotEqual(2L, r.CustomerId));
        }

        [Fact]
        public async Task GroupBy_HavingLogicalCombination_CombinesConditions()
        {
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId })
                .Having(g => g.Count() >= 1 && g.Sum(x => x.Qty) < 5)
                .Select(g => new { g.Key.CustomerId })
                .ToListAsync();

            // 组内 Qty 合计：客户 1（3）、客户 2（7）、客户 3（5）——只有客户 1 通过 Sum < 5
            var row = Assert.Single(rows);
            Assert.Equal(1L, row.CustomerId);
        }

        #endregion

        #region 窗口与终结执行

        [Fact]
        public async Task GroupBy_WindowOverGroupedResults_ReturnsWindowGroups()
        {
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId })
                .OrderByDescending(g => g.Sum(x => x.Amount))
                .ThenBy(g => g.Key.CustomerId)
                .Skip(1)
                .Take(2)
                .Select(g => new { g.Key.CustomerId, Total = g.Sum(x => x.Amount) })
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Equal(2L, rows[0].CustomerId);
            Assert.Equal(50m, rows[0].Total);
            Assert.Equal(3L, rows[1].CustomerId);
            Assert.Equal(10m, rows[1].Total);
        }

        [Fact]
        public async Task GroupBy_FirstAsync_ReturnsTopGroup()
        {
            var row = await Query()
                .GroupBy(u => new { u.CustomerId })
                .OrderByDescending(g => g.Sum(x => x.Amount))
                .Select(g => new { g.Key.CustomerId, Total = g.Sum(x => x.Amount) })
                .FirstOrDefaultAsync();

            Assert.NotNull(row);
            Assert.Equal(1L, row.CustomerId);
            Assert.Equal(150m, row.Total);
        }

        [Fact]
        public async Task GroupBy_CountAsync_ReturnsGroupCount()
        {
            var count = await Query().GroupBy(u => new { u.CustomerId, u.Region }).CountAsync();

            Assert.Equal(4, count);
        }

        [Fact]
        public async Task GroupBy_CountAsync_WithHaving_CountsFilteredGroups()
        {
            // 按 CustomerId 分组：客户 1 与客户 2 各 2 行通过 Count >= 2
            var count = await Query()
                .GroupBy(u => new { u.CustomerId })
                .Having(g => g.Count() >= 2)
                .CountAsync();

            Assert.Equal(2, count);
        }

        [Fact]
        public async Task GroupBy_CountAsync_WithWindow_CountsWindowGroups()
        {
            var count = await Query()
                .GroupBy(u => new { u.CustomerId })
                .OrderByDescending(g => g.Count())
                .Take(2)
                .CountAsync();

            Assert.Equal(2, count);
        }

        [Fact]
        public async Task GroupBy_LongCount_ReturnsGroupCount()
        {
            var count = await Query().GroupBy(u => new { u.CustomerId }).Select(g => g.LongCount()).FirstAsync();

            Assert.Equal(2, count);
        }

        [Fact]
        public async Task GroupBy_MaxAggregate_ReturnsGroupMax()
        {
            // 按客户分组取最大 Qty：客户 1（2）、客户 2（4）、客户 3（5）
            var rows = await Query().GroupBy(u => new { u.CustomerId }).Select(g => g.Max(x => x.Qty)).ToListAsync();

            rows.Sort();
            Assert.Equal(new[] { 2, 4, 5 }, rows);
        }

        [Fact]
        public async Task GroupBy_EnumKeyColumn_GroupsBySemanticValue()
        {
            var rows = await Query()
                .GroupBy(u => u.Status)
                .OrderByDescending(g => g.Count())
                .Select(g => new { Key = g.Key, Cnt = g.Count() })
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Equal(GroupUserStatus.One, rows[0].Key);
            Assert.Equal(3, rows[0].Cnt);
            Assert.Equal(GroupUserStatus.Nine, rows[1].Key);
            Assert.Equal(2, rows[1].Cnt);
        }

        [Fact]
        public async Task FromSql_GroupBy_EndToEnd()
        {
            var rows = await DbAccess.FromSql<GroupUserEntity>("SELECT * FROM GroupByUsers WHERE Amount > 15")
                .GroupBy(u => new { u.CustomerId })
                .Having(g => g.Count() >= 2)
                .Select(g => new { g.Key.CustomerId, Total = g.Sum(x => x.Amount) })
                .ToListAsync();

            // Amount > 15 剔除客户 3（10）；客户 1（100+50）、客户 2（30+20）各 2 行且通过 HAVING
            Assert.Equal(2, rows.Count);
            Assert.Equal(150m, rows.Single(r => r.CustomerId == 1L).Total);
            Assert.Equal(50m, rows.Single(r => r.CustomerId == 2L).Total);
        }

        [Fact]
        public async Task EntitySet_Where_GroupBy_BridgesToGroupedQuery()
        {
            // Set 桥接：Where 一跳生成 SQL 后返回 IEntityQuery，分组能力开箱即用（设计文档 §2/§6）
            var rows = await DbAccess.Set<GroupUserEntity>()
                .Where(u => u.CustomerId > 0)
                .GroupBy(u => new { u.CustomerId })
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key.CustomerId)
                .Select(g => new { g.Key.CustomerId, Cnt = g.Count() })
                .ToListAsync();

            Assert.Equal(3, rows.Count);
            Assert.Equal(2, rows[0].Cnt);
            Assert.Equal(2, rows[1].Cnt);
            Assert.Equal(1, rows[2].Cnt);
        }

        #endregion

        #region HAVING 组合形态与窗口边界

        [Fact]
        public async Task GroupBy_HavingOrComposition_FiltersByEitherBranch()
        {
            // OR 组合：Qty 和 >= 7 或键 == 3——c2(7✓)、c3(5✗ 但键命中✓)；c1(3✗) 被滤
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId })
                .Having(g => g.Sum(x => x.Qty) >= 7 || g.Key.CustomerId == 3)
                .Select(g => new { g.Key.CustomerId })
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, r => r.CustomerId == 2L);
            Assert.Contains(rows, r => r.CustomerId == 3L);
        }

        [Fact]
        public async Task GroupBy_HavingNegation_ExcludesMatchedGroups()
        {
            // NOT 包裹：!(Qty 和 < 5)——c1(3) 被排除，c2(7)、c3(5) 保留
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId })
                .Having(g => !(g.Sum(x => x.Qty) < 5))
                .Select(g => new { g.Key.CustomerId, TotalQty = g.Sum(x => x.Qty) })
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Equal(7, rows.Single(r => r.CustomerId == 2L).TotalQty);
            Assert.Equal(5, rows.Single(r => r.CustomerId == 3L).TotalQty);
        }

        [Fact]
        public async Task GroupBy_HavingClosureValue_IsParameterized()
        {
            // 闭包值参数化（永不内联）：threshold 经参数绑定而非拼接
            var threshold = 6;
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId })
                .Having(g => g.Sum(x => x.Qty) > threshold)
                .Select(g => new { g.Key.CustomerId })
                .ToListAsync();

            var row = Assert.Single(rows);
            Assert.Equal(2L, row.CustomerId);// 仅 c2 的 Qty 和 7 > 6
        }

        [Fact]
        public async Task GroupBy_HavingAggregateVersusKeyColumn_DirectSplice()
        {
            // 聚合与键列比较（HAVING COUNT(*) >= CustomerId）：c1(2>=1✓)、c2(2>=2✓)、c3(1>=3✗)
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId })
                .Having(g => g.Count() >= g.Key.CustomerId)
                .Select(g => new { g.Key.CustomerId, Cnt = g.Count() })
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, r => r.CustomerId == 1L);
            Assert.Contains(rows, r => r.CustomerId == 2L);
        }

        [Fact]
        public async Task GroupBy_HavingSameAggregateTwice_ParameterNamesAvoid()
        {
            // 同一聚合出现在两个 HAVING 谓词：参数名避让（@Qty 与后缀名并存），AND 合并后语义正确
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId })
                .Having(g => g.Sum(x => x.Qty) >= 3)
                .Having(g => g.Sum(x => x.Qty) <= 6)
                .Select(g => new { g.Key.CustomerId, TotalQty = g.Sum(x => x.Qty) })
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Equal(3, rows.Single(r => r.CustomerId == 1L).TotalQty);
            Assert.Equal(5, rows.Single(r => r.CustomerId == 3L).TotalQty);
        }

        [Fact]
        public async Task GroupBy_SelectDuplicateFragment_SharesAlias()
        {
            // 同一聚合片段重复出现在投影中：别名按片段文本去重共用，两列读取同值
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId })
                .OrderBy(g => g.Key.CustomerId)
                .Select(g => new { A = g.Sum(x => x.Qty), B = g.Sum(x => x.Qty) })
                .ToListAsync();

            Assert.Equal(3, rows.Count);
            Assert.All(rows, r => Assert.Equal(r.A, r.B));
            Assert.Equal(3, rows[0].A);
            Assert.Equal(7, rows[1].A);
            Assert.Equal(5, rows[2].A);
        }

        [Fact]
        public async Task GroupBy_TakeAlone_WindowFromFirstGroup()
        {
            // Take 单独使用（等价 OFFSET 0）：主序计数降序（c1/c2 同为 2），次序键升序决出 c1 为第一组
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId })
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key.CustomerId)
                .Take(1)
                .Select(g => new { g.Key.CustomerId, Cnt = g.Count() })
                .ToListAsync();

            var row = Assert.Single(rows);
            Assert.Equal(1L, row.CustomerId);
            Assert.Equal(2, row.Cnt);
        }

        [Fact]
        public async Task GroupBy_SkipZeroTake_WindowFromFirstGroup()
        {
            // Skip(0) 合法（窗口起点，分页第一页常规值）
            var rows = await Query()
                .GroupBy(u => new { u.CustomerId })
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key.CustomerId)
                .Skip(0)
                .Take(2)
                .Select(g => new { g.Key.CustomerId })
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Equal(1L, rows[0].CustomerId);
            Assert.Equal(2L, rows[1].CustomerId);
        }

        [Fact]
        public void GroupBy_TakeZero_Throws()
        {
            // 窗口行数必须为正数（与实体查询 Take 同约定）
            Assert.Throws<ArgumentOutOfRangeException>(
                () => Query().GroupBy(u => new { u.CustomerId }).OrderBy(g => g.Key.CustomerId).Take(0));
        }

        [Fact]
        public void GroupBy_NullKeySelector_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Query().GroupBy<GroupUserEntity>(null));
        }

        #endregion

        #region 拒绝清单（链式形态）

        [Fact]
        public void GroupBy_AfterOrdering_Throws()
        {
            // 源行序对分组结果无意义：GroupBy 前不能设置排序（链式构建阶段同步抛出）
            Assert.Throws<InvalidOperationException>(
                () => Query().OrderBy(u => u.Id).GroupBy(u => new { u.CustomerId }));
        }

        [Fact]
        public void GroupBy_AfterWindow_Throws()
        {
            Assert.Throws<InvalidOperationException>(
                () => Query().OrderBy(u => u.Id).Take(5).GroupBy(u => new { u.CustomerId }));
        }

        [Fact]
        public void GroupedQuery_SkipWithoutOrdering_Throws()
        {
            Assert.Throws<InvalidOperationException>(
                () => Query().GroupBy(u => new { u.CustomerId }).Skip(1));
        }

        [Fact]
        public void GroupedQuery_TakeWithoutOrdering_Throws()
        {
            Assert.Throws<InvalidOperationException>(
                () => Query().GroupBy(u => new { u.CustomerId }).Take(2));
        }

        [Fact]
        public void GroupedQuery_ThenByBeforeOrderBy_Throws()
        {
            Assert.Throws<InvalidOperationException>(
                () => Query().GroupBy(u => new { u.CustomerId }).ThenBy(g => g.Key.CustomerId));
        }

        [Fact]
        public void GroupBy_ComputedKey_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => Query().GroupBy(u => u.Amount * 2));
        }

        [Fact]
        public void GroupBy_EmptyAnonymousKey_Throws()
        {
            Assert.Throws<NotSupportedException>(
                () => Query().GroupBy(u => new { }));
        }

        [Fact]
        public void GroupBy_TupleKey_Throws()
        {
            // 键成员名是 g.Key.X 的寻址落点，元组形态（Create 调用）无元素名：显式拒绝并引导匿名/初始化器形态
            Assert.Throws<NotSupportedException>(
                () => Query().GroupBy(u => ValueTuple.Create(u.CustomerId, u.Region)));
        }

        [Fact]
        public void GroupedQuery_ConditionalCount_Throws()
        {
            Assert.Throws<NotSupportedException>(
                () => Query().GroupBy(u => new { u.CustomerId }).Select(g => g.Count(x => x.Amount > 0)));
        }

        [Fact]
        public void GroupedQuery_ConverterColumnAggregate_Throws()
        {
            // 转换器列的存储值与属性值不同构：聚合资格守卫拒绝（Max 对字符串列合法，转换器列被拦下）
            Assert.Throws<NotSupportedException>(
                () => Query().GroupBy(u => new { u.CustomerId }).Select(g => g.Max(x => x.Token)));
        }

        [Fact]
        public async Task GroupedQuery_AggregateToAggregateHaving_Throws()
        {
            // HAVING 延迟到终结构建时翻译（参数名避让依赖终结时的源参数域），非法形态在执行时抛出
            await Assert.ThrowsAsync<NotSupportedException>(
                () => Query().GroupBy(u => new { u.CustomerId }).Having(g => g.Count() == g.Sum(x => x.Qty)).CountAsync());
        }

        [Fact]
        public async Task GroupedQuery_ConstantHaving_Throws()
        {
            await Assert.ThrowsAsync<NotSupportedException>(
                () => Query().GroupBy(u => new { u.CustomerId }).Having(g => true).CountAsync());
        }

        [Fact]
        public async Task GroupedQuery_ArithmeticOnAggregateHaving_Throws()
        {
            await Assert.ThrowsAsync<NotSupportedException>(
                () => Query().GroupBy(u => new { u.CustomerId }).Having(g => g.Count() + 1 > 5).CountAsync());
        }

        [Fact]
        public void GroupedQuery_CompositeKeyInsideMultiColumnSelect_Throws()
        {
            Assert.Throws<NotSupportedException>(
                () => Query().GroupBy(u => new { u.CustomerId, u.Region }).Select(g => new { g.Key, Cnt = g.Count() }));
        }

        [Fact]
        public void GroupedQuery_NullSelector_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => Query().GroupBy(u => new { u.CustomerId }).Select<GroupUserEntity>(null));
        }

        #endregion

        #region 分组层 SQL 形态

        /// <summary>SQL 形态断言用的复合键替身（成员名与键成员一致）。</summary>
        public sealed class CompositeAliasKey
        {
            public long CustomerId { get; set; }

            public string Region { get; set; }
        }

        /// <summary>两个成员指向同一列的键替身（SELECT/GROUP BY 列名去重断言载体）。</summary>
        public sealed class DuplicateAliasKey
        {
            public long First { get; set; }

            public long Second { get; set; }
        }

        [Fact]
        public void GroupedLayer_ExposesAllKeysAndAliasedAggregates()
        {
            // 分组层形态断言：全部键列自然名暴露 + 聚合生成别名 + GROUP BY 键列组（别名机制保证外层可引用）
            var metadata = EntityMetadataCache<GroupUserEntity>.Metadata;
            var keyColumns = AggregateSqlTranslator.ParseKeySelector(
                (Expression<Func<GroupUserEntity, CompositeAliasKey>>)(u => new CompositeAliasKey { CustomerId = u.CustomerId, Region = u.Region }), metadata);
            var orderings = AggregateSqlTranslator.ParseOrderingSelector(
                (Expression<Func<IGrouping<CompositeAliasKey, GroupUserEntity>, decimal>>)(g => g.Sum(x => x.Amount)),
                keyColumns, metadata, DbAccess.ProviderFactory.SqlDialect, descending: true);

            var layer = GroupedLayerBuilder.Build(
                DbAccess, "SELECT * FROM GroupByUsers", Array.Empty<DbParameter>(),
                metadata, keyColumns, new List<LambdaExpression>(), orderings,
                projectionAggregates: null, exposeOrderingAggregates: true);

            Assert.Contains("SELECT \"CustomerId\", \"Region\", SUM(\"Amount\") AS \"s_", layer.Sql);
            Assert.Contains("GROUP BY \"CustomerId\", \"Region\"", layer.Sql);
            Assert.DoesNotContain("ORDER BY", layer.Sql.Split('\n').First());// 分组层不含裸 ORDER BY（窗口包裹前提）
            Assert.StartsWith("\"s_", layer.OrderByClause.Split(' ').First());// 排序引用片段 = 聚合别名引用
            Assert.EndsWith(" DESC", layer.OrderByClause);
        }

        [Fact]
        public void GroupedLayer_HavingParameters_AreAppendedAfterSourceParameters()
        {
            var metadata = EntityMetadataCache<GroupUserEntity>.Metadata;
            var keyColumns = AggregateSqlTranslator.ParseKeySelector(
                (Expression<Func<GroupUserEntity, CompositeAliasKey>>)(u => new CompositeAliasKey { CustomerId = u.CustomerId }), metadata);
            var sourceParameter = DbAccess.ProviderFactory.ParameterBuilder.CreateParameter("p0", 15);

            var layer = GroupedLayerBuilder.Build(
                DbAccess, "SELECT * FROM GroupByUsers WHERE Amount > @p0", new[] { sourceParameter },
                metadata, keyColumns,
                new List<LambdaExpression> { (Expression<Func<IGrouping<CompositeAliasKey, GroupUserEntity>, bool>>)(g => g.Count() >= 2) },
                new List<GroupedOrderingTarget>(),
                projectionAggregates: null, exposeOrderingAggregates: false);

            Assert.Contains("HAVING (COUNT(*) >= ", layer.Sql);
            Assert.Contains(layer.Parameters[1].ParameterName, layer.Sql);// HAVING 占位符与参数对应
            Assert.Equal(2, layer.Parameters.Length);
            Assert.Equal("@p0", layer.Parameters[0].ParameterName);// 源参数在前
            Assert.NotEqual("@p0", layer.Parameters[1].ParameterName);// HAVING 参数名自动避让源参数名域
            Assert.Equal(2, layer.Parameters[1].Value);
        }

        [Fact]
        public void GroupedLayer_AggregateAliasDeduplication_SharesAliasBetweenProjectionAndOrdering()
        {
            // 同一聚合片段同时出现在投影与排序：别名按片段文本去重共用（分组 SELECT 中片段只出现一次）
            var metadata = EntityMetadataCache<GroupUserEntity>.Metadata;
            var sqlDialect = DbAccess.ProviderFactory.SqlDialect;
            Expression<Func<IGrouping<CompositeAliasKey, GroupUserEntity>, decimal>> sumAmount = g => g.Sum(x => x.Amount);
            var projectionAggregates = new List<AggregateFragment>
            {
                AggregateSqlTranslator.TranslateAggregateCall(
                    (MethodCallExpression)sumAmount.Body, sumAmount.Parameters[0], metadata, sqlDialect),
            };
            var keyColumns = AggregateSqlTranslator.ParseKeySelector(
                (Expression<Func<GroupUserEntity, CompositeAliasKey>>)(u => new CompositeAliasKey { CustomerId = u.CustomerId }), metadata);
            var orderings = AggregateSqlTranslator.ParseOrderingSelector(
                sumAmount, keyColumns, metadata, sqlDialect, descending: true);

            var layer = GroupedLayerBuilder.Build(
                DbAccess, "SELECT * FROM GroupByUsers", Array.Empty<DbParameter>(),
                metadata, keyColumns, new List<LambdaExpression>(), orderings,
                projectionAggregates: projectionAggregates, exposeOrderingAggregates: true);

            Assert.Equal(1, layer.Sql.Split("SUM(\"Amount\")").Length - 1);// 分组 SELECT 中聚合片段仅出现一次
            Assert.StartsWith("\"s_", layer.OrderByClause);// 排序引用与投影共用同一别名
            Assert.Equal(1, layer.AggregateAliases.Count);
        }

        [Fact]
        public void GroupedLayer_MultipleHavingPredicates_AreAndCombinedWithParens()
        {
            // 多 HAVING 谓词：各谓词片段包裹括号后 AND 连接（OR 优先级不被合并破坏）
            var metadata = EntityMetadataCache<GroupUserEntity>.Metadata;
            var keyColumns = AggregateSqlTranslator.ParseKeySelector(
                (Expression<Func<GroupUserEntity, CompositeAliasKey>>)(u => new CompositeAliasKey { CustomerId = u.CustomerId }), metadata);

            var layer = GroupedLayerBuilder.Build(
                DbAccess, "SELECT * FROM GroupByUsers", Array.Empty<DbParameter>(),
                metadata, keyColumns,
                new List<LambdaExpression>
                {
                    (Expression<Func<IGrouping<CompositeAliasKey, GroupUserEntity>, bool>>)(g => g.Count() >= 2),
                    (Expression<Func<IGrouping<CompositeAliasKey, GroupUserEntity>, bool>>)(g => g.Sum(x => x.Qty) <= 10),
                },
                new List<GroupedOrderingTarget>(),
                projectionAggregates: null, exposeOrderingAggregates: false);

            // HAVING 聚合为裸片段（组级作用域合法，无需别名进分组 SELECT），列名带方言引用
            Assert.Contains("HAVING (COUNT(*) >= @Count) AND (SUM(\"Qty\") <= @Qty)", layer.Sql);
            Assert.DoesNotContain("SUM(\"Qty\") AS", layer.Sql);// HAVING 聚合不进分组 SELECT 暴露集
            Assert.Equal(2, layer.Parameters.Length);
            Assert.NotEqual(layer.Parameters[0].ParameterName, layer.Parameters[1].ParameterName);// 同基名参数自动避让
        }

        [Fact]
        public void GroupedLayer_KeyColumnOrdering_ReferencesNaturalName()
        {
            // 键列排序的引用片段 = 键列自然名（分组 SELECT 已暴露全部键列）
            var metadata = EntityMetadataCache<GroupUserEntity>.Metadata;
            var keyColumns = AggregateSqlTranslator.ParseKeySelector(
                (Expression<Func<GroupUserEntity, CompositeAliasKey>>)(u => new CompositeAliasKey { CustomerId = u.CustomerId, Region = u.Region }), metadata);
            var orderings = AggregateSqlTranslator.ParseOrderingSelector(
                (Expression<Func<IGrouping<CompositeAliasKey, GroupUserEntity>, long>>)(g => g.Key.CustomerId),
                keyColumns, metadata, DbAccess.ProviderFactory.SqlDialect, descending: false);

            var layer = GroupedLayerBuilder.Build(
                DbAccess, "SELECT * FROM GroupByUsers", Array.Empty<DbParameter>(),
                metadata, keyColumns, new List<LambdaExpression>(), orderings,
                projectionAggregates: null, exposeOrderingAggregates: true);

            Assert.Equal("\"CustomerId\" ASC", layer.OrderByClause);
            Assert.DoesNotContain(" AS \"s_", layer.Sql);// 无聚合参与时分组 SELECT 不含别名列
        }

        [Fact]
        public void GroupedLayer_ExposeOrderingAggregatesFalse_IgnoresAggregateOrderings()
        {
            // 排序被忽略方（CountAsync 无窗口）：排序聚合不进分组 SELECT、无排序引用片段
            var metadata = EntityMetadataCache<GroupUserEntity>.Metadata;
            var keyColumns = AggregateSqlTranslator.ParseKeySelector(
                (Expression<Func<GroupUserEntity, CompositeAliasKey>>)(u => new CompositeAliasKey { CustomerId = u.CustomerId }), metadata);
            var orderings = AggregateSqlTranslator.ParseOrderingSelector(
                (Expression<Func<IGrouping<CompositeAliasKey, GroupUserEntity>, decimal>>)(g => g.Sum(x => x.Amount)),
                keyColumns, metadata, DbAccess.ProviderFactory.SqlDialect, descending: false);

            var layer = GroupedLayerBuilder.Build(
                DbAccess, "SELECT * FROM GroupByUsers", Array.Empty<DbParameter>(),
                metadata, keyColumns, new List<LambdaExpression>(), orderings,
                projectionAggregates: null, exposeOrderingAggregates: false);

            Assert.DoesNotContain("SUM(", layer.Sql);
            Assert.Null(layer.OrderByClause);
            Assert.Empty(layer.AggregateAliases);
        }

        [Fact]
        public void GroupedLayer_DuplicateKeyMembers_SingleColumnInSelectAndGroupBy()
        {
            // 两个键成员指向同一列：分组 SELECT 与 GROUP BY 按列名去重各出现一次
            var metadata = EntityMetadataCache<GroupUserEntity>.Metadata;
            var keyColumns = AggregateSqlTranslator.ParseKeySelector(
                (Expression<Func<GroupUserEntity, DuplicateAliasKey>>)(u => new DuplicateAliasKey { First = u.CustomerId, Second = u.CustomerId }), metadata);
            Assert.Equal(2, keyColumns.Count);// 键列组保留两个成员（g.Key.First/Second 均可寻址）

            var layer = GroupedLayerBuilder.Build(
                DbAccess, "SELECT * FROM GroupByUsers", Array.Empty<DbParameter>(),
                metadata, keyColumns, new List<LambdaExpression>(), new List<GroupedOrderingTarget>(),
                projectionAggregates: null, exposeOrderingAggregates: false);

            Assert.Equal(1, layer.Sql.Split("SELECT \"CustomerId\"").Length - 1);
            Assert.Equal(1, layer.Sql.Split("GROUP BY \"CustomerId\"").Length - 1);
        }

        #endregion

        #region 不可变构建器语义

        [Fact]
        public async Task GroupedQuery_ImmutableBuilder_DerivedDoesNotAffectBase()
        {
            // 链式派生复制状态：派生实例的 HAVING/排序不影响原实例的语义与结果
            var baseQuery = Query().GroupBy(u => new { u.CustomerId });
            var filtered = baseQuery.Having(g => g.Count() >= 2);
            var ordered = baseQuery.OrderByDescending(g => g.Count());

            // 原实例无 HAVING（3 组）、派生 HAVING 后仅计数达标的组（c1/c2 各 2 行）
            Assert.Equal(3L, await baseQuery.CountAsync());
            Assert.Equal(2L, await filtered.CountAsync());
            // 原实例无窗口时 CountAsync 忽略排序链，排序派生同样不改变组数
            Assert.Equal(3L, await ordered.CountAsync());
        }

        [Fact]
        public async Task GroupedQuery_RepeatedExecution_ProducesStableResults()
        {
            // 同一查询对象重复执行：分组层每次构建可重复（表达式树翻译无状态副作用）
            var query = Query().GroupBy(u => new { u.CustomerId }).OrderBy(g => g.Key.CustomerId);

            var first = await query.Select(g => new { g.Key.CustomerId, Cnt = g.Count() }).ToListAsync();
            var second = await query.Select(g => new { g.Key.CustomerId, Cnt = g.Count() }).ToListAsync();

            Assert.Equal(first.Select(r => (r.CustomerId, r.Cnt)), second.Select(r => (r.CustomerId, r.Cnt)));
            Assert.Equal(3, first.Count);
        }

        #endregion
    }
}
