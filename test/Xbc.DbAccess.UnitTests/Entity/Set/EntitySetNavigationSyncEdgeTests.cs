using Moq;
using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity.Set
{
    /// <summary>
    /// 导航外键同步边缘分支（EntitySet.cs 的 SyncOneToOneForeignKeys / SyncOneToManyForeignKeys）的 SQLite 真库补测：
    /// 混合 1:1 与 1:N 导航的类型分流、1:1 引用对方键值为默认值时跳过同步、1:N 本方 ThisKey 列为默认值时跳过同步，
    /// 以及非框架 DbAccess 实现的构造期拦截。
    /// </summary>
    public class EntitySetNavigationSyncEdgeTests : EntityTestBase
    {
        #region 测试实体

        [XbcDbEntity.Table("NavSyncTargets")]
        public class NavTarget
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public int LegacyId { get; set; }
        }

        [XbcDbEntity.Table("NavSyncChildren")]
        public class NavChild
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public int ParentId { get; set; }
        }

        [XbcDbEntity.Table("NavSyncMixedParents")]
        public class MixedNavParent
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
            public int TargetId { get; set; }

            [XbcDbEntity.Navigation(ThisKey = "TargetId", OtherKey = "LegacyId")]
            public NavTarget Target { get; set; }

            [XbcDbEntity.Navigation(OtherKey = "ParentId")]
            public List<NavChild> Children { get; set; }
        }

        [XbcDbEntity.Table("NavSyncNoPkTargets")]
        public class NoPkNavTarget
        {
            public int SomeId { get; set; }
        }

        [XbcDbEntity.Table("NavSyncDefaultRefParents")]
        public class DefaultKeyRefParent
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public int TargetId { get; set; }

            [XbcDbEntity.Navigation(ThisKey = "TargetId", OtherKey = "SomeId")]
            public NoPkNavTarget Target { get; set; }
        }

        [XbcDbEntity.Table("NavSyncGroupChildren")]
        public class GroupChild
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public int ParentGroupId { get; set; }
        }

        [XbcDbEntity.Table("NavSyncGroupParents")]
        public class NonPkThisKeyParent
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
            public int GroupId { get; set; }

            [XbcDbEntity.Navigation(ThisKey = "GroupId", OtherKey = "ParentGroupId")]
            public List<GroupChild> Children { get; set; }
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE NavSyncTargets (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    LegacyId INTEGER NOT NULL
                );
                CREATE TABLE NavSyncChildren (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ParentId INTEGER NOT NULL
                );
                CREATE TABLE NavSyncMixedParents (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    TargetId INTEGER NOT NULL
                );
                CREATE TABLE NavSyncNoPkTargets (
                    SomeId INTEGER NOT NULL
                );
                CREATE TABLE NavSyncDefaultRefParents (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TargetId INTEGER NOT NULL
                );
                CREATE TABLE NavSyncGroupChildren (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ParentGroupId INTEGER NOT NULL
                );
                CREATE TABLE NavSyncGroupParents (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    GroupId INTEGER NOT NULL
                );
            ");
        }

        [Fact]
        public void Constructor_NonFrameworkDbAccess_Throws()
        {
            // 构造期即拦截非框架实现，避免推迟到首次写入才失败
            Assert.Throws<NotSupportedException>(() => new EntitySet<NavTarget>(new Mock<IDbAccess>().Object));
        }

        [Fact]
        public async Task Insert_MixedOneToOneAndOneToManyNavigations_SyncsEachByType()
        {
            var set = DbAccess.Set<MixedNavParent>();

            var parent = new MixedNavParent
            {
                Name = "mixed",
                Target = new NavTarget { Id = 1, LegacyId = 777 },
                Children = new List<NavChild> { new NavChild(), new NavChild() },
            };

            // 1:1 引用在写入前同步（TargetId 回填 777），1:N 集合在写入后同步（子外键回填自增主键）
            await set.InsertAsync(parent);

            Assert.Equal(777, parent.TargetId);
            Assert.True(parent.Id > 0);
            Assert.All(parent.Children, c => Assert.Equal(parent.Id, c.ParentId));

            var rows = await DbAccess.FromSql<MixedNavParent>("SELECT * FROM NavSyncMixedParents").ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal(777, row.TargetId);
        }

        [Fact]
        public async Task Insert_OneToOneTargetKeyDefaultValue_SkipsForeignKeySync()
        {
            var set = DbAccess.Set<DefaultKeyRefParent>();

            // 对方键值（OtherKey 列）为默认值：跳过同步，外键列保持默认值 0
            var parent = new DefaultKeyRefParent { Target = new NoPkNavTarget { SomeId = 0 } };
            await set.InsertAsync(parent);

            Assert.Equal(0, parent.TargetId);
            var rows = await DbAccess.FromSql<DefaultKeyRefParent>("SELECT * FROM NavSyncDefaultRefParents").ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal(0, row.TargetId);
        }

        [Fact]
        public async Task Insert_OneToManyThisKeyDefaultValue_SkipsChildForeignKeySync()
        {
            var set = DbAccess.Set<NonPkThisKeyParent>();

            // 1:N 的 ThisKey 指向非主键列：该列值为默认值（0）时跳过同步，子外键不回填
            var parent1 = new NonPkThisKeyParent
            {
                Id = 1,
                Name = "g1",
                GroupId = 0,
                Children = new List<GroupChild> { new GroupChild(), new GroupChild() },
            };
            await set.InsertAsync(parent1);
            Assert.All(parent1.Children, c => Assert.Equal(0, c.ParentGroupId));

            // ThisKey 列有有效值时照常回填（对照：证明跳过源于默认值分支）
            var parent2 = new NonPkThisKeyParent
            {
                Id = 2,
                Name = "g2",
                GroupId = 5,
                Children = new List<GroupChild> { new GroupChild() },
            };
            await set.InsertAsync(parent2);
            Assert.All(parent2.Children, c => Assert.Equal(5, c.ParentGroupId));

            var rows = await DbAccess.FromSql<NonPkThisKeyParent>("SELECT * FROM NavSyncGroupParents ORDER BY Id").ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.Equal((1, 0), (rows[0].Id, rows[0].GroupId));
            Assert.Equal((2, 5), (rows[1].Id, rows[1].GroupId));
        }
    }
}
