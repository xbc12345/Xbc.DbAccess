using Xbc.DbAccess.Entity;
using Xbc.DbAccess.IntegrationTests.Infrastructure;
using Xunit;
using static Xbc.DbAccess.IntegrationTests.Infrastructure.RealDbDialect;

namespace Xbc.DbAccess.IntegrationTests
{
    /// <summary>
    /// 导航装配（Include/ThenInclude）的真库回归：1:1 LEFT JOIN 与 1:N 分裂查询的 SQL 在各方言上形态合法、
    /// 装配语义正确，且与排序/窗口状态（Skip/Take 截取根实体行集）组合时导航装配不越界。
    /// </summary>
    public abstract class NavigationIncludeTestsBase : RealDbTestBase
    {
        protected NavigationIncludeTestsBase(RealDbDialect dialect) : base(dialect)
        {
        }

        protected override Type[] EntityTypes => new[]
        {
            typeof(ItNavCustomer),
            typeof(ItNavOrder),
            typeof(ItNavOrderDetail),
            typeof(ItNavProduct)
        };

        private async Task<List<ItNavOrder>> SeedAsync(EntitySet<ItNavCustomer> customers, EntitySet<ItNavProduct> products, EntitySet<ItNavOrder> orders, EntitySet<ItNavOrderDetail> details)
        {
            await customers.InsertRangeAsync(new List<ItNavCustomer>
            {
                new ItNavCustomer { Name = "Alice" },
                new ItNavCustomer { Name = "Bob" }
            });

            await products.InsertRangeAsync(new List<ItNavProduct>
            {
                new ItNavProduct { ProductName = "Keyboard" },
                new ItNavProduct { ProductName = "Mouse" }
            });

            var seededOrders = new List<ItNavOrder>
            {
                new ItNavOrder { CustomerId = 1, Amount = 100m },
                new ItNavOrder { CustomerId = 1, Amount = 150m },
                new ItNavOrder { CustomerId = 2, Amount = 200m }
            };
            await orders.InsertRangeAsync(seededOrders);

            await details.InsertRangeAsync(new List<ItNavOrderDetail>
            {
                new ItNavOrderDetail { OrderId = 1, ProductName = "Keyboard#1", ProductId = 1 },
                new ItNavOrderDetail { OrderId = 1, ProductName = "Mouse#1", ProductId = 2 },
                new ItNavOrderDetail { OrderId = 2, ProductName = "Keyboard#2", ProductId = 1 }
            });

            return seededOrders;
        }

        [SkippableFact]
        public async Task Include_OneToOne_CustomerPopulated()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<ItNavOrder>(DbAccess);
            await SeedAsync(new EntitySet<ItNavCustomer>(DbAccess), new EntitySet<ItNavProduct>(DbAccess), entitySet, new EntitySet<ItNavOrderDetail>(DbAccess));

            var rows = await entitySet.AsQuery().OrderBy(o => o.OrderId).Include(o => o.Customer).ToListAsync();

            Assert.Equal(3, rows.Count);
            Assert.Equal("Alice", rows[0].Customer.Name);
            Assert.Equal("Alice", rows[1].Customer.Name);
            Assert.Equal("Bob", rows[2].Customer.Name);
        }

        [SkippableFact]
        public async Task Include_OneToMany_DetailsPopulated()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<ItNavOrder>(DbAccess);
            await SeedAsync(new EntitySet<ItNavCustomer>(DbAccess), new EntitySet<ItNavProduct>(DbAccess), entitySet, new EntitySet<ItNavOrderDetail>(DbAccess));

            var rows = await entitySet.AsQuery().OrderBy(o => o.OrderId).Include(o => o.Details).ToListAsync();

