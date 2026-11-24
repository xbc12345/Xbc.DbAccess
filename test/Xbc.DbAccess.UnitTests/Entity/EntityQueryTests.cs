using Xbc.DbAccess.Entity;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// EntityQuery 的单元测试。
    /// </summary>
    public class EntityQueryTests : EntityTestBase
    {
        #region 测试实体

        public class TestEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public int Age { get; set; }
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE TestEntities (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Age INTEGER NOT NULL
                );
                INSERT INTO TestEntities (Id, Name, Age) VALUES (1, 'Alice', 25);
                INSERT INTO TestEntities (Id, Name, Age) VALUES (2, 'Bob', 30);
                INSERT INTO TestEntities (Id, Name, Age) VALUES (3, 'Charlie', 35);
            ");
        }

        #region First 测试

        [Fact]
        public async Task First_ExistingRecords_ReturnsFirstRecord()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities ORDER BY Id").FirstAsync();

            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
            Assert.Equal("Alice", result.Name);
        }

        [Fact]
        public async Task First_NoRecords_ThrowsInvalidOperationException()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Id = 999").FirstAsync());
        }

        [Fact]
        public async Task First_WithParameters_ReturnsMatchingRecord()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Id = @Id", new { Id = 2 }).FirstAsync();

            Assert.NotNull(result);
            Assert.Equal(2, result.Id);
            Assert.Equal("Bob", result.Name);
        }

        #endregion

        #region FirstAsync 测试

        [Fact]
        public async Task FirstAsync_ExistingRecords_ReturnsFirstRecord()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities ORDER BY Id").FirstAsync();

            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
            Assert.Equal("Alice", result.Name);
        }

        [Fact]
        public async Task FirstAsync_NoRecords_ThrowsInvalidOperationException()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Id = 999").FirstAsync());
        }

        [Fact]
        public async Task FirstAsync_WithParameters_ReturnsMatchingRecord()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Id = @Id", new { Id = 3 }).FirstAsync();

            Assert.NotNull(result);
            Assert.Equal(3, result.Id);
            Assert.Equal("Charlie", result.Name);
        }

        [Fact]
        public async Task FirstAsync_CancellationToken_ThrowsTaskCanceledException()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(() =>
                DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities").FirstAsync(cts.Token));
        }

        #endregion

        #region FirstOrDefault 测试

        [Fact]
        public async Task FirstOrDefault_ExistingRecords_ReturnsFirstRecord()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities ORDER BY Id").FirstOrDefaultAsync();

            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
        }

        [Fact]
        public async Task FirstOrDefault_NoRecords_ReturnsNull()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Id = 999").FirstOrDefaultAsync();

            Assert.Null(result);
        }

        [Fact]
        public async Task FirstOrDefault_WithParameters_ReturnsMatchingRecord()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Name = @Name", new { Name = "Bob" }).FirstOrDefaultAsync();

            Assert.NotNull(result);
            Assert.Equal(2, result.Id);
        }

        #endregion

        #region FirstOrDefaultAsync 测试

        [Fact]
        public async Task FirstOrDefaultAsync_ExistingRecords_ReturnsFirstRecord()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities ORDER BY Id").FirstOrDefaultAsync();

            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
        }

        [Fact]
        public async Task FirstOrDefaultAsync_NoRecords_ReturnsNull()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Id = 999").FirstOrDefaultAsync();

            Assert.Null(result);
        }

        [Fact]
        public async Task FirstOrDefaultAsync_WithParameters_ReturnsMatchingRecord()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Age = @Age", new { Age = 30 }).FirstOrDefaultAsync();

            Assert.NotNull(result);
            Assert.Equal(2, result.Id);
        }

        #endregion

        #region Single 测试

        [Fact]
        public async Task Single_SingleRecord_ReturnsRecord()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Id = 1").SingleAsync();

            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
        }

        [Fact]
        public async Task Single_NoRecords_ThrowsInvalidOperationException()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Id = 999").SingleAsync());
        }

        [Fact]
        public async Task Single_MultipleRecords_ThrowsInvalidOperationException()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities").SingleAsync());
        }

        #endregion

        #region SingleAsync 测试

        [Fact]
        public async Task SingleAsync_SingleRecord_ReturnsRecord()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Id = 2").SingleAsync();

            Assert.NotNull(result);
            Assert.Equal(2, result.Id);
        }

        [Fact]
        public async Task SingleAsync_NoRecords_ThrowsInvalidOperationException()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Id = 999").SingleAsync());
        }

        [Fact]
        public async Task SingleAsync_MultipleRecords_ThrowsInvalidOperationException()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities").SingleAsync());
        }

        #endregion

        #region SingleOrDefault 测试

        [Fact]
        public async Task SingleOrDefault_SingleRecord_ReturnsRecord()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Id = 1").SingleOrDefaultAsync();

            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
        }

        [Fact]
        public async Task SingleOrDefault_NoRecords_ReturnsNull()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Id = 999").SingleOrDefaultAsync();

            Assert.Null(result);
        }

        [Fact]
        public async Task SingleOrDefault_MultipleRecords_ThrowsInvalidOperationException()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities").SingleOrDefaultAsync());
        }

        #endregion

        #region SingleOrDefaultAsync 测试

        [Fact]
        public async Task SingleOrDefaultAsync_SingleRecord_ReturnsRecord()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Id = 3").SingleOrDefaultAsync();

            Assert.NotNull(result);
            Assert.Equal(3, result.Id);
        }

        [Fact]
        public async Task SingleOrDefaultAsync_NoRecords_ReturnsNull()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Id = 999").SingleOrDefaultAsync();

            Assert.Null(result);
        }

        [Fact]
        public async Task SingleOrDefaultAsync_MultipleRecords_ThrowsInvalidOperationException()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities").SingleOrDefaultAsync());
        }

        #endregion

        #region ToList 测试

        [Fact]
        public async Task ToList_ExistingRecords_ReturnsAllRecords()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities ORDER BY Id").ToListAsync();

            Assert.NotNull(result);
            Assert.Equal(3, result.Count);
            Assert.Equal(1, result[0].Id);
            Assert.Equal(2, result[1].Id);
            Assert.Equal(3, result[2].Id);
        }

        [Fact]
        public async Task ToList_NoRecords_ReturnsEmptyList()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Id = 999").ToListAsync();

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task ToList_WithParameters_ReturnsMatchingRecords()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Age > @Age", new { Age = 25 }).ToListAsync();

            Assert.Equal(2, result.Count);
        }

        #endregion

        #region ToListAsync 测试

        [Fact]
        public async Task ToListAsync_ExistingRecords_ReturnsAllRecords()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities ORDER BY Id").ToListAsync();

            Assert.NotNull(result);
            Assert.Equal(3, result.Count);
        }

        [Fact]
        public async Task ToListAsync_NoRecords_ReturnsEmptyList()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Id = 999").ToListAsync();

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task ToListAsync_WithParameters_ReturnsMatchingRecords()
        {
            var result = await DbAccess.FromSql<TestEntity>("SELECT * FROM TestEntities WHERE Age < @Age", new { Age = 35 }).ToListAsync();

            Assert.Equal(2, result.Count);
        }

        #endregion

        #region QueryMultiple 多结果集测试

        [Fact]
        public async Task QueryMultiple_读取次数超过结果集数_应抛出InvalidOperationException()
        {
            // SQL 返回 2 个结果集，读取 2 次成功后，第 3 次读取应抛出异常而非静默返回空列表
            using (var multi = await DbAccess.ExecuteMultipleAsync("SELECT Id, Name, Age FROM TestEntities; SELECT Id, Name, Age FROM TestEntities"))
            {
                var first = await multi.ReadAsync<TestEntity>();
                Assert.Equal(3, first.Count);

                var second = await multi.ReadAsync<TestEntity>();
                Assert.Equal(3, second.Count);

                await Assert.ThrowsAsync<InvalidOperationException>(() => multi.ReadAsync<TestEntity>());
            }
        }

        [Fact]
        public async Task QueryMultiple_结果集数量与读取次数匹配_应正常读取()
        {
            using (var multi = await DbAccess.ExecuteMultipleAsync("SELECT Id, Name, Age FROM TestEntities; SELECT Id, Name, Age FROM TestEntities"))
            {
                var first = await multi.ReadFirstAsync<TestEntity>();
                Assert.Equal(1, first.Id);

                var second = await multi.ReadAsync<TestEntity>();
                Assert.Equal(3, second.Count);
            }
        }

        #endregion
    }
}
