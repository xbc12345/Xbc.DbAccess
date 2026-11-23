using System;
using System.Linq;
using System.Threading.Tasks;
using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 查询链 keyset 分页（Query().OrderBy(...).KeysetAfterAsync/KeysetBeforeAsync）的 SQLite 真库测试：
    /// 验证混合方向键集的翻页遍历（不丢行不重行）、向前翻页的正向输出、谓词组合、与 Skip/Take 的互斥校验及键列守卫。
    /// </summary>
    public class EntityQueryKeysetTests : EntityTestBase
    {
        #region 测试实体

        [XbcDbEntity.Table("KeysetUsers")]
        public class UserEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;

            public int Score { get; set; }

            public string Tag { get; set; }

            // 带自定义转换器的列：keyset 键集列守卫的测试载体（存储值与属性值不同构）
            [XbcDbEntity.Column(ConverterType = typeof(KeysetTokenConverter))]
            public string Token { get; set; }

            // 枚举名称字符串列：存储值序为名称字典序而非枚举数值序，键集列守卫的测试载体
            [XbcDbEntity.Column(EnumAsString = true)]
            public KeysetUserStatus Status { get; set; }

            [XbcDbEntity.NotMapped]
            public string Note { get; set; }
        }

        /// <summary>
        /// Status 列的枚举类型：名称字典序（Nine &lt; One）与数值序（One(1) &lt; Nine(9)）刻意相反，验证键集列拒绝依据。
        /// </summary>
        public enum KeysetUserStatus
        {
            One = 1,
            Nine = 9
        }

        /// <summary>
        /// Token 列的模拟转换器：属性值加前缀后存储（模拟加密等不同构存储）。
        /// </summary>
        private sealed class KeysetTokenConverter : Xbc.DbAccess.Entity.IDbValueConverter<string>
        {
            public string ConvertFrom(object dbValue) => dbValue?.ToString() ?? string.Empty;
            public object ConvertTo(string value) => value == null ? null! : "enc:" + value;
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE KeysetUsers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Score INTEGER NOT NULL,
                    Tag TEXT NULL,
                    Token TEXT NULL,
                    Status TEXT NOT NULL
                );
                INSERT INTO KeysetUsers (Name, Score, Tag, Status) VALUES ('Alice', 30, 't1', 'One');
                INSERT INTO KeysetUsers (Name, Score, Tag, Status) VALUES ('Bob', 10, 't2', 'Nine');
                INSERT INTO KeysetUsers (Name, Score, Tag, Status) VALUES ('Cara', 30, 't3', 'Nine');
                INSERT INTO KeysetUsers (Name, Score, Tag, Status) VALUES ('Dave', 20, 't4', 'One');
                INSERT INTO KeysetUsers (Name, Score, Tag, Status) VALUES ('Eve', 30, 't5', 'Nine');
                INSERT INTO KeysetUsers (Name, Score, Tag, Status) VALUES ('Frank', 10, 't6', 'One');
                INSERT INTO KeysetUsers (Name, Score, Tag, Status) VALUES ('Grace', 20, 't7', 'One');
                INSERT INTO KeysetUsers (Name, Score, Tag, Status) VALUES ('Heidi', 10, 't8', 'Nine');
                INSERT INTO KeysetUsers (Name, Score, Tag, Status) VALUES ('Ivan', 20, 't9', 'One');
                INSERT INTO KeysetUsers (Name, Score, Tag, Status) VALUES ('Judy', 10, 't10', 'One');
            ");
        }

        /// <summary>
        /// 混合方向键集（Score DESC, Id ASC）的期望全序：Score 分组降序，组内 Id 升序。
        /// </summary>
        private static readonly int[] ExpectedMixedOrder = { 1, 3, 5, 4, 7, 9, 2, 6, 8, 10 };

        /// <summary>
        /// 混合方向键集查询链（Score DESC, Id ASC）。
        /// </summary>
        private IEntityQuery<UserEntity> MixedQuery()
        {
            return DbAccess.Set<UserEntity>().AsQuery()
                .OrderByDescending(u => u.Score)
                .ThenBy(u => u.Id);
        }

        /// <summary>
        /// 带谓词的混合方向键集查询链：同时验证键参数与谓词参数（同名列 Score）不撞名。
        /// </summary>
        private IEntityQuery<UserEntity> FilteredMixedQuery()
        {
            return DbAccess.Set<UserEntity>().Where(u => u.Score >= 20)
                .OrderByDescending(u => u.Score)
                .ThenBy(u => u.Id);
        }

        #region After（向后翻页）

        [Fact]
        public async Task After_NullCursor_ReturnsFirstPageInKeysetOrder()
        {
            var page = await MixedQuery().KeysetAfterAsync(null, 3);

            Assert.Equal(new[] { 1, 3, 5 }, page.Select(u => u.Id).ToArray());
        }

        [Fact]
        public async Task After_WalksAllPages_NoLossNoDuplicate()
        {
            var query = MixedQuery();
            var visited = new System.Collections.Generic.List<int>();
            UserEntity cursor = null;

            while (true)
            {
                var page = await query.KeysetAfterAsync(cursor, 4);
                if (page.Count == 0)
                    break;

                visited.AddRange(page.Select(u => u.Id));
                cursor = page[page.Count - 1];
            }

            Assert.Equal(ExpectedMixedOrder, visited.ToArray());
        }

        [Fact]
        public async Task After_SingleDescendingKey_WalksAllRows()
        {
            var query = DbAccess.Set<UserEntity>().AsQuery().OrderByDescending(u => u.Id);
            var visited = new System.Collections.Generic.List<int>();
            UserEntity cursor = null;

            while (true)
            {
                var page = await query.KeysetAfterAsync(cursor, 3);
                if (page.Count == 0)
                    break;

                visited.AddRange(page.Select(u => u.Id));
                cursor = page[page.Count - 1];
            }

            Assert.Equal(new[] { 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, visited.ToArray());
        }

        [Fact]
        public async Task After_LastRowCursor_ReturnsEmpty()
        {
            var cursor = new UserEntity { Id = 10, Score = 10 };

            var page = await MixedQuery().KeysetAfterAsync(cursor, 3);

            Assert.Empty(page);
        }

        [Fact]
        public async Task After_WithPredicate_WalksFilteredPages()
        {
            var query = FilteredMixedQuery();
            var visited = new System.Collections.Generic.List<int>();
            UserEntity cursor = null;

            while (true)
            {
                var page = await query.KeysetAfterAsync(cursor, 2);
                if (page.Count == 0)
                    break;

                visited.AddRange(page.Select(u => u.Id));
                cursor = page[page.Count - 1];
            }

            // 过滤后全序：Score >= 20 的行按 (Score DESC, Id ASC) 为 1,3,5,4,7,9
            Assert.Equal(new[] { 1, 3, 5, 4, 7, 9 }, visited.ToArray());
        }

        #endregion

        #region Before（向前翻页）

        [Fact]
        public async Task Before_MidCursor_ReturnsPrecedingPageInForwardOrder()
        {
            // 游标 Id=4（全序位置 3），紧邻之前的 3 行为位置 2,1,0 = 5,3,1，正向输出 1,3,5
            var cursor = new UserEntity { Id = 4, Score = 20 };

            var page = await MixedQuery().KeysetBeforeAsync(cursor, 3);

            Assert.Equal(new[] { 1, 3, 5 }, page.Select(u => u.Id).ToArray());
        }

        [Fact]
        public async Task Before_PageSizeExceedsPrecedingRows_ReturnsAllPreceding()
        {
            var cursor = new UserEntity { Id = 4, Score = 20 };

            var page = await MixedQuery().KeysetBeforeAsync(cursor, 10);

            Assert.Equal(new[] { 1, 3, 5 }, page.Select(u => u.Id).ToArray());
        }

        [Fact]
        public async Task Before_CursorDeeperInOrder_ReturnsImmediatelyPrecedingPage()
        {
            // 游标 Id=2（全序位置 6），紧邻之前的 4 行为位置 5,4,3,2 = 9,7,4,5，正向输出 5,4,7,9
            var cursor = new UserEntity { Id = 2, Score = 10 };

            var page = await MixedQuery().KeysetBeforeAsync(cursor, 4);

            Assert.Equal(new[] { 5, 4, 7, 9 }, page.Select(u => u.Id).ToArray());
        }

        [Fact]
        public async Task Before_FirstRowCursor_ReturnsEmpty()
        {
            var cursor = new UserEntity { Id = 1, Score = 30 };

            var page = await MixedQuery().KeysetBeforeAsync(cursor, 3);

            Assert.Empty(page);
        }

        [Fact]
        public async Task Before_WithPredicate_ReturnsFilteredPrecedingPage()
        {
            // 过滤 Score >= 20 后全序为 1,3,5,4,7,9；游标 Id=4（位置 3），紧邻之前的 2 行为位置 2,1 = 5,3，正向输出 3,5
            var cursor = new UserEntity { Id = 4, Score = 20 };

            var page = await FilteredMixedQuery().KeysetBeforeAsync(cursor, 2);

            Assert.Equal(new[] { 3, 5 }, page.Select(u => u.Id).ToArray());
        }

        [Fact]
        public async Task Before_WalksAllPagesBackward_NoLossNoDuplicate()
        {
            // 从末行游标连续向前翻页：回翻各页前插拼接应与全序完全一致（多页边界组合下的翻转序正确性）
            var visited = new System.Collections.Generic.List<int>();
            var cursor = new UserEntity { Id = 10, Score = 10 };

            while (true)
            {
                var page = await MixedQuery().KeysetBeforeAsync(cursor, 4);
                if (page.Count == 0)
                    break;

                // 每页正向输出，回翻时前插：较早的页位于序列前部
                visited.InsertRange(0, page.Select(u => u.Id).ToArray());
                cursor = page[0];
            }

            // 从末行游标连续向前翻页：回翻各页前插拼接应与全序（除起点末行自身）完全一致——
            // 向前翻页只取游标之前的行，起点行不在遍历结果中（与 After 全量遍历互为补集）
            Assert.Equal(ExpectedMixedOrder.Take(ExpectedMixedOrder.Length - 1), visited);
        }

        #endregion

        #region 校验与守卫

        [Fact]
        public async Task Before_NullCursor_Throws()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => MixedQuery().KeysetBeforeAsync(null, 3));
        }

        [Fact]
        public async Task After_NoOrdering_Throws()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => DbAccess.Set<UserEntity>().AsQuery().KeysetAfterAsync(null, 3));
        }

        [Fact]
        public async Task After_WithSkipTake_Throws()
        {
            var query = DbAccess.Set<UserEntity>().AsQuery()
                .OrderByDescending(u => u.Score)
                .ThenBy(u => u.Id)
                .Skip(1)
                .Take(3);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => query.KeysetAfterAsync(null, 3));
        }

        [Fact]
        public async Task Before_WithTake_Throws()
        {
            var query = DbAccess.Set<UserEntity>().AsQuery()
                .OrderByDescending(u => u.Score)
                .ThenBy(u => u.Id)
                .Take(3);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => query.KeysetBeforeAsync(new UserEntity { Id = 4, Score = 20 }, 3));
        }

        [Fact]
        public void OrderBy_ConverterColumn_Throws()
        {
            // 转换器列的存储值序与属性值序可能不一致，排序键解析端即拒绝（keyset 键集随之同样不可用）
            Assert.Throws<NotSupportedException>(
                () => DbAccess.Set<UserEntity>().AsQuery().OrderBy(u => u.Token));
        }

        [Fact]
        public async Task After_EnumAsStringColumnKey_PaginatesByStoredNameOrder()
        {
            // 枚举名称字符串列的行序为名称字典序（Nine < One）而非枚举数值序（One < Nine），本枚举刻意令两序相反；
            // 游标参数经 GetValueForDb 与存储值同域，行序与游标比较序一致（名称字典序），翻页自洽无重复无丢失
            var firstPage = await DbAccess.Set<UserEntity>().AsQuery().OrderBy(u => u.Status).KeysetAfterAsync(null, 4);

            Assert.Equal(4, firstPage.Count);
            Assert.All(firstPage, u => Assert.Equal(KeysetUserStatus.Nine, u.Status));

            var cursor = firstPage[^1];
            var secondPage = await DbAccess.Set<UserEntity>().AsQuery().OrderBy(u => u.Status).KeysetAfterAsync(cursor, 4);

            Assert.Equal(4, secondPage.Count);
            Assert.All(secondPage, u => Assert.Equal(KeysetUserStatus.One, u.Status));

            var firstIds = firstPage.Select(u => u.Id).ToHashSet();
            Assert.All(secondPage, u => Assert.False(firstIds.Contains(u.Id)));
        }

        [Fact]
        public async Task After_NonPositivePageSize_Throws()
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => MixedQuery().KeysetAfterAsync(null, 0));
        }

        [Fact]
        public async Task After_NullCursorKeyValue_Throws()
        {
            var cursor = new UserEntity { Id = 1, Tag = null };
            var query = DbAccess.Set<UserEntity>().AsQuery().OrderBy(u => u.Tag);

            await Assert.ThrowsAsync<ArgumentException>(
                () => query.KeysetAfterAsync(cursor, 3));
        }

        #endregion
    }
}
