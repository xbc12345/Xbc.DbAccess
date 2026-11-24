using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 三键 keyset 分页的 SQLite 真库测试（Stryker NoCoverage 补测）：
    /// 多键键集条件在派生表上展开为 OR 链，第三个键（i=2）的内层等值链含 ≥2 个前序键，
    /// 触达复合键条件拼接的「 AND 」分支；翻页遍历验证不丢行不重行与向前翻页的正向输出。
    /// </summary>
    public class EntityQueryKeysetMultiKeyTests : EntityTestBase
    {
        #region 测试实体

        /// <summary>三键 keyset 载体：键集为 (Score DESC, Id ASC, Tag ASC)，Id 唯一保证全序确定。</summary>
        [XbcDbEntity.Table("KeysetMultiItems")]
        public class MultiKeyEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public int Score { get; set; }

            public string Tag { get; set; } = string.Empty;
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE KeysetMultiItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Score INTEGER NOT NULL,
                    Tag TEXT NOT NULL
                );
                INSERT INTO KeysetMultiItems (Score, Tag) VALUES (30, 'a');
                INSERT INTO KeysetMultiItems (Score, Tag) VALUES (10, 'b');
                INSERT INTO KeysetMultiItems (Score, Tag) VALUES (30, 'c');
                INSERT INTO KeysetMultiItems (Score, Tag) VALUES (20, 'd');
                INSERT INTO KeysetMultiItems (Score, Tag) VALUES (30, 'e');
                INSERT INTO KeysetMultiItems (Score, Tag) VALUES (10, 'f');
                INSERT INTO KeysetMultiItems (Score, Tag) VALUES (20, 'g');
                INSERT INTO KeysetMultiItems (Score, Tag) VALUES (10, 'h');
                INSERT INTO KeysetMultiItems (Score, Tag) VALUES (20, 'i');
                INSERT INTO KeysetMultiItems (Score, Tag) VALUES (10, 'j');
            ");
        }

        /// <summary>键集（Score DESC, Id ASC, Tag ASC）的期望全序：Score 分组降序，组内 Id 升序。</summary>
        private static readonly int[] ExpectedKeysetOrder = { 1, 3, 5, 4, 7, 9, 2, 6, 8, 10 };

        /// <summary>三键排序链（Score DESC, Id ASC, Tag ASC）。</summary>
        private IEntityQuery<MultiKeyEntity> MultiKeyQuery()
        {
            return DbAccess.Set<MultiKeyEntity>().AsQuery()
                .OrderByDescending(u => u.Score)
                .ThenBy(u => u.Id)
                .ThenBy(u => u.Tag);
        }

        [Fact]
        public async Task After_ThreeKeys_WalksAllPages_NoLossNoDuplicate()
        {
            // 三键键集向后翻页：逐页推进游标应完整遍历全序，无丢失无重复
            var visited = new List<int>();
            MultiKeyEntity cursor = null;

            while (true)
            {
                var page = await MultiKeyQuery().KeysetAfterAsync(cursor, 3);
                if (page.Count == 0)
                    break;

                visited.AddRange(page.Select(u => u.Id));
                cursor = page[page.Count - 1];
            }

            Assert.Equal(ExpectedKeysetOrder, visited.ToArray());
        }

        [Fact]
        public async Task After_ThreeKeys_MidCursor_ReturnsNextPage()
        {
            // 游标为全序位置 2 的行（Id=5, Score=30, Tag='e'），下一页 3 行为位置 3,4,5 = 4,7,9
            var cursor = new MultiKeyEntity { Id = 5, Score = 30, Tag = "e" };

            var page = await MultiKeyQuery().KeysetAfterAsync(cursor, 3);

            Assert.Equal(new[] { 4, 7, 9 }, page.Select(u => u.Id).ToArray());
        }

        [Fact]
        public async Task Before_ThreeKeys_MidCursor_ReturnsPrecedingPageInForwardOrder()
        {
            // 游标为全序位置 3 的行（Id=4, Score=20, Tag='d'），紧邻之前的 3 行为位置 2,1,0 = 5,3,1，正向输出
            var cursor = new MultiKeyEntity { Id = 4, Score = 20, Tag = "d" };

            var page = await MultiKeyQuery().KeysetBeforeAsync(cursor, 3);

            Assert.Equal(new[] { 1, 3, 5 }, page.Select(u => u.Id).ToArray());
        }
    }
}
