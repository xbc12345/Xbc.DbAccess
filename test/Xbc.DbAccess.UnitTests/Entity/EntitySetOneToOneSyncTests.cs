using System.Threading.Tasks;
using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 1:1 导航外键同步（SyncOneToOneForeignKeys）的复现测试：
    /// 验证写路径同步的值取自 OtherKey 列（与读路径 Include 装配使用的键列一致），
    /// 而不是目标实体的主键列；以及外键列与导航引用的值优先级规则
    /// （外键列已有值时以外键为准，为默认值时由引用回填）。
    /// </summary>
    public class EntitySetOneToOneSyncTests : EntityTestBase
    {
        #region 测试实体

        /// <summary>
        /// OtherKey 显式指向目标的非主键列（int 外键 ↔ int 引用列）：
        /// 现状下写路径取目标主键值（同为 int），静默写入错误值，无任何异常。
        /// </summary>
        [XbcDbEntity.Table("SyncLegacyTargets")]
        public class SyncLegacyTarget
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            // 非主键的引用键列
            public int LegacyId { get; set; }
        }

        [XbcDbEntity.Table("SyncLegacyParents")]
        public class SyncLegacyParent
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            // 外键按目标的 LegacyId 引用
            public int TargetLegacyId { get; set; }

            // 手动指定键：TargetLegacyId → SyncLegacyTarget.LegacyId（OtherKey 为非主键列）
            [Navigation(ThisKey = "TargetLegacyId", OtherKey = "LegacyId")]
            public SyncLegacyTarget Target { get; set; }
        }

        /// <summary>
        /// 目标实体无主键 + 显式 OtherKey：元数据构建允许（FindOtherKeyColumn 只按名查列），
        /// 现状下写路径访问 TargetEntityMetadata.PrimaryKey 时抛 NullReferenceException。
        /// </summary>
        [XbcDbEntity.Table("SyncNoPkTargets")]
        public class SyncNoPkTarget
        {
            public int SomeId { get; set; }
        }

        [XbcDbEntity.Table("SyncNoPkParents")]
        public class SyncNoPkParent
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public int TargetId { get; set; }

            // 目标无主键，显式指定 OtherKey
            [Navigation(ThisKey = "TargetId", OtherKey = "SomeId")]
            public SyncNoPkTarget Target { get; set; }
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE SyncLegacyTargets (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    LegacyId INTEGER NOT NULL
                );
                CREATE TABLE SyncLegacyParents (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TargetLegacyId INTEGER NOT NULL
                );
                CREATE TABLE SyncNoPkTargets (
                    SomeId INTEGER NOT NULL
                );
                CREATE TABLE SyncNoPkParents (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TargetId INTEGER NOT NULL
                );
            ");
        }

        [Fact]
        public async Task Insert_OneToOneNonPrimaryKeyOtherKey_SyncsOtherKeyValue()
        {
            var entitySet = new EntitySet<SyncLegacyParent>(DbAccess);

            var parent = new SyncLegacyParent
            {
                Target = new SyncLegacyTarget { Id = 1, LegacyId = 777 }
            };

            await entitySet.InsertAsync(parent);

            // 外键列应同步 OtherKey（LegacyId）的值 777，而不是目标主键值 1
            Assert.Equal(777, parent.TargetLegacyId);
        }

        [Fact]
        public async Task Insert_OneToOneNoPrimaryKeyTarget_SyncsOtherKeyValue()
        {
            var entitySet = new EntitySet<SyncNoPkParent>(DbAccess);

            var parent = new SyncNoPkParent
            {
                Target = new SyncNoPkTarget { SomeId = 5 }
            };

            await entitySet.InsertAsync(parent);

            // 不应抛 NullReferenceException，外键列应同步 OtherKey（SomeId）的值
            Assert.Equal(5, parent.TargetId);
        }

        /// <summary>
        /// 值优先级规则：外键列已有有效值时以外键为准，导航引用（可能已过期）不覆盖用户修改。
        /// 场景：FindAsync 含 Include 加载后仅修改外键列（如员工变更部门只改 DepartmentId），
        /// 引用仍指向旧目标，此时外键修改必须生效。
        /// </summary>
        [Fact]
        public async Task Update_ForeignKeyChangedWithStaleReference_KeepsForeignKeyValue()
        {
            var entitySet = new EntitySet<SyncLegacyParent>(DbAccess);

            // 外键与引用一致地插入，模拟 FindAsync 含 Include 加载的实体
            var parent = new SyncLegacyParent
            {
                TargetLegacyId = 777,
                Target = new SyncLegacyTarget { Id = 1, LegacyId = 777 }
            };
            await entitySet.InsertAsync(parent);

            // 用户仅修改外键列，导航引用仍指向旧目标（过期引用）
            parent.TargetLegacyId = 888;

            await entitySet.UpdateAsync(parent);

            // 外键列应保持用户修改值 888，不被过期引用回滚为 777（内存与数据库均一致）
            Assert.Equal(888, parent.TargetLegacyId);
            var reloaded = await entitySet.FindAsync(parent.Id);
            Assert.Equal(888, reloaded.TargetLegacyId);
        }

        /// <summary>
        /// 值优先级规则（插入路径）：外键已显式赋值且与引用的 OtherKey 不一致时，以外键为准。
        /// </summary>
        [Fact]
        public async Task Insert_ForeignKeySetWithDifferentReference_KeepsForeignKeyValue()
        {
            var entitySet = new EntitySet<SyncLegacyParent>(DbAccess);

            // 外键已显式赋值且与引用的 OtherKey 不一致：以外键为准
            var parent = new SyncLegacyParent
            {
                TargetLegacyId = 999,
                Target = new SyncLegacyTarget { Id = 1, LegacyId = 777 }
            };

            await entitySet.InsertAsync(parent);

            // 外键列应保持显式值 999，不被引用的 777 覆盖
            Assert.Equal(999, parent.TargetLegacyId);
        }
    }
}
