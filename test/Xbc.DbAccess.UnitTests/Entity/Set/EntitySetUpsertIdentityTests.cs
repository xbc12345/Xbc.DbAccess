using System.Threading.Tasks;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.UnitTests.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity.Set
{
    /// <summary>
    /// Upsert 自增列为非主键（需回填）分支（EntitySet.Upsert.cs）的 SQLite 真库补测：
    /// 非主键自增列按方言能力选择 RETURNING 结果集取回或事务内按主键回读回填，
    /// 以及除主键与自增列外没有可更新列的守卫。SQLite 非主键列无自生成能力，
    /// 以 AFTER INSERT 触发器模拟自增序列（触发器只在真实 INSERT 时填充，更新路径保留原值）。
    /// </summary>
    public class EntitySetUpsertIdentityTests : EntityTestBase
    {
        #region 测试实体

        [XbcDbEntity.Table("UpsertSeqDocs")]
        public class UpsertSeqDoc
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            [XbcDbEntity.Column(IsIdentity = true)]
            public int Seq { get; set; }

            public string Name { get; set; } = string.Empty;
        }

        [XbcDbEntity.Table("UpsertOnlyPkDocs")]
        public class UpsertOnlyPkDoc
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE UpsertSeqDocs (
                    Id INTEGER PRIMARY KEY,
                    Seq INTEGER NULL,
                    Name TEXT NOT NULL
                );
            ");

            // 模拟自增序列：插入时 Seq 为 NULL 则按最大值 + 1 填充（同语句内完成，后续回读可见）
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TRIGGER trg_UpsertSeqDocs_Seq AFTER INSERT ON UpsertSeqDocs
                WHEN NEW.Seq IS NULL
                BEGIN
                    UPDATE UpsertSeqDocs
                    SET Seq = (SELECT IFNULL(MAX(Seq), 0) + 1 FROM UpsertSeqDocs)
                    WHERE Id = NEW.Id;
                END;
            ");

            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE UpsertOnlyPkDocs (
                    Id INTEGER PRIMARY KEY
                );
            ");
        }

        [Fact]
        public async Task Upsert_NonPkIdentityDefaultValue_UpdatePath_BackfillsFromReturn()
        {
            var set = DbAccess.Set<UpsertSeqDoc>();
            await DbAccess.ExecuteNonQueryAsync("INSERT INTO UpsertSeqDocs (Id, Seq, Name) VALUES (1, 100, 'a');");

            // 自增列（非主键）为默认值：冲突更新路径，RETURNING 结果集取回既有 Seq 回填
            var affectedRows = await set.UpsertAsync(new UpsertSeqDoc { Id = 1, Name = "b" });

            Assert.Equal(1, affectedRows);
            var rows = await DbAccess.FromSql<UpsertSeqDoc>("SELECT * FROM UpsertSeqDocs").ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal((1, 100, "b"), (row.Id, row.Seq, row.Name));
        }

        [Fact]
        public async Task Upsert_OnlyNonIdentityPrimaryKey_Throws()
        {
            var set = DbAccess.Set<UpsertOnlyPkDoc>();

            // 除主键与自增列外没有可更新的列：无法构建 Upsert 语句，显式失败
            await Assert.ThrowsAsync<NotSupportedException>(
                () => set.UpsertAsync(new UpsertOnlyPkDoc { Id = 1 }));
        }
    }

    /// <summary>
    /// Upsert 非主键自增列的事务内按主键回读回填路径（版本探测降级为 3.34.0，无取回子句）的 SQLite 真库测试。
    /// </summary>
    public class EntitySetUpsertLegacyProbeTests
    {
        [XbcDbEntity.Table("UpsertLegacySeqDocs")]
        public class UpsertLegacySeqDoc
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            [XbcDbEntity.Column(IsIdentity = true)]
            public int Seq { get; set; }

            public string Name { get; set; } = string.Empty;
        }

        [Fact]
        public async Task Upsert_NonPkIdentityDefaultValue_InsertAndReadBackPath_BackfillsWithinTransaction()
        {
            using var scope = new VariantSqliteScope(VariantSqliteScope.Kind.LegacyIdentityProbe);
            await scope.DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE UpsertLegacySeqDocs (
                    Id INTEGER PRIMARY KEY,
                    Seq INTEGER NULL,
                    Name TEXT NOT NULL
                );
                CREATE TRIGGER trg_UpsertLegacySeqDocs_Seq AFTER INSERT ON UpsertLegacySeqDocs
                WHEN NEW.Seq IS NULL
                BEGIN
                    UPDATE UpsertLegacySeqDocs
                    SET Seq = (SELECT IFNULL(MAX(Seq), 0) + 1 FROM UpsertLegacySeqDocs)
                    WHERE Id = NEW.Id;
                END;
            ");
            var set = new EntitySet<UpsertLegacySeqDoc>(scope.DbAccess);

            // 插入路径：事务包裹 Upsert 与按主键回读，触发器生成的 Seq 经回读回填
            var affectedRows = await set.UpsertAsync(new UpsertLegacySeqDoc { Id = 5, Name = "x" });
            Assert.Equal(1, affectedRows);

            var first = Assert.Single(await scope.DbAccess.FromSql<UpsertLegacySeqDoc>("SELECT * FROM UpsertLegacySeqDocs").ToListAsync());
            Assert.Equal((5, 1, "x"), (first.Id, first.Seq, first.Name));

            // 更新路径：同事务内回读既有 Seq，Name 更新、Seq 保留原值
            var affectedRows2 = await set.UpsertAsync(new UpsertLegacySeqDoc { Id = 5, Name = "y" });
            Assert.Equal(1, affectedRows2);

            var second = Assert.Single(await scope.DbAccess.FromSql<UpsertLegacySeqDoc>("SELECT * FROM UpsertLegacySeqDocs").ToListAsync());
            Assert.Equal((5, 1, "y"), (second.Id, second.Seq, second.Name));
        }
    }
}
