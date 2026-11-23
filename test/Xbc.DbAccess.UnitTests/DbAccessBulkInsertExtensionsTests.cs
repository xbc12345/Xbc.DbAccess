using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xbc.DbAccess.UnitTests.Entity;
using Xunit;

namespace Xbc.DbAccess.UnitTests
{
    /// <summary>
    /// bulk 插入扩展方法（<see cref="DbAccessBulkInsertExtensions.BulkInsertAsync"/>）的 SQLite 真库测试：
    /// 不经实体层直接以 <see cref="BulkInsertContext"/> 写入，覆盖回退路径（SQLite 逐条策略：事务内逐条 + 命令复用）的行数返回、
    /// 参数校验异常、行数组长度校验、空行集、大集合流式执行与调用方事务嵌套。
    /// </summary>
    public class DbAccessBulkInsertExtensionsTests : EntityTestBase
    {
        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE L2Users (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Age INTEGER NULL
                );
            ");
        }

        /// <summary>
        /// 构建 L2Users 表的 bulk 插入上下文（Id/Name/Age 三列）。
        /// </summary>
        private static BulkInsertContext CreateContext(IEnumerable<object[]> rows)
        {
            var columns = new List<BulkInsertColumn>
            {
                new BulkInsertColumn("Id", typeof(int)),
                new BulkInsertColumn("Name", typeof(string)),
                new BulkInsertColumn("Age", typeof(int))
            };
            return new BulkInsertContext(null, "L2Users", columns, rows);
        }

        [Fact]
        public async Task BulkInsert_InsertsRowsAndReturnsCount()
        {
            var rows = new List<object[]>
            {
                new object[] { 1, "Alice", 20 },
                new object[] { 2, "Bob", DBNull.Value }
            };

            var affectedRows = await DbAccess.BulkInsertAsync(CreateContext(rows));

            Assert.Equal(2, affectedRows);
            Assert.Equal(2L, await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM L2Users"));
            Assert.Equal("Bob", await DbAccess.ExecuteScalarAsync("SELECT Name FROM L2Users WHERE Id = 2"));
            Assert.Equal(DBNull.Value, await DbAccess.ExecuteScalarAsync("SELECT Age FROM L2Users WHERE Id = 2"));
        }

        [Fact]
        public async Task BulkInsert_NullArguments_Throw()
        {
            var context = CreateContext(new List<object[]> { new object[] { 1, "Alice", 20 } });

            await Assert.ThrowsAsync<ArgumentNullException>(() => DbAccess.BulkInsertAsync(null));
            await Assert.ThrowsAsync<ArgumentNullException>(() => DbAccessBulkInsertExtensions.BulkInsertAsync(DbAccess, null));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task BulkInsert_NonPositiveBatchSize_Throws(int batchSize)
        {
            var context = CreateContext(new List<object[]> { new object[] { 1, "Alice", 20 } });

            await Assert.ThrowsAsync<ArgumentException>(
                () => DbAccess.BulkInsertAsync(context, new BulkInsertOptions { BatchSize = batchSize }));
        }

        [Fact]
        public async Task BulkInsert_NegativeCommandTimeout_Throws()
        {
            var context = CreateContext(new List<object[]> { new object[] { 1, "Alice", 20 } });

            await Assert.ThrowsAsync<ArgumentException>(
                () => DbAccess.BulkInsertAsync(context, new BulkInsertOptions { CommandTimeout = -1 }));
        }

        [Fact]
        public async Task BulkInsert_RowLengthMismatch_Throws()
        {
            // 行值数量（2）与目标列数（3）不一致：给出明确异常优于索引越界的隐晦错误
            var rows = new List<object[]> { new object[] { 1, "Alice" } };

            await Assert.ThrowsAsync<ArgumentException>(() => DbAccess.BulkInsertAsync(CreateContext(rows)));
            Assert.Equal(0L, await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM L2Users"));
        }

        [Fact]
        public async Task BulkInsert_EmptyRows_InsertsNothing()
        {
            var affectedRows = await DbAccess.BulkInsertAsync(CreateContext(Enumerable.Empty<object[]>()));

            Assert.Equal(0, affectedRows);
            Assert.Equal(0L, await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM L2Users"));
        }

        [Fact]
        public async Task BulkInsert_OptionsIgnoredInFallbackPath()
        {
            // 回退路径静默忽略选项（SQLite 无原生 API），合法值不影响插入结果
            var rows = new List<object[]> { new object[] { 1, "Alice", 20 } };

            var affectedRows = await DbAccess.BulkInsertAsync(CreateContext(rows), new BulkInsertOptions { BatchSize = 10, CommandTimeout = 30 });

            Assert.Equal(1, affectedRows);
            Assert.Equal(1L, await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM L2Users"));
        }

        [Fact]
        public async Task BulkInsert_LargeSet_InsertsAllRowsWithinTransaction()
        {
            // SQLite 已切换逐条策略（不再按 VALUES 批次分批），1200 行逐条执行；行数据延迟枚举（流式执行）
            var rows = Enumerable.Range(1, 1200).Select(i => new object[] { i, $"User{i}", 20 + i % 50 });

            var affectedRows = await DbAccess.BulkInsertAsync(CreateContext(rows));

            Assert.Equal(1200, affectedRows);
            Assert.Equal(1200L, await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM L2Users"));
        }

        [Fact]
        public async Task BulkInsert_WithinCallerTransaction_CommitsAsSavepoint()
        {
            var rows = new List<object[]> { new object[] { 1, "Alice", 20 } };

            // 调用方已有活动事务：bulk 插入嵌套为保存点子事务，随调用方提交落库
            using (var transaction = await DbAccess.BeginTransactionAsync())
            {
                await DbAccess.BulkInsertAsync(CreateContext(rows));
                transaction.Commit();
            }

            Assert.Equal(1L, await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM L2Users"));
        }
    }
}
