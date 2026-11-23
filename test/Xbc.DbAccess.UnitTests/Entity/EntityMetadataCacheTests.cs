using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xunit;
using Xbc.DbAccess.Common;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    public class EntityMetadataCacheTests
    {
        #region 测试实体类

        [XbcDbEntity.Table("Users")]
        public class UserEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; }
            public string Email { get; set; }
            public int Age { get; set; }
        }

        public class ProductEntity
        {
            [XbcDbEntity.PrimaryKey]
            public Guid ProductId { get; set; }
            public string ProductName { get; set; }
            public decimal Price { get; set; }
        }

        public class OrderEntity
        {
            [XbcDbEntity.PrimaryKey("OrderID")]
            public int Id { get; set; }

            [XbcDbEntity.Column("OrderDate")]
            public DateTime CreatedAt { get; set; }

            [XbcDbEntity.NotMapped]
            public string TempData { get; set; }
        }

        [XbcDbEntity.Table("Categories", Schema = "dbo")]
        public class CategoryEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int CategoryId { get; set; }
            public string CategoryName { get; set; }
        }

        public class NoKeyEntity
        {
            public string Name { get; set; }
            public int Value { get; set; }
        }

        public class CompositeKeyEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Key1 { get; set; }

            [XbcDbEntity.PrimaryKey]
            public int Key2 { get; set; }
        }

        /// <summary>
        /// NotMapped 测试实体：IgnoredField 用 [NotMapped] 忽略，TempData 不标记特性正常映射，验证忽略与正常映射并存。
        /// 注意使用 XbcDbEntity 前缀，避免与本文件 using 的 System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute 混淆。
        /// </summary>
        public class NotMappedEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }
            public string Name { get; set; }

            [XbcDbEntity.NotMapped]
            public string IgnoredField { get; set; }

            public string TempData { get; set; }
        }

        /// <summary>
        /// 主键与忽略映射冲突实体：验证 [PrimaryKey] + [NotMapped] 抛出明确异常。
        /// </summary>
        public class NotMappedConflictEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            [XbcDbEntity.NotMapped]
            public int Id { get; set; }

            public string Name { get; set; }
        }

        /// <summary>
        /// NotMapped 与 Column 冲突实体：验证 [NotMapped] + [Column] 抛出明确异常。
        /// </summary>
        public class NotMappedColumnConflictEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column("temp_data")]
            [XbcDbEntity.NotMapped]
            public string TempData { get; set; }
        }

        /// <summary>
        /// NotMapped 与 Navigation 冲突实体：验证 [NotMapped] + [Navigation] 抛出明确异常。
        /// </summary>
        public class NotMappedNavigationConflictEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.NotMapped]
            [XbcDbEntity.Navigation]
            public CategoryEntity Category { get; set; }
        }

        /// <summary>
        /// 自引用 1:N 约定误配实体：主键名符合 "{实体名}Id" 约定，1:N 约定推断的 OtherKey（"{实体名}Id"）
        /// 恰命中目标实体（自身）的主键而非外键，元数据构建应抛出明确的配置错误。
        /// </summary>
        public class NavSelfRefNode
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int NavSelfRefNodeId { get; set; }

            [XbcDbEntity.Navigation]
            public List<NavSelfRefNode> Children { get; set; }
        }

        /// <summary>
        /// 自引用 1:1 约定误配实体：主键名恰为 "{导航属性名}Id"（Peer → PeerId），
        /// 约定推断的 ThisKey 恰命中本方主键而非外键，元数据构建应抛出明确的配置错误。
        /// </summary>
        public class NavSelfRefPeer
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int PeerId { get; set; }

            [XbcDbEntity.Navigation]
            public NavSelfRefPeer Peer { get; set; }
        }

        /// <summary>
        /// 共享主键 1:1 明细实体：主键 Id 同时是外键（如 OrderDetail.OrderId），
        /// 手动显式指定 ThisKey 指向本方主键是意图信号，应放行构建。
        /// </summary>
        public class NavSharedPkDetailEntity
        {
            [XbcDbEntity.PrimaryKey]// 共享主键由调用方赋值或外键同步回填，非自增
            public int Id { get; set; }

            public string Note { get; set; }

            [XbcDbEntity.Navigation(ThisKey = "Id")]
            public NavSharedPkMasterEntity Master { get; set; }
        }

        /// <summary>
        /// NavSharedPkDetailEntity 的导航目标（主方）实体。
        /// </summary>
        public class NavSharedPkMasterEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }
        }

        /// <summary>
        /// ConvertFrom 返回类型不兼容实体：属性为 int 而转换器 ConvertFrom 返回 string，
        /// 读取路径拆箱会抛 InvalidCastException，构建期应报出赋值不兼容。
        /// </summary>
        public class BadConvertFromEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(ConverterType = typeof(StringReturningConverter))]
            public int Count { get; set; }
        }

        /// <summary>
        /// 返回 string 的错误转换器（目标是 int 属性）。
        /// </summary>
        public sealed class StringReturningConverter
        {
            public string ConvertFrom(object dbValue) => dbValue?.ToString();

            public object ConvertTo(int value) => value.ToString();
        }

        /// <summary>
        /// 标识列配置类型转换器：标识列由数据库生成、回填走数值直转（不经转换器），应显式失败。
        /// </summary>
        public class IdentityConverterConflictEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            [XbcDbEntity.Column(IsIdentity = true, ConverterType = typeof(StringReturningConverter))]
            public int Seq { get; set; }
        }

        /// <summary>
        /// 标识列配置枚举字符串存储：标识列由数据库自增生成数值，枚举字符串存储格式不生效，应显式失败。
        /// </summary>
        public class IdentityEnumStringConflictEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            [XbcDbEntity.Column(IsIdentity = true, EnumAsString = true)]
            public IdentityTestEnum Seq { get; set; }
        }

        /// <summary>
        /// 标识列测试用枚举。
        /// </summary>
        public enum IdentityTestEnum
        {
            A,
            B
        }

        /// <summary>
        /// [PrimaryKey] 只读属性实体。
        /// </summary>
        public class ReadOnlyPkEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; }
        }

        /// <summary>
        /// [Column] 只读属性实体。
        /// </summary>
        public class ReadOnlyColumnEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column("code")]
            public string Code { get; }
        }

        /// <summary>
        /// [Navigation] 只读属性实体。
        /// </summary>
        public class ReadOnlyNavigationEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Navigation]
            public ReadOnlyNavigationTargetEntity Target { get; }
        }

        /// <summary>
        /// ReadOnlyNavigationEntity 的导航目标实体。
        /// </summary>
        public class ReadOnlyNavigationTargetEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }
        }

        /// <summary>
        /// 无映射特性只读属性实体：只读属性视为计算属性，应静默跳过而不抛异常。
        /// </summary>
        public class ReadOnlyNoAttrEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; }

            public string Computed => Name + "!";
        }

        /// <summary>
        /// DbType 透传实体：兼容性校验已移除，int 属性显式指定 DbType.String 时原样透传，
        /// 由提供程序在首次执行时判定是否接受。
        /// </summary>
        public class DbTypeMismatchEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(DbType = DbType.String)]
            public int Count { get; set; }
        }

        /// <summary>
        /// DbType 兼容实体：byte[] 属性指定 DbType.Binary，应正常构建并保留配置。
        /// </summary>
        public class DbTypeBinaryEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(DbType = DbType.Binary)]
            public byte[] Data { get; set; }
        }

        /// <summary>
        /// 枚举 DbType 透传实体：兼容性校验已移除，指定 DbType 应原样保留到列元数据。
        /// </summary>
        public class EnumDbTypeCompatibleEntity
        {
            public enum Status
            {
                Active,
                Inactive,
            }

            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(DbType = DbType.Int32)]
            public Status CurrentStatus { get; set; }
        }

        /// <summary>
        /// [Column(DbType = AnsiString)] 透传实体：AnsiString 枚举值为 0（与旧"哨兵值 0"冲突的取值），
        /// object 属性 + null 未指定语义下应正常透传，不再被静默当作未指定。
        /// </summary>
        public class AnsiStringColumnEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(DbType = DbType.AnsiString)]
            public string Code { get; set; }
        }

        /// <summary>
        /// DbType 非法取值实体：object 属性误传字符串常量，构建期应报出取值非法。
        /// </summary>
        public class InvalidDbTypeValueEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(DbType = "String")]
            public string Name { get; set; }
        }

        /// <summary>
        /// 未闭合泛型实体（配合 typeof(GenericEntity&lt;&gt;) 使用）。
        /// </summary>
        public class GenericEntity<T>
        {
            public int Id { get; set; }

            public T Value { get; set; }
        }

        /// <summary>
        /// 显式指定表名的封闭泛型实体：表名不含泛型参数记号，构建应成功。
        /// </summary>
        [XbcDbEntity.Table("generic_values")]
        public class NamedGenericEntity<T>
        {
            public int Id { get; set; }

            public T Value { get; set; }
        }

        /// <summary>
        /// 无参构造函数抛异常实体：构建元数据时应还原为带实体类型上下文的包装异常（原始异常作 InnerException）。
        /// </summary>
        public class ThrowingCtorEntity
        {
            public ThrowingCtorEntity()
            {
                throw new InvalidOperationException("无参构造函数内部抛出的原始异常");
            }

            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }
        }

        /// <summary>
        /// [PrimaryKey(DbType)] 配置实体：string 主键指定 AnsiString。
        /// </summary>
        public class StringPkDbTypeEntity
        {
            [XbcDbEntity.PrimaryKey(DbType = DbType.AnsiString)]
            public string Code { get; set; }
        }

        /// <summary>
        /// [PrimaryKey(DbType)] 非法取值实体：object 属性误传字符串常量，构建期应报出取值非法。
        /// </summary>
        public class StringPkInvalidDbTypeEntity
        {
            [XbcDbEntity.PrimaryKey(DbType = "AnsiString")]
            public string Code { get; set; }
        }

        #endregion

        #region EntityMetadataCache 非泛型版本测试

        [Fact]
        public void GetMetadata_实体类型_应返回元数据()
        {
            var metadata = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(UserEntity));

            Assert.NotNull(metadata);
            Assert.Equal(typeof(UserEntity), metadata.EntityType);
        }

        [Fact]
        public void GetMetadata_重复获取_应返回相同缓存()
        {
            var metadata1 = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(UserEntity));
            var metadata2 = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(UserEntity));

            Assert.Same(metadata1, metadata2);
        }

        [Fact]
        public void GetMetadata_null类型_应抛出ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => XbcDbEntity.EntityMetadataCache.GetMetadata(null));
        }

        [Fact]
        public void GetTableName_有Table特性_应返回特性指定的表名()
        {
            var tableName = XbcDbEntity.EntityMetadataCache.GetTableName(typeof(UserEntity));

            Assert.Equal("Users", tableName);
        }

        [Fact]
        public void GetTableName_无Table特性_应返回复数形式的类名()
        {
            var tableName = XbcDbEntity.EntityMetadataCache.GetTableName(typeof(ProductEntity));

            Assert.Equal("ProductEntities", tableName);
        }

        [Fact]
        public void GetTableName_有Schema_应在元数据中包含Schema()
        {
            var metadata = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(CategoryEntity));

            Assert.Equal("Categories", metadata.TableName);
            Assert.Equal("dbo", metadata.Schema);
        }

        [Fact]
        public void GetColumns_实体类型_应返回所有列()
        {
            var columns = XbcDbEntity.EntityMetadataCache.GetColumns(typeof(UserEntity));

            Assert.NotNull(columns);
            Assert.Equal(4, columns.Count);
            Assert.Contains(columns, c => c.PropertyName == "Id");
            Assert.Contains(columns, c => c.PropertyName == "Name");
            Assert.Contains(columns, c => c.PropertyName == "Email");
            Assert.Contains(columns, c => c.PropertyName == "Age");
        }

        [Fact]
        public void GetColumns_有Ignore特性_应排除该属性()
        {
            var columns = XbcDbEntity.EntityMetadataCache.GetColumns(typeof(OrderEntity));

            Assert.DoesNotContain(columns, c => c.PropertyName == "TempData");
        }

        [Fact]
        public void GetColumns_有Column特性_应使用特性指定的列名()
        {
            var columns = XbcDbEntity.EntityMetadataCache.GetColumns(typeof(OrderEntity));

            var orderIdColumn = columns.FirstOrDefault(c => c.PropertyName == "Id");
            Assert.NotNull(orderIdColumn);
            Assert.Equal("OrderID", orderIdColumn.ColumnName);
        }

        [Fact]
        public void GetPrimaryKey_有Key特性_应返回主键列()
        {
            var primaryKey = XbcDbEntity.EntityMetadataCache.GetPrimaryKey(typeof(UserEntity));

            Assert.NotNull(primaryKey);
            Assert.Equal("Id", primaryKey.PropertyName);
        }

        [Fact]
        public void GetPrimaryKey_无主键_应返回null()
        {
            var primaryKey = XbcDbEntity.EntityMetadataCache.GetPrimaryKey(typeof(NoKeyEntity));

            Assert.Null(primaryKey);
        }

        [Fact]
        public void GetIdentityColumn_有IsIdentity标记_应返回标识列()
        {
            var identityColumn = XbcDbEntity.EntityMetadataCache.GetIdentityColumn(typeof(UserEntity));

            Assert.NotNull(identityColumn);
            Assert.Equal("Id", identityColumn.PropertyName);
            Assert.True(identityColumn.IsIdentity);
        }

        [Fact]
        public void GetIdentityColumn_无标识列_应返回null()
        {
            var identityColumn = XbcDbEntity.EntityMetadataCache.GetIdentityColumn(typeof(ProductEntity));

            Assert.Null(identityColumn);
        }

        [Fact]
        public void GetColumnMetadata_存在的属性_应返回列元数据()
        {
            var column = XbcDbEntity.EntityMetadataCache.GetColumn(typeof(UserEntity), "Name");

            Assert.NotNull(column);
            Assert.Equal("Name", column.PropertyName);
        }

        [Fact]
        public void GetColumnMetadata_不存在的属性_应返回null()
        {
            var column = XbcDbEntity.EntityMetadataCache.GetColumn(typeof(UserEntity), "NonExistent");

            Assert.Null(column);
        }

        [Fact]
        public void GetColumnMetadata_null实体类型_应抛出ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => XbcDbEntity.EntityMetadataCache.GetColumn(null, "Name"));
        }

        [Fact]
        public void GetColumnMetadata_null属性名_应抛出ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => XbcDbEntity.EntityMetadataCache.GetColumn(typeof(UserEntity), null));
        }

        [Fact]
        public void GetColumnMetadata_空属性名_应抛出ArgumentException()
        {
            Assert.Throws<ArgumentException>(() => XbcDbEntity.EntityMetadataCache.GetColumn(typeof(UserEntity), ""));
        }

        [Fact]
        public void BuildEntityMetadata_不支持的类型_应抛出InvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(string)));
        }

        #endregion

        #region EntityMetadataCache<T> 泛型版本测试

        [Fact]
        public void Metadata_泛型版本_应返回元数据()
        {
            var metadata = XbcDbEntity.EntityMetadataCache<UserEntity>.Metadata;

            Assert.NotNull(metadata);
            Assert.Equal(typeof(UserEntity), metadata.EntityType);
        }

        [Fact]
        public void TableName_泛型版本_应返回表名()
        {
            var tableName = XbcDbEntity.EntityMetadataCache<UserEntity>.TableName;

            Assert.Equal("Users", tableName);
        }

        [Fact]
        public void Columns_泛型版本_应返回列集合()
        {
            var columns = XbcDbEntity.EntityMetadataCache<UserEntity>.Columns;

            Assert.NotNull(columns);
            Assert.Equal(4, columns.Count);
        }

        [Fact]
        public void PrimaryKey_泛型版本_应返回主键()
        {
            var primaryKey = XbcDbEntity.EntityMetadataCache<UserEntity>.PrimaryKey;

            Assert.NotNull(primaryKey);
            Assert.Equal("Id", primaryKey.PropertyName);
        }

        [Fact]
        public void IdentityColumn_泛型版本_应返回标识列()
        {
            var identityColumn = XbcDbEntity.EntityMetadataCache<UserEntity>.IdentityColumn;

            Assert.NotNull(identityColumn);
            Assert.Equal("Id", identityColumn.PropertyName);
        }

        [Fact]
        public void GetColumnMetadata_泛型版本_存在的属性_应返回列元数据()
        {
            var column = XbcDbEntity.EntityMetadataCache<UserEntity>.GetColumn("Email");

            Assert.NotNull(column);
            Assert.Equal("Email", column.PropertyName);
        }

        [Fact]
        public void GetColumnMetadata_泛型版本_不存在的属性_应返回null()
        {
            var column = XbcDbEntity.EntityMetadataCache<UserEntity>.GetColumn("NonExistent");

            Assert.Null(column);
        }

        [Fact]
        public void GetColumnMetadata_泛型版本_null属性名_应抛出ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => XbcDbEntity.EntityMetadataCache<UserEntity>.GetColumn(null));
        }

        [Fact]
        public void 泛型与非泛型_应返回相同元数据()
        {
            var genericMetadata = XbcDbEntity.EntityMetadataCache<UserEntity>.Metadata;
            var nonGenericMetadata = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(UserEntity));

            Assert.Same(genericMetadata, nonGenericMetadata);
        }

        #endregion

        #region NotMapped 特性测试

        [Fact]
        public void NotMapped_标记的属性_不应出现在列元数据中()
        {
            var metadata = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(NotMappedEntity));

            Assert.Null(metadata.Columns.FirstOrDefault(c => c.PropertyName == "IgnoredField"));
            // 未忽略的属性正常映射
            Assert.NotNull(metadata.Columns.FirstOrDefault(c => c.PropertyName == "Name"));
            Assert.NotNull(metadata.Columns.FirstOrDefault(c => c.PropertyName == "TempData"));
            // IgnoredField 被忽略后，列集合中仅剩 Id、Name 与 TempData
            Assert.Equal(3, metadata.Columns.Count);
        }

        [Fact]
        public void NotMapped_与Column同时标记_应抛出InvalidOperationException()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(NotMappedColumnConflictEntity)));

            Assert.Contains("同时声明了 [Column]、[NotMapped]", ex.Message);
            Assert.Contains("属性级映射特性至多声明一个", ex.Message);
            Assert.Contains("NotMappedColumnConflictEntity.TempData", ex.Message);
        }

        [Fact]
        public void NotMapped_与Navigation同时标记_应抛出InvalidOperationException()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(NotMappedNavigationConflictEntity)));

            Assert.Contains("同时声明了 [Navigation]、[NotMapped]", ex.Message);
            Assert.Contains("属性级映射特性至多声明一个", ex.Message);
            Assert.Contains("NotMappedNavigationConflictEntity.Category", ex.Message);
        }

        [Fact]
        public void 标识列_配置ConverterType_应抛出InvalidOperationException()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(IdentityConverterConflictEntity)));

            Assert.Contains("同时声明了 IsIdentity、ConverterType", ex.Message);
            Assert.Contains("列级开关至多其一", ex.Message);
        }

        [Fact]
        public void 标识列_配置EnumAsString_应抛出InvalidOperationException()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(IdentityEnumStringConflictEntity)));

            Assert.Contains("同时声明了 IsIdentity、EnumAsString", ex.Message);
            Assert.Contains("列级开关至多其一", ex.Message);
        }

        [Fact]
        public void NotMapped_与PrimaryKey同时标记_应抛出InvalidOperationException()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(NotMappedConflictEntity)));

            Assert.Contains("同时声明了 [PrimaryKey]、[NotMapped]", ex.Message);
            Assert.Contains("NotMappedConflictEntity.Id", ex.Message);
        }

        [Fact]
        public void NotMapped_可忽略符合主键命名规则的属性()
        {
            // 属性名为 "Id" 符合主键命名约定，但 [NotMapped] 优先于命名约定，不应被视为主键
            var metadata = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(NotMappedNamedKeyEntity));

            Assert.Null(metadata.PrimaryKey);
        }

        public class NotMappedNamedKeyEntity
        {
            [XbcDbEntity.NotMapped]
            public int Id { get; set; }

            public string Name { get; set; }
        }

        #endregion

        #region 导航约定推断命中主键测试

        [Fact]
        public void GetMetadata_自引用一对多_约定推断命中目标主键_应抛出InvalidOperationException()
        {
            // 自引用 1:N：约定推断的 OtherKey = "{实体名}Id" 恰为目标实体（自身）的主键而非外键，
            // 装配会把父实体自身作为唯一子实体匹配，构建期应报出清晰的配置错误
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(NavSelfRefNode)));

            Assert.Contains("1:N 约定推断命中了对方实体", ex.Message);
            Assert.Contains("主键", ex.Message);
            Assert.Contains("NavSelfRefNodeId", ex.Message);
            Assert.Contains("显式指定外键", ex.Message);
        }

        [Fact]
        public void GetMetadata_自引用一对一_约定推断命中本方主键_应抛出InvalidOperationException()
        {
            // 1:1 自引用：约定推断的 ThisKey = "{导航属性名}Id"（Peer → PeerId）恰为本方主键而非外键，
            // 装配会以本方主键匹配引用，构建期应报出清晰的配置错误
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(NavSelfRefPeer)));

            Assert.Contains("1:1 约定推断命中了本方主键", ex.Message);
            Assert.Contains("PeerId", ex.Message);
            Assert.Contains("显式指定外键", ex.Message);
        }

        #endregion

        #region 构建期校验测试（共享主键 1:1 / ConvertFrom 兼容性 / 只读属性 / DbType 兼容性 / 未闭合泛型 / 构造异常 / 主键 DbType）

        [Fact]
        public void GetMetadata_共享主键一对一_手动ThisKey指向本方主键_应构建成功()
        {
            // 共享主键 1:1（主键即外键，如 OrderDetail.OrderId）：手动显式指定 ThisKey 是意图信号，构建放行；
            // 写路径的外键同步（本方键列为默认值时回填对方键值）正是该模式的期望行为。
            // 约定推断路径仍守卫（见"导航约定推断命中主键测试"），只有显式指定可表达该模式
            var metadata = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(NavSharedPkDetailEntity));

            var navigation = metadata.Navigations.Single(n => n.PropertyName == "Master");
            Assert.True(navigation.ThisKeyColumn.IsPrimaryKey);
            Assert.Equal("Id", navigation.ThisKeyColumn.PropertyName);
            Assert.True(navigation.OtherKeyColumn.IsPrimaryKey);// 1:1 引用的 OtherKey 恒为对方主键
        }

        [Fact]
        public void GetMetadata_转换器ConvertFrom返回类型不兼容_应抛出InvalidOperationException()
        {
            // ConvertFrom 返回 string 而属性为 int：读取路径直接拆箱会抛 InvalidCastException，构建期即报出
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(BadConvertFromEntity)));

            Assert.Contains("BadConvertFromEntity.Count", ex.Message);
            Assert.Contains("ConvertFrom 方法返回类型", ex.Message);
            Assert.Contains("'String' 不能赋给属性类型 'Int32'", ex.Message);
        }

        [Fact]
        public void GetMetadata_主键属性只读_应抛出InvalidOperationException()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(ReadOnlyPkEntity)));

            Assert.Contains("标记了 [PrimaryKey]", ex.Message);
            Assert.Contains("只读属性不能参与映射", ex.Message);
            Assert.Contains("ReadOnlyPkEntity.Id", ex.Message);
        }

        [Fact]
        public void GetMetadata_列属性只读_应抛出InvalidOperationException()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(ReadOnlyColumnEntity)));

            Assert.Contains("标记了 [Column]", ex.Message);
            Assert.Contains("只读属性不能参与映射", ex.Message);
            Assert.Contains("ReadOnlyColumnEntity.Code", ex.Message);
        }

        [Fact]
        public void GetMetadata_导航属性只读_应抛出InvalidOperationException()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(ReadOnlyNavigationEntity)));

            Assert.Contains("标记了 [Navigation]", ex.Message);
            Assert.Contains("只读属性不能参与映射", ex.Message);
            Assert.Contains("ReadOnlyNavigationEntity.Target", ex.Message);
        }

        [Fact]
        public void GetMetadata_无映射特性只读属性_应静默跳过()
        {
            // 无映射特性的只读属性视为计算属性，不参与映射也不抛异常（保持既有行为）
            var metadata = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(ReadOnlyNoAttrEntity));

            Assert.Equal(2, metadata.Columns.Count);
            Assert.Null(metadata.Columns.FirstOrDefault(c => c.PropertyName == "Computed"));
        }

        [Fact]
        public void GetMetadata_DbType与属性类型不匹配_应原样透传不校验兼容性()
        {
            // 兼容性校验已移除（各提供程序宽容度差异大，预设表会误拒合法组合），由提供程序在首次执行时判定
            var column = XbcDbEntity.EntityMetadataCache.GetColumn(typeof(DbTypeMismatchEntity), "Count");

            Assert.NotNull(column);
            Assert.Equal(DbType.String, column.DbType);
        }

        [Fact]
        public void GetMetadata_DbType与属性类型兼容_应正常构建并保留DbType()
        {
            var column = XbcDbEntity.EntityMetadataCache.GetColumn(typeof(DbTypeBinaryEntity), "Data");

            Assert.NotNull(column);
            Assert.Equal(DbType.Binary, column.DbType);
        }

        [Fact]
        public void GetMetadata_枚举属性指定DbType_应透传不校验兼容性()
        {
            // 兼容性校验已移除（各提供程序宽容度差异大，预设表会误拒合法组合），由提供程序在首次执行时判定
            var column = XbcDbEntity.EntityMetadataCache.GetColumn(typeof(EnumDbTypeCompatibleEntity), "CurrentStatus");

            Assert.NotNull(column);
            Assert.Equal(DbType.Int32, column.DbType);
        }

        [Fact]
        public void GetMetadata_列特性指定AnsiString_应透传不被哨兵吞掉()
        {
            // DbType.AnsiString 枚举值为 0，旧的"哨兵值 0 表示未指定"实现会把它静默当作未指定；
            // object 属性 + null 未指定语义修复后应正常透传
            var column = XbcDbEntity.EntityMetadataCache.GetColumn(typeof(AnsiStringColumnEntity), "Code");

            Assert.NotNull(column);
            Assert.Equal(DbType.AnsiString, column.DbType);
        }

        [Fact]
        public void GetMetadata_DbType配置非枚举取值_应抛出InvalidOperationException()
        {
            // object 属性接受任意特性常量，误传字符串等非 DbType 取值应在构建期报出
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(InvalidDbTypeValueEntity)));

            Assert.Contains("不是有效的 System.Data.DbType 枚举值", ex.Message);
            Assert.Contains("String", ex.Message);
        }

        [Fact]
        public void GetMetadata_未闭合泛型类型_应抛出明确异常()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(GenericEntity<>)));

            Assert.Contains("不能是未闭合泛型", ex.Message);
            Assert.Contains("请使用封闭泛型类型", ex.Message);
        }

        [Fact]
        public void GetMetadata_封闭泛型未显式指定表名_应抛出InvalidOperationException()
        {
            // 默认表名 "GenericEntity`1" 含反引号记号，部分数据库不接受该标识符，构建期强制显式指定 [Table]
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(GenericEntity<string>)));

            Assert.Contains("未显式指定 [Table] 表名", ex.Message);
            Assert.Contains("含泛型参数记号", ex.Message);
        }

        [Fact]
        public void GetMetadata_封闭泛型显式指定表名_应构建成功()
        {
            var metadata = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(NamedGenericEntity<int>));

            Assert.Equal("generic_values", metadata.TableName);
        }

        [Fact]
        public void GetMetadata_无参构造函数抛异常_应包装原始异常()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(ThrowingCtorEntity)));

            Assert.Contains("ThrowingCtorEntity", ex.Message);
            Assert.Contains("无参构造函数执行时抛出异常", ex.Message);

            var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Equal("无参构造函数内部抛出的原始异常", inner.Message);
        }

        [Fact]
        public void GetMetadata_主键特性指定DbType_应透传到主键列元数据()
        {
            var primaryKey = XbcDbEntity.EntityMetadataCache.GetPrimaryKey(typeof(StringPkDbTypeEntity));

            Assert.NotNull(primaryKey);
            Assert.Equal(DbType.AnsiString, primaryKey.DbType);
        }

        [Fact]
        public void GetMetadata_主键特性未指定DbType_应为null按类型推断()
        {
            // 默认（未指定）不设置 DbType，写路径按 .NET 类型推断参数类型
            var primaryKey = XbcDbEntity.EntityMetadataCache.GetPrimaryKey(typeof(UserEntity));

            Assert.NotNull(primaryKey);
            Assert.Null(primaryKey.DbType);
        }

        [Fact]
        public void GetMetadata_主键DbType配置非枚举取值_应抛出InvalidOperationException()
        {
            // [PrimaryKey(DbType = ...)] 与列特性共用同一取值约束：误传非 DbType 取值在构建期报出
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(StringPkInvalidDbTypeEntity)));

            Assert.Contains("不是有效的 System.Data.DbType 枚举值", ex.Message);
            Assert.Contains("AnsiString", ex.Message);
        }

        #endregion

        #region PluralizationHelper 复数化规则测试

        [Theory]
        [InlineData("leaf", "leaves")]
        [InlineData("knife", "knives")]
        [InlineData("wife", "wives")]
        [InlineData("life", "lives")]
        public void ToPlural_f结尾规则词_应变为ves(string singular, string expected)
        {
            // 规则词保留 f/fe → ves 变化：leaf→leaves、knife→knives、wife→wives、life→lives
            Assert.Equal(expected, XbcDbEntity.PluralizationHelper.ToPlural(singular));
        }

        #endregion
    }

    /// <summary>
    /// 无主键实体构建验证：元数据正常构建、不产生告警日志。
    /// <para>DiagnosticLog 路由是全局静态状态，与 DiagnosticLogTests 同 Collection 串行执行，避免并行污染。</para>
    /// </summary>
    [Collection("DiagnosticLogSerial")]
    public class EntityMetadataNoKeyWarnTests : IDisposable
    {
        private readonly CapturingLogger logger = new CapturingLogger();

        public EntityMetadataNoKeyWarnTests()
        {
            DiagnosticLog.Configure(new CapturingLoggerFactory(logger));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // 恢复全局默认路由，避免影响其他测试
            DiagnosticLog.Configure(null);
        }

        [Fact]
        public void GetMetadata_无主键实体_正常构建且不记录Warn()
        {
            var metadata = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(NoKeyWarnEntity));

            // 元数据正常构建（插入路径不依赖主键），不再产生无主键告警日志
            Assert.NotNull(metadata);
            Assert.Null(metadata.PrimaryKey);
            Assert.Empty(logger.Entries);
        }

        /// <summary>
        /// 无主键警告专用实体：警告只在首次构建时触发，复用其他测试已构建缓存的实体（如 NoKeyEntity）将收不到警告。
        /// </summary>
        public class NoKeyWarnEntity
        {
            public string Name { get; set; }
        }

        /// <summary>
        /// 捕获日志条目的测试日志器。
        /// </summary>
        private sealed class CapturingLogger : ILogger
        {
            public List<(LogLevel Level, string Message)> Entries { get; }
                = new List<(LogLevel, string)>();

            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception exception, Func<TState, Exception, string> formatter)
            {
                Entries.Add((logLevel, state.ToString() ?? string.Empty));
            }
        }

        /// <summary>
        /// 返回固定日志器的测试日志工厂。
        /// </summary>
        private sealed class CapturingLoggerFactory : ILoggerFactory
        {
            private readonly ILogger logger;

            public CapturingLoggerFactory(ILogger logger)
            {
                this.logger = logger;
            }

            public void AddProvider(ILoggerProvider provider)
            {
            }

            public ILogger CreateLogger(string categoryName) => logger;

            public void Dispose()
            {
            }
        }
    }
}
