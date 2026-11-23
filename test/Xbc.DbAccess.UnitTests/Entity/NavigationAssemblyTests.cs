using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 导航装配的补充测试：三级导航链端到端装配、父无子时的空导航语义、string 主键装配、
    /// 外键为 NULL 的子行不参与装配、可空外键为 0 时按默认值回填父键。
    /// </summary>
    public class NavigationAssemblyTests : EntityTestBase
    {
        #region 测试实体

        // 三级链 A→B→C：验证 Include(...).ThenInclude(...) 多级分裂查询装配
        [XbcDbEntity.Table("NavChainAs")]
        public class NavChainA
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int AId { get; set; }
            public string Name { get; set; }

            // 1:N 手动指定键：AId → NavChainB.AId（约定推断为 "NavChainAId"，不符合）
            [Navigation(OtherKey = "AId")]
            public List<NavChainB> Bs { get; set; }
        }

        [XbcDbEntity.Table("NavChainBs")]
        public class NavChainB
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int BId { get; set; }
            public int AId { get; set; }
            public string Name { get; set; }

            // 1:N 手动指定键：BId → NavChainC.BId
            [Navigation(OtherKey = "BId")]
            public List<NavChainC> Cs { get; set; }
        }

        [XbcDbEntity.Table("NavChainCs")]
        public class NavChainC
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int CId { get; set; }
            public int BId { get; set; }
            public string Name { get; set; }
        }

        // 父无子装配语义：部分父实体无子行，1:N 装配空列表（非 null）、1:1 保持 null
        [XbcDbEntity.Table("NavEmptyParents")]
        public class NavEmptyParent
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int ParentId { get; set; }
            public string Name { get; set; }

            [Navigation(OtherKey = "ParentId")]
            public List<NavEmptyChild> Children { get; set; }
        }

        [XbcDbEntity.Table("NavEmptyChildren")]
        public class NavEmptyChild
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int ChildId { get; set; }
            public int ParentId { get; set; }
            public string Name { get; set; }
        }

        [XbcDbEntity.Table("NavEmptyRefParents")]
        public class NavEmptyRefParent
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int ParentId { get; set; }
            public int TargetId { get; set; }

            // 1:1 约定推断：TargetId → NavEmptyTarget.TargetId
            [Navigation]
            public NavEmptyTarget Target { get; set; }
        }

        [XbcDbEntity.Table("NavEmptyTargets")]
        public class NavEmptyTarget
        {
            [XbcDbEntity.PrimaryKey]
            public int TargetId { get; set; }
            public string Name { get; set; }
        }

        // string 主键装配：约定推断 ThisKey=本方 string 主键，OtherKey=子表 string 外键
        [XbcDbEntity.Table("NavStringParents")]
        public class NavStringParent
        {
            [XbcDbEntity.PrimaryKey]
            public string NavStringParentId { get; set; }
            public string Name { get; set; }

            [Navigation]
            public List<NavStringChild> Children { get; set; }
        }

        [XbcDbEntity.Table("NavStringChildren")]
        public class NavStringChild
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int ChildId { get; set; }
            public string NavStringParentId { get; set; }
            public string Name { get; set; }
        }

        [XbcDbEntity.Table("NavStringRefOrders")]
        public class NavStringRefOrder
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int OrderId { get; set; }
            public string NavStringParentId { get; set; }

            // 1:1 手动指定键：NavStringParentId → NavStringParent.NavStringParentId
            [Navigation(ThisKey = "NavStringParentId", OtherKey = "NavStringParentId")]
            public NavStringParent Parent { get; set; }
        }

        // 可空外键：子行外键为 NULL 的不参与装配；外键为 0（int? 非 null）时按默认值由父键回填
        [XbcDbEntity.Table("NavOptionalParents")]
        public class NavOptionalParent
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int ParentId { get; set; }
            public string Name { get; set; }

            [Navigation(OtherKey = "ParentId")]
            public List<NavOptionalChild> Children { get; set; }
        }

        [XbcDbEntity.Table("NavOptionalChildren")]
        public class NavOptionalChild
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int ChildId { get; set; }
            public int? ParentId { get; set; }// 可空外键：未挂靠的子行在数据库中为 NULL
            public string Name { get; set; }
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE NavChainAs (
                    AId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                );
                INSERT INTO NavChainAs (Name) VALUES ('A1');
                INSERT INTO NavChainAs (Name) VALUES ('A2');
                INSERT INTO NavChainAs (Name) VALUES ('A3');

                CREATE TABLE NavChainBs (
                    BId INTEGER PRIMARY KEY AUTOINCREMENT,
                    AId INTEGER NOT NULL,
                    Name TEXT NOT NULL
                );
                INSERT INTO NavChainBs (AId, Name) VALUES (1, 'B1');
                INSERT INTO NavChainBs (AId, Name) VALUES (1, 'B2');
                INSERT INTO NavChainBs (AId, Name) VALUES (2, 'B3');

                CREATE TABLE NavChainCs (
                    CId INTEGER PRIMARY KEY AUTOINCREMENT,
                    BId INTEGER NOT NULL,
                    Name TEXT NOT NULL
                );
                INSERT INTO NavChainCs (BId, Name) VALUES (1, 'C1');
                INSERT INTO NavChainCs (BId, Name) VALUES (1, 'C2');
                INSERT INTO NavChainCs (BId, Name) VALUES (2, 'C3');

                CREATE TABLE NavEmptyParents (
                    ParentId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                );
                INSERT INTO NavEmptyParents (Name) VALUES ('有子');
                INSERT INTO NavEmptyParents (Name) VALUES ('无子1');
                INSERT INTO NavEmptyParents (Name) VALUES ('无子2');

                CREATE TABLE NavEmptyChildren (
                    ChildId INTEGER PRIMARY KEY AUTOINCREMENT,
                    ParentId INTEGER NOT NULL,
                    Name TEXT NOT NULL
                );
                INSERT INTO NavEmptyChildren (ParentId, Name) VALUES (1, '子1');

                CREATE TABLE NavEmptyRefParents (
                    ParentId INTEGER PRIMARY KEY AUTOINCREMENT,
                    TargetId INTEGER NOT NULL
                );
                INSERT INTO NavEmptyRefParents (TargetId) VALUES (1);
                INSERT INTO NavEmptyRefParents (TargetId) VALUES (2);
                INSERT INTO NavEmptyRefParents (TargetId) VALUES (3);

                CREATE TABLE NavEmptyTargets (
                    TargetId INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL
                );
                INSERT INTO NavEmptyTargets (TargetId, Name) VALUES (1, '目标1');

                CREATE TABLE NavStringParents (
                    NavStringParentId TEXT PRIMARY KEY,
                    Name TEXT NOT NULL
                );
                INSERT INTO NavStringParents (NavStringParentId, Name) VALUES ('P1', '父1');
                INSERT INTO NavStringParents (NavStringParentId, Name) VALUES ('P2', '父2');
                INSERT INTO NavStringParents (NavStringParentId, Name) VALUES ('P3', '父3');

                CREATE TABLE NavStringChildren (
                    ChildId INTEGER PRIMARY KEY AUTOINCREMENT,
                    NavStringParentId TEXT NOT NULL,
                    Name TEXT NOT NULL
                );
                INSERT INTO NavStringChildren (NavStringParentId, Name) VALUES ('P1', '字符串子1');
                INSERT INTO NavStringChildren (NavStringParentId, Name) VALUES ('P1', '字符串子2');
                INSERT INTO NavStringChildren (NavStringParentId, Name) VALUES ('P2', '字符串子3');

                CREATE TABLE NavStringRefOrders (
                    OrderId INTEGER PRIMARY KEY AUTOINCREMENT,
                    NavStringParentId TEXT NOT NULL
                );
                INSERT INTO NavStringRefOrders (NavStringParentId) VALUES ('P1');
                INSERT INTO NavStringRefOrders (NavStringParentId) VALUES ('P4');

                CREATE TABLE NavOptionalParents (
                    ParentId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                );
                INSERT INTO NavOptionalParents (Name) VALUES ('唯一父');

                CREATE TABLE NavOptionalChildren (
                    ChildId INTEGER PRIMARY KEY AUTOINCREMENT,
                    ParentId INTEGER,
                    Name TEXT NOT NULL
                );
                INSERT INTO NavOptionalChildren (ParentId, Name) VALUES (1, '挂靠子');
                INSERT INTO NavOptionalChildren (ParentId, Name) VALUES (NULL, '孤儿子1');
                INSERT INTO NavOptionalChildren (ParentId, Name) VALUES (NULL, '孤儿子2');
            ");
        }

        #region 三级导航链装配

        [Fact]
        public async Task Include_ThreeLevelChain_AssemblesAllLevels()
        {
            // 三级链 A→B→C：Include(A.Bs).ThenInclude(B.Cs)，各级均应按键装配到位
            var roots = await DbAccess.FromSql<NavChainA>("SELECT * FROM NavChainAs")
                .Include(a => a.Bs)
                .ThenInclude(b => b.Cs)
                .ToListAsync();

            Assert.Equal(3, roots.Count);

            var a1 = roots.First(x => x.AId == 1);
            Assert.NotNull(a1.Bs);
            Assert.Equal(2, a1.Bs.Count);

            // B1 → C1、C2
            var b1 = a1.Bs.First(x => x.BId == 1);
            Assert.Equal(2, b1.Cs.Count);
            Assert.Contains(b1.Cs, c => c.Name == "C1");
            Assert.Contains(b1.Cs, c => c.Name == "C2");

            // B2 → C3
            var b2 = a1.Bs.First(x => x.BId == 2);
            Assert.Single(b2.Cs);

            var a2 = roots.First(x => x.AId == 2);
            Assert.Single(a2.Bs);
            // B3 无 C：装配为空列表而非 null
            Assert.NotNull(a2.Bs[0].Cs);
            Assert.Empty(a2.Bs[0].Cs);

            // A3 无 B：装配为空列表而非 null
            var a3 = roots.First(x => x.AId == 3);
            Assert.NotNull(a3.Bs);
            Assert.Empty(a3.Bs);
        }

        #endregion

        #region 父无子的导航语义

        [Fact]
        public async Task Include_ParentWithoutChildren_AssignsEmptyList()
        {
            // 1:N：无子行的父实体装配空列表（非 null）
            var parents = await DbAccess.FromSql<NavEmptyParent>("SELECT * FROM NavEmptyParents")
                .Include(p => p.Children)
                .ToListAsync();

            Assert.Equal(3, parents.Count);

            var withChildren = parents.First(p => p.ParentId == 1);
            Assert.NotNull(withChildren.Children);
            Assert.Single(withChildren.Children);
            Assert.Equal("子1", withChildren.Children[0].Name);

            foreach (var parent in parents.Where(p => p.ParentId != 1))
            {
                Assert.NotNull(parent.Children);
                Assert.Empty(parent.Children);
            }
        }

        [Fact]
        public async Task Include_ParentWithoutReference_KeepsNull()
        {
            // 1:1：未命中引用实体的父实体导航属性保持 null
            var parents = await DbAccess.FromSql<NavEmptyRefParent>("SELECT * FROM NavEmptyRefParents")
                .Include(p => p.Target)
                .ToListAsync();

            Assert.Equal(3, parents.Count);

            var matched = parents.First(p => p.ParentId == 1);
            Assert.NotNull(matched.Target);
            Assert.Equal("目标1", matched.Target.Name);

            Assert.Null(parents.First(p => p.ParentId == 2).Target);
            Assert.Null(parents.First(p => p.ParentId == 3).Target);
        }

        #endregion

        #region string 主键装配

        [Fact]
        public async Task Include_StringPrimaryKey_OneToMany_AssemblesByKey()
        {
            // string 主键的 1:N 约定推断与装配：ThisKey=本方 string 主键，OtherKey=子表 string 外键
            var parents = await DbAccess.FromSql<NavStringParent>("SELECT * FROM NavStringParents")
                .Include(p => p.Children)
                .ToListAsync();

            Assert.Equal(3, parents.Count);

            var p1 = parents.First(p => p.NavStringParentId == "P1");
            Assert.Equal(2, p1.Children.Count);
            Assert.Contains(p1.Children, c => c.Name == "字符串子1");
            Assert.Contains(p1.Children, c => c.Name == "字符串子2");

            var p2 = parents.First(p => p.NavStringParentId == "P2");
            Assert.Single(p2.Children);

            // P3 无子：装配空列表
            var p3 = parents.First(p => p.NavStringParentId == "P3");
            Assert.NotNull(p3.Children);
            Assert.Empty(p3.Children);
        }

        [Fact]
        public async Task Include_StringForeignKey_OneToOne_AssemblesByKey()
        {
            // 1:1 + string 外键：命中目标的装配引用，未命中的保持 null
            var orders = await DbAccess.FromSql<NavStringRefOrder>("SELECT * FROM NavStringRefOrders")
                .Include(o => o.Parent)
                .ToListAsync();

            Assert.Equal(2, orders.Count);

            var matched = orders.First(o => o.NavStringParentId == "P1");
            Assert.NotNull(matched.Parent);
            Assert.Equal("父1", matched.Parent.Name);

            var unmatched = orders.First(o => o.NavStringParentId == "P4");
            Assert.Null(unmatched.Parent);
        }

        #endregion

        #region 外键为 NULL 的子行

        [Fact]
        public async Task Include_NullForeignKeyChildren_AreNotAssembled()
        {
            // 外键为 NULL 的子行不参与装配：仅挂靠子行进入父实体集合，孤儿行不出现在任何父实体
            var parents = await DbAccess.FromSql<NavOptionalParent>("SELECT * FROM NavOptionalParents")
                .Include(p => p.Children)
                .ToListAsync();

            Assert.Single(parents);
            var parent = parents[0];
            Assert.NotNull(parent.Children);
            Assert.Single(parent.Children);
            Assert.Equal("挂靠子", parent.Children[0].Name);
        }

        [Fact]
        public void PopulateChildren_NullForeignKeyChildren_AreSkipped()
        {
            // 直接调用装配器：外键为 null 的子实体不参与分组，父实体仅装配到外键非 null 的子实体
            var metadata = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(NavOptionalParent));
            var nav = metadata.GetNavigation("Children");

            var parent = new NavOptionalParent { ParentId = 1, Name = "父" };
            var children = new List<object>
            {
                new NavOptionalChild { ChildId = 1, ParentId = 1, Name = "挂靠子" },
                new NavOptionalChild { ChildId = 2, ParentId = null, Name = "孤儿子" },
            };

            XbcDbEntity.NavigationAssembler.PopulateChildren(new List<object> { parent }, nav, children);

            Assert.NotNull(parent.Children);
            Assert.Single(parent.Children);
            Assert.Equal(1, parent.Children[0].ChildId);
        }

        #endregion

        #region 可空外键默认值回填

        [Fact]
        public async Task Insert_NullableForeignKeyZero_IsTreatedAsDefaultAndBackfilled()
        {
            // int? 外键值为 0（非 null）时应判为默认值：父实体插入（自增回填）后由父键回填到子实体外键
            var entitySet = new EntitySet<NavOptionalParent>(DbAccess);

            var parent = new NavOptionalParent
            {
                Name = "回填父",
                Children = new List<NavOptionalChild>
                {
                    new NavOptionalChild { Name = "零外键子", ParentId = 0 },
                }
            };

            await entitySet.InsertAsync(parent);

            Assert.True(parent.ParentId > 0);
            // 修复前：int? 的 0 被判为非默认值（按 Nullable<int> 取默认值得到 null），外键不回填
            Assert.True(parent.Children[0].ParentId == parent.ParentId);
        }

        #endregion
    }
}
