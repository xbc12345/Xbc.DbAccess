using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xbc.DbAccess.UnitTests.Infrastructure;
using System.Linq.Expressions;
using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// EntitySet 的单元测试。
    /// </summary>
    public class EntitySetTests : EntityTestBase
    {
        private readonly EntitySet<TestEntity> _entitySet;

        #region 测试实体

        [XbcDbEntity.Table("TestEntities")]
        public class TestEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public int Age { get; set; }
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE TestEntities (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Age INTEGER NOT NULL
                );
                INSERT INTO TestEntities (Name, Age) VALUES ('Alice', 25);
                INSERT INTO TestEntities (Name, Age) VALUES ('Bob', 30);
            ");
        }

        public EntitySetTests()
        {
            _entitySet = new EntitySet<TestEntity>(DbAccess);
        }

        #region 构造函数测试

        [Fact]
        public void Constructor_NullDbAccess_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new EntitySet<TestEntity>(null!));
        }

        [Fact]
        public void Constructor_ValidDbAccess_CreatesInstance()
        {
            var entitySet = new EntitySet<TestEntity>(DbAccess);
            Assert.NotNull(entitySet);
        }

        #endregion

        #region Find 测试

        [Fact]
        public async Task Find_ExistingRecord_ReturnsRecord()
        {
            var result = await _entitySet.FindAsync(1);

            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
            Assert.Equal("Alice", result.Name);
        }

        [Fact]
        public async Task Find_NonExistingRecord_ReturnsNull()
        {
            var result = await _entitySet.FindAsync(999);

            Assert.Null(result);
        }

        #endregion

        #region FindAsync 测试

        [Fact]
        public async Task FindAsync_ExistingRecord_ReturnsRecord()
        {
            var result = await _entitySet.FindAsync(2);

            Assert.NotNull(result);
            Assert.Equal(2, result.Id);
            Assert.Equal("Bob", result.Name);
        }

        [Fact]
        public async Task FindAsync_NonExistingRecord_ReturnsNull()
        {
            var result = await _entitySet.FindAsync(999);

            Assert.Null(result);
        }

        [Fact]
        public async Task FindAsync_CancellationToken_ThrowsTaskCanceledException()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await _entitySet.FindAsync(1, cts.Token));
        }

        #endregion

        #region Insert 测试

        [Fact]
        public async Task Insert_NewEntity_InsertsAndReturnsAffectedRows()
        {
            var entity = new TestEntity { Name = "Charlie", Age = 35 };
            var affectedRows = await _entitySet.InsertAsync(entity);

            Assert.Equal(1, affectedRows);
            Assert.True(entity.Id > 0);
        }

        [Fact]
        public async Task Insert_NullEntity_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _entitySet.InsertAsync(null!));
        }

        #endregion

        #region InsertAsync 测试

        [Fact]
        public async Task InsertAsync_NewEntity_InsertsSuccessfully()
        {
            var entity = new TestEntity { Name = "Charlie", Age = 35 };
            var affectedRows = await _entitySet.InsertAsync(entity);

            Assert.Equal(1, affectedRows);
            Assert.True(entity.Id > 0);
        }

        #endregion

        #region InsertRange 测试

        [Fact]
        public async Task InsertRange_MultipleEntities_InsertsAll()
        {
            var entities = new List<TestEntity>
            {
                new TestEntity { Name = "Charlie", Age = 35 },
                new TestEntity { Name = "David", Age = 40 }
            };
            var affectedRows = await _entitySet.InsertRangeAsync(entities);

            Assert.Equal(2, affectedRows);
            Assert.True(entities.All(e => e.Id > 0));
        }

        [Fact]
        public async Task InsertRange_NullCollection_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _entitySet.InsertRangeAsync(null!));
        }

        [Fact]
        public async Task InsertRange_EmptyCollection_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _entitySet.InsertRangeAsync([]));
        }

        #endregion

        #region InsertRangeAsync 测试

        [Fact]
        public async Task InsertRangeAsync_MultipleEntities_InsertsAll()
        {
            var entities = new List<TestEntity>
            {
                new TestEntity { Name = "Charlie", Age = 35 },
                new TestEntity { Name = "David", Age = 40 }
            };
            var affectedRows = await _entitySet.InsertRangeAsync(entities);

            Assert.Equal(2, affectedRows);
        }

        #endregion

        #region Update 测试

        [Fact]
        public async Task Update_ExistingEntity_UpdatesSuccessfully()
        {
            var entity = await _entitySet.FindAsync(1);
            entity.Name = "UpdatedAlice";
            var affectedRows = await _entitySet.UpdateAsync(entity);

            Assert.Equal(1, affectedRows);

            var updated = await _entitySet.FindAsync(1);
            Assert.Equal("UpdatedAlice", updated.Name);
        }

        [Fact]
        public async Task Update_NullEntity_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _entitySet.UpdateAsync(null!));
        }

        #endregion

        #region UpdateAsync 测试

        [Fact]
        public async Task UpdateAsync_ExistingEntity_UpdatesSuccessfully()
        {
            var entity = await _entitySet.FindAsync(2);
            entity.Name = "UpdatedBob";
            var affectedRows = await _entitySet.UpdateAsync(entity);

            Assert.Equal(1, affectedRows);

            var updated = await _entitySet.FindAsync(2);
            Assert.Equal("UpdatedBob", updated.Name);
        }

        #endregion

        #region UpdateRange 测试

        [Fact]
        public async Task UpdateRange_MultipleEntities_UpdatesAll()
        {
            var entities = new List<TestEntity>
            {
                await _entitySet.FindAsync(1),
                await _entitySet.FindAsync(2)
            };
            entities[0].Name = "UpdatedAlice";
            entities[1].Name = "UpdatedBob";

            var affectedRows = await _entitySet.UpdateRangeAsync(entities);

            Assert.Equal(2, affectedRows);

            var updated1 = await _entitySet.FindAsync(1);
            var updated2 = await _entitySet.FindAsync(2);
            Assert.Equal("UpdatedAlice", updated1.Name);
            Assert.Equal("UpdatedBob", updated2.Name);
        }

        #endregion

        #region UpdateRangeAsync 测试

        [Fact]
        public async Task UpdateRangeAsync_MultipleEntities_UpdatesAll()
        {
            var entities = new List<TestEntity>
            {
                await _entitySet.FindAsync(1),
                await _entitySet.FindAsync(2)
            };
            entities[0].Name = "UpdatedAlice2";
            entities[1].Name = "UpdatedBob2";

            var affectedRows = await _entitySet.UpdateRangeAsync(entities);

            Assert.Equal(2, affectedRows);
        }

        #endregion

        #region Delete 测试

        [Fact]
        public async Task Delete_ExistingEntity_DeletesSuccessfully()
        {
            var entity = await _entitySet.FindAsync(1);
            var affectedRows = await _entitySet.DeleteAsync(entity);

            Assert.Equal(1, affectedRows);
            Assert.Null(await _entitySet.FindAsync(1));
        }

        [Fact]
        public async Task Delete_NullEntity_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _entitySet.DeleteAsync((TestEntity)null!));
        }

        #endregion

        #region DeleteAsync 测试

        [Fact]
        public async Task DeleteAsync_ExistingEntity_DeletesSuccessfully()
        {
            var entity = await _entitySet.FindAsync(2);
            var affectedRows = await _entitySet.DeleteAsync(entity);

            Assert.Equal(1, affectedRows);
            Assert.Null(await _entitySet.FindAsync(2));
        }

        #endregion

        #region DeleteRange 测试

        [Fact]
        public async Task DeleteRange_MultipleEntities_DeletesAll()
        {
            var entities = new List<TestEntity>
            {
                await _entitySet.FindAsync(1),
                await _entitySet.FindAsync(2)
            };

            var affectedRows = await _entitySet.DeleteRangeAsync(entities);

            Assert.Equal(2, affectedRows);
            Assert.Null(await _entitySet.FindAsync(1));
            Assert.Null(await _entitySet.FindAsync(2));
        }

        [Fact]
        public async Task DeleteRange_NullCollection_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _entitySet.DeleteRangeAsync(null!));
        }

        #endregion

        #region DeleteRangeAsync 测试

        [Fact]
        public async Task DeleteRangeAsync_MultipleEntities_DeletesAll()
        {
            // 先插入两条新记录用于删除测试
            var entity3 = new TestEntity { Name = "Charlie", Age = 35 };
            var entity4 = new TestEntity { Name = "David", Age = 40 };
            await _entitySet.InsertAsync(entity3);
            await _entitySet.InsertAsync(entity4);

            var entities = new List<TestEntity> { entity3, entity4 };
            var affectedRows = await _entitySet.DeleteRangeAsync(entities);

            Assert.Equal(2, affectedRows);
            Assert.Null(await _entitySet.FindAsync(entity3.Id));
            Assert.Null(await _entitySet.FindAsync(entity4.Id));
        }

        #endregion
    }
}
