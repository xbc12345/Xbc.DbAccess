using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.UnitTests.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity.Set
{
    /// <summary>
    /// EntitySet 更新/删除边缘路径（EntitySet.Update.cs / EntitySet.Delete.cs）的 SQLite 真库补测：
    /// 批量更新的单实体路由、条件更新的乐观锁令牌换发、无可更新列守卫、令牌实体的单实体批量删除路由，
    /// 以及无令牌实体参数超限的 IN 分批删除（方言变体缩小参数上限）。
    /// </summary>
    public class EntitySetUpdateDeleteEdgeTests : EntityTestBase
    {
        #region 测试实体

        [XbcDbEntity.Table("UpdateRangeDocs")]
        public class UpdateRangeDoc
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
        }

        [XbcDbEntity.Table("UpdateOnlyPkDocs")]
        public class UpdateOnlyPkDoc
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }
        }

        [XbcDbEntity.Table("UpdateTokenDocs")]
        public class TokenDoc
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
            public int Age { get; set; }

            [XbcDbEntity.Column(IsConcurrencyToken = true)]
            public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        }

        [XbcDbEntity.Table("DeleteTokenDocs")]
        public class DeleteTokenDoc
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;

            [XbcDbEntity.Column(IsConcurrencyToken = true)]
            public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE UpdateRangeDocs (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL
                );

                CREATE TABLE UpdateOnlyPkDocs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT
                );

                CREATE TABLE UpdateTokenDocs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Age INTEGER NOT NULL,
                    RowVersion BLOB NOT NULL
                );

                CREATE TABLE DeleteTokenDocs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    RowVersion BLOB NOT NULL
                );
            ");
        }

        [Fact]
        public async Task UpdateRange_SingleEntity_UpdatesWithoutTransaction()
        {
            var set = DbAccess.Set<UpdateRangeDoc>();
            await set.InsertAsync(new UpdateRangeDoc { Id = 1, Name = "before" });

            // 单实体路由：不开事务直接按主键更新
            var affectedRows = await set.UpdateRangeAsync(new List<UpdateRangeDoc> { new UpdateRangeDoc { Id = 1, Name = "after" } });

            Assert.Equal(1, affectedRows);
            var rows = await DbAccess.FromSql<UpdateRangeDoc>("SELECT * FROM UpdateRangeDocs").ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal("after", row.Name);
        }

        [Fact]
        public async Task Update_NoUpdatableColumns_Throws()
        {
            var set = DbAccess.Set<UpdateOnlyPkDoc>();

            // 除主键与自增列外没有其他列：无法构建 UPDATE 语句，显式失败（无需预插入数据，语句构建先于执行）
            await Assert.ThrowsAsync<NotSupportedException>(
                () => set.UpdateAsync(new UpdateOnlyPkDoc { Id = 999 }));
        }

        [Fact]
        public async Task Update_ConditionalAssignment_RenewsConcurrencyTokenInSet()
        {
            var set = DbAccess.Set<TokenDoc>();
            await set.InsertAsync(new TokenDoc { Name = "A1", Age = 10 });
            await set.InsertAsync(new TokenDoc { Name = "A2", Age = 10 });
            var before = await DbAccess.FromSql<TokenDoc>("SELECT * FROM UpdateTokenDocs ORDER BY Id").ToListAsync();

            // 条件更新配置了令牌列：SET 中换发新随机令牌（旧令牌失效），无实体引用不回填
            var affectedRows = await set.UpdateAsync(
                u => new { Age = 99 },
                u => u.Age == 10);

            Assert.Equal(2, affectedRows);

            var after = await DbAccess.FromSql<TokenDoc>("SELECT * FROM UpdateTokenDocs ORDER BY Id").ToListAsync();
            Assert.Equal(2, after.Count);
            Assert.All(after, r => Assert.Equal(99, r.Age));
            Assert.All(after, r => Assert.Equal(16, r.RowVersion.Length));
            // 令牌已在数据库侧换发，持有旧令牌的实体后续更新检出并发冲突
            Assert.NotEqual(before[0].RowVersion, after[0].RowVersion);
            Assert.NotEqual(before[1].RowVersion, after[1].RowVersion);

            var stale = new TokenDoc { Id = after[0].Id, Name = "Stale", Age = 1, RowVersion = before[0].RowVersion };
            await Assert.ThrowsAsync<DbConcurrencyException>(() => set.UpdateAsync(stale));
        }

        [Fact]
        public async Task DeleteRange_TokenEntity_SingleEntity_DeletesDirectly()
        {
            var set = DbAccess.Set<DeleteTokenDoc>();
            var entity = new DeleteTokenDoc { Name = "d1" };
            await set.InsertAsync(entity);
            var fresh = Assert.Single(await DbAccess.FromSql<DeleteTokenDoc>("SELECT * FROM DeleteTokenDocs").ToListAsync());

            // 配置令牌列 + 单实体：一条 DELETE 天然原子，不开事务直接删除
            var affectedRows = await set.DeleteRangeAsync(new List<DeleteTokenDoc> { fresh });

            Assert.Equal(1, affectedRows);
            Assert.Equal(0L, await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM DeleteTokenDocs"));
        }
    }

    /// <summary>
    /// 无令牌实体参数超限分批删除（EntitySet.Delete.cs 的 DeleteRangeAsync 事务分支与 DeleteEntitiesAsync）的
    /// SQLite 真库测试：方言变体将参数上限缩至 4，5 个实体分两批（4 + 1）在事务内删除。
    /// </summary>
    public class EntitySetDeleteBatchLimitTests
    {
        [XbcDbEntity.Table("DeleteBatchDocs")]
        public class DeleteBatchDoc
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
        }

        [Fact]
        public async Task DeleteRange_OverParameterLimit_SplitsIntoBatchesWithinTransaction()
        {
            using var scope = new VariantSqliteScope(VariantSqliteScope.Kind.SmallParameterLimit);
            await scope.DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE DeleteBatchDocs (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL
                );
                INSERT INTO DeleteBatchDocs (Id, Name) VALUES (1, 'u1');
                INSERT INTO DeleteBatchDocs (Id, Name) VALUES (2, 'u2');
                INSERT INTO DeleteBatchDocs (Id, Name) VALUES (3, 'u3');
                INSERT INTO DeleteBatchDocs (Id, Name) VALUES (4, 'u4');
                INSERT INTO DeleteBatchDocs (Id, Name) VALUES (5, 'u5');
            ");
            var set = new EntitySet<DeleteBatchDoc>(scope.DbAccess);

            // 5 个实体超过参数上限（4）：分批 4 + 1，事务保证原子性，全部删除
            var entities = Enumerable.Range(1, 5).Select(i => new DeleteBatchDoc { Id = i, Name = $"u{i}" }).ToList();
            var affectedRows = await set.DeleteRangeAsync(entities);

            Assert.Equal(5, affectedRows);
            Assert.Equal(0L, await scope.DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM DeleteBatchDocs"));
        }
    }
}
