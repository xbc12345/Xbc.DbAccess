using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.MySql;
using Xbc.DbAccess.SqlServer;
using Xbc.DbAccess.Sqlite;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// IEntityQuery 链式排序与窗口（OrderBy/ThenBy/Skip/Take）的 SQLite 真库集成测试：
    /// 验证升序/降序/多键排序、与 Include/ThenInclude 混用、窗口截取、分页新语义、不可变性、异常路径及方言 SQL 快照。
    /// </summary>
    public class EntityQueryOrderByTests : EntityTestBase
    {
        #region 测试实体

        [XbcDbEntity.Table("OrderByUsers")]
        public class UserEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
            public int Age { get; set; }

            [XbcDbEntity.Column(IsJson = true)]
            public List<string> Tags { get; set; }

            // 1:N — 手动指定键：Id → OrderByOrders.UserId
            [Navigation(ThisKey = "Id", OtherKey = "UserId")]
            public List<OrderEntity> Orders { get; set; }
        }

        [XbcDbEntity.Table("OrderByOrders")]
        public class OrderEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public int UserId { get; set; }
            public decimal Amount { get; set; }

            // 1:N — 手动指定键：Id → OrderByOrderItems.OrderId
            [Navigation(ThisKey = "Id", OtherKey = "OrderId")]
            public List<OrderItemEntity> Items { get; set; }
        }

        [XbcDbEntity.Table("OrderByOrderItems")]
        public class OrderItemEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public int OrderId { get; set; }
            public string Product { get; set; } = string.Empty;
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE OrderByUsers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Age INTEGER NOT NULL,
                    Tags TEXT NULL
                );
                INSERT INTO OrderByUsers (Name, Age, Tags) VALUES ('Alice', 25, '[""a""]');
                INSERT INTO OrderByUsers (Name, Age, Tags) VALUES ('Bob', 30, '[""b""]');
                INSERT INTO OrderByUsers (Name, Age, Tags) VALUES ('Carol', 35, NULL);
                INSERT INTO OrderByUsers (Name, Age, Tags) VALUES ('Dave', 30, NULL);
                INSERT INTO OrderByUsers (Name, Age, Tags) VALUES ('Eve', 28, NULL);
                INSERT INTO OrderByUsers (Name, Age, Tags) VALUES ('Frank', 22, NULL);

                CREATE TABLE OrderByOrders (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    Amount DECIMAL(18,2) NOT NULL
                );
                INSERT INTO OrderByOrders (UserId, Amount) VALUES (1, 100.50);
                INSERT INTO OrderByOrders (UserId, Amount) VALUES (1, 200.00);
                INSERT INTO OrderByOrders (UserId, Amount) VALUES (2, 50.00);

                CREATE TABLE OrderByOrderItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    OrderId INTEGER NOT NULL,
                    Product TEXT NOT NULL
                );
                INSERT INTO OrderByOrderItems (OrderId, Product) VALUES (1, 'Keyboard');
                INSERT INTO OrderByOrderItems (OrderId, Product) VALUES (2, 'Mouse');
            ");
        }

        #region 排序

        [Fact]
        public async Task OrderBy_Ascending_ToListAsyncReturnsSortedRows()
        {
            var users = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .ToListAsync();

            Assert.Equal(new[] { "Frank", "Alice", "Eve", "Bob", "Dave", "Carol" }, users.Select(u => u.Name));
        }

        [Fact]
        public async Task OrderByDescending_Descending_ToListAsyncReturnsSortedRows()
        {
            var users = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderByDescending(u => u.Age)
                .ToListAsync();

            Assert.Equal(new[] { 35, 30, 30, 28, 25, 22 }, users.Select(u => u.Age));
        }

        [Fact]
        public async Task OrderBy_ThenBy_SortsBySecondaryKeyOnEqualValues()
        {
            // Age=30 有 Bob/Dave 两条，次级键 Name 升序决定组内先后
            var users = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .ThenBy(u => u.Name)
                .ToListAsync();

            Assert.Equal(new[] { "Frank", "Alice", "Eve", "Bob", "Dave", "Carol" }, users.Select(u => u.Name));
        }

        [Fact]
        public async Task OrderByDescending_ThenByDescending_MultiKeyMixedDirections()
        {
            // Age 降序、组内 Name 降序：Age=30 组内 Dave 在 Bob 前
            var users = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderByDescending(u => u.Age)
                .ThenByDescending(u => u.Name)
                .ToListAsync();

            Assert.Equal(new[] { "Carol", "Dave", "Bob", "Eve", "Alice", "Frank" }, users.Select(u => u.Name));
        }

        [Fact]
        public async Task OrderBy_RepeatedOrderBy_ReplacesEntireChain()
        {
            // 与 LINQ 语义一致：后一个 OrderBy 替换整条排序链
            var users = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .OrderBy(u => u.Name)
                .ToListAsync();

            Assert.Equal(new[] { "Alice", "Bob", "Carol", "Dave", "Eve", "Frank" }, users.Select(u => u.Name));
        }

        [Fact]
        public async Task FirstOrDefaultAsync_WithOrdering_ReturnsFirstRowByOrder()
        {
            var user = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .FirstOrDefaultAsync();

            Assert.NotNull(user);
            Assert.Equal("Frank", user.Name);
        }

        [Fact]
        public async Task SingleOrDefaultAsync_WithOrdering_MultipleRowsThrows()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .SingleOrDefaultAsync());
        }

        #endregion

        #region 与 Include/ThenInclude 混用

        [Fact]
        public async Task OrderBy_AfterInclude_SortsRootEntitiesAndLoadsNavigations()
        {
            var users = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .Include(u => u.Orders)
                .OrderBy(u => u.Age)
                .ToListAsync();

            Assert.Equal(new[] { "Frank", "Alice", "Eve", "Bob", "Dave", "Carol" }, users.Select(u => u.Name));

            var alice = users.Single(u => u.Name == "Alice");
            Assert.Equal(2, alice.Orders.Count);
        }

        [Fact]
        public async Task Include_AfterOrderBy_PreservesOrdering()
        {
            var users = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .Include(u => u.Orders)
                .ToListAsync();

            Assert.Equal(new[] { "Frank", "Alice", "Eve", "Bob", "Dave", "Carol" }, users.Select(u => u.Name));

            var alice = users.Single(u => u.Name == "Alice");
            Assert.Equal(2, alice.Orders.Count);
        }

        [Fact]
        public async Task OrderBy_ThenIncludeChain_PreservesOrderingState()
        {
            // Where → OrderBy → Include → ThenInclude：排序状态经 ThenIncludeCore 透传不丢失
            var users = await DbAccess.Set<UserEntity>()
                .Where(u => u.Age >= 25)
                .OrderBy(u => u.Age)
                .Include(u => u.Orders)
                .ThenInclude(o => o.Items)
                .ToListAsync();

            Assert.Equal(new[] { "Alice", "Eve", "Bob", "Dave", "Carol" }, users.Select(u => u.Name));

            var alice = users.Single(u => u.Name == "Alice");
            Assert.Equal(2, alice.Orders.Count);
            Assert.Equal(2, alice.Orders.SelectMany(o => o.Items).Count());
        }

        #endregion

        #region 窗口（Skip/Take）

        [Fact]
        public async Task Skip_Take_ReturnsWindowRowsInOrder()
        {
            var users = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .ThenBy(u => u.Name)
                .Skip(1)
                .Take(2)
                .ToListAsync();

            Assert.Equal(new[] { "Alice", "Eve" }, users.Select(u => u.Name));
        }

        [Fact]
        public async Task Take_Only_ReturnsFirstNRows()
        {
            var users = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .Take(3)
                .ToListAsync();

            Assert.Equal(new[] { "Frank", "Alice", "Eve" }, users.Select(u => u.Name));
        }

        [Fact]
        public async Task Skip_Zero_WithTake_MatchesTakeOnly()
        {
            // Skip(0) 合法：从第一行开始（动态分页 pageSize * (page - 1) 首页的常规值，翻译为 OFFSET 0）；
            // 与其他 Skip(n) 一样必须配 Take（单独 Skip 仍是不可移植的无界窗口，由窗口态校验统一拒绝）
            var paged = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .Skip(0)
                .Take(3)
                .ToListAsync();

            var expected = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .Take(3)
                .ToListAsync();

            Assert.Equal(expected.Select(u => u.Name), paged.Select(u => u.Name));
        }

        [Fact]
        public void Take_Zero_Throws()
        {
            // Take(0) 的空窗口无实用语义，且各数据库对 0 的窗口截断行为不一，构建期即拒绝
            Assert.Throws<ArgumentOutOfRangeException>(() => DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .Take(0));
        }

        [Fact]
        public async Task FirstAsync_WithSkip_ReturnsNthRowByOrder()
        {
            // First 应用 Skip：排序后跳过 2 行取第 3 条（窗口语义 = OFFSET 2 FETCH 1）
            var user = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .Skip(2)
                .Take(3)
                .FirstAsync();

            Assert.Equal("Eve", user.Name);
        }

        [Fact]
        public async Task Skip_Take_WithInclude_LoadsNavigationsForWindow()
        {
            var users = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .Skip(2)
                .Take(2)
                .Include(u => u.Orders)
                .ToListAsync();

            // 窗口内 Bob（Age=30）与 Eve（Age=28）按序返回，Bob 的导航照常装配
            Assert.Equal(new[] { "Eve", "Bob" }, users.Select(u => u.Name));
            var bob = users.Single(u => u.Name == "Bob");
            Assert.Single(bob.Orders);
        }

        [Fact]
        public async Task SingleOrDefaultAsync_AfterTake1_DoesNotThrowOnMultipleMatches()
        {
            // 查询级 Take(1) 截断后，多条结果集对 Single 不再抛（与 LINQ Take(1).Single() 语义一致）
            var user = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .Take(1)
                .SingleOrDefaultAsync();

            Assert.NotNull(user);
            Assert.Equal("Frank", user.Name);
        }

        #endregion

        #region 窗口分页（Skip/Take）

        [Fact]
        public async Task SkipTake_WithQueryOrdering_PaginatesInOrder()
        {
            var page = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .ThenBy(u => u.Name)
                .Skip(2)
                .Take(2)
                .ToListAsync();

            Assert.Equal(new[] { "Eve", "Bob" }, page.Select(u => u.Name));
        }

        [Fact]
        public async Task SkipTake_WithoutOrderBy_Throws()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .Skip(1)
                .Take(2)
                .ToListAsync());
        }

        [Fact]
        public async Task Skip_AfterWindowState_ReplacesOffset()
        {
            // 窗口为绝对行号：后设 Skip/Take 替换既有窗口而非叠加（叠加语义无从定义）
            var paged = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .Skip(1)
                .Take(2)
                .Skip(1)
                .Take(2)
                .ToListAsync();

            var expected = await DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .Skip(1)
                .Take(2)
                .ToListAsync();

            Assert.Equal(expected.Select(u => u.Name), paged.Select(u => u.Name));
        }

        #endregion

        #region 异常路径

        [Fact]
        public void ThenBy_WithoutOrderBy_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .ThenBy(u => u.Name));
        }

        [Fact]
        public void ThenByDescending_WithoutOrderBy_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .ThenByDescending(u => u.Name));
        }

        [Fact]
        public void Skip_WithoutOrderBy_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .Skip(1));
        }

        [Fact]
        public void Take_WithoutOrderBy_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .Take(1));
        }

        [Fact]
        public void OrderBy_WhenSqlAlreadyHasOrderBy_ThrowsImmediately()
        {
            // 构建期 fail fast：混用在 OrderBy 调用点立即抛出，无需等到终结执行
            var ex = Assert.Throws<InvalidOperationException>(() => DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers ORDER BY Name")
                .OrderBy(u => u.Age));
            Assert.Contains("已包含 ORDER BY 子句", ex.Message);
        }

        [Fact]
        public void OrderByDescending_OnIncludable_WhenSqlAlreadyHasOrderBy_ThrowsImmediately()
        {
            // Includable 侧同款 fail fast；终结端 PrepareWrappableBaseSql 保留同款检测兜底（公共 API 已被构建期拦截）
            var ex = Assert.Throws<InvalidOperationException>(() => DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers ORDER BY Name")
                .Include(u => u.Orders)
                .OrderByDescending(u => u.Age));
            Assert.Contains("已包含 ORDER BY 子句", ex.Message);
        }

        [Fact]
        public async Task Skip_WithoutTake_ThrowsOnTerminalMethod()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .Skip(2)
                .ToListAsync());
        }

        [Fact]
        public async Task OrderBy_NavigationProperty_ThrowsNotSupportedException()
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Orders)
                .ToListAsync());
        }

        [Fact]
        public async Task OrderBy_ComputedExpression_ThrowsArgumentException()
        {
            // 计算表达式属解析失败（EntityMetadata.ResolveColumn），抛 ArgumentException；未映射成员与 JSON 列仍为 NotSupportedException
            await Assert.ThrowsAsync<ArgumentException>(() => DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age + 10)
                .ToListAsync());
        }

        [Fact]
        public async Task OrderBy_JsonColumn_ThrowsNotSupportedException()
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Tags)
                .ToListAsync());
        }

        [Fact]
        public async Task OrderBy_ManualSqlWithOrderBy_ThrowsInvalidOperationException()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers ORDER BY Age")
                .OrderBy(u => u.Name)
                .ToListAsync());
        }

        [Fact]
        public void Skip_NegativeCount_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .Skip(-1));
        }

        [Fact]
        public void Take_NegativeCount_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .Take(-1));
        }

        [Fact]
        public void Skip_WithoutTake_ThrowsOnTerminal()
        {
            Assert.Throws<InvalidOperationException>(() => DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers")
                .OrderBy(u => u.Age)
                .Skip(2)
                .ToListAsync()
                .GetAwaiter().GetResult());
        }

        #endregion

        #region 不可变性

        [Fact]
        public async Task OrderBy_Immutability_SourceQueryUnaffectedByDerivedQuery()
        {
            var source = DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers");
            var ordered = source.OrderBy(u => u.Age);

            var sourceRows = await source.ToListAsync();
            var orderedRows = await ordered.ToListAsync();

            // 派生查询的排序不影响源查询（源按数据库默认序返回，派生按 Age 升序返回）
            Assert.Equal("Alice", sourceRows[0].Name);
            Assert.Equal("Frank", orderedRows[0].Name);
        }

        [Fact]
        public async Task OrderBy_Immutability_SkipOnDerivedQueryDoesNotAffectSource()
        {
            var ordered = DbAccess.FromSql<UserEntity>("SELECT * FROM OrderByUsers").OrderBy(u => u.Age);
            var windowed = ordered.Skip(2).Take(2);

            var orderedRows = await ordered.ToListAsync();
            var windowedRows = await windowed.ToListAsync();

            Assert.Equal(6, orderedRows.Count);
            Assert.Equal(new[] { "Eve", "Bob" }, windowedRows.Select(u => u.Name));
        }

        #endregion

        #region 方言 SQL 快照（SqlBuilder 层）

        [Fact]
        public void BuildWindowSql_Sqlite_LimitOffsetPath()
        {
            var sql = SqliteSqlBuilder.Instance.BuildWindowSql("SELECT * FROM Users", 5, 10, "Id ASC");

            Assert.Contains("ORDER BY Id ASC", sql);
            Assert.Contains("LIMIT 10 OFFSET 5", sql);
        }

        [Fact]
        public void BuildWindowSql_SqlServer_RowNumberPathWhenServerInfoUnknown()
        {
            // serverInfo 未知时走 ROW_NUMBER 兼容路径（SQL Server 2008 及以下）
            var sql = SqlServerSqlBuilder.Instance.BuildWindowSql("SELECT * FROM Users", 5, 10, "Id ASC");

            Assert.Contains("ROW_NUMBER() OVER (ORDER BY Id ASC)", sql);
            Assert.Contains("> 5", sql);
            Assert.Contains("<= 15", sql);
            // 顶层排序契约：ROW_NUMBER 降级路径的最外层必须有 ORDER BY（行序等于排序键序，
            // 不能依赖优化器保留 ROW_NUMBER 计算序的无契约行为），与 LIMIT/OFFSET 路径对齐
            Assert.Matches(@"(?m)^ORDER BY Id ASC\r?$", sql);
        }

        [Fact]
        public void BuildWindowSql_TakeZero_Throws()
        {
            // 窗口行数必须为正数：0 的空窗口无实用语义且各数据库对 0 的截断行为不一，公共入口即拒绝（方言层无需特判）
            var serverInfo = new DbServerInfo { ServerVersion = "14.0.1000", MajorVersion = 14 };

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SqlServerSqlBuilder.Instance.BuildWindowSql("SELECT * FROM Users", 5, 0, "Id ASC", serverInfo: serverInfo));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SqliteSqlBuilder.Instance.BuildWindowSql("SELECT * FROM Users", 5, 0, "Id ASC"));
        }

        [Fact]
        public void BuildWindowSql_MultiLineQuerySql_IndentsEveryLine_Sqlite()
        {
            // 多行内嵌 SQL 逐行缩进（IndentLines）：包裹后子查询树状对齐，换行风格保留
            var sql = SqliteSqlBuilder.Instance.BuildWindowSql("SELECT *\nFROM Users\nWHERE Age > 18", 5, 10, "Id ASC");

            Assert.Matches(@"(?m)^    SELECT \*\r?$", sql);
            Assert.Matches(@"(?m)^    FROM Users\r?$", sql);
            Assert.Matches(@"(?m)^    WHERE Age > 18\r?$", sql);
        }

        [Fact]
        public void BuildWindowSql_MultiLineQuerySql_IndentsEveryLine_SqlServer2012()
        {
            // OFFSET/FETCH 形态：内嵌 SQL 独立成行、逐行缩进
            var serverInfo = new DbServerInfo { ServerVersion = "14.0.1000", MajorVersion = 14 };

            var sql = SqlServerSqlBuilder.Instance.BuildWindowSql("SELECT *\r\nFROM Users\r\nWHERE Age > 18", 5, 10, "Id ASC", serverInfo: serverInfo);

            Assert.Matches(@"(?m)^    SELECT \*\r?$", sql);
            Assert.Matches(@"(?m)^    FROM Users\r?$", sql);
            Assert.Matches(@"(?m)^    WHERE Age > 18\r?$", sql);
        }

        [Fact]
        public void BuildWindowSql_MultiLineQuerySql_IndentsEveryLine_SqlServer2008()
        {
            // ROW_NUMBER 形态：内嵌 SQL 同样独立成行、嵌套两层缩进（与其他形态同构，不因行内嵌破坏缩进）
            var sql = SqlServerSqlBuilder.Instance.BuildWindowSql("SELECT *\nFROM Users\nWHERE Age > 18", 5, 10, "Id ASC");

            Assert.Matches(@"(?m)^        SELECT \*\r?$", sql);
            Assert.Matches(@"(?m)^        FROM Users\r?$", sql);
            Assert.Matches(@"(?m)^        WHERE Age > 18\r?$", sql);
            Assert.Contains("ROW_NUMBER() OVER (ORDER BY Id ASC)", sql);
        }

        #endregion

        #region 无行序模式快照（appendRowOrder=false）

        [Fact]
        public void BuildWindowSql_WithoutRowOrder_Sqlite_LimitOffsetPath()
        {
            // LIMIT/OFFSET 方言形态与行序模式无关：派生表内 ORDER BY 合法（回归绕过方言分派误发 ROW_NUMBER）
            var sql = SqliteSqlBuilder.Instance.BuildWindowSql("SELECT * FROM Users", 5, 10, "Id ASC", appendRowOrder: false);

            Assert.Contains("ORDER BY Id ASC", sql);
            Assert.Contains("LIMIT 10 OFFSET 5", sql);
            Assert.DoesNotContain("ROW_NUMBER", sql);
        }

        [Fact]
        public void BuildWindowSql_WithoutRowOrder_MySql_LimitOffsetPath()
        {
            var sql = MySqlSqlBuilder.Instance.BuildWindowSql("SELECT * FROM Users", 0, 10, "Id ASC", appendRowOrder: false);

            Assert.Contains("ORDER BY Id ASC", sql);
            Assert.Contains("LIMIT 10 OFFSET 0", sql);
            Assert.DoesNotContain("ROW_NUMBER", sql);
        }

        [Fact]
        public void BuildWindowSql_WithoutRowOrder_SqlServer2012_OffsetFetchPath()
        {
            var serverInfo = new DbServerInfo { ServerVersion = "14.0.1000", MajorVersion = 14 };

            var sql = SqlServerSqlBuilder.Instance.BuildWindowSql("SELECT * FROM Users", 5, 10, "Id ASC", appendRowOrder: false, serverInfo);

            Assert.Contains("OFFSET 5 ROWS", sql);
            Assert.Contains("FETCH NEXT 10 ROWS ONLY", sql);
        }

        [Fact]
        public void BuildWindowSql_WithoutRowOrder_SqlServer2008_RowNumberWithoutTrailingOrder()
        {
            // serverInfo 未知（2008 及以下）走 ROW_NUMBER 兼容路径；包裹场景（appendRowOrder=false）回退嵌套形态
            // （CTE 不能出现在派生表内），且无尾部裸 ORDER BY（派生表内为 Msg 1033 语法错误）
            var sql = SqlServerSqlBuilder.Instance.BuildWindowSql("SELECT * FROM Users", 5, 10, "Id ASC", appendRowOrder: false);

            Assert.Contains("ROW_NUMBER() OVER (ORDER BY Id ASC)", sql);
            Assert.Contains("> 5", sql);
            Assert.Contains("<= 15", sql);
            // 无行序变体：最外层不得有独立 ORDER BY 行
            Assert.DoesNotMatch(@"(?m)^ORDER BY Id ASC\r?$", sql);
        }

        #endregion
    }
}
