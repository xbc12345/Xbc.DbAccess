using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using System.Data.Common;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    public class EntityMapperTests
    {
        #region 测试实体类

        public class SimpleEntity
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int Age { get; set; }
        }

        public class EntityWithNullable
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int? OptionalValue { get; set; }
            public DateTime? OptionalDate { get; set; }
        }

        /// <summary>
        /// 带 ProviderException 列的实体（Number 跳过 NULL 检查，Name 可空走默认行为）。
        /// </summary>
        public class EntityWithProviderException
        {
            [XbcDbEntity.Column(DbNullHandling = XbcDbEntity.DbNullHandling.ProviderException)]
            public int Number { get; set; }

            public string Name { get; set; }
        }

        /// <summary>
        /// 带 UseDefaultValue 列的实体（显式声明 NULL 映射为 default(T) 的旧默认语义）。
        /// </summary>
        public class EntityWithUseDefaultValue
        {
            [XbcDbEntity.Column(DbNullHandling = XbcDbEntity.DbNullHandling.UseDefaultValue)]
            public int Number { get; set; }

            public string Name { get; set; }
        }

        /// <summary>
        /// 带 Table 特性的实体
        /// </summary>
        [XbcDbEntity.Table("Users")]
        public class EntityWithTableAttribute
        {
            [XbcDbEntity.PrimaryKey("user_id")]
            public int Id { get; set; }

            [XbcDbEntity.Column("user_name")]
            public string UserName { get; set; }

            [XbcDbEntity.Column("email")]
            public string Email { get; set; }
        }

        /// <summary>
        /// 带忽略列的实体
        /// </summary>
        public class EntityWithIgnoredColumn
        {
            public int Id { get; set; }
            public string Name { get; set; }

            [XbcDbEntity.NotMapped]
            public string ComputedProperty => $"{Id}-{Name}";

            [XbcDbEntity.NotMapped]
            public int TempData { get; set; }
        }

        /// <summary>
        /// 带自增列的实体
        /// </summary>
        [XbcDbEntity.Table("Products")]
        public class EntityWithIdentity
        {
            [XbcDbEntity.PrimaryKey("product_id", IsIdentity = true)]
            public int ProductId { get; set; }

            [XbcDbEntity.Column("product_name")]
            public string ProductName { get; set; }

            [XbcDbEntity.Column("price")]
            public decimal Price { get; set; }
        }
        #endregion

        #region EntityMapper<T> 泛型版本测试 - 参数验证

        [Fact]
        public async Task Map_null读取器_应抛出ArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => XbcDbEntity.EntityMapper<SimpleEntity>.MapAsync(null!));
        }

        [Fact]
        public async Task MapList_null读取器_应抛出ArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => XbcDbEntity.EntityMapper<SimpleEntity>.MapListAsync(null!));
        }

        [Fact]
        public void ClearCache_调用后_应清除缓存()
        {
            // 清除缓存不应该抛出异常
            var exception = Record.Exception(() => XbcDbEntity.EntityMapper<SimpleEntity>.ClearCache());
            Assert.Null(exception);
        }

        #endregion

        #region EntityMapper 非泛型版本测试 - 参数验证

        [Fact]
        public async Task Map_非泛型版本_null类型_应抛出ArgumentNullException()
        {
            var mockReader = new Mock<DbDataReader>();
            await Assert.ThrowsAsync<ArgumentNullException>(() => XbcDbEntity.EntityMapper.MapAsync(null!, mockReader.Object));
        }

        [Fact]
        public async Task Map_非泛型版本_null读取器_应抛出ArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => XbcDbEntity.EntityMapper.MapAsync(typeof(SimpleEntity), null!));
        }

        [Fact]
        public async Task MapList_非泛型版本_null类型_应抛出ArgumentNullException()
        {
            var mockReader = new Mock<DbDataReader>();
            await Assert.ThrowsAsync<ArgumentNullException>(() => XbcDbEntity.EntityMapper.MapListAsync(null!, mockReader.Object));
        }

        [Fact]
        public async Task MapList_非泛型版本_null读取器_应抛出ArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => XbcDbEntity.EntityMapper.MapListAsync(typeof(SimpleEntity), null!));
        }

        [Fact]
        public void ClearCache_非泛型版本_null类型_应抛出ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => XbcDbEntity.EntityMapper.ClearCache(null!));
        }

        [Fact]
        public void ClearCache_非泛型版本_指定类型_应清除该类型缓存()
        {
            // 清除缓存不应该抛出异常
            var exception = Record.Exception(() => XbcDbEntity.EntityMapper.ClearCache(typeof(SimpleEntity)));
            Assert.Null(exception);
        }

        [Fact]
        public void ClearAllCache_应清除所有类型缓存()
        {
            // 清除所有缓存不应该抛出异常
            var exception = Record.Exception(() => XbcDbEntity.EntityMapper.ClearAllCache());
            Assert.Null(exception);
        }

        #endregion

        #region ProviderException 映射测试

        /// <summary>
        /// 构建带单行数据的模拟读取器（Number 为 int 列，Name 为 string 列）。
        /// </summary>
        private static Mock<DbDataReader> CreateProviderExceptionReader()
        {
            var mockReader = new Mock<DbDataReader>();
            mockReader.Setup(r => r.FieldCount).Returns(2);
            mockReader.Setup(r => r.GetName(0)).Returns("Number");
            mockReader.Setup(r => r.GetName(1)).Returns("Name");
            mockReader.Setup(r => r.GetFieldType(0)).Returns(typeof(int));
            mockReader.Setup(r => r.GetFieldType(1)).Returns(typeof(string));
            mockReader.SetupSequence(r => r.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
            return mockReader;
        }

        [Fact]
        public async Task MapListAsync_ProviderException非空值_应正常映射且跳过IsDBNull检查()
        {
            var mockReader = CreateProviderExceptionReader();
            mockReader.Setup(r => r.GetInt32(0)).Returns(42);
            mockReader.Setup(r => r.GetString(1)).Returns("测试");
            mockReader.Setup(r => r.IsDBNull(1)).Returns(false);

            var result = await XbcDbEntity.EntityMapper<EntityWithProviderException>.MapListAsync(mockReader.Object);

            Assert.Single(result);
            Assert.Equal(42, result[0].Number);
            Assert.Equal("测试", result[0].Name);
            // Number 列声明了 ProviderException，映射委托不应调用 IsDBNull 检查
            mockReader.Verify(r => r.IsDBNull(0), Times.Never);
        }

        [Fact]
        public async Task MapListAsync_ProviderException遇到NULL_应透传提供程序异常()
        {
            var mockReader = CreateProviderExceptionReader();
            // 模拟 ADO.NET 提供程序对 NULL 列调用类型化读取方法时抛出的原生异常
            mockReader.Setup(r => r.GetInt32(0)).Throws(new InvalidOperationException("Data is Null. This method or property cannot be called on Null values."));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => XbcDbEntity.EntityMapper<EntityWithProviderException>.MapListAsync(mockReader.Object));
            Assert.Contains("Data is Null", exception.Message);// 确认是读取方法抛出的异常，而非其他来源
        }

        /// <summary>
        /// 构建带单行数据的模拟读取器（Id 为 int 列，Name 为 string 列，Age 为 int 列）。
        /// </summary>
        private static Mock<DbDataReader> CreateSimpleEntityReader()
        {
            var mockReader = new Mock<DbDataReader>();
            mockReader.Setup(r => r.FieldCount).Returns(3);
            mockReader.Setup(r => r.GetName(0)).Returns("Id");
            mockReader.Setup(r => r.GetName(1)).Returns("Name");
            mockReader.Setup(r => r.GetName(2)).Returns("Age");
            mockReader.Setup(r => r.GetFieldType(0)).Returns(typeof(int));
            mockReader.Setup(r => r.GetFieldType(1)).Returns(typeof(string));
            mockReader.Setup(r => r.GetFieldType(2)).Returns(typeof(int));
            mockReader.SetupSequence(r => r.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
            return mockReader;
        }

        [Fact]
        public async Task MapListAsync_默认行为非可空列_应跳过IsDBNull检查()
        {
            var mockReader = CreateSimpleEntityReader();
            mockReader.Setup(r => r.GetInt32(0)).Returns(1);
            mockReader.Setup(r => r.GetString(1)).Returns("测试");
            mockReader.Setup(r => r.GetInt32(2)).Returns(25);
            mockReader.Setup(r => r.IsDBNull(1)).Returns(false);

            var result = await XbcDbEntity.EntityMapper<SimpleEntity>.MapListAsync(mockReader.Object);

            Assert.Single(result);
            Assert.Equal(1, result[0].Id);
            Assert.Equal("测试", result[0].Name);
            Assert.Equal(25, result[0].Age);
            // 默认行为下非可空值类型列（Id、Age）不应调用 IsDBNull 检查
            mockReader.Verify(r => r.IsDBNull(0), Times.Never);
            mockReader.Verify(r => r.IsDBNull(2), Times.Never);
        }

        [Fact]
        public async Task MapListAsync_默认行为非可空列遇NULL_应透传提供程序异常()
        {
            var mockReader = CreateSimpleEntityReader();
            mockReader.Setup(r => r.GetInt32(0)).Returns(1);
            mockReader.Setup(r => r.GetString(1)).Returns("测试");
            mockReader.Setup(r => r.IsDBNull(1)).Returns(false);
            // 模拟 Age 列为 NULL 时提供程序抛出的原生异常
            mockReader.Setup(r => r.GetInt32(2)).Throws(new InvalidOperationException("Data is Null. This method or property cannot be called on Null values."));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => XbcDbEntity.EntityMapper<SimpleEntity>.MapListAsync(mockReader.Object));
            Assert.Contains("Data is Null", exception.Message);
        }

        [Fact]
        public async Task MapListAsync_UseDefaultValue显式声明遇NULL_应映射为默认值()
        {
            var mockReader = CreateProviderExceptionReader();
            // Number 列为 NULL，IsDBNull 短路为 default(int) = 0，不调用类型化读取方法
            mockReader.Setup(r => r.IsDBNull(0)).Returns(true);
            mockReader.Setup(r => r.IsDBNull(1)).Returns(false);
            mockReader.Setup(r => r.GetString(1)).Returns("测试");

            var result = await XbcDbEntity.EntityMapper<EntityWithUseDefaultValue>.MapListAsync(mockReader.Object);

            Assert.Single(result);
            Assert.Equal(0, result[0].Number);
            Assert.Equal("测试", result[0].Name);
            mockReader.Verify(r => r.GetInt32(It.IsAny<int>()), Times.Never);
        }

        #endregion

        #region 异步方法参数验证

        [Fact]
        public async Task MapAsync_null读取器_应抛出ArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => XbcDbEntity.EntityMapper<SimpleEntity>.MapAsync(null!));
        }

        [Fact]
        public async Task MapListAsync_null读取器_应抛出ArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => XbcDbEntity.EntityMapper<SimpleEntity>.MapListAsync(null!));
        }

        #endregion

        #region 实体特性测试

        [Fact]
        public void GetMetadata_带Table特性的实体_应返回正确的表名()
        {
            var metadata = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(EntityWithTableAttribute));

            Assert.NotNull(metadata);
            Assert.Equal("Users", metadata.TableName);
        }

        [Fact]
        public void GetMetadata_带Column特性的实体_应返回正确的列名()
        {
            var columns = XbcDbEntity.EntityMetadataCache.GetColumns(typeof(EntityWithTableAttribute));

            Assert.NotNull(columns);
            Assert.Equal(3, columns.Count);

            var idColumn = columns.FirstOrDefault(c => c.PropertyName == "Id");
            Assert.NotNull(idColumn);
            Assert.Equal("user_id", idColumn.ColumnName);
            Assert.True(idColumn.IsPrimaryKey);

            var nameColumn = columns.FirstOrDefault(c => c.PropertyName == "UserName");
            Assert.NotNull(nameColumn);
            Assert.Equal("user_name", nameColumn.ColumnName);
        }

        [Fact]
        public void GetMetadata_带忽略列的实体_应排除忽略的属性()
        {
            var columns = XbcDbEntity.EntityMetadataCache.GetColumns(typeof(EntityWithIgnoredColumn));

            Assert.NotNull(columns);
            Assert.Equal(2, columns.Count);
            Assert.DoesNotContain(columns, c => c.PropertyName == "ComputedProperty");
            Assert.DoesNotContain(columns, c => c.PropertyName == "TempData");
        }

        [Fact]
        public void GetMetadata_带自增列的实体_应正确识别标识列()
        {
            var identityColumn = XbcDbEntity.EntityMetadataCache.GetIdentityColumn(typeof(EntityWithIdentity));

            Assert.NotNull(identityColumn);
            Assert.Equal("ProductId", identityColumn.PropertyName);
            Assert.Equal("product_id", identityColumn.ColumnName);
            Assert.True(identityColumn.IsIdentity);
            Assert.True(identityColumn.IsPrimaryKey);
        }

        [Fact]
        public void GetMetadata_重复获取_应使用缓存()
        {
            var metadata1 = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(EntityWithTableAttribute));
            var metadata2 = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(EntityWithTableAttribute));

            Assert.Same(metadata1, metadata2);
        }

        #endregion

        #region 泛型与非泛型一致性测试

        [Fact]
        public void 泛型与非泛型Metadata_应返回相同结果()
        {
            var genericMetadata = XbcDbEntity.EntityMetadataCache<EntityWithTableAttribute>.Metadata;
            var nonGenericMetadata = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(EntityWithTableAttribute));

            Assert.Same(genericMetadata, nonGenericMetadata);
        }

        [Fact]
        public void 泛型与非泛型TableName_应返回相同结果()
        {
            var genericTableName = XbcDbEntity.EntityMetadataCache<EntityWithTableAttribute>.TableName;
            var nonGenericTableName = XbcDbEntity.EntityMetadataCache.GetTableName(typeof(EntityWithTableAttribute));

            Assert.Equal(genericTableName, nonGenericTableName);
        }

        [Fact]
        public void 泛型与非泛型Columns_应返回相同结果()
        {
            var genericColumns = XbcDbEntity.EntityMetadataCache<EntityWithTableAttribute>.Columns;
            var nonGenericColumns = XbcDbEntity.EntityMetadataCache.GetColumns(typeof(EntityWithTableAttribute));

            Assert.Equal(genericColumns.Count, nonGenericColumns.Count);
        }

        [Fact]
        public void 泛型与非泛型PrimaryKey_应返回相同结果()
        {
            var genericPrimaryKey = XbcDbEntity.EntityMetadataCache<EntityWithTableAttribute>.PrimaryKey;
            var nonGenericPrimaryKey = XbcDbEntity.EntityMetadataCache.GetPrimaryKey(typeof(EntityWithTableAttribute));

            Assert.Equal(genericPrimaryKey?.PropertyName, nonGenericPrimaryKey?.PropertyName);
        }

        #endregion

        #region 缓存清除测试

        [Fact]
        public void ClearCache_泛型版本_应清除该类型缓存()
        {
            // 先获取元数据填充缓存
            var metadata1 = XbcDbEntity.EntityMetadataCache<EntityWithTableAttribute>.Metadata;

            // 清除缓存
            XbcDbEntity.EntityMapper<EntityWithTableAttribute>.ClearCache();

            // 再次获取（应该重新创建，但对我们来说是透明的）
            var metadata2 = XbcDbEntity.EntityMetadataCache<EntityWithTableAttribute>.Metadata;

            // 验证可以正常获取
            Assert.NotNull(metadata2);
            Assert.Equal(metadata1.TableName, metadata2.TableName);
        }

        [Fact]
        public void ClearAllCache_应清除所有实体映射缓存()
        {
            // 填充多个类型的缓存
            _ = XbcDbEntity.EntityMetadataCache<SimpleEntity>.Metadata;
            _ = XbcDbEntity.EntityMetadataCache<EntityWithTableAttribute>.Metadata;
            _ = XbcDbEntity.EntityMetadataCache<EntityWithIdentity>.Metadata;

            // 清除所有缓存
            var exception = Record.Exception(() => XbcDbEntity.EntityMapper.ClearAllCache());

            Assert.Null(exception);
        }

        #endregion

        #region 标量投影映射测试

        [Fact]
        public void GetOrAddProjectionMapper_同一实体同一列_不同结果类型应各自缓存()
        {
            // 模拟单列（Age，int）读取器
            var mockReader = new Mock<DbDataReader>();
            mockReader.Setup(r => r.FieldCount).Returns(1);
            mockReader.Setup(r => r.GetName(0)).Returns("Age");
            mockReader.Setup(r => r.GetFieldType(0)).Returns(typeof(int));
            mockReader.Setup(r => r.IsDBNull(0)).Returns(false);
            mockReader.Setup(r => r.GetInt32(0)).Returns(21);
            var reader = mockReader.Object;

            var columnMetadata = XbcDbEntity.EntityMetadataCache<SimpleEntity>.GetColumn(nameof(SimpleEntity.Age));

            // int 投影：固化 Func<DbDataReader, int> 委托
            var intMapper = XbcDbEntity.EntityMapperCore.GetOrAddProjectionMapper<SimpleEntity, int>(reader, XbcDbEntity.ProjectionPlan.Scalar(new[] { columnMetadata }));
            Assert.Equal(21, intMapper(reader));

            // long 投影：同一实体同一列，缓存键包含结果类型，不应误用 int 委托（修复前此处抛 InvalidCastException）
            var longMapper = XbcDbEntity.EntityMapperCore.GetOrAddProjectionMapper<SimpleEntity, long>(reader, XbcDbEntity.ProjectionPlan.Scalar(new[] { columnMetadata }));
            Assert.Equal(21L, longMapper(reader));
        }

        #endregion

        #region 读取路径数值不匹配的精度防护测试

        /// <summary>
        /// 带 long 属性的实体（字段类型窄于属性类型的读取场景，如 COUNT(*) 提升）。
        /// </summary>
        public class EntityWithWideValue
        {
            public int Id { get; set; }

            public long WideCount { get; set; }
        }

        /// <summary>
        /// 带 uint 属性的实体（有符号字段跨符号窄化到无符号目标的读取场景）。
        /// </summary>
        public class EntityWithUnsignedValue
        {
            public int Id { get; set; }

            public uint Code { get; set; }
        }

        /// <summary>
        /// 浮点字段值带小数部分投影到 int 属性：静默截断（3.7 → 3）会把无法映射的值伪装成合法行数据，
        /// 读取路径无处"保持原值交数据库裁决"（值已离开数据库），应抛带列名上下文的异常。
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task MapListAsync_浮点字段投影int属性_带小数值应抛异常而非静默截断()
        {
            var mockReader = CreateSimpleEntityReader();
            mockReader.Setup(r => r.GetFieldType(2)).Returns(typeof(double));
            mockReader.Setup(r => r.GetDouble(2)).Returns(3.7);
            mockReader.Setup(r => r.GetInt32(0)).Returns(1);
            mockReader.Setup(r => r.GetString(1)).Returns("测试");
            mockReader.Setup(r => r.IsDBNull(1)).Returns(false);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => XbcDbEntity.EntityMapper<SimpleEntity>.MapListAsync(mockReader.Object));

            Assert.Contains("带非零小数部分", ex.Message);
        }

        /// <summary>
        /// 整数字段窄化到 int 属性超范围（如 long 2^33）：未检查的 conv 指令静默丢弃高位产生垃圾值，
        /// 应抛异常而非静默错数据。
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task MapListAsync_long字段窄化int属性_超范围应抛异常而非静默截断()
        {
            var mockReader = CreateSimpleEntityReader();
            mockReader.Setup(r => r.GetFieldType(2)).Returns(typeof(long));
            mockReader.Setup(r => r.GetInt64(2)).Returns(8_589_934_592L);// 2^33，超出 int 范围
            mockReader.Setup(r => r.GetInt32(0)).Returns(1);
            mockReader.Setup(r => r.GetString(1)).Returns("测试");
            mockReader.Setup(r => r.IsDBNull(1)).Returns(false);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => XbcDbEntity.EntityMapper<SimpleEntity>.MapListAsync(mockReader.Object));

            Assert.Contains("超出目标类型", ex.Message);
        }

        /// <summary>
        /// 浮点字段值为整数值（3.0）时投影到 int 属性：无损，应正常映射为 3（与谓词路径的有损舍入防护同口径）。
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task MapListAsync_浮点字段投影int属性_整数值应正常映射()
        {
            var mockReader = CreateSimpleEntityReader();
            mockReader.Setup(r => r.GetFieldType(2)).Returns(typeof(double));
            mockReader.Setup(r => r.GetDouble(2)).Returns(42.0);
            mockReader.Setup(r => r.GetInt32(0)).Returns(1);
            mockReader.Setup(r => r.GetString(1)).Returns("测试");
            mockReader.Setup(r => r.IsDBNull(1)).Returns(false);

            var result = await XbcDbEntity.EntityMapper<SimpleEntity>.MapListAsync(mockReader.Object);

            Assert.Single(result);
            Assert.Equal(42, result[0].Age);
        }

        /// <summary>
        /// long 字段窄化到 int 属性且在范围内（SQLite 的 INTEGER 列返回 long 的主流场景）：应正常映射。
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task MapListAsync_long字段窄化int属性_范围内应正常映射()
        {
            var mockReader = CreateSimpleEntityReader();
            mockReader.Setup(r => r.GetFieldType(2)).Returns(typeof(long));
            mockReader.Setup(r => r.GetInt64(2)).Returns(42L);
            mockReader.Setup(r => r.GetInt32(0)).Returns(1);
            mockReader.Setup(r => r.GetString(1)).Returns("测试");
            mockReader.Setup(r => r.IsDBNull(1)).Returns(false);

            var result = await XbcDbEntity.EntityMapper<SimpleEntity>.MapListAsync(mockReader.Object);

            Assert.Single(result);
            Assert.Equal(42, result[0].Age);
        }

        /// <summary>
        /// int 字段宽化到 long 属性（COUNT(*) 字段类型为 int 的提升场景）：无损，应正常映射。
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task MapListAsync_int字段宽化long属性_应正常映射()
        {
            var mockReader = new Mock<DbDataReader>();
            mockReader.Setup(r => r.FieldCount).Returns(2);
            mockReader.Setup(r => r.GetName(0)).Returns("Id");
            mockReader.Setup(r => r.GetName(1)).Returns("WideCount");
            mockReader.Setup(r => r.GetFieldType(0)).Returns(typeof(int));
            mockReader.Setup(r => r.GetFieldType(1)).Returns(typeof(int));
            mockReader.SetupSequence(r => r.ReadAsync(System.Threading.CancellationToken.None)).ReturnsAsync(true).ReturnsAsync(false);
            mockReader.Setup(r => r.GetInt32(0)).Returns(1);
            mockReader.Setup(r => r.GetInt32(1)).Returns(987654321);

            var result = await XbcDbEntity.EntityMapper<EntityWithWideValue>.MapListAsync(mockReader.Object);

            Assert.Single(result);
            Assert.Equal(987654321L, result[0].WideCount);
        }

        /// <summary>
        /// decimal 字段值带小数部分投影到 int 属性（如 AVG 聚合别名到属性名）：与 double 同族，应抛异常。
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task MapListAsync_decimal字段投影int属性_带小数值应抛异常()
        {
            var mockReader = CreateSimpleEntityReader();
            mockReader.Setup(r => r.GetFieldType(2)).Returns(typeof(decimal));
            mockReader.Setup(r => r.GetDecimal(2)).Returns(37.5m);
            mockReader.Setup(r => r.GetInt32(0)).Returns(1);
            mockReader.Setup(r => r.GetString(1)).Returns("测试");
            mockReader.Setup(r => r.IsDBNull(1)).Returns(false);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => XbcDbEntity.EntityMapper<SimpleEntity>.MapListAsync(mockReader.Object));

            Assert.Contains("带非零小数部分", ex.Message);
        }

        /// <summary>
        /// float 字段带小数投影到 int 属性：小数源三形态（double/float/decimal）中读取路径唯独 float 缺测试，
        /// 与 double 同族同守卫（HasNonZeroFraction 泛型分支），应抛异常。
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task MapListAsync_float字段投影int属性_带小数值应抛异常()
        {
            var mockReader = CreateSimpleEntityReader();
            mockReader.Setup(r => r.GetFieldType(2)).Returns(typeof(float));
            mockReader.Setup(r => r.GetFloat(2)).Returns(3.7f);
            mockReader.Setup(r => r.GetInt32(0)).Returns(1);
            mockReader.Setup(r => r.GetString(1)).Returns("测试");
            mockReader.Setup(r => r.IsDBNull(1)).Returns(false);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => XbcDbEntity.EntityMapper<SimpleEntity>.MapListAsync(mockReader.Object));

            Assert.Contains("带非零小数部分", ex.Message);
        }

        /// <summary>
        /// float 字段值为整数值（42f）投影到 int 属性：无损，应正常映射为 42。
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task MapListAsync_float字段投影int属性_整数值应正常映射()
        {
            var mockReader = CreateSimpleEntityReader();
            mockReader.Setup(r => r.GetFieldType(2)).Returns(typeof(float));
            mockReader.Setup(r => r.GetFloat(2)).Returns(42f);
            mockReader.Setup(r => r.GetInt32(0)).Returns(1);
            mockReader.Setup(r => r.GetString(1)).Returns("测试");
            mockReader.Setup(r => r.IsDBNull(1)).Returns(false);

            var result = await XbcDbEntity.EntityMapper<SimpleEntity>.MapListAsync(mockReader.Object);

            Assert.Single(result);
            Assert.Equal(42, result[0].Age);
        }

        /// <summary>
        /// 有符号 long 字段的负值窄化到无符号 uint 属性（跨符号）：构建期判不出无损宽化进守卫，
        /// 负值对 uint 不可表示，应抛超范围异常而非隐式回绕成大正数。
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task MapListAsync_long字段负值窄化uint属性_应抛超范围异常()
        {
            var mockReader = new Mock<DbDataReader>();
            mockReader.Setup(r => r.FieldCount).Returns(2);
            mockReader.Setup(r => r.GetName(0)).Returns("Id");
            mockReader.Setup(r => r.GetName(1)).Returns("Code");
            mockReader.Setup(r => r.GetFieldType(0)).Returns(typeof(int));
            mockReader.Setup(r => r.GetFieldType(1)).Returns(typeof(long));
            mockReader.SetupSequence(r => r.ReadAsync(System.Threading.CancellationToken.None)).ReturnsAsync(true).ReturnsAsync(false);
            mockReader.Setup(r => r.GetInt32(0)).Returns(1);
            mockReader.Setup(r => r.GetInt64(1)).Returns(-1L);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => XbcDbEntity.EntityMapper<EntityWithUnsignedValue>.MapListAsync(mockReader.Object));

            Assert.Contains("超出目标类型", ex.Message);
        }

        /// <summary>
        /// 无符号 uint 字段（30 亿）窄化到有符号 int 属性：ADO.NET 无 GetUInt32 强类型 getter，走
        /// EntityValueConverter.ConvertFrom 兜底路径；值超出 int 上限（约 21.4 亿），应经 ChangeType 的
        /// checked 语义抛 OverflowException，而非静默回绕产生垃圾负数。
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task MapListAsync_uint字段窄化int属性_超范围应抛异常()
        {
            var mockReader = CreateSimpleEntityReader();
            mockReader.Setup(r => r.GetFieldType(2)).Returns(typeof(uint));
            mockReader.Setup(r => r.GetValue(2)).Returns(3_000_000_000u);
            mockReader.Setup(r => r.GetInt32(0)).Returns(1);
            mockReader.Setup(r => r.GetString(1)).Returns("测试");
            mockReader.Setup(r => r.IsDBNull(1)).Returns(false);

            var ex = await Assert.ThrowsAsync<OverflowException>(
                () => XbcDbEntity.EntityMapper<SimpleEntity>.MapListAsync(mockReader.Object));

            Assert.NotNull(ex);
        }

        #endregion
    }
}