            Assert.Equal(3, rows.Count);
            Assert.Equal(2, rows[0].Details.Count);
            Assert.Equal("Keyboard#1", rows[0].Details[0].ProductName);
            Assert.Equal("Mouse#1", rows[0].Details[1].ProductName);
            Assert.Single(rows[1].Details);
            Assert.Empty(rows[2].Details);
        }

        [SkippableFact]
        public async Task ThenInclude_ChainToProduct_Populated()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<ItNavOrder>(DbAccess);
            await SeedAsync(new EntitySet<ItNavCustomer>(DbAccess), new EntitySet<ItNavProduct>(DbAccess), entitySet, new EntitySet<ItNavOrderDetail>(DbAccess));

            var rows = await entitySet.AsQuery().OrderBy(o => o.OrderId).Include(o => o.Details).ThenInclude(d => d.Product).ToListAsync();

            // 明细中的 1:1 Product 经 ThenInclude 二级装配
            Assert.Equal("Keyboard", rows[0].Details[0].Product.ProductName);
            Assert.Equal("Mouse", rows[0].Details[1].Product.ProductName);
            Assert.Equal("Keyboard", rows[1].Details[0].Product.ProductName);
        }

        [SkippableFact]
        public async Task Include_AfterSkipTake_Window_PopulatesNavigationsForWindowRows()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<ItNavOrder>(DbAccess);
            await SeedAsync(new EntitySet<ItNavCustomer>(DbAccess), new EntitySet<ItNavProduct>(DbAccess), entitySet, new EntitySet<ItNavOrderDetail>(DbAccess));

            // 窗口态根实体行集（OrderId 2/3）先截取再装配：装配不越出窗口行集
            var rows = await entitySet.AsQuery()
                .OrderBy(o => o.OrderId)
                .Skip(1)
                .Take(2)
                .Include(o => o.Customer)
                .Include(o => o.Details)
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Equal(2, rows[0].OrderId);
            Assert.Equal("Alice", rows[0].Customer.Name);
            Assert.Single(rows[0].Details);
            Assert.Equal(3, rows[1].OrderId);
            Assert.Equal("Bob", rows[1].Customer.Name);
            Assert.Empty(rows[1].Details);
        }

        #region 测试实体

        /// <summary>导航客户实体（本测试类专属，表名 it_nav_customer）。</summary>
        [Table("it_nav_customer")]
        public class ItNavCustomer
        {
            [PrimaryKey(IsIdentity = true)]
            public int CustomerId { get; set; }

            public string Name { get; set; }
        }

        /// <summary>导航订单实体（本测试类专属，表名 it_nav_order）。</summary>
        [Table("it_nav_order")]
        public class ItNavOrder
        {
            [PrimaryKey(IsIdentity = true)]
            public int OrderId { get; set; }

            public int CustomerId { get; set; }

            public decimal Amount { get; set; }

            // 1:1 — 约定推断：CustomerId → ItNavCustomer.CustomerId
            [Navigation]
            public ItNavCustomer Customer { get; set; }

            // 1:N — 手动指定键：OrderId → ItNavOrderDetail.OrderId
            [Navigation(ThisKey = "OrderId", OtherKey = "OrderId")]
            public List<ItNavOrderDetail> Details { get; set; }
        }

        /// <summary>导航明细实体（本测试类专属，表名 it_nav_detail）。</summary>
        [Table("it_nav_detail")]
        public class ItNavOrderDetail
        {
            [PrimaryKey(IsIdentity = true)]
            public int DetailId { get; set; }

            public int OrderId { get; set; }

            public string ProductName { get; set; }

            public int ProductId { get; set; }

            // 1:1 — 约定推断：ProductId → ItNavProduct.ProductId
            [Navigation]
            public ItNavProduct Product { get; set; }
        }

        /// <summary>导航产品实体（本测试类专属，表名 it_nav_product）。</summary>
        [Table("it_nav_product")]
        public class ItNavProduct
        {
            [PrimaryKey(IsIdentity = true)]
            public int ProductId { get; set; }

            public string ProductName { get; set; }
        }

        #endregion
    }

    /// <summary>导航装配回归的 PostgreSQL 真库用例。</summary>
    public class PgSqlNavigationIncludeTests : NavigationIncludeTestsBase
    {
        public PgSqlNavigationIncludeTests() : base(PgSql)
        {
        }
    }

    /// <summary>导航装配回归的 SQL Server 真库用例。</summary>
    public class SqlServerNavigationIncludeTests : NavigationIncludeTestsBase
    {
        public SqlServerNavigationIncludeTests() : base(RealDbDialect.SqlServer)
        {
        }
    }

    /// <summary>导航装配回归的 MySQL 真库用例。</summary>
    public class MySqlNavigationIncludeTests : NavigationIncludeTestsBase
    {
        public MySqlNavigationIncludeTests() : base(RealDbDialect.MySql)
        {
        }
    }

    /// <summary>导航装配回归的 Oracle 真库用例。</summary>
    public class OracleNavigationIncludeTests : NavigationIncludeTestsBase
    {
        public OracleNavigationIncludeTests() : base(RealDbDialect.Oracle)
        {
        }
    }

    /// <summary>导航装配回归的 SQLite 真库用例（本地文件数据库，恒可用）。</summary>
    public class SqliteNavigationIncludeTests : NavigationIncludeTestsBase
    {
        public SqliteNavigationIncludeTests() : base(RealDbDialect.Sqlite)
        {
        }
    }
}
