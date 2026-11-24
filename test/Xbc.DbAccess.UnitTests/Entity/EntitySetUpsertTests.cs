using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// EntitySet Upsert（存在则更新，不存在则插入）的 SQLite 真库集成测试：
    /// 验证自增主键的插入回填、已存在主键的更新路径、无自增主键的两路径与校验异常。
    /// </summary>
    public class EntitySetUpsertTests : EntityTestBase
    {
        #region 测试实体

        [XbcDbEntity.Table("UpsertUsers")]
        public class UserEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
            public int Age { get; set; }
        }

        [XbcDbEntity.Table("UpsertExplicitPk")]
        public class ExplicitPkEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
        }

        [XbcDbEntity.Table("UpsertNoPk")]
        public class NoPkEntity
        {
            public string Name { get; set; } = string.Empty;
        }

        [XbcDbEntity.Table("UpsertOnlyPk")]
        public class OnlyPkEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE UpsertUsers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Age INTEGER NOT NULL
                );

                CREATE TABLE UpsertExplicitPk (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL
                );

                CREATE TABLE UpsertNoPk (
                    Name TEXT NOT NULL
                );

                CREATE TABLE UpsertOnlyPk (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT
                );
            ");
        }

        #region 自增主键

        [Fact]
        public async Task Upsert_IdentityDefault_InsertsAndBackfillsId()
        {
            var entity = new UserEntity { Name = "Alice", Age = 20 };

            var affectedRows = await DbAccess.Set<UserEntity>().UpsertAsync(entity);

            Assert.Equal(1, affectedRows);
            Assert.True(entity.Id > 0);// 自增 ID 已回填

            var row = await DbAccess.FromSql<UserEntity>("SELECT * FROM UpsertUsers WHERE Id = @id", new { id = entity.Id })
                .FirstAsync();
            Assert.Equal("Alice", row.Name);
            Assert.Equal(20, row.Age);
        }

        [Fact]
        public async Task Upsert_ExistingPrimaryKey_UpdatesOtherColumns()
        {
            var inserted = new UserEntity { Name = "Alice", Age = 20 };
            await DbAccess.Set<UserEntity>().InsertAsync(inserted);

            // 自增列已有值：走更新路径，其余列全列覆盖
            var updated = new UserEntity { Id = inserted.Id, Name = "Alice2", Age = 30 };
            var affectedRows = await DbAccess.Set<UserEntity>().UpsertAsync(updated);

            Assert.Equal(1, affectedRows);
            Assert.Equal(inserted.Id, updated.Id);

            var rows = await DbAccess.FromSql<UserEntity>("SELECT * FROM UpsertUsers").ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal("Alice2", row.Name);
            Assert.Equal(30, row.Age);
        }

        [Fact]
        public async Task Upsert_ExplicitIdentityValue_InsertsNewRow()
        {
            // 自增列带显式值且不冲突：走插入路径且无需回填
            var entity = new UserEntity { Id = 42, Name = "Bob", Age = 25 };

            var affectedRows = await DbAccess.Set<UserEntity>().UpsertAsync(entity);

            Assert.Equal(1, affectedRows);
            var row = await DbAccess.FromSql<UserEntity>("SELECT * FROM UpsertUsers WHERE Id = @id", new { id = 42 })
                .FirstAsync();
            Assert.Equal("Bob", row.Name);
            Assert.Equal(25, row.Age);
        }

        [Fact]
        public async Task Upsert_IdentityDefault_Twice_InsertsTwoRows()
        {
            // 自增主键为默认值时恒走插入路径（不与已有行冲突），两次 Upsert 产生两行
            await DbAccess.Set<UserEntity>().UpsertAsync(new UserEntity { Name = "A", Age = 1 });
            await DbAccess.Set<UserEntity>().UpsertAsync(new UserEntity { Name = "B", Age = 2 });

            var rows = await DbAccess.FromSql<UserEntity>("SELECT * FROM UpsertUsers ORDER BY Id").ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.Equal("A", rows[0].Name);
            Assert.Equal("B", rows[1].Name);
        }

        #endregion

        #region 无自增主键

        [Fact]
        public async Task Upsert_NonIdentityPrimaryKey_InsertAndUpdatePaths()
        {
            var set = DbAccess.Set<ExplicitPkEntity>();

            // 新主键：插入路径
            await set.UpsertAsync(new ExplicitPkEntity { Id = 1, Name = "A" });

            // 已存在主键：更新路径
            await set.UpsertAsync(new ExplicitPkEntity { Id = 1, Name = "B" });

            var rows = await DbAccess.FromSql<ExplicitPkEntity>("SELECT * FROM UpsertExplicitPk").ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal(1, row.Id);
            Assert.Equal("B", row.Name);
        }

        #endregion

        #region 校验异常

        [Fact]
        public async Task Upsert_NoPrimaryKey_ThrowsNotSupportedException()
        {
            await Assert.ThrowsAsync<NotSupportedException>(
                () => DbAccess.Set<NoPkEntity>().UpsertAsync(new NoPkEntity { Name = "x" }));
        }

        [Fact]
        public async Task Upsert_OnlyIdentityPrimaryKey_ThrowsNotSupportedException()
        {
            // 除自增主键外没有其他列：无可插入/可更新的列，显式失败优于生成非法 SQL
            await Assert.ThrowsAsync<NotSupportedException>(
                () => DbAccess.Set<OnlyPkEntity>().UpsertAsync(new OnlyPkEntity()));
        }

        #endregion
    }
}
