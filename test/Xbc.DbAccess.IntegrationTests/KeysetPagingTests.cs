using System.Text.Json;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.IntegrationTests.Infrastructure;
using Xunit;
using static Xbc.DbAccess.IntegrationTests.Infrastructure.RealDbDialect;

namespace Xbc.DbAccess.IntegrationTests
{
    /// <summary>
    /// 查询链 keyset 分页（Query()/Where() + OrderBy + KeysetAfterAsync/KeysetBeforeAsync）的真库回归：
    /// 游标条件（OR 链展开）、Before 翻转、谓词组合在 PostgreSQL 与 SQL Server 上行为一致。
    /// </summary>
    public abstract class KeysetPagingTestsBase : RealDbTestBase
    {
        protected KeysetPagingTestsBase(RealDbDialect dialect) : base(dialect)
        {
        }

        protected override Type[] EntityTypes => new[] { typeof(FeedItem) };

        private async Task<List<FeedItem>> SeedAsync(EntitySet<FeedItem> entitySet)
        {
            var items = Enumerable.Range(1, 10).Select(i => new FeedItem
            {
                Seq = i,
                Title = $"Item{i:00}",
                PublishedAt = new DateTime(2026, 6, 1, 0, 0, 0).AddHours(i)
            }).ToList();
            await entitySet.InsertRangeAsync(items);
            return items;
        }

        [SkippableFact]
        public async Task KeysetAfterAsync_PaginatesForwardInKeyOrder()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<FeedItem>(DbAccess);
            await SeedAsync(entitySet);

            var query = entitySet.AsQuery().OrderBy(x => x.Seq);

            var page1 = await query.KeysetAfterAsync(null, 4);
            Assert.Equal(new long[] { 1, 2, 3, 4 }, page1.Select(x => x.Seq));

            var page2 = await query.KeysetAfterAsync(page1[^1], 4);
            Assert.Equal(new long[] { 5, 6, 7, 8 }, page2.Select(x => x.Seq));

            var page3 = await query.KeysetAfterAsync(page2[^1], 4);
            Assert.Equal(new long[] { 9, 10 }, page3.Select(x => x.Seq));

            var page4 = await query.KeysetAfterAsync(page3[^1], 4);
            Assert.Empty(page4);
        }


        [SkippableFact]
        public async Task KeysetBeforeAsync_PaginatesBackward()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<FeedItem>(DbAccess);
            await SeedAsync(entitySet);

            var query = entitySet.AsQuery().OrderBy(x => x.Seq);

            var page3 = await entitySet.Where(x => x.Seq > 8).OrderBy(x => x.Seq).KeysetAfterAsync(null, 4);
            Assert.Equal(new long[] { 9, 10 }, page3.Select(x => x.Seq));

            // Before 以游标为界向前取页，结果按键序返回（内部翻转后内存反转）
            var page2 = await query.KeysetBeforeAsync(page3[0], 4);
            Assert.Equal(new long[] { 5, 6, 7, 8 }, page2.Select(x => x.Seq));

            var page1 = await query.KeysetBeforeAsync(page2[0], 4);
            Assert.Equal(new long[] { 1, 2, 3, 4 }, page1.Select(x => x.Seq));

            var page0 = await query.KeysetBeforeAsync(page1[0], 4);
            Assert.Empty(page0);
        }

        [SkippableFact]
        public async Task KeysetAfterAsync_CompositeKeyWithMixedOrder()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<FeedItem>(DbAccess);
            await SeedAsync(entitySet);

            // 双键混合方向：PublishedAt 降序 + Seq 升序
            var query = entitySet.AsQuery()
                .OrderByDescending(x => x.PublishedAt)
                .ThenBy(x => x.Seq);

            var page1 = await query.KeysetAfterAsync(null, 7);
            Assert.Equal(new long[] { 10, 9, 8, 7, 6, 5, 4 }, page1.Select(x => x.Seq));

            var page2 = await query.KeysetAfterAsync(page1[^1], 7);
            Assert.Equal(new long[] { 3, 2, 1 }, page2.Select(x => x.Seq));
        }

        [SkippableFact]
        public async Task KeysetAfterAsync_WithPredicate()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<FeedItem>(DbAccess);
            await SeedAsync(entitySet);

            var query = entitySet.Where(x => x.Seq > 2).OrderBy(x => x.Seq);

            var page1 = await query.KeysetAfterAsync(null, 3);
            Assert.Equal(new long[] { 3, 4, 5 }, page1.Select(x => x.Seq));

            var page2 = await query.KeysetAfterAsync(page1[^1], 3);
            Assert.Equal(new long[] { 6, 7, 8 }, page2.Select(x => x.Seq));
        }

        #region 测试实体

        /// <summary>Keyset 分页测试实体（本测试类专属，表名 it_feed_item）。</summary>
        [Table("it_feed_item")]
        public class FeedItem
        {
            [PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public long Seq { get; set; }

            public string Title { get; set; }

            public DateTime PublishedAt { get; set; }
        }

        #endregion
    }

    /// <summary>Keyset 分页回归的 PostgreSQL 真库用例。</summary>
    public class PgSqlKeysetPagingTests : KeysetPagingTestsBase
    {
        public PgSqlKeysetPagingTests() : base(PgSql)
        {
        }
    }

    /// <summary>Keyset 分页回归的 SQL Server 真库用例。</summary>
    public class SqlServerKeysetPagingTests : KeysetPagingTestsBase
    {
        public SqlServerKeysetPagingTests() : base(RealDbDialect.SqlServer)
        {
        }
    }

    /// <summary>Keyset 分页回归的 MySQL 真库用例。</summary>
    public class MySqlKeysetPagingTests : KeysetPagingTestsBase
    {
        public MySqlKeysetPagingTests() : base(RealDbDialect.MySql)
        {
        }
    }

    /// <summary>Keyset 分页回归的 Oracle 真库用例。</summary>
    public class OracleKeysetPagingTests : KeysetPagingTestsBase
    {
        public OracleKeysetPagingTests() : base(RealDbDialect.Oracle)
        {
        }
    }

    /// <summary>Keyset 分页回归的 SQLite 真库用例（本地文件数据库，恒可用）。</summary>
    public class SqliteKeysetPagingTests : KeysetPagingTestsBase
    {
        public SqliteKeysetPagingTests() : base(RealDbDialect.Sqlite)
        {
        }
    }
}
