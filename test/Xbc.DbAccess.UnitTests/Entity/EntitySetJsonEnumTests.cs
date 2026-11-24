using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 列级存储格式开关的 SQLite 真库集成测试：
    /// [Column(EnumAsString = true)]（枚举名称字符串存储）与 [Column(IsJson = true)]（复杂类型 JSON 文本存储）
    /// 的读写往返、谓词过滤、条件更新、bulk 插入与构建期校验异常。
    /// </summary>
    public class EntitySetJsonEnumTests : EntityTestBase
    {
        #region 测试实体与类型

        public enum UserStatus
        {
            Active = 1,
            Suspended = 2,
            Closed = 3
        }

        public class Address
        {
            public string City { get; set; } = string.Empty;

            public string Street { get; set; } = string.Empty;
        }

        [XbcDbEntity.Table("JsonEnumUsers")]
        public class UserEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;

            [XbcDbEntity.Column(EnumAsString = true)]
            public UserStatus Status { get; set; }

            [XbcDbEntity.Column(EnumAsString = true)]
            public UserStatus? NextStatus { get; set; }

            public UserStatus PlainStatus { get; set; }

            [XbcDbEntity.Column(IsJson = true)]
            public Address Address { get; set; }

            [XbcDbEntity.Column(IsJson = true)]
            public List<string> Tags { get; set; }
        }

        // 仅用于构建期校验异常测试的实体（不建表，构建元数据即触发校验）

        [XbcDbEntity.Table("InvalidEnumAsStringUsers")]
        public class InvalidEnumAsStringEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            [XbcDbEntity.Column(EnumAsString = true)]
            public string Name { get; set; }
        }

        [XbcDbEntity.Table("InvalidJsonFlagUsers")]
        public class InvalidJsonFlagEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            [XbcDbEntity.Column(IsJson = true, ConverterType = typeof(object))]
            public string Name { get; set; }
        }

        public struct Point
        {
            public int X { get; set; }

            public int Y { get; set; }
        }

        [XbcDbEntity.Table("InvalidValueTypeJsonUsers")]
        public class InvalidValueTypeJsonEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            [XbcDbEntity.Column(IsJson = true)]
            public Point Point { get; set; }
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE JsonEnumUsers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    NextStatus TEXT NULL,
                    PlainStatus INTEGER NOT NULL,
                    Address TEXT NULL,
                    Tags TEXT NULL
                );
            ");
        }

        #region 枚举字符串存储

        [Fact]
        public async Task EnumAsString_Insert_StoresNameStringInDatabase()
        {
            await DbAccess.Set<UserEntity>().InsertAsync(new UserEntity
            {
                Name = "Alice",
                Status = UserStatus.Suspended,
                PlainStatus = UserStatus.Suspended
            });

            var statusValue = (string)await DbAccess.ExecuteScalarAsync("SELECT Status FROM JsonEnumUsers WHERE Id = 1");
            var plainValue = (long)await DbAccess.ExecuteScalarAsync("SELECT PlainStatus FROM JsonEnumUsers WHERE Id = 1");

            Assert.Equal("Suspended", statusValue);
            Assert.Equal(2L, plainValue);
        }

        [Fact]
        public async Task EnumAsString_Query_RoundTripsEnum()
        {
            await DbAccess.Set<UserEntity>().InsertAsync(new UserEntity { Name = "Alice", Status = UserStatus.Closed });

            var user = await DbAccess.Set<UserEntity>().FindAsync(1);

            Assert.Equal(UserStatus.Closed, user.Status);
        }

        [Fact]
        public async Task EnumAsString_Predicate_FiltersByEnumValue()
        {
            var set = DbAccess.Set<UserEntity>();
            await set.InsertAsync(new UserEntity { Name = "Alice", Status = UserStatus.Active });
            await set.InsertAsync(new UserEntity { Name = "Bob", Status = UserStatus.Closed });
            await set.InsertAsync(new UserEntity { Name = "Cara", Status = UserStatus.Active });

            var activeCount = await set.Where(u => u.Status == UserStatus.Active).CountAsync();
            var closedCount = await set.Where(u => u.Status == UserStatus.Closed).CountAsync();

            Assert.Equal(2L, activeCount);
            Assert.Equal(1L, closedCount);
        }

        [Fact]
        public async Task EnumAsString_NullableEnum_RoundTripsNullAndValue()
        {
            var set = DbAccess.Set<UserEntity>();
            await set.InsertAsync(new UserEntity { Name = "Alice", Status = UserStatus.Active, NextStatus = null });
            await set.InsertAsync(new UserEntity { Name = "Bob", Status = UserStatus.Active, NextStatus = UserStatus.Closed });

            var alice = await set.FindAsync(1);
            var bob = await set.FindAsync(2);

            Assert.Null(alice.NextStatus);
            Assert.Equal(UserStatus.Closed, bob.NextStatus);
        }

        [Fact]
        public async Task EnumAsString_ConditionalUpdate_UpdatesByEnumPredicate()
        {
            var set = DbAccess.Set<UserEntity>();
            await set.InsertAsync(new UserEntity { Name = "Alice", Status = UserStatus.Active });

            await set.UpdateAsync(u => new UserEntity { Status = UserStatus.Suspended }, u => u.Status == UserStatus.Active);

            var user = await set.FindAsync(1);
            Assert.Equal(UserStatus.Suspended, user.Status);
        }

        [Fact]
        public async Task EnumAsString_BulkInsert_StoresNameString()
        {
            var set = DbAccess.Set<UserEntity>();
            await set.BulkInsertAsync(new[]
            {
                new UserEntity { Name = "Alice", Status = UserStatus.Active },
                new UserEntity { Name = "Bob", Status = UserStatus.Closed }
            });

            var aliceStatus = (string)await DbAccess.ExecuteScalarAsync("SELECT Status FROM JsonEnumUsers WHERE Name = 'Alice'");
            var bobStatus = (string)await DbAccess.ExecuteScalarAsync("SELECT Status FROM JsonEnumUsers WHERE Name = 'Bob'");

            Assert.Equal("Active", aliceStatus);
            Assert.Equal("Closed", bobStatus);
        }

        #endregion

        #region JSON 列

        [Fact]
        public async Task Json_InsertThenQuery_RoundTripsObject()
        {
            var set = DbAccess.Set<UserEntity>();
            await set.InsertAsync(new UserEntity
            {
                Name = "Alice",
                Status = UserStatus.Active,
                Address = new Address { City = "Shanghai", Street = "Nanjing Road" },
                Tags = new List<string> { "vip", "beta" }
            });

            var storedJson = (string)await DbAccess.ExecuteScalarAsync("SELECT Address FROM JsonEnumUsers WHERE Id = 1");
            var user = await set.FindAsync(1);

            Assert.Contains("\"City\":\"Shanghai\"", storedJson);
            Assert.Equal("Shanghai", user.Address.City);
            Assert.Equal("Nanjing Road", user.Address.Street);
            Assert.Equal(new[] { "vip", "beta" }, user.Tags.ToArray());
        }

        [Fact]
        public async Task Json_NullObject_StoresNullAndSkipsAssignment()
        {
            var set = DbAccess.Set<UserEntity>();
            await set.InsertAsync(new UserEntity { Name = "Alice", Status = UserStatus.Active, Address = null, Tags = null });

            var storedJson = await DbAccess.ExecuteScalarAsync("SELECT Address FROM JsonEnumUsers WHERE Id = 1");
            var user = await set.FindAsync(1);

            Assert.Equal(DBNull.Value, storedJson);
            Assert.Null(user.Address);
            Assert.Null(user.Tags);
        }

        [Fact]
        public async Task Json_ConditionalUpdate_UpdatesJsonColumn()
        {
            var set = DbAccess.Set<UserEntity>();
            await set.InsertAsync(new UserEntity
            {
                Name = "Alice",
                Status = UserStatus.Active,
                Address = new Address { City = "Beijing", Street = "Old Street" }
            });

            await set.UpdateAsync(
                u => new UserEntity { Address = new Address { City = "Shenzhen", Street = "New Street" } },
                u => u.Name == "Alice");

            var user = await set.FindAsync(1);
            Assert.Equal("Shenzhen", user.Address.City);
            Assert.Equal("New Street", user.Address.Street);
        }

        [Fact]
        public async Task Json_ColumnInPredicate_Throws()
        {
            var set = DbAccess.Set<UserEntity>();

            await Assert.ThrowsAsync<NotSupportedException>(
                () => set.Where(u => u.Address == new Address()).CountAsync());
        }

        [Fact]
        public void Json_ColumnAsKeysetKey_Throws()
        {
            // JSON 列作排序键（keyset 键集）在 OrderBy 端即被拒绝：序列化文本序不等于结构化内容序
            Assert.Throws<NotSupportedException>(
                () => DbAccess.Set<UserEntity>().AsQuery().OrderBy(u => u.Address));
        }

        [Fact]
        public async Task Json_BulkInsert_StoresJsonText()
        {
            var set = DbAccess.Set<UserEntity>();
            await set.BulkInsertAsync(new[]
            {
                new UserEntity
                {
                    Name = "Alice",
                    Status = UserStatus.Active,
                    Address = new Address { City = "Shanghai", Street = "Nanjing Road" },
                    Tags = new List<string> { "vip" }
                }
            });

            var storedJson = (string)await DbAccess.ExecuteScalarAsync("SELECT Address FROM JsonEnumUsers WHERE Id = 1");
            var user = await set.FindAsync(1);

            Assert.Contains("\"City\":\"Shanghai\"", storedJson);
            Assert.Equal("Shanghai", user.Address.City);
            Assert.Equal(new[] { "vip" }, user.Tags.ToArray());
        }

        #endregion

        #region 构建期校验

        [Fact]
        public void EnumAsString_NonEnumType_ThrowsAtMetadataBuild()
        {
            Assert.Throws<InvalidOperationException>(() => DbAccess.Set<InvalidEnumAsStringEntity>());
        }

        [Fact]
        public void IsJson_WithConverterType_ThrowsAtMetadataBuild()
        {
            Assert.Throws<InvalidOperationException>(() => DbAccess.Set<InvalidJsonFlagEntity>());
        }

        [Fact]
        public void IsJson_ValueType_ThrowsAtMetadataBuild()
        {
            Assert.Throws<InvalidOperationException>(() => DbAccess.Set<InvalidValueTypeJsonEntity>());
        }

        #endregion
    }
}
