using System.Data;
using Xbc.DbAccess.Entity;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// ColumnMetadata 的单元测试。
    /// </summary>
    public class ColumnMetadataTests
    {
        #region 测试实体

        public class TestEntity
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int? Age { get; set; }
            public DateTime CreatedAt { get; set; }
            public Guid? ExternalId { get; set; }
        }

        /// <summary>
        /// 构建测试用的 ColumnMetadata 实例（通过 Builder 构建，符合生产代码路径）。
        /// </summary>
        private static ColumnMetadata BuildMetadata(string propertyName, Action<ColumnMetadata.Builder> configure = null)
        {
            var property = typeof(TestEntity).GetProperty(propertyName);
            var builder = new ColumnMetadata.Builder { PropertyInfo = property };
            configure?.Invoke(builder);
            return builder.Build();
        }

        #endregion

        #region 属性测试

        [Fact]
        public void PropertyName_应返回属性名()
        {
            var metadata = BuildMetadata("Name");
            Assert.Equal("Name", metadata.PropertyName);
        }

        [Fact]
        public void PropertyType_应返回属性类型()
        {
            var metadata = BuildMetadata("Id");
            Assert.Equal(typeof(int), metadata.PropertyType);
        }

        [Fact]
        public void ColumnName_设置后_应返回设置的值()
        {
            var metadata = BuildMetadata("Name", b => b.ColumnName = "user_name");
            Assert.Equal("user_name", metadata.ColumnName);
        }

        [Fact]
        public void IsPrimaryKey_默认应为False()
        {
            var metadata = BuildMetadata("Id");
            Assert.False(metadata.IsPrimaryKey);
        }

        [Fact]
        public void IsPrimaryKey_设置为True后_应返回True()
        {
            var metadata = BuildMetadata("Id", b => b.IsPrimaryKey = true);
            Assert.True(metadata.IsPrimaryKey);
        }

        [Fact]
        public void IsIdentity_默认应为False()
        {
            var metadata = BuildMetadata("Id");
            Assert.False(metadata.IsIdentity);
        }

        [Fact]
        public void IsIdentity_设置为True后_应返回True()
        {
            var metadata = BuildMetadata("Id", b => b.IsIdentity = true);
            Assert.True(metadata.IsIdentity);
        }

        #endregion

        #region 可空类型测试

        [Fact]
        public void UnderlyingType_可空Int_应为Int32()
        {
            var metadata = BuildMetadata("Age");
            Assert.Equal(typeof(int), metadata.UnderlyingType);
        }

        [Fact]
        public void UnderlyingType_可空Guid_应为Guid()
        {
            var metadata = BuildMetadata("ExternalId");
            Assert.Equal(typeof(Guid), metadata.UnderlyingType);
        }

        #endregion

        #region DbNullHandling 测试

        [Fact]
        public void DbNullHandling_默认应为Default()
        {
            var metadata = BuildMetadata("Name");
            Assert.Equal(DbNullHandling.Default, metadata.DbNullHandling);
        }

        [Theory]
        [InlineData(DbNullHandling.Default)]
        [InlineData(DbNullHandling.ThrowException)]
        [InlineData(DbNullHandling.UseDefaultValue)]
        [InlineData(DbNullHandling.SkipAssignment)]
        public void DbNullHandling_设置后_应返回设置的值(DbNullHandling handling)
        {
            var metadata = BuildMetadata("Name", b => b.DbNullHandling = handling);
            Assert.Equal(handling, metadata.DbNullHandling);
        }

        #endregion

        #region DbType 测试

        [Fact]
        public void DbType_未设置_应为Null()
        {
            var metadata = BuildMetadata("Name");
            Assert.Null(metadata.DbType);
        }

        [Theory]
        [InlineData(DbType.String)]
        [InlineData(DbType.Int32)]
        [InlineData(DbType.DateTime)]
        [InlineData(DbType.Guid)]
        public void DbType_设置后_应返回设置的值(DbType dbType)
        {
            var metadata = BuildMetadata("Name", b => b.DbType = dbType);
            Assert.Equal(dbType, metadata.DbType);
        }

        #endregion

        #region ConvertToDb 测试

        [Fact]
        public void ConvertToDb_Null值_应返回DBNull()
        {
            var metadata = BuildMetadata("Name");
            var result = metadata.ConvertToDb(null);
            Assert.Equal(DBNull.Value, result);
        }

        [Fact]
        public void ConvertToDb_字符串值_应返回相同值()
        {
            var metadata = BuildMetadata("Name");
            var result = metadata.ConvertToDb("Test");
            Assert.Equal("Test", result);
        }

        [Fact]
        public void ConvertToDb_整数值_应返回相同值()
        {
            var metadata = BuildMetadata("Id");
            var result = metadata.ConvertToDb(42);
            Assert.Equal(42, result);
        }

        [Fact]
        public void ConvertToDb_DateTime值_应返回DateTime()
        {
            var metadata = BuildMetadata("CreatedAt");
            var dateTime = new DateTime(2024, 1, 15);
            var result = metadata.ConvertToDb(dateTime);
            Assert.Equal(dateTime, result);
        }

        [Fact]
        public void ConvertToDb_Guid值_应返回Guid()
        {
            var metadata = BuildMetadata("ExternalId");
            var guid = Guid.NewGuid();
            var result = metadata.ConvertToDb(guid);
            Assert.Equal(guid, result);
        }

        [Fact]
        public void ConvertToDb_可空Int有值_应返回整数值()
        {
            var metadata = BuildMetadata("Age");
            var result = metadata.ConvertToDb(25);
            Assert.Equal(25, result);
        }

        [Fact]
        public void ConvertToDb_可空Int为Null_应返回DBNull()
        {
            var metadata = BuildMetadata("Age");
            var result = metadata.ConvertToDb(null);
            Assert.Equal(DBNull.Value, result);
        }

        #endregion

        #region GetValueForDb 测试

        [Fact]
        public void GetValueForDb_实体属性值_应返回转换后的值()
        {
            var metadata = BuildMetadata("Name");
            var entity = new TestEntity { Name = "TestName" };
            var result = metadata.GetValueForDb(entity);
            Assert.Equal("TestName", result);
        }

        [Fact]
        public void GetValueForDb_实体属性为Null_应返回DBNull()
        {
            var metadata = BuildMetadata("Name");
            var entity = new TestEntity { Name = null };
            var result = metadata.GetValueForDb(entity);
            Assert.Equal(DBNull.Value, result);
        }

        [Fact]
        public void GetValueForDb_整型属性值_应返回整数值()
        {
            var metadata = BuildMetadata("Id");
            var entity = new TestEntity { Id = 100 };
            var result = metadata.GetValueForDb(entity);
            Assert.Equal(100, result);
        }

        #endregion

        #region BackfillIdentity 测试

        [Fact]
        public void BackfillIdentity_整数值_应设置到实体属性()
        {
            var metadata = BuildMetadata("Id", b => b.IsPrimaryKey = true);
            var entity = new TestEntity();
            metadata.BackfillIdentity(entity, 42);
            Assert.Equal(42, entity.Id);
        }

        [Fact]
        public void BackfillIdentity_数值类型不一致_应兼容转换后赋值()
        {
            var metadata = BuildMetadata("Id", b => b.IsPrimaryKey = true);
            var entity = new TestEntity();
            // 模拟 SQLite LAST_INSERT_ROWID() 恒返回 Int64 的场景
            metadata.BackfillIdentity(entity, 42L);
            Assert.Equal(42, entity.Id);
        }

        #endregion

        #region 自定义转换器测试

        public class CustomGuidConverter
        {
            public Guid ConvertFrom(object dbValue)
            {
                if (dbValue is string str)
                    return Guid.Parse(str);
                return (Guid)dbValue;
            }

            public object ConvertTo(Guid value)
            {
                return value.ToString("N");
            }
        }

        [Fact]
        public void ConvertToDb_使用自定义转换器_应调用转换器()
        {
            var metadata = BuildMetadata("ExternalId", b =>
            {
                b.ConverterType = typeof(CustomGuidConverter);
                b.ConvertToMethod = typeof(CustomGuidConverter).GetMethod("ConvertTo");
            });

            var guid = Guid.NewGuid();
            var result = metadata.ConvertToDb(guid);
            Assert.Equal(guid.ToString("N"), result);
        }

        #endregion
    }
}
