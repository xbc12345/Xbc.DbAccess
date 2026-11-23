using System.Collections.Generic;
using System.Threading.Tasks;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.UnitTests.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity.Set
{
    /// <summary>
    /// EntitySet 插入路径（EntitySet.Insert.cs）的 SQLite 真库补测：
    /// 无自增列直写、显式自增值写入、单实体批量路由、自增列混合显式/默认的逐条批量路径。
    /// </summary>
    public class EntitySetInsertPathTests : EntityTestBase
    {
        #region 测试实体

        [XbcDbEntity.Table("InsertManualPkDocs")]
        public class ManualPkDoc
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
        }

        [XbcDbEntity.Table("InsertIdentityDocs")]
        public class IdentityDoc
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
                CREATE TABLE InsertManualPkDocs (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL
                );

                CREATE TABLE InsertIdentityDocs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Age INTEGER NOT NULL
                );
            ");
        }

        [Fact]
        public async Task Insert_NoIdentityColumn_WritesRowDirectly()
        {
            var set = DbAccess.Set<ManualPkDoc>();

            // 无自增列：无需回填，单语句直接执行（不走事务与自摄取回路径）
            var affectedRows = await set.InsertAsync(new ManualPkDoc { Id = 7, Name = "m7" });

            Assert.Equal(1, affectedRows);
            var rows = await DbAccess.FromSql<ManualPkDoc>("SELECT * FROM InsertManualPkDocs").ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal(7, row.Id);
            Assert.Equal("m7", row.Name);
        }

        [Fact]
        public async Task Insert_ExplicitIdentityValue_WritesExplicitValueWithoutBackfill()
        {
            var set = DbAccess.Set<IdentityDoc>();

            // 自增列带显式值：照常进入列清单按值写入，不回填、不走取回路径
            var affectedRows = await set.InsertAsync(new IdentityDoc { Id = 42, Name = "explicit", Age = 7 });

            Assert.Equal(1, affectedRows);
            var rows = await DbAccess.FromSql<IdentityDoc>("SELECT * FROM InsertIdentityDocs").ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal(42, row.Id);
            Assert.Equal("explicit", row.Name);
        }

        [Fact]
        public async Task InsertRange_SingleEntity_RoutesToSingleInsert()
        {
            var set = DbAccess.Set<IdentityDoc>();

            // 单条记录走 InsertAsync（由其决定事务与自增取回方式）
            var entity = new IdentityDoc { Name = "solo", Age = 1 };
            var affectedRows = await set.InsertRangeAsync(new List<IdentityDoc> { entity });

            Assert.Equal(1, affectedRows);
            Assert.True(entity.Id > 0);
            Assert.Equal(1L, await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM InsertIdentityDocs"));
        }

        [Fact]
        public async Task InsertRange_IdentityMixedExplicitAndDefault_WritesExplicitRowAndBackfillsDefaultRow()
        {
            var set = DbAccess.Set<IdentityDoc>();

            // 逐条路径：显式值行按值写入（不回填），默认值行由数据库生成并回填
            var explicitRow = new IdentityDoc { Id = 100, Name = "e1", Age = 1 };
            var defaultRow = new IdentityDoc { Name = "e2", Age = 2 };
            var affectedRows = await set.InsertRangeAsync(new List<IdentityDoc> { explicitRow, defaultRow });

            Assert.Equal(2, affectedRows);
            Assert.Equal(100, explicitRow.Id);
            Assert.True(defaultRow.Id > 0 && defaultRow.Id != 100);

            var rows = await DbAccess.FromSql<IdentityDoc>("SELECT * FROM InsertIdentityDocs ORDER BY Id").ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.Equal((100, "e1", 1), (rows[0].Id, rows[0].Name, rows[0].Age));
            Assert.Equal((defaultRow.Id, "e2", 2), (rows[1].Id, rows[1].Name, rows[1].Age));
        }
    }

    /// <summary>
    /// SELECT LAST_INSERT_ROWID() 自增取回路径（版本探测降级为 3.34.0）的 SQLite 真库测试：
    /// 两条语句由事务保证同连接执行，插入后自增 ID 经取回语句回填。
    /// </summary>
    public class EntitySetInsertLegacyProbeTests
    {
        [XbcDbEntity.Table("InsertLegacyDocs")]
        public class LegacyDoc
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
        }

        [Fact]
        public async Task Insert_IdentityDefault_LegacyProbePath_BackfillsIdWithinTransaction()
        {
            using var scope = new VariantSqliteScope(VariantSqliteScope.Kind.LegacyIdentityProbe);
            await scope.DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE InsertLegacyDocs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                );
            ");
            var set = new EntitySet<LegacyDoc>(scope.DbAccess);

            // SELECT LAST_INSERT_ROWID() 模式：INSERT 与取回语句在事务内同连接执行，成功后提交
            var affectedRows = await set.InsertAsync(new LegacyDoc { Name = "legacy" });

            Assert.Equal(1, affectedRows);
            var rows = await scope.DbAccess.FromSql<LegacyDoc>("SELECT * FROM InsertLegacyDocs").ToListAsync();
            var row = Assert.Single(rows);
            Assert.True(row.Id > 0);
            Assert.Equal("legacy", row.Name);
        }
    }
}
