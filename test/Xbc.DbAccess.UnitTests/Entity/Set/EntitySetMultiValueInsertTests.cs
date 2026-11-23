using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity.Set
{
    /// <summary>
    /// 多值 INSERT 批量插入路径（EntitySet.Insert.cs 的 InsertRangeMultiValueAsync / BuildBatchInsertSql）的 SQLite 真库测试：
    /// 标准 SQLite 方言恒为逐条策略，多值路径经方言变体（每批 2 行）在真实引擎上覆盖单批直写、跨批事务划分、
    /// 1:1 外键同步与取消令牌。
    /// </summary>
    public class EntitySetMultiValueInsertTests
    {
        #region 测试实体

        [XbcDbEntity.Table("MultiValueDocs")]
        public class MultiValueDoc
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
        }

        [XbcDbEntity.Table("MultiValueTargets")]
        public class MultiValueTarget
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public int LegacyId { get; set; }
        }

        [XbcDbEntity.Table("MultiValueRefDocs")]
        public class MultiValueRefDoc
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            public int TargetId { get; set; }

            [XbcDbEntity.Navigation(ThisKey = "TargetId", OtherKey = "LegacyId")]
            public MultiValueTarget Target { get; set; }
        }

        #endregion

        /// <summary>初始化多值插入路径的表结构。</summary>
        private static async Task InitializeTablesAsync(IDbAccess dbAccess)
        {
            await dbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE MultiValueDocs (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL
                );

                CREATE TABLE MultiValueTargets (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    LegacyId INTEGER NOT NULL
                );

                CREATE TABLE MultiValueRefDocs (
                    Id INTEGER PRIMARY KEY,
                    TargetId INTEGER NOT NULL
                );
            ");
        }

        [Fact]
        public async Task InsertRange_MultiValue_SingleBatch_InsertsWithoutTransaction()
        {
            using var scope = new VariantSqliteScope(VariantSqliteScope.Kind.MultiValueStrategy);
            await InitializeTablesAsync(scope.DbAccess);
            var set = new EntitySet<MultiValueDoc>(scope.DbAccess);

            // 行数等于单批上限（2）：单条多值 INSERT 直接执行，无需事务
            var affectedRows = await set.InsertRangeAsync(new List<MultiValueDoc>
            {
                new MultiValueDoc { Id = 1, Name = "a" },
                new MultiValueDoc { Id = 2, Name = "b" },
            });

            Assert.Equal(2, affectedRows);
            var rows = await scope.DbAccess.FromSql<MultiValueDoc>("SELECT * FROM MultiValueDocs ORDER BY Id").ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.Equal(new[] { 1, 2 }, rows.Select(r => r.Id));
            Assert.Equal(new[] { "a", "b" }, rows.Select(r => r.Name));
        }

        [Fact]
        public async Task InsertRange_MultiValue_OverBatchLimit_SplitsWithinTransaction()
        {
            using var scope = new VariantSqliteScope(VariantSqliteScope.Kind.MultiValueStrategy);
            await InitializeTablesAsync(scope.DbAccess);
            var set = new EntitySet<MultiValueDoc>(scope.DbAccess);

            // 3 行超过单批上限（2）：分批 2 + 1，事务保证原子性，末批为不足批（Math.Min 分支）
            var affectedRows = await set.InsertRangeAsync(new List<MultiValueDoc>
            {
                new MultiValueDoc { Id = 1, Name = "a" },
                new MultiValueDoc { Id = 2, Name = "b" },
                new MultiValueDoc { Id = 3, Name = "c" },
            });

            Assert.Equal(3, affectedRows);
            Assert.Equal(3L, await scope.DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM MultiValueDocs"));
            var rows = await scope.DbAccess.FromSql<MultiValueDoc>("SELECT * FROM MultiValueDocs ORDER BY Id").ToListAsync();
            Assert.Equal(new[] { "a", "b", "c" }, rows.Select(r => r.Name));
        }

        [Fact]
        public async Task InsertRange_MultiValue_SyncsOneToOneForeignKeys()
        {
            using var scope = new VariantSqliteScope(VariantSqliteScope.Kind.MultiValueStrategy);
            await InitializeTablesAsync(scope.DbAccess);
            await scope.DbAccess.ExecuteNonQueryAsync("INSERT INTO MultiValueTargets (LegacyId) VALUES (777);");
            var set = new EntitySet<MultiValueRefDoc>(scope.DbAccess);

            // 多值路径在写入前逐实体执行 1:1 外键同步：外键列默认值时由引用的 OtherKey 值回填
            var affectedRows = await set.InsertRangeAsync(new List<MultiValueRefDoc>
            {
                new MultiValueRefDoc { Id = 1, Target = new MultiValueTarget { Id = 1, LegacyId = 777 } },
                new MultiValueRefDoc { Id = 2, Target = new MultiValueTarget { Id = 1, LegacyId = 777 } },
            });

            Assert.Equal(2, affectedRows);
            var rows = await scope.DbAccess.FromSql<MultiValueRefDoc>("SELECT * FROM MultiValueRefDocs ORDER BY Id").ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal(777, r.TargetId));
        }

        [Fact]
        public async Task InsertRange_MultiValue_CancelledToken_ThrowsWithoutInsert()
        {
            using var scope = new VariantSqliteScope(VariantSqliteScope.Kind.MultiValueStrategy);
            await InitializeTablesAsync(scope.DbAccess);
            var set = new EntitySet<MultiValueDoc>(scope.DbAccess);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // 单批可容纳（无事务包装）：循环内的取消检查直接短路，不产生任何写入
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => set.InsertRangeAsync(
                    new List<MultiValueDoc>
                    {
                        new MultiValueDoc { Id = 1, Name = "a" },
                        new MultiValueDoc { Id = 2, Name = "b" },
                    },
                    cts.Token));

            Assert.Equal(0L, await scope.DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM MultiValueDocs"));
        }
    }
}
