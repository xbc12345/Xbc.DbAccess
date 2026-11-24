using System.Linq.Expressions;
using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// IEntitySet.Where 谓词查询扩展方法的 SQLite 真库集成测试：
    /// 验证过滤结果、与 Include/分页的组合、翻译器各能力面（字符串变换/日期部分/HasValue/空值判断）及异常路径。
    /// </summary>
    public class EntitySetWhereQueryTests : EntityTestBase
    {
        #region 测试实体

        [XbcDbEntity.Table("WhereQueryUsers")]
        public class UserEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
            public int Age { get; set; }
            public bool IsActive { get; set; }
            public int? Score { get; set; }
            public string Email { get; set; }
            public DateTime CreatedAt { get; set; }

            // 1:N — 手动指定键：Id → WhereQueryOrders.UserId
            [Navigation(ThisKey = "Id", OtherKey = "UserId")]
            public List<OrderEntity> Orders { get; set; }
        }

        [XbcDbEntity.Table("WhereQueryOrders")]
        public class OrderEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public int UserId { get; set; }
            public decimal Amount { get; set; }
        }

        // 并发令牌拦截测试实体：仅注册元数据（拦截发生在 WHERE 翻译期、SQL 执行前，无需建表）
        [XbcDbEntity.Table("WhereTokenDocs")]
        public class TokenDocEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(IsConcurrencyToken = true)]
            public byte[] RowVersion { get; set; }
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE WhereQueryUsers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Age INTEGER NOT NULL,
                    IsActive INTEGER NOT NULL,
                    Score INTEGER NULL,
                    Email TEXT NULL,
                    CreatedAt TEXT NOT NULL
                );
                INSERT INTO WhereQueryUsers (Name, Age, IsActive, Score, Email, CreatedAt) VALUES ('Alice', 25, 1, 90, NULL, '2024-01-15 10:30:00');
                INSERT INTO WhereQueryUsers (Name, Age, IsActive, Score, Email, CreatedAt) VALUES ('Bob', 30, 0, NULL, '', '2024-02-20 08:00:00');
                INSERT INTO WhereQueryUsers (Name, Age, IsActive, Score, Email, CreatedAt) VALUES ('Carol', 35, 1, 60, 'carol@example.com', '2024-03-25 18:45:00');

                CREATE TABLE WhereQueryOrders (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    Amount DECIMAL(18,2) NOT NULL
                );
                INSERT INTO WhereQueryOrders (UserId, Amount) VALUES (1, 100.50);
                INSERT INTO WhereQueryOrders (UserId, Amount) VALUES (1, 200.00);
                INSERT INTO WhereQueryOrders (UserId, Amount) VALUES (2, 50.00);
            ");
        }

        #region 基本过滤

        [Fact]
        public async Task Where_Comparison_ToListAsync_FiltersMatchingRows()
        {
            var users = await DbAccess.Set<UserEntity>()
                .Where(u => u.Age > 26)
                .ToListAsync();

            Assert.Equal(2, users.Count);
            Assert.All(users, u => Assert.InRange(u.Age, 27, int.MaxValue));
            Assert.Contains(users, u => u.Name == "Bob");
            Assert.Contains(users, u => u.Name == "Carol");
        }

        [Fact]
        public async Task Where_ReusedQueryObject_ExecutesCorrectlyAcrossRuns()
        {
            // 查询对象持有翻译产物的参数数组，重复执行验证命令释放时解除参数归属后复用安全（分页循环同形态；
            // SQL Server/PostgreSQL/Oracle 驱动对已归属参数再绑定抛底层异常，SQLite 不追踪归属，故以功能等价守护）
            var query = DbAccess.Set<UserEntity>().Where(u => u.Age > 26);

            var first = await query.ToListAsync();
            var second = await query.ToListAsync();

            Assert.Equal(first.Count, second.Count);
            Assert.Equal(first.Select(u => u.Id), second.Select(u => u.Id));

            // 列表路径与单条路径混合复用同一参数数组
            var firstUser = await query.FirstOrDefaultAsync();
            Assert.Equal(first[0].Id, firstUser.Id);
        }

        [Fact]
        public async Task Where_LogicalOperators_CombineConditions()
        {
            var users = await DbAccess.Set<UserEntity>()
                .Where(u => u.IsActive && u.Score >= 60 || u.Name == "Bob")
                .ToListAsync();

            Assert.Equal(3, users.Count);
        }

        [Fact]
        public async Task Where_NoMatch_ReturnsEmptyList()
        {
            var users = await DbAccess.Set<UserEntity>()
                .Where(u => u.Name == "Nobody")
                .ToListAsync();

            Assert.Empty(users);
        }

        [Fact]
        public async Task Where_ClosureVariables_AreParameterized()
        {
            var minAge = 28;
            var namePrefix = "Ca";

            var users = await DbAccess.Set<UserEntity>()
                .Where(u => u.Age >= minAge && u.Name.StartsWith(namePrefix))
                .ToListAsync();

            Assert.Single(users);
            Assert.Equal("Carol", users[0].Name);
        }

        [Fact]
        public async Task Where_FirstOrDefaultAsync_ReturnsFirstMatch()
        {
            var user = await DbAccess.Set<UserEntity>()
                .Where(u => u.IsActive)
                .FirstOrDefaultAsync();

            Assert.NotNull(user);
            Assert.Equal("Alice", user.Name);
        }

        [Fact]
        public async Task Where_SingleOrDefaultAsync_UniqueMatch()
        {
            var user = await DbAccess.Set<UserEntity>()
                .Where(u => u.Name == "Carol")
                .SingleOrDefaultAsync();

            Assert.NotNull(user);
            Assert.Equal(35, user.Age);
        }

        [Fact]
        public async Task Where_SingleOrDefaultAsync_MultipleMatches_Throws()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => DbAccess.Set<UserEntity>()
                .Where(u => u.IsActive)
                .SingleOrDefaultAsync());
        }

        [Fact]
        public async Task Where_Contains_InClause()
        {
            var names = new[] { "Alice", "Carol" };

            var users = await DbAccess.Set<UserEntity>()
                .Where(u => names.Contains(u.Name))
                .ToListAsync();

            Assert.Equal(2, users.Count);
            Assert.All(users, u => Assert.NotEqual("Bob", u.Name));
        }

        [Fact]
        public async Task Where_StringContains_LikeClause()
        {
            var users = await DbAccess.Set<UserEntity>()
                .Where(u => u.Name.Contains("o"))
                .ToListAsync();

            Assert.Equal(2, users.Count);
            Assert.Contains(users, u => u.Name == "Bob");
            Assert.Contains(users, u => u.Name == "Carol");
        }

        #endregion

        #region 翻译器能力面

        [Fact]
        public async Task Where_BoolColumn_DirectPredicate()
        {
            var users = await DbAccess.Set<UserEntity>()
                .Where(u => u.IsActive)
                .ToListAsync();

            Assert.Equal(2, users.Count);
            Assert.All(users, u => Assert.True(u.IsActive));
        }

        [Fact]
        public async Task Where_NullEquality_IsNull()
        {
            var users = await DbAccess.Set<UserEntity>()
                .Where(u => u.Email == null)
                .ToListAsync();

            Assert.Single(users);
            Assert.Equal("Alice", users[0].Name);
        }

        [Fact]
        public async Task Where_HasValue_NullableColumn()
        {
            var users = await DbAccess.Set<UserEntity>()
                .Where(u => u.Score.HasValue)
                .ToListAsync();

            Assert.Equal(2, users.Count);
            Assert.DoesNotContain(users, u => u.Name == "Bob");
        }

        [Fact]
        public async Task Where_IsNullOrWhiteSpace_MatchesNullAndEmpty()
        {
            var users = await DbAccess.Set<UserEntity>()
                .Where(u => string.IsNullOrWhiteSpace(u.Email))
                .ToListAsync();

            Assert.Equal(2, users.Count);
            Assert.Contains(users, u => u.Name == "Alice");
            Assert.Contains(users, u => u.Name == "Bob");
        }

        [Fact]
        public async Task Where_StringTransform_ToUpperComparison()
        {
            var users = await DbAccess.Set<UserEntity>()
                .Where(u => u.Name.ToUpper() == "CAROL")
                .ToListAsync();

            Assert.Single(users);
            Assert.Equal("Carol", users[0].Name);
        }

        [Fact]
        public async Task Where_DatePart_YearMonthComparison()
        {
            var users = await DbAccess.Set<UserEntity>()
                .Where(u => u.CreatedAt.Year == 2024 && u.CreatedAt.Month == 2)
                .ToListAsync();

            Assert.Single(users);
            Assert.Equal("Bob", users[0].Name);
        }

        [Fact]
        public async Task Where_DatePart_DayComparison()
        {
            var users = await DbAccess.Set<UserEntity>()
                .Where(u => u.CreatedAt.Day >= 20)
                .ToListAsync();

            Assert.Equal(2, users.Count);
            Assert.Contains(users, u => u.Name == "Bob");
            Assert.Contains(users, u => u.Name == "Carol");
        }

        #endregion

        #region 与查询能力组合

        [Fact]
        public async Task Where_Include_LoadsNavigationProperties()
        {
            var users = await DbAccess.Set<UserEntity>()
                .Where(u => u.IsActive)
                .Include(u => u.Orders)
                .ToListAsync();

            // Alice、Carol 激活；Alice 有 2 个订单，Carol 无订单（未匹配的导航属性为空列表）
            Assert.Equal(2, users.Count);

            var alice = users.Single(u => u.Name == "Alice");
            Assert.Equal(2, alice.Orders.Count);
            Assert.Contains(alice.Orders, o => o.Amount == 100.50m);
            Assert.Contains(alice.Orders, o => o.Amount == 200.00m);

            var carol = users.Single(u => u.Name == "Carol");
            Assert.Empty(carol.Orders);
        }

        [Fact]
        public async Task Where_SkipTake_PaginatesFilteredRows()
        {
            var page = await DbAccess.Set<UserEntity>()
                .Where(u => u.Age >= 25)
                .OrderByDescending(u => u.Age)
                .Take(2)
                .ToListAsync();

            Assert.Equal(2, page.Count);
            Assert.Equal("Carol", page[0].Name);
            Assert.Equal("Bob", page[1].Name);
        }

        #endregion

        #region 异常路径

        [Fact]
        public void Where_NullPredicate_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => DbAccess.Set<UserEntity>()
                .Where((Expression<Func<UserEntity, bool>>)null!));
        }

        [Fact]
        public async Task Where_NavigationProperty_ThrowsNotSupportedException()
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => DbAccess.Set<UserEntity>()
                .Where(u => u.Orders.Count > 0)
                .ToListAsync());
        }

        [Fact]
        public async Task Where_ArithmeticOperation_ThrowsNotSupportedException()
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => DbAccess.Set<UserEntity>()
                .Where(u => u.Age + 10 > 50)
                .ToListAsync());
        }

        [Fact]
        public async Task Where_ConcurrencyTokenComparison_ThrowsNotSupportedException()
        {
            // 并发令牌由框架乐观锁专用管理（SET 自动换发、乐观锁条件内部拼接生成），用户查询条件不参与版本比较
            var version = new byte[] { 1, 2, 3 };

            await Assert.ThrowsAsync<NotSupportedException>(() => DbAccess.Set<TokenDocEntity>()
                .Where(d => d.RowVersion == version)
                .ToListAsync());
        }

        #endregion
    }
}
