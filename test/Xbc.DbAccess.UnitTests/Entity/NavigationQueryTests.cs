using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 导航查询的单元测试。
    /// </summary>
    public class NavigationQueryTests : EntityTestBase
    {
        #region 测试实体

        [XbcDbEntity.Table("NavCustomers")]
        public class NavCustomer
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int CustomerId { get; set; }
            public string Name { get; set; }
        }

        [XbcDbEntity.Table("NavOrders")]
        public class NavOrder
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int OrderId { get; set; }
            public int CustomerId { get; set; }
            public DateTime OrderDate { get; set; }
            public decimal TotalAmount { get; set; }

            // 1:1 — 约定推断：CustomerId → NavCustomer.CustomerId
            [Navigation]
            public NavCustomer Customer { get; set; }

            // 1:N — 手动指定键：OrderId → NavOrderDetail.OrderId
            // （约定推断为 NavOrderId，不符合，需手动指定）
            [Navigation(ThisKey = "OrderId", OtherKey = "OrderId")]
            public List<NavOrderDetail> Details { get; set; }
        }

        [XbcDbEntity.Table("NavOrderDetails")]
        public class NavOrderDetail
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int DetailId { get; set; }
            public int OrderId { get; set; }
            public string ProductName { get; set; }
            public int Quantity { get; set; }
            public decimal Price { get; set; }
            public int ProductId { get; set; }

            // 1:1 — 约定推断：ProductId → NavOrderProduct.ProductId
            [Navigation]
            public NavOrderProduct Product { get; set; }
        }

        [XbcDbEntity.Table("NavOrderProducts")]
        public class NavOrderProduct
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int ProductId { get; set; }
            public string Name { get; set; }
        }

        /// <summary>
        /// 自引用导航实体：Manager 的目标类型仍是 NavEmployee，用于验证元数据构建不会无限递归。
        /// </summary>
        [XbcDbEntity.Table("NavEmployees")]
        public class NavEmployee
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int EmployeeId { get; set; }
            public string Name { get; set; }
            public int? ManagerId { get; set; }

            // 自引用 1:1：ManagerId → NavEmployee.EmployeeId
            [Navigation(ThisKey = "ManagerId", OtherKey = "EmployeeId")]
            public NavEmployee Manager { get; set; }
        }

        /// <summary>
        /// 循环引用导航实体对：NavAuthor 与 NavProfile 互相引用，用于验证元数据构建不会无限递归。
        /// </summary>
        [XbcDbEntity.Table("NavAuthors")]
        public class NavAuthor
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int AuthorId { get; set; }
            public string Name { get; set; }
            public int ProfileId { get; set; }

            // 1:1 — 约定推断：ProfileId → NavProfile.ProfileId
            [Navigation]
            public NavProfile Profile { get; set; }
        }

        [XbcDbEntity.Table("NavProfiles")]
        public class NavProfile
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int ProfileId { get; set; }
            public string Bio { get; set; }
            public int AuthorId { get; set; }

            // 1:1 — 约定推断：AuthorId → NavAuthor.AuthorId
            [Navigation]
            public NavAuthor Author { get; set; }
        }

        /// <summary>
        /// 键类型不匹配实体：父表主键为 int，子表外键为 long，用于验证元数据构建时抛出清晰的配置错误。
        /// </summary>
        [XbcDbEntity.Table("NavMismatchParents")]
        public class NavMismatchParent
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int ParentId { get; set; }

            [Navigation(OtherKey = "ParentId")]
            public List<NavMismatchChild> Children { get; set; }
        }

        [XbcDbEntity.Table("NavMismatchChildren")]
        public class NavMismatchChild
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int ChildId { get; set; }
            public long ParentId { get; set; }
        }

        // 手动指定键的实体
        [XbcDbEntity.Table("NavBuyers")]
        public class NavBuyer
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int BuyerId { get; set; }
            public string Name { get; set; }
        }

        [XbcDbEntity.Table("NavPurchases")]
        public class NavPurchase
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int PurchaseId { get; set; }
            public int BuyerRef { get; set; }
            public string ItemName { get; set; }

            // 手动指定键：BuyerRef → NavBuyer.BuyerId
            [Navigation(ThisKey = "BuyerRef", OtherKey = "BuyerId")]
            public NavBuyer Buyer { get; set; }
        }

        // 符合 1:N 约定推断的实体（外键为 {父实体名}Id）
        [XbcDbEntity.Table("NavCategories")]
        public class NavCategory
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int CategoryId { get; set; }
            public string Name { get; set; }

            // 1:N 约定推断：CategoryId → NavProduct.NavCategoryId
            [Navigation]
            public List<NavProduct> Products { get; set; }
        }

        [XbcDbEntity.Table("NavProducts")]
        public class NavProduct
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int ProductId { get; set; }
            // 外键名符合约定：NavCategoryId
            public int NavCategoryId { get; set; }
            public string ProductName { get; set; }
        }

        /// <summary>
        /// 分批装配测试实体：父实体数量超过 SQLite 参数上限（32766），验证 1:N 导航分批装配不丢失数据。
        /// </summary>
        [XbcDbEntity.Table("NavBatchParents")]
        public class NavBatchParent
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int ParentId { get; set; }
            public string Name { get; set; }

            // 1:N 手动指定键：ParentId → NavBatchChild.ParentId
            // （约定推断为 NavBatchParentId，不符合，需手动指定）
            [Navigation(OtherKey = "ParentId")]
            public List<NavBatchChild> Children { get; set; }
        }

        [XbcDbEntity.Table("NavBatchChildren")]
        public class NavBatchChild
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int ChildId { get; set; }
            public int ParentId { get; set; }
            public string Name { get; set; }
        }

        /// <summary>
        /// 分批装配测试实体（1:1 引用导航）：父实体外键数量超过 SQLite 参数上限（32766）。
        /// </summary>
        [XbcDbEntity.Table("NavBatchRefParents")]
        public class NavBatchRefParent
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int ParentId { get; set; }
            public int RefId { get; set; }

            // 1:1 约定推断：RefId → NavBatchRefTarget.RefId
            [Navigation]
            public NavBatchRefTarget Ref { get; set; }
        }

        [XbcDbEntity.Table("NavBatchRefTargets")]
        public class NavBatchRefTarget
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int RefId { get; set; }
            public string Name { get; set; }
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE NavCustomers (
                    CustomerId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                );
                INSERT INTO NavCustomers (Name) VALUES ('张三');
                INSERT INTO NavCustomers (Name) VALUES ('李四');

                CREATE TABLE NavOrders (
                    OrderId INTEGER PRIMARY KEY AUTOINCREMENT,
                    CustomerId INTEGER NOT NULL,
                    OrderDate TEXT NOT NULL,
                    TotalAmount REAL NOT NULL
                );
                INSERT INTO NavOrders (CustomerId, OrderDate, TotalAmount) VALUES (1, '2024-01-01', 100.50);
                INSERT INTO NavOrders (CustomerId, OrderDate, TotalAmount) VALUES (1, '2024-01-02', 200.00);
                INSERT INTO NavOrders (CustomerId, OrderDate, TotalAmount) VALUES (2, '2024-01-03', 300.00);

                CREATE TABLE NavOrderDetails (
                    DetailId INTEGER PRIMARY KEY AUTOINCREMENT,
                    OrderId INTEGER NOT NULL,
                    ProductName TEXT NOT NULL,
                    Quantity INTEGER NOT NULL,
                    Price REAL NOT NULL,
                    ProductId INTEGER NOT NULL
                );
                INSERT INTO NavOrderDetails (OrderId, ProductName, Quantity, Price, ProductId) VALUES (1, '商品A', 2, 50.25, 1);
                INSERT INTO NavOrderDetails (OrderId, ProductName, Quantity, Price, ProductId) VALUES (1, '商品B', 1, 100.00, 2);
                INSERT INTO NavOrderDetails (OrderId, ProductName, Quantity, Price, ProductId) VALUES (2, '商品C', 3, 66.67, 1);
                INSERT INTO NavOrderDetails (OrderId, ProductName, Quantity, Price, ProductId) VALUES (3, '商品D', 1, 300.00, 2);

                CREATE TABLE NavOrderProducts (
                    ProductId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                );
                INSERT INTO NavOrderProducts (Name) VALUES ('手机');
                INSERT INTO NavOrderProducts (Name) VALUES ('耳机');

                CREATE TABLE NavEmployees (
                    EmployeeId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    ManagerId INTEGER
                );
                INSERT INTO NavEmployees (Name, ManagerId) VALUES ('Bob', NULL);
                INSERT INTO NavEmployees (Name, ManagerId) VALUES ('Alice', 1);
                INSERT INTO NavEmployees (Name, ManagerId) VALUES ('Charlie', 1);

                CREATE TABLE NavAuthors (
                    AuthorId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    ProfileId INTEGER NOT NULL
                );
                INSERT INTO NavAuthors (Name, ProfileId) VALUES ('作者甲', 1);

                CREATE TABLE NavProfiles (
                    ProfileId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Bio TEXT NOT NULL,
                    AuthorId INTEGER NOT NULL
                );
                INSERT INTO NavProfiles (Bio, AuthorId) VALUES ('简介甲', 1);

                CREATE TABLE NavBuyers (
                    BuyerId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                );
                INSERT INTO NavBuyers (Name) VALUES ('王五');

                CREATE TABLE NavPurchases (
                    PurchaseId INTEGER PRIMARY KEY AUTOINCREMENT,
                    BuyerRef INTEGER NOT NULL,
                    ItemName TEXT NOT NULL
                );
                INSERT INTO NavPurchases (BuyerRef, ItemName) VALUES (1, '物品X');

                CREATE TABLE NavCategories (
                    CategoryId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                );
                INSERT INTO NavCategories (Name) VALUES ('电子产品');
                INSERT INTO NavCategories (Name) VALUES ('食品');

                CREATE TABLE NavProducts (
                    ProductId INTEGER PRIMARY KEY AUTOINCREMENT,
                    NavCategoryId INTEGER NOT NULL,
                    ProductName TEXT NOT NULL
                );
                INSERT INTO NavProducts (NavCategoryId, ProductName) VALUES (1, '手机');
                INSERT INTO NavProducts (NavCategoryId, ProductName) VALUES (1, '电脑');
                INSERT INTO NavProducts (NavCategoryId, ProductName) VALUES (2, '苹果');

                CREATE TABLE NavBatchParents (
                    ParentId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                );
                CREATE TABLE NavBatchChildren (
                    ChildId INTEGER PRIMARY KEY AUTOINCREMENT,
                    ParentId INTEGER NOT NULL,
                    Name TEXT NOT NULL
                );
                CREATE TABLE NavBatchRefParents (
                    ParentId INTEGER PRIMARY KEY AUTOINCREMENT,
                    RefId INTEGER NOT NULL
                );
                CREATE TABLE NavBatchRefTargets (
                    RefId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                );
            ");

            // 分批装配测试数据：父实体数量（33000）超过 SQLite 参数上限（32766），触发导航 IN 查询分批（32766 + 234 两批）；
            // 数据准备按 500 行/条分块插入（避免超出 SQLite 单语句多值 VALUES 行数限制 SQLITE_MAX_COMPOUND_SELECT），
            // 整体包一个事务把 264 次分块插入合并为单次提交
            using (var transaction = await DbAccess.BeginTransactionAsync())
            {
                for (int chunkStart = 1; chunkStart <= 33000; chunkStart += 500)
                {
                    var parentValues = new System.Text.StringBuilder("INSERT INTO NavBatchParents (Name) VALUES ");
                    var childValues = new System.Text.StringBuilder("INSERT INTO NavBatchChildren (ParentId, Name) VALUES ");
                    var refParentValues = new System.Text.StringBuilder("INSERT INTO NavBatchRefParents (RefId) VALUES ");
                    var refTargetValues = new System.Text.StringBuilder("INSERT INTO NavBatchRefTargets (Name) VALUES ");
                    for (int i = chunkStart; i < chunkStart + 500; i++)
                    {
                        parentValues.Append($"('父{i}'),");
                        childValues.Append($"({i}, '子{i}'),");
                        refParentValues.Append($"({i}),");
                        refTargetValues.Append($"('引用{i}'),");
                    }
                    parentValues.Length--;// 去掉尾逗号
                    childValues.Length--;
                    refParentValues.Length--;
                    refTargetValues.Length--;
                    await DbAccess.ExecuteNonQueryAsync(parentValues.ToString());
                    await DbAccess.ExecuteNonQueryAsync(childValues.ToString());
                    await DbAccess.ExecuteNonQueryAsync(refParentValues.ToString());
                    await DbAccess.ExecuteNonQueryAsync(refTargetValues.ToString());
                }

                transaction.Commit();
            }
        }

        #region 1:1 导航属性测试

        [Fact]
        public async Task Include_OneToOne_LeftJoin()
        {
            var orders = await DbAccess.FromSql<NavOrder>("SELECT * FROM NavOrders")
                .Include(o => o.Customer)
                .ToListAsync();

            Assert.Equal(3, orders.Count);

            // 验证 1:1 导航属性
            Assert.All(orders, o => Assert.NotNull(o.Customer));
            Assert.Equal("张三", orders[0].Customer.Name);
            Assert.Equal("张三", orders[1].Customer.Name);
            Assert.Equal("李四", orders[2].Customer.Name);
        }

        [Fact]
        public async Task Include_OneToOne_LeftJoin_Async()
        {
            var orders = await DbAccess.FromSql<NavOrder>("SELECT * FROM NavOrders")
                .Include(o => o.Customer)
                .ToListAsync();

            Assert.Equal(3, orders.Count);
            Assert.All(orders, o => Assert.NotNull(o.Customer));
        }

        [Fact]
        public async Task Include_OneToOne_WithWhere()
        {
            var orders = await DbAccess.FromSql<NavOrder>("SELECT * FROM NavOrders WHERE CustomerId = @cid", new { cid = 1 })
                .Include(o => o.Customer)
                .ToListAsync();

            Assert.Equal(2, orders.Count);
            Assert.All(orders, o => Assert.Equal("张三", o.Customer.Name));
        }

        [Fact]
        public async Task Include_OneToOne_ManualKey()
        {
            var purchases = await DbAccess.FromSql<NavPurchase>("SELECT * FROM NavPurchases")
                .Include(p => p.Buyer)
                .ToListAsync();

            Assert.Single(purchases);
            Assert.NotNull(purchases[0].Buyer);
            Assert.Equal("王五", purchases[0].Buyer.Name);
        }

        #endregion

        #region 1:N 导航属性测试

        [Fact]
        public async Task Include_OneToMany_LeftJoin()
        {
            var orders = await DbAccess.FromSql<NavOrder>("SELECT * FROM NavOrders")
                .Include(o => o.Details)
                .ToListAsync();

            Assert.Equal(3, orders.Count);

            // 订单1 有 2 个明细
            var order1 = orders.First(o => o.OrderId == 1);
            Assert.NotNull(order1.Details);
            Assert.Equal(2, order1.Details.Count);

            // 订单2 有 1 个明细
            var order2 = orders.First(o => o.OrderId == 2);
            Assert.NotNull(order2.Details);
            Assert.Single(order2.Details);

            // 订单3 有 1 个明细
            var order3 = orders.First(o => o.OrderId == 3);
            Assert.NotNull(order3.Details);
            Assert.Single(order3.Details);
        }

        [Fact]
        public async Task Include_OneToMany_LeftJoin_Async()
        {
            var orders = await DbAccess.FromSql<NavOrder>("SELECT * FROM NavOrders")
                .Include(o => o.Details)
                .ToListAsync();

            Assert.Equal(3, orders.Count);
            var order1 = orders.First(o => o.OrderId == 1);
            Assert.Equal(2, order1.Details.Count);
        }

        [Fact]
        public async Task Include_OneToMany_SplitQuery()
        {
            var orders = await DbAccess.FromSql<NavOrder>("SELECT * FROM NavOrders")
                .Include(o => o.Details)
                .ToListAsync();

            Assert.Equal(3, orders.Count);

            var order1 = orders.First(o => o.OrderId == 1);
            Assert.NotNull(order1.Details);
            Assert.Equal(2, order1.Details.Count);
            Assert.Contains(order1.Details, d => d.ProductName == "商品A");
            Assert.Contains(order1.Details, d => d.ProductName == "商品B");
        }

        [Fact]
        public async Task Include_OneToMany_SplitQuery_Async()
        {
            var orders = await DbAccess.FromSql<NavOrder>("SELECT * FROM NavOrders")
                .Include(o => o.Details)
                .ToListAsync();

            Assert.Equal(3, orders.Count);
            var order1 = orders.First(o => o.OrderId == 1);
            Assert.Equal(2, order1.Details.Count);
        }

        #endregion

        #region 1:1 + 1:N 混合测试

        [Fact]
        public async Task Include_OneToOne_And_OneToMany()
        {
            var orders = await DbAccess.FromSql<NavOrder>("SELECT * FROM NavOrders")
                .Include(o => o.Customer)
                .Include(o => o.Details)
                .ToListAsync();

            Assert.Equal(3, orders.Count);

            // 验证 1:1
            Assert.All(orders, o => Assert.NotNull(o.Customer));

            // 验证 1:N
            var order1 = orders.First(o => o.OrderId == 1);
            Assert.Equal(2, order1.Details.Count);
        }

        [Fact]
        public async Task Include_OneToOne_And_OneToMany_SplitQuery()
        {
            var orders = await DbAccess.FromSql<NavOrder>("SELECT * FROM NavOrders")
                .Include(o => o.Customer)
                .Include(o => o.Details)
                .ToListAsync();

            Assert.Equal(3, orders.Count);

            // 验证 1:1
            Assert.All(orders, o => Assert.NotNull(o.Customer));

            // 验证 1:N
            var order1 = orders.First(o => o.OrderId == 1);
            Assert.Equal(2, order1.Details.Count);
        }

        [Fact]
        public async Task Include_OneToOne_And_OneToMany_Async()
        {
            var orders = await DbAccess.FromSql<NavOrder>("SELECT * FROM NavOrders")
                .Include(o => o.Customer)
                .Include(o => o.Details)
                .ToListAsync();

            Assert.Equal(3, orders.Count);
            Assert.All(orders, o => Assert.NotNull(o.Customer));
            var order1 = orders.First(o => o.OrderId == 1);
            Assert.Equal(2, order1.Details.Count);
        }

        #endregion

        #region FirstOrDefault 测试

        [Fact]
        public async Task FirstOrDefault_WithInclude()
        {
            var order = await DbAccess.FromSql<NavOrder>("SELECT * FROM NavOrders WHERE OrderId = @id", new { id = 1 })
                .Include(o => o.Customer)
                .Include(o => o.Details)
                .FirstOrDefaultAsync();

            Assert.NotNull(order);
            Assert.Equal("张三", order.Customer.Name);
            Assert.Equal(2, order.Details.Count);
        }

        [Fact]
        public async Task FirstOrDefault_NoMatch_ReturnsNull()
        {
            var order = await DbAccess.FromSql<NavOrder>("SELECT * FROM NavOrders WHERE OrderId = @id", new { id = 999 })
                .Include(o => o.Customer)
                .FirstOrDefaultAsync();

            Assert.Null(order);
        }

        [Fact]
        public async Task FirstOrDefaultAsync_WithInclude()
        {
            var order = await DbAccess.FromSql<NavOrder>("SELECT * FROM NavOrders WHERE OrderId = @id", new { id = 1 })
                .Include(o => o.Customer)
                .Include(o => o.Details)
                .FirstOrDefaultAsync();

            Assert.NotNull(order);
            Assert.Equal("张三", order.Customer.Name);
            Assert.Equal(2, order.Details.Count);
        }

        #endregion

        #region 无 Include 测试

        [Fact]
        public async Task Query_WithoutInclude_ReturnsPlainEntities()
        {
            var orders = await DbAccess.FromSql<NavOrder>("SELECT * FROM NavOrders").ToListAsync();

            Assert.Equal(3, orders.Count);
            Assert.All(orders, o => Assert.Null(o.Customer));
            Assert.All(orders, o => Assert.Null(o.Details));
        }

        #endregion

        #region 1:N 约定推断测试

        [Fact]
        public async Task Include_OneToMany_ConventionInference()
        {
            var categories = await DbAccess.FromSql<NavCategory>("SELECT * FROM NavCategories")
                .Include(c => c.Products)
                .ToListAsync();

            Assert.Equal(2, categories.Count);

            var electronics = categories.First(c => c.CategoryId == 1);
            Assert.NotNull(electronics.Products);
            Assert.Equal(2, electronics.Products.Count);

            var food = categories.First(c => c.CategoryId == 2);
            Assert.NotNull(food.Products);
            Assert.Single(food.Products);
            Assert.Equal("苹果", food.Products[0].ProductName);
        }

        [Fact]
        public async Task Include_OneToMany_ConventionInference_SplitQuery()
        {
            var categories = await DbAccess.FromSql<NavCategory>("SELECT * FROM NavCategories")
                .Include(c => c.Products)
                .ToListAsync();

            Assert.Equal(2, categories.Count);
            var electronics = categories.First(c => c.CategoryId == 1);
            Assert.Equal(2, electronics.Products.Count);
        }

        #endregion

        #region 自引用/循环引用与链尾挂载测试

        [Fact]
        public async Task Include_SelfReference_OneToOne()
        {
            // 自引用导航：Manager 的目标类型仍是 NavEmployee，元数据构建不应无限递归导致栈溢出
            var employees = await DbAccess.FromSql<NavEmployee>("SELECT * FROM NavEmployees")
                .Include(e => e.Manager)
                .ToListAsync();

            Assert.Equal(3, employees.Count);

            // Bob 无上级（外键为 NULL），导航属性保持 null
            var bob = employees.First(e => e.Name == "Bob");
            Assert.Null(bob.Manager);

            // Alice/Charlie 的上级是 Bob
            var alice = employees.First(e => e.Name == "Alice");
            Assert.NotNull(alice.Manager);
            Assert.Equal("Bob", alice.Manager.Name);
        }

        [Fact]
        public async Task Include_CircularReference_BetweenEntities()
        {
            // 循环引用：NavAuthor.Profile 的目标 NavProfile 又引用 NavAuthor，构建不应栈溢出
            var authors = await DbAccess.FromSql<NavAuthor>("SELECT * FROM NavAuthors")
                .Include(a => a.Profile)
                .ToListAsync();

            Assert.Single(authors);
            Assert.NotNull(authors[0].Profile);
            Assert.Equal("简介甲", authors[0].Profile.Bio);
        }

        [Fact]
        public async Task Include_CircularReference_ThenInclude_BackToRoot()
        {
            // 循环引用 + ThenInclude 回到根实体类型
            var authors = await DbAccess.FromSql<NavAuthor>("SELECT * FROM NavAuthors")
                .Include(a => a.Profile)
                .ThenInclude(p => p.Author)
                .ToListAsync();

            Assert.Single(authors);
            Assert.NotNull(authors[0].Profile);
            Assert.NotNull(authors[0].Profile.Author);
            Assert.Equal("作者甲", authors[0].Profile.Author.Name);
        }

        [Fact]
        public async Task Include_DuplicateRoot_ThenIncludeAttachesToRestartedChain()
        {
            // Include 去重（第三次 Include(Details)）后，ThenInclude 应挂载到重新指定的根链 Details.Product，
            // 而非导航链列表的尾节点 Customer（修复前会抛 'NavCustomer.Product 不是导航属性'）
            var orders = await DbAccess.FromSql<NavOrder>("SELECT * FROM NavOrders")
                .Include(o => o.Details)
                .Include(o => o.Customer)
                .Include(o => o.Details)
                .ThenInclude(d => d.Product)
                .ToListAsync();

            Assert.Equal(3, orders.Count);

            var order1 = orders.First(o => o.OrderId == 1);
            Assert.Equal(2, order1.Details.Count);
            Assert.All(order1.Details, d => Assert.NotNull(d.Product));
            Assert.Equal("手机", order1.Details[0].Product.Name);
            Assert.Equal("耳机", order1.Details[1].Product.Name);
        }

        [Fact]
        public async Task Include_DuplicateThenInclude_IsIdempotent()
        {
            // 重复的 ThenInclude 链（Include(A).ThenInclude(B).Include(A).ThenInclude(B)）应幂等无操作
            var orders = await DbAccess.FromSql<NavOrder>("SELECT * FROM NavOrders")
                .Include(o => o.Details)
                .ThenInclude(d => d.Product)
                .Include(o => o.Details)
                .ThenInclude(d => d.Product)
                .ToListAsync();

            Assert.Equal(3, orders.Count);
            var order2 = orders.First(o => o.OrderId == 2);
            Assert.Single(order2.Details);
            Assert.Equal("手机", order2.Details[0].Product.Name);
        }

        [Fact]
        public void Navigation_KeyTypeMismatch_Throws()
        {
            // int 主键配 long 外键：装配按键值装箱相等匹配会静默失败，应在元数据构建时报出清晰的配置错误
            var ex = Assert.Throws<InvalidOperationException>(
                () => DbAccess.FromSql<NavMismatchParent>("SELECT * FROM NavMismatchParents").Include(p => p.Children));

            Assert.Contains("键类型不匹配", ex.Message);
            Assert.Contains("ParentId", ex.Message);
        }

        #endregion

        #region 分批装配测试（键值数超过提供程序参数上限）

        [Fact]
        public async Task Include_OneToMany_KeyCountExceedsParameterLimit_PopulatesAllBatches()
        {
            // 父实体去重后键值数（33000）超过 SQLite 参数上限（32766），1:N 导航的 IN 查询分 2 批执行
            // 修复前：后批装配会以空列表覆盖前批已装配的集合导航，仅最后一批（234 个）父实体的 Children 非空
            var parents = await DbAccess.FromSql<NavBatchParent>("SELECT * FROM NavBatchParents")
                .Include(p => p.Children)
                .ToListAsync();

            Assert.Equal(33000, parents.Count);
            Assert.All(parents, p =>
            {
                Assert.NotNull(p.Children);
                Assert.Single(p.Children);
                Assert.Equal($"子{p.ParentId}", p.Children[0].Name);
            });
        }

        [Fact]
        public async Task Include_OneToOne_KeyCountExceedsParameterLimit_PopulatesAllBatches()
        {
            // 1:1 引用导航的父表外键数同样超过参数上限分批执行，全部父实体的引用导航均应装配成功
            var parents = await DbAccess.FromSql<NavBatchRefParent>("SELECT * FROM NavBatchRefParents")
                .Include(p => p.Ref)
                .ToListAsync();

            Assert.Equal(33000, parents.Count);
            Assert.All(parents, p =>
            {
                Assert.NotNull(p.Ref);
                Assert.Equal($"引用{p.RefId}", p.Ref.Name);
            });
        }

        #endregion
    }
}
