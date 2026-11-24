using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// EntitySet 条件 UPDATE/DELETE（谓词翻译路径）的 SQLite 真库集成测试：验证影响行数与落库结果。
    /// </summary>
    public class EntitySetConditionalTests : EntityTestBase
    {
        #region 测试实体

        [XbcDbEntity.Table("ConditionalUsers")]
        public class UserEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
            public int Age { get; set; }
            public bool IsActive { get; set; }
            public int? Score { get; set; }

            [XbcDbEntity.NotMapped]
            public string Note { get; set; }
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE ConditionalUsers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Age INTEGER NOT NULL,
                    IsActive INTEGER NOT NULL,
                    Score INTEGER NULL
                );
                INSERT INTO ConditionalUsers (Name, Age, IsActive, Score) VALUES ('Alice', 25, 1, 90);
                INSERT INTO ConditionalUsers (Name, Age, IsActive, Score) VALUES ('Bob', 30, 0, NULL);
                INSERT INTO ConditionalUsers (Name, Age, IsActive, Score) VALUES ('Carol', 35, 1, 60);
            ");
        }

        private Task<List<UserEntity>> QueryAllAsync()
        {
            return DbAccess.FromSql<UserEntity>("SELECT * FROM ConditionalUsers ORDER BY Id").ToListAsync();
        }

        #region 条件更新

        [Fact]
        public async Task Update_AnonymousAssignment_UpdatesOnlyMatchingRow()
        {
            var affectedRows = await DbAccess.Set<UserEntity>().UpdateAsync(
                u => new { Age = 26 },
                u => u.Name == "Alice");

            Assert.Equal(1, affectedRows);

            var rows = await QueryAllAsync();
            Assert.Equal(3, rows.Count);
            Assert.Equal(26, rows[0].Age);
            Assert.Equal("Alice", rows[0].Name);
            Assert.True(rows[0].IsActive);
            Assert.Equal(90, rows[0].Score);
            Assert.Equal(30, rows[1].Age);
            Assert.Equal(35, rows[2].Age);
        }

        [Fact]
        public async Task Update_MemberInitAssignment_UpdatesAllMatchingRows()
        {
            var affectedRows = await DbAccess.Set<UserEntity>().UpdateAsync(
                u => new UserEntity { Score = 70 },
                u => u.IsActive);

            Assert.Equal(2, affectedRows);

            var rows = await QueryAllAsync();
            Assert.Equal(70, rows[0].Score);
            Assert.Null(rows[1].Score);
            Assert.Equal(70, rows[2].Score);
        }

        [Fact]
        public async Task Update_NoMatch_ReturnsZero()
        {
            var affectedRows = await DbAccess.Set<UserEntity>().UpdateAsync(
                u => new { Age = 50 },
                u => u.Name == "Nobody");

            Assert.Equal(0, affectedRows);

            var rows = await QueryAllAsync();
            Assert.All(rows, r => Assert.NotEqual(50, r.Age));
        }

        [Fact]
        public async Task Update_ClosureAndLike_MatchesExactRows()
        {
            var keyword = "li";
            var minAge = 20;

            var affectedRows = await DbAccess.Set<UserEntity>().UpdateAsync(
                u => new { IsActive = false },
                u => u.Name.Contains(keyword) && u.Age >= minAge);

            Assert.Equal(1, affectedRows);

            var rows = await QueryAllAsync();
            Assert.False(rows[0].IsActive);
            Assert.False(rows[1].IsActive);
            Assert.True(rows[2].IsActive);
        }

        [Fact]
        public async Task Update_SetAndWhereShareColumnName_UsesDistinctParameters()
        {
            var affectedRows = await DbAccess.Set<UserEntity>().UpdateAsync(
                u => new { Age = 60 },
                u => u.Age < 30);

            Assert.Equal(1, affectedRows);

            var rows = await QueryAllAsync();
            Assert.Equal(60, rows[0].Age);
            Assert.Equal(30, rows[1].Age);
            Assert.Equal(35, rows[2].Age);
        }

        [Fact]
        public async Task Update_NullAssignment_SetsColumnNull()
        {
            var affectedRows = await DbAccess.Set<UserEntity>().UpdateAsync(
                u => new { Score = (int?)null },
                u => u.Name == "Alice");

            Assert.Equal(1, affectedRows);

            var rows = await QueryAllAsync();
            Assert.Null(rows[0].Score);
            Assert.Null(rows[1].Score);
            Assert.Equal(60, rows[2].Score);
        }

        [Fact]
        public async Task Update_IdentityAssignment_Throws()
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => DbAccess.Set<UserEntity>().UpdateAsync(
                u => new { Id = 100 },
                u => u.Name == "Alice"));
        }

        [Fact]
        public async Task Update_UnmappedMemberAssignment_Throws()
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => DbAccess.Set<UserEntity>().UpdateAsync(
                u => new { Note = "x" },
                u => u.Name == "Alice"));
        }

        [Fact]
        public async Task Update_AssignmentReferencingEntityProperty_Throws()
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => DbAccess.Set<UserEntity>().UpdateAsync(
                u => new { Name = u.Name },
                u => u.Name == "Alice"));
        }

        [Fact]
        public async Task Update_NullPredicate_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => DbAccess.Set<UserEntity>().UpdateAsync(
                u => new { Age = 26 },
                null!));
        }

        #endregion

        #region 条件删除

        [Fact]
        public async Task Delete_ByCondition_DeletesMatchingRows()
        {
            var affectedRows = await DbAccess.Set<UserEntity>().DeleteAsync(u => u.Age >= 30);

            Assert.Equal(2, affectedRows);

            var rows = await QueryAllAsync();
            var row = Assert.Single(rows);
            Assert.Equal("Alice", row.Name);
        }

        [Fact]
        public async Task Delete_NoMatch_ReturnsZero()
        {
            var affectedRows = await DbAccess.Set<UserEntity>().DeleteAsync(u => u.Age > 100);

            Assert.Equal(0, affectedRows);
            Assert.Equal(3, (await QueryAllAsync()).Count);
        }

        [Fact]
        public async Task Delete_WithCollectionContains_DeletesMatchingRows()
        {
            var ids = new List<int> { 1, 3 };

            var affectedRows = await DbAccess.Set<UserEntity>().DeleteAsync(u => ids.Contains(u.Id));

            Assert.Equal(2, affectedRows);

            var rows = await QueryAllAsync();
            var row = Assert.Single(rows);
            Assert.Equal("Bob", row.Name);
        }

        [Fact]
        public async Task Delete_WithNullComparison_DeletesNullRows()
        {
            var affectedRows = await DbAccess.Set<UserEntity>().DeleteAsync(u => u.Score == null);

            Assert.Equal(1, affectedRows);

            var rows = await QueryAllAsync();
            Assert.DoesNotContain(rows, r => r.Score == null);
        }

        [Fact]
        public async Task Delete_WithLogicalNot_DeletesNonActiveRows()
        {
            var affectedRows = await DbAccess.Set<UserEntity>().DeleteAsync(u => !u.IsActive);

            Assert.Equal(1, affectedRows);

            var rows = await QueryAllAsync();
            Assert.Equal(2, rows.Count);
            Assert.DoesNotContain(rows, r => r.Name == "Bob");
        }

        [Fact]
        public async Task Delete_NullPredicate_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => DbAccess.Set<UserEntity>().DeleteAsync(
                (System.Linq.Expressions.Expression<Func<UserEntity, bool>>)null!));
        }

        #endregion
    }
}
