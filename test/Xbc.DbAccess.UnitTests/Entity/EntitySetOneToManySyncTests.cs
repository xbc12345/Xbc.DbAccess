using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 1:N 集合导航外键同步（SyncOneToManyForeignKeys）的测试：
    /// 验证父实体插入（含自增回填）/更新时，按值优先级同步已赋值子集合中每个子实体的外键列（OtherKey 列）——
    /// 子外键为默认值时回填父键值（ThisKey 列），已有值时以子外键为准（键值不一致时输出诊断警告）。
    /// </summary>
    public class EntitySetOneToManySyncTests : EntityTestBase
    {
        #region 测试实体

        [XbcDbEntity.Table("O2OSyncOrders")]
        public class O2OSyncOrder
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public int UserId { get; set; }
        }

        /// <summary>
        /// 自增主键父实体：1:N 同步须在插入完成（自增回填）后执行。
        /// </summary>
        [XbcDbEntity.Table("O2OSyncUsers")]
        public class O2OSyncUser
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; }

            // ThisKey 推断为父主键 Id，OtherKey 显式指定为子表外键 UserId
            [Navigation(OtherKey = "UserId")]
            public List<O2OSyncOrder> Orders { get; set; }
        }

        /// <summary>
        /// 非自增主键父实体：走批量插入的方言逐条路径（InsertRangeAsync，SQLite）。
        /// </summary>
        [XbcDbEntity.Table("O2OSyncManualUsers")]
        public class O2OSyncManualUser
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            public string Name { get; set; }

            [Navigation(OtherKey = "UserId")]
            public List<O2OSyncOrder> Orders { get; set; }
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE O2OSyncUsers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                );
                CREATE TABLE O2OSyncOrders (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL
                );
                CREATE TABLE O2OSyncManualUsers (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL
                );
            ");
        }

        [Fact]
        public async Task Insert_OneToMany_SyncsChildForeignKeysAfterIdentityBackfill()
        {
            var entitySet = new EntitySet<O2OSyncUser>(DbAccess);

            var user = new O2OSyncUser
            {
                Name = "u1",
                Orders = new List<O2OSyncOrder>
                {
                    new O2OSyncOrder(),
                    new O2OSyncOrder(),
                }
            };

            await entitySet.InsertAsync(user);

            // 父自增 Id 已回填，子实体外键列应同步为该值
            Assert.True(user.Id > 0);
            Assert.All(user.Orders, o => Assert.Equal(user.Id, o.UserId));
        }

        [Fact]
        public async Task Insert_OneToMany_KeepsExistingChildForeignKey()
        {
            var entitySet = new EntitySet<O2OSyncUser>(DbAccess);

            var user = new O2OSyncUser
            {
                Name = "u2",
                Orders = new List<O2OSyncOrder>
                {
                    new O2OSyncOrder(),// 外键为默认值：由父键回填
                    new O2OSyncOrder { UserId = 999 },// 已指向其他父实体：保留子外键值
                }
            };

            await entitySet.InsertAsync(user);

            // 外键为默认值的子实体回填父键值；已有值的子实体以子外键为准（子实体可能已挪出本集合）
            Assert.True(user.Id > 0);
            Assert.Equal(user.Id, user.Orders[0].UserId);
            Assert.Equal(999, user.Orders[1].UserId);
        }

        [Fact]
        public async Task InsertRange_NoIdentity_SyncsChildForeignKeys()
        {
            var entitySet = new EntitySet<O2OSyncManualUser>(DbAccess);

            var users = new List<O2OSyncManualUser>
            {
                new O2OSyncManualUser
                {
                    Id = 1, Name = "m1",
                    Orders = new List<O2OSyncOrder> { new O2OSyncOrder() }
                },
                new O2OSyncManualUser
                {
                    Id = 2, Name = "m2",
                    Orders = new List<O2OSyncOrder> { new O2OSyncOrder(), new O2OSyncOrder() }
                },
            };

            await entitySet.InsertRangeAsync(users);

            // 批量插入路径（多值或方言逐条策略）同样在插入后同步子实体外键列
            Assert.All(users[0].Orders, o => Assert.Equal(1, o.UserId));
            Assert.All(users[1].Orders, o => Assert.Equal(2, o.UserId));
        }

        [Fact]
        public async Task Update_OneToMany_SyncsChildForeignKeys()
        {
            var entitySet = new EntitySet<O2OSyncUser>(DbAccess);

            var user = new O2OSyncUser
            {
                Id = 5,
                Name = "u5",
                Orders = new List<O2OSyncOrder> { new O2OSyncOrder() }
            };

            await entitySet.UpdateAsync(user);

            // 更新路径同样回填默认值子实体的外键列（纯内存，与受影响行数无关）
            Assert.Equal(5, user.Orders[0].UserId);
        }

        [Fact]
        public async Task Update_OneToMany_ChildForeignKeyChangedWithStaleCollection_KeepsChildForeignKeyValue()
        {
            var entitySet = new EntitySet<O2OSyncUser>(DbAccess);

            // 模拟挪出集合场景：Include 加载后用户改了子外键指向其他父实体，但未从本集合移除
            var user = new O2OSyncUser
            {
                Id = 5,
                Name = "u5",
                Orders = new List<O2OSyncOrder> { new O2OSyncOrder { Id = 1, UserId = 888 } }
            };

            await entitySet.UpdateAsync(user);

            // 子外键已有值且与父键不一致：以子外键为准（子实体可能已挪出本集合），不静默回滚用户修改
            Assert.Equal(888, user.Orders[0].UserId);
        }

        [Fact]
        public async Task Insert_OneToMany_NullOrEmptyCollection_NoError()
        {
            var entitySet = new EntitySet<O2OSyncUser>(DbAccess);

            // null 集合导航
            var user1 = new O2OSyncUser { Name = "n1" };
            await entitySet.InsertAsync(user1);

            // 空集合
            var user2 = new O2OSyncUser { Name = "n2", Orders = new List<O2OSyncOrder>() };
            await entitySet.InsertAsync(user2);

            // 集合含 null 元素：跳过，不抛异常
            var user3 = new O2OSyncUser { Name = "n3", Orders = new List<O2OSyncOrder> { null } };
            await entitySet.InsertAsync(user3);

            Assert.True(user1.Id > 0);
            Assert.True(user2.Id > 0);
            Assert.True(user3.Id > 0);
        }
    }
}
