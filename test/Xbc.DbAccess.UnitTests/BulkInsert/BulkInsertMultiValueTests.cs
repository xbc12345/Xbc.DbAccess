using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xbc.DbAccess.UnitTests.Entity.Set;
using Xunit;

namespace Xbc.DbAccess.UnitTests.BulkInsert
{
    /// <summary>
    /// bulk 插入扩展的多值 INSERT 回退路径（DbAccessBulkInsertExtensions 的 InsertWithMultiValueAsync /
    /// ExecuteMultiValueBatchAsync）的 SQLite 真库测试：标准 SQLite 方言恒为逐条策略，经方言变体
    /// （每批 2 行）在真实引擎上覆盖满批/尾批划分、空行集与枚举中途取消。
    /// </summary>
    public class BulkInsertMultiValueTests
    {
        /// <summary>构建 L3MvUsers 表的 bulk 插入上下文（Id/Name/Age 三列）。</summary>
        private static BulkInsertContext CreateContext(IEnumerable<object[]> rows)
        {
            var columns = new List<BulkInsertColumn>
            {
                new BulkInsertColumn("Id", typeof(int)),
                new BulkInsertColumn("Name", typeof(string)),
                new BulkInsertColumn("Age", typeof(int)),
            };
            return new BulkInsertContext(null, "L3MvUsers", columns, rows);
        }

        /// <summary>初始化目标表。</summary>
        private static async Task InitializeTableAsync(IDbAccess dbAccess)
        {
            await dbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE L3MvUsers (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Age INTEGER NULL
                );
            ");
        }

        [Fact]
        public async Task BulkInsert_MultiValue_BatchSplit_InsertsAllRows()
        {
            using var scope = new VariantSqliteScope(VariantSqliteScope.Kind.MultiValueStrategy);
            await InitializeTableAsync(scope.DbAccess);

            // 5 行超过每批上限（2）：分批 2 + 2 + 1（满批两次 + 尾批一次），事务保证原子性
            var rows = Enumerable.Range(1, 5).Select(i => new object[] { i, $"u{i}", 20 + i });

            var affectedRows = await scope.DbAccess.BulkInsertAsync(CreateContext(rows));

            Assert.Equal(5L, affectedRows);
            Assert.Equal(5L, await scope.DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM L3MvUsers"));

            // 跨批取值校验：满批首行、满批末行、尾批行的参数不错位
            Assert.Equal("u3", await scope.DbAccess.ExecuteScalarAsync("SELECT Name FROM L3MvUsers WHERE Id = 3"));
            Assert.Equal(24L, await scope.DbAccess.ExecuteScalarAsync("SELECT Age FROM L3MvUsers WHERE Id = 4"));
            Assert.Equal("u5", await scope.DbAccess.ExecuteScalarAsync("SELECT Name FROM L3MvUsers WHERE Id = 5"));
        }

        [Fact]
        public async Task BulkInsert_MultiValue_EmptyRows_CommitsWithoutBatches()
        {
            using var scope = new VariantSqliteScope(VariantSqliteScope.Kind.MultiValueStrategy);
            await InitializeTableAsync(scope.DbAccess);

            // 空行集：循环零次、尾批跳过，仅提交空事务
            var affectedRows = await scope.DbAccess.BulkInsertAsync(CreateContext(Enumerable.Empty<object[]>()));

            Assert.Equal(0L, affectedRows);
            Assert.Equal(0L, await scope.DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM L3MvUsers"));
        }

        [Fact]
        public async Task BulkInsert_MultiValue_CancelledDuringEnumeration_ThrowsAndLeavesTableEmpty()
        {
            using var scope = new VariantSqliteScope(VariantSqliteScope.Kind.MultiValueStrategy);
            await InitializeTableAsync(scope.DbAccess);

            using var cts = new CancellationTokenSource();

            // 行数据延迟枚举：第二行 MoveNext 时取消令牌，下一行的取消检查短路执行
            IEnumerable<object[]> LazyRows()
            {
                yield return new object[] { 1, "a", 21 };
                cts.Cancel();
                yield return new object[] { 2, "b", 22 };
            }

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => scope.DbAccess.BulkInsertAsync(CreateContext(LazyRows()), null, cts.Token));

            Assert.Equal(0L, await scope.DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM L3MvUsers"));
        }
    }
}
