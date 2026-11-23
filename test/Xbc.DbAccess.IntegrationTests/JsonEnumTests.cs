using System.Text.Json;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.IntegrationTests.Infrastructure;
using Xunit;
using static Xbc.DbAccess.IntegrationTests.Infrastructure.RealDbDialect;

namespace Xbc.DbAccess.IntegrationTests
{
    /// <summary>
    /// JSON 列与枚举名称列的真库回归：读写往返、条件更新、bulk 原生路径（Pg COPY text / SqlServer nvarchar(max)）的类型归一。
    /// </summary>
    public abstract class JsonEnumTestsBase : RealDbTestBase
    {
        protected JsonEnumTestsBase(RealDbDialect dialect) : base(dialect)
        {
        }

        protected override Type[] EntityTypes => new[] { typeof(TypedItem) };

        [SkippableFact]
        public async Task EnumAsString_RoundTripsAsName()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<TypedItem>(DbAccess);
            var item = new TypedItem { Status = OrderStatus.Paused, Tags = new List<string> { "a" }, Score = 1 };
            await entitySet.InsertAsync(item);

            var found = await entitySet.FindAsync(item.Id);
            Assert.NotNull(found);
            Assert.Equal(OrderStatus.Paused, found!.Status);

            // 直接验证库中存储的是枚举名称字符串而非数值
            var raw = await DbAccess.ExecuteScalarAsync($"SELECT {Qi("Status")} FROM {Qi("it_typed_item")} WHERE {Qi("Id")} = {item.Id}");
            Assert.Equal("Paused", raw is string s ? s : raw.ToString());
        }

        [SkippableFact]
        public async Task JsonColumn_RoundTripsComplexValue()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<TypedItem>(DbAccess);
            var item = new TypedItem { Status = OrderStatus.Active, Tags = new List<string> { "red", "green", "blue" }, Score = 2 };
            await entitySet.InsertAsync(item);

            var found = await entitySet.FindAsync(item.Id);
            Assert.NotNull(found);
            Assert.Equal(new[] { "red", "green", "blue" }, found!.Tags);

            // 库中存储的是合法 JSON 文本
            var raw = (await DbAccess.ExecuteScalarAsync($"SELECT {Qi("Tags")} FROM {Qi("it_typed_item")} WHERE {Qi("Id")} = {item.Id}")).ToString();
            var parsed = JsonSerializer.Deserialize<List<string>>(raw);
            Assert.Equal(new[] { "red", "green", "blue" }, parsed);
        }

        [SkippableFact]
        public async Task ConditionalUpdate_SetsEnumAndJsonColumns()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<TypedItem>(DbAccess);
            var items = Enumerable.Range(1, 3).Select(i => new TypedItem
            {
                Status = OrderStatus.Active,
                Tags = new List<string> { $"t{i}" },
                Score = i
            }).ToList();
            await entitySet.InsertRangeAsync(items);

            var updated = await entitySet.UpdateAsync(
                x => new { Status = OrderStatus.Closed, Tags = new List<string> { "updated" } },
                x => x.Score >= 2);
            Assert.Equal(2, updated);

            var row = await entitySet.FindAsync(items[0].Id + 1);
            Assert.NotNull(row);
            Assert.Equal(OrderStatus.Closed, row!.Status);
            Assert.Equal(new[] { "updated" }, row.Tags);

            var unchanged = await entitySet.FindAsync(items[0].Id);
            Assert.Equal(OrderStatus.Active, unchanged!.Status);
        }

        [SkippableFact]
        public async Task BulkInsertAsync_WritesEnumAndJsonColumns()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<TypedItem>(DbAccess);
            var items = Enumerable.Range(1, 20).Select(i => new TypedItem
            {
                Status = i % 2 == 0 ? OrderStatus.Active : OrderStatus.Closed,
                Tags = new List<string> { $"bulk{i}" },
                Score = i
            }).ToList();

            var affected = await entitySet.BulkInsertAsync(items);
            Assert.Equal(20, affected);

            Assert.Equal(10, await entitySet.Where(x => x.Status == OrderStatus.Active).CountAsync());
            Assert.Equal(10, await entitySet.Where(x => x.Status == OrderStatus.Closed).CountAsync());

            // JSON 列不支持参与 WHERE 谓词（设计约束），以原生 SQL 抽查序列化文本已落库
            var jsonLike = await DbAccess.ExecuteScalarAsync(
                $"SELECT COUNT(*) FROM {Qi("it_typed_item")} WHERE {Qi("Tags")} LIKE '%bulk7%'");
            Assert.Equal(1L, Convert.ToInt64(jsonLike));
        }

        #region 测试实体

        /// <summary>测试用订单状态枚举。</summary>
        public enum OrderStatus
        {
            Active = 1,
            Paused = 2,
            Closed = 3
        }

        /// <summary>JSON/枚举测试实体（本测试类专属，表名 it_typed_item）。</summary>
        [Table("it_typed_item")]
        public class TypedItem
        {
            [PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [Column(EnumAsString = true)]
            public OrderStatus Status { get; set; }

            [Column(IsJson = true)]
            public List<string> Tags { get; set; }

            public int Score { get; set; }
        }

        #endregion
    }

    /// <summary>JSON/枚举回归的 PostgreSQL 真库用例。</summary>
    public class PgSqlJsonEnumTests : JsonEnumTestsBase
    {
        public PgSqlJsonEnumTests() : base(PgSql)
        {
        }
    }

    /// <summary>JSON/枚举回归的 SQL Server 真库用例。</summary>
    public class SqlServerJsonEnumTests : JsonEnumTestsBase
    {
        public SqlServerJsonEnumTests() : base(RealDbDialect.SqlServer)
        {
        }
    }

    /// <summary>JSON/枚举回归的 MySQL 真库用例。</summary>
    public class MySqlJsonEnumTests : JsonEnumTestsBase
    {
        public MySqlJsonEnumTests() : base(RealDbDialect.MySql)
        {
        }
    }

    /// <summary>JSON/枚举回归的 Oracle 真库用例。</summary>
    public class OracleJsonEnumTests : JsonEnumTestsBase
    {
        public OracleJsonEnumTests() : base(RealDbDialect.Oracle)
        {
        }
    }

    /// <summary>JSON/枚举回归的 SQLite 真库用例（本地文件数据库，恒可用）。</summary>
    public class SqliteJsonEnumTests : JsonEnumTestsBase
    {
        public SqliteJsonEnumTests() : base(RealDbDialect.Sqlite)
        {
        }
    }
}
