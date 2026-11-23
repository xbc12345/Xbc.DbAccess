using System;
using System.Collections.Generic;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 实体元数据构建器「非法配置拒绝」单元测试（Stryker NoCoverage 补齐）：
    /// 锁定 EntityMetadataCache 构建期各验证拒绝分支——非法实体配置应抛 InvalidOperationException 且消息含关键定位片段。
    /// 全部为纯单元测试，不需要数据库；每个替身实体用私有嵌套类，通过 EntityMetadataCache&lt;T&gt;.Metadata 触发构建。
    /// <para>刻意不覆盖的两处等价变异：源码 L748（GetTypeDisplayName 的三目字符串拼接，String 变异仅改变展示格式）、
    /// L779（IsNumericPrimaryKeyType 的 Block removal，等价变异），对异常行为无可观察影响。</para>
    /// </summary>
    public class EntityMetadataValidationTests
    {
        #region 测试实体（替身）

        // L335：索引器标 [Column]——索引器没有单值列语义，不能进入映射路径
        private class IndexerColumnEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column]
            public string this[int index]
            {
                get => "v";
                set { }
            }
        }

        // L365：两个属性映射同一列名（大小写不敏感冲突）——INSERT/UPDATE 会生成重复列
        private class DuplicateColumnEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column("dup_name")]
            public string Name1 { get; set; }

            [XbcDbEntity.Column("Dup_Name")]
            public string Name2 { get; set; }
        }

        // L372：复合主键——当前仅支持单列主键
        private class CompositePkEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Key1 { get; set; }

            [XbcDbEntity.PrimaryKey]
            public int Key2 { get; set; }
        }

        // L380：多个标识列——通常一张表只有一个自增列
        private class DuplicateIdentityEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(IsIdentity = true)]
            public int Seq { get; set; }
        }

        // L436：属性名 "Id" 符合主键命名约定却被误标 [Column]——会被同时识别为主键与列映射
        private class ConventionPkColumnEntity
        {
            [XbcDbEntity.Column]
            public int Id { get; set; }
        }

        // L436：属性名 "Id" 符合主键命名约定却被误标 [Navigation]——放行会使同一属性同时进入列与导航集合
        private class ConventionPkNavigationEntity
        {
            [XbcDbEntity.Navigation]
            public int Id { get; set; }
        }

        // L505：主键为可空类型（int?）——主键不可为 NULL
        private class NullablePkEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int? Id { get; set; }
        }

        // L509：主键类型不支持（double 不在有效主键类型集合内）
        private class DoublePkEntity
        {
            [XbcDbEntity.PrimaryKey]
            public double Id { get; set; }
        }

        // L509：主键类型不支持（DateTime 不在有效主键类型集合内）
        private class DateTimePkEntity
        {
            [XbcDbEntity.PrimaryKey]
            public DateTime Id { get; set; }
        }

        // L560 的载体类型：非基础类型的自定义类
        private sealed class ComplexPayload
        {
            public string Value { get; set; }
        }

        // L560：非基础类型属性标 [Column] 但未配 ConverterType 且非 IsJson——无法完成读写转换
        private class NonPrimitiveColumnEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column]
            public ComplexPayload Payload { get; set; }
        }

        // L608：属性 getter 抛异常——初始值是跳过赋值判定的比较基准，构建期读取即报出
        private class ThrowingGetterEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Boom
            {
                get => throw new InvalidOperationException("getter 内部异常");
                set { }
            }
        }

        // L666/L736：非主键标识列类型不支持（string 不在 int/long/uint/ulong 集合内）
        private class StringIdentityEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(IsIdentity = true)]
            public string Code { get; set; }
        }

        // L682：基础类型（string 可直接映射列）误标 IsJson——会改变列存储形态并禁用文本匹配
        private class StringIsJsonEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(IsJson = true)]
            public string Name { get; set; }
        }

        // L706：转换器私有构造——构建元数据时无法实例化转换器
        private sealed class PrivateCtorConverter
        {
            private PrivateCtorConverter()
            {
            }

            public string ConvertFrom(object dbValue) => dbValue?.ToString();

            public object ConvertTo(string value) => value;
        }

        // L712：转换器缺 ConvertFrom(object) 公共实例方法
        private sealed class NoConvertFromConverter
        {
            public object ConvertTo(string value) => value;
        }

        // L724：转换器缺 ConvertTo(string) 公共实例方法（ConvertFrom 返回类型合法以越过前一校验）
        private sealed class NoConvertToConverter
        {
            public string ConvertFrom(object dbValue) => dbValue?.ToString();
        }

        private class ConverterMissingCtorEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(ConverterType = typeof(PrivateCtorConverter))]
            public string Token { get; set; }
        }

        private class ConverterNoConvertFromEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(ConverterType = typeof(NoConvertFromConverter))]
            public string Token { get; set; }
        }

        private class ConverterNoConvertToEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(ConverterType = typeof(NoConvertToConverter))]
            public string Token { get; set; }
        }

        #endregion

        #region 导航测试实体（替身）

        // 共享的合法子实体：带 OwnerId 外键列，供键名缺失/推断失败等场景复用
        private class NavSimpleChildEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public int OwnerId { get; set; }
        }

        // L829 的非法导航目标替身：接口 / 抽象类
        private interface IInvalidNavTarget
        {
        }

        private abstract class AbstractNavTarget
        {
        }

        // L285：导航对方为 string——string 是最常见的误用类型
        private class NavStringTargetEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Navigation]
            public string Label { get; set; }
        }

        // L285：导航对方为 object
        private class NavObjectTargetEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Navigation]
            public object Payload { get; set; }
        }

        // L278：导航对方为接口——接口无法实例化，不能作为实体
        private class NavInterfaceTargetEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Navigation]
            public IInvalidNavTarget Target { get; set; }
        }

        // L278：导航对方为抽象类
        private class NavAbstractTargetEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Navigation]
            public AbstractNavTarget Target { get; set; }
        }

        // L278：导航对方为值类型——值类型没有引用语义
        private class NavValueTypeTargetEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Navigation]
            public int Score { get; set; }
        }

        // L294：导航对方为委托类型
        private class NavDelegateTargetEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Navigation]
            public Action Callback { get; set; }
        }

        // L884：导航集合为 Dictionary<K,V>——不能接受 List<T> 赋值，集合类型不受支持
        private class NavDictionaryEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Navigation]
            public Dictionary<string, NavSimpleChildEntity> Children { get; set; }
        }

        // L932：对方外键列配置了 ConverterType——键值在实体赋值与数据库参数两个场景流转，应用转换器会不一致
        private class NavKeyConverterChildEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(ConverterType = typeof(NavKeyTestConverter))]
            public string OwnerId { get; set; }
        }

        private class NavKeyConverterOwnerEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Navigation(ThisKey = "Id", OtherKey = "OwnerId")]
            public List<NavKeyConverterChildEntity> Children { get; set; }
        }

        // L935：对方外键列配置 EnumAsString——名称字符串存储与对方键存储形态不一致会静默不命中
        private enum NavKeyTestStatus
        {
            A,
            B
        }

        private class NavKeyEnumChildEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(EnumAsString = true)]
            public NavKeyTestStatus OwnerId { get; set; }
        }

        private class NavKeyEnumOwnerEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Navigation(ThisKey = "Id", OtherKey = "OwnerId")]
            public List<NavKeyEnumChildEntity> Children { get; set; }
        }

        // L938：对方外键列配置 IsJson——复杂类型 JSON 值无单值等值匹配的键语义
        private sealed class NavKeyJsonPayload
        {
            public string Value { get; set; }
        }

        private class NavKeyJsonChildEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(IsJson = true)]
            public NavKeyJsonPayload OwnerId { get; set; }
        }

        private class NavKeyJsonOwnerEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Navigation(ThisKey = "Id", OtherKey = "OwnerId")]
            public List<NavKeyJsonChildEntity> Children { get; set; }
        }

        // L941：对方外键列是并发令牌——令牌由框架乐观锁专用管理，无键语义
        private class NavKeyTokenChildEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(IsConcurrencyToken = true)]
            public byte[] RowVersion { get; set; }
        }

        private class NavKeyTokenOwnerEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Navigation(ThisKey = "Id", OtherKey = "RowVersion")]
            public List<NavKeyTokenChildEntity> Children { get; set; }
        }

        // L932-941 四个分支共用的合法转换器（子实体自身构建必须成功，才能走到键列校验）
        private sealed class NavKeyTestConverter
        {
            public string ConvertFrom(object dbValue) => dbValue?.ToString();

            public object ConvertTo(string value) => value;
        }

        // L956：ThisKey 指定的属性名在本方实体中不存在
        private class NavThisKeyMissingEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Navigation(ThisKey = "NoSuchColumn", OtherKey = "OwnerId")]
            public List<NavSimpleChildEntity> Children { get; set; }
        }

        // L976：OtherKey 指定的属性名在对方实体中不存在
        private class NavOtherKeyMissingEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Navigation(ThisKey = "Id", OtherKey = "NoSuchColumn")]
            public List<NavSimpleChildEntity> Children { get; set; }
        }

        // L1001：1:N 集合导航，本方实体无主键且未显式指定 ThisKey
        private class NavOneToManyNoPkOwnerEntity
        {
            public string Name { get; set; }

            [XbcDbEntity.Navigation]
            public List<NavSimpleChildEntity> Children { get; set; }
        }

        // L1013：1:1 引用导航，按约定推断外键 "{导航属性名}Id"（ChildId）在本方实体中不存在
        private class NavOneToOneInferEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Navigation]
            public NavSimpleChildEntity Child { get; set; }
        }

        // L1053：1:N 集合导航，按约定推断对方外键 "{本方类型名}Id" 在对方实体中不存在
        private class NavCollectionInferEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Navigation]
            public List<NavNoForeignKeyChildEntity> Children { get; set; }
        }

        // NavCollectionInferEntity 的对方实体：不包含按约定命名的外键列
        private class NavNoForeignKeyChildEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; }
        }

        // L1072：1:1 引用导航，对方实体未定义主键且未显式指定 OtherKey
        private class NavTargetNoPkEntity
        {
            public string Name { get; set; }
        }

        private class NavOneToOneTargetNoPkEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public int RefId { get; set; }

            [XbcDbEntity.Navigation(ThisKey = "RefId")]
            public NavTargetNoPkEntity Target { get; set; }
        }

        // L298 的替身：仅实现非泛型 IEnumerable 的类型（L302 顶层实体类型校验拒绝）
        private sealed class LegacyEnumerableEntity : System.Collections.IEnumerable
        {
            public System.Collections.IEnumerator GetEnumerator() => null;
        }

        #endregion

        #region 列构建拒绝

        [Fact]
        public void Metadata_索引器标记Column_应抛出InvalidOperationException()
        {
            // 索引器没有单值列语义，标映射特性属配置错误，构建期即报出
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<IndexerColumnEntity>.Metadata);

            Assert.Contains("IndexerColumnEntity.Item", ex.Message);
            Assert.Contains("是索引器属性，不能标记 [Column]", ex.Message);
        }

        [Fact]
        public void Metadata_两个属性映射同一列名_应抛出InvalidOperationException()
        {
            // 列名唯一性校验不区分大小写，冲突时点名双方属性
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<DuplicateColumnEntity>.Metadata);

            Assert.Contains("映射了重复的列名 'Dup_Name'", ex.Message);
            Assert.Contains("Name1", ex.Message);
            Assert.Contains("Name2", ex.Message);
        }

        [Fact]
        public void Metadata_复合主键_应抛出InvalidOperationException()
        {
            // 多个 [PrimaryKey] 组成复合主键，当前不支持，第二个主键声明时抛出
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<CompositePkEntity>.Metadata);

            Assert.Contains("定义了多个主键", ex.Message);
            Assert.Contains("不支持复合主键", ex.Message);
        }

        [Fact]
        public void Metadata_多个标识列_应抛出InvalidOperationException()
        {
            // 主键标识列之外的列再声明 IsIdentity 即构成第二个标识列
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<DuplicateIdentityEntity>.Metadata);

            Assert.Contains("定义了多个标识列", ex.Message);
        }

        [Fact]
        public void Metadata_约定主键属性标记Column_应抛出InvalidOperationException()
        {
            // 属性名 "Id" 符合主键命名约定却被误标 [Column]，主键应改用 [PrimaryKey] 配置
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<ConventionPkColumnEntity>.Metadata);

            Assert.Contains("符合主键命名约定", ex.Message);
            Assert.Contains("不能标记 [Column] 或 [Navigation]", ex.Message);
            Assert.Contains("ConventionPkColumnEntity.Id", ex.Message);
        }

        [Fact]
        public void Metadata_约定主键属性标记Navigation_应抛出InvalidOperationException()
        {
            // 属性名 "Id" 符合主键命名约定却被误标 [Navigation]，放行会使同一属性同时进入列与导航集合
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<ConventionPkNavigationEntity>.Metadata);

            Assert.Contains("符合主键命名约定", ex.Message);
            Assert.Contains("不能标记 [Column] 或 [Navigation]", ex.Message);
            Assert.Contains("ConventionPkNavigationEntity.Id", ex.Message);
        }

        [Fact]
        public void Metadata_可空主键_应抛出InvalidOperationException()
        {
            // 主键列不可为可空类型（int?），可空意味着允许 NULL 主键值
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NullablePkEntity>.Metadata);

            Assert.Contains("是主键列，不能为可空类型", ex.Message);
            Assert.Contains("NullablePkEntity.Id", ex.Message);
        }

        [Fact]
        public void Metadata_主键类型为double_应抛出InvalidOperationException()
        {
            // double 不在有效主键类型集合（string/Guid/整数系）内
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<DoublePkEntity>.Metadata);

            Assert.Contains("是主键列，其类型 'Double' 不是有效的主键类型", ex.Message);
        }

        [Fact]
        public void Metadata_主键类型为DateTime_应抛出InvalidOperationException()
        {
            // DateTime 不在有效主键类型集合内
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<DateTimePkEntity>.Metadata);

            Assert.Contains("是主键列，其类型 'DateTime' 不是有效的主键类型", ex.Message);
        }

        [Fact]
        public void Metadata_非基础类型标记Column未配转换器_应抛出InvalidOperationException()
        {
            // 自定义类属性必须经 [Column(ConverterType = ...)] 或 [Column(IsJson = true)] 提供映射手段
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NonPrimitiveColumnEntity>.Metadata);

            Assert.Contains("类型不是基础类型", ex.Message);
            Assert.Contains("ConverterType", ex.Message);
            Assert.Contains("NonPrimitiveColumnEntity.Payload", ex.Message);
        }

        [Fact]
        public void Metadata_属性getter抛异常_应抛出InvalidOperationException()
        {
            // 初始值是"值未变跳过赋值"的比较基准，getter 抛异常时附加实体/属性定位上下文并保留原始异常
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<ThrowingGetterEntity>.Metadata);

            Assert.Contains("属性 getter 抛出异常", ex.Message);
            Assert.Contains("ThrowingGetterEntity.Boom", ex.Message);
            Assert.IsType<InvalidOperationException>(ex.InnerException);
        }

        [Fact]
        public void Metadata_非主键标识列类型为string_应抛出InvalidOperationException()
        {
            // 标识列（数据库自增）仅支持 int/long/uint/ulong，string 标识列在构建期拒绝
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<StringIdentityEntity>.Metadata);

            Assert.Contains("是标识列，其类型 'String' 不是有效的类型", ex.Message);
            Assert.Contains("int/long/uint/ulong", ex.Message);
        }

        [Fact]
        public void Metadata_基础类型标记IsJson_应抛出InvalidOperationException()
        {
            // string 可直接映射列，误标 IsJson 会改变列存储形态（引号包裹）并禁用文本匹配与排序
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<StringIsJsonEntity>.Metadata);

            Assert.Contains("标记了 IsJson", ex.Message);
            Assert.Contains("可直接映射为数据库列", ex.Message);
            Assert.Contains("StringIsJsonEntity.Name", ex.Message);
        }

        [Fact]
        public void Metadata_转换器私有构造_应抛出InvalidOperationException()
        {
            // 转换器类型必须有无参（公共）构造函数，供构建元数据时实例化
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<ConverterMissingCtorEntity>.Metadata);

            Assert.Contains("未定义无参构造函数", ex.Message);
            Assert.Contains("PrivateCtorConverter", ex.Message);
        }

        [Fact]
        public void Metadata_转换器缺ConvertFrom_应抛出InvalidOperationException()
        {
            // 转换器必须定义公共实例方法 ConvertFrom(object)（读取方向转换）
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<ConverterNoConvertFromEntity>.Metadata);

            Assert.Contains("未定义公共实例方法 ConvertFrom(object)", ex.Message);
            Assert.Contains("NoConvertFromConverter", ex.Message);
        }

        [Fact]
        public void Metadata_转换器缺ConvertTo_应抛出InvalidOperationException()
        {
            // 转换器必须定义公共实例方法 ConvertTo(T)（写入方向转换，T 为属性类型）
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<ConverterNoConvertToEntity>.Metadata);

            Assert.Contains("未定义公共实例方法 ConvertTo(String)", ex.Message);
            Assert.Contains("NoConvertToConverter", ex.Message);
        }

        #endregion

        #region 实体类型校验拒绝（顶层入口）

        // 说明：IsValidEntityType 中「未闭合泛型（L282）」分支经导航路径不可达——C# 无法把开放泛型类型
        // 声明为属性类型，顶层入口在进入校验前已单独报错（既有测试已覆盖）；「匿名类型（L289）」分支
        // 同理不可达——匿名类型无法作为实体/导航属性类型显式声明。

        [Fact]
        public void GetMetadata_实体类型为数组_应抛出InvalidOperationException()
        {
            // 数组类型不是可实例化的单值实体，顶层入口直接拒绝
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(NavSimpleChildEntity[])));

            Assert.Contains("必须是可实例化的引用类型", ex.Message);
            Assert.Contains("不能是接口、抽象类、值类型、string 或 object", ex.Message);
        }

        [Fact]
        public void GetMetadata_实体类型为非泛型集合_应抛出InvalidOperationException()
        {
            // 仅实现非泛型 IEnumerable 的类型按集合类型拒绝，不能作为实体
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(LegacyEnumerableEntity)));

            Assert.Contains("必须是可实例化的引用类型", ex.Message);
        }

        #endregion

        #region 导航构建拒绝——对方类型

        [Fact]
        public void Metadata_导航对方为string_应抛出InvalidOperationException()
        {
            // string 虽是引用类型但不是实体类型（且不按集合处理），导航对方必须是具体实体
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NavStringTargetEntity>.Metadata);

            Assert.Contains("不是有效的实体类型", ex.Message);
            Assert.Contains("NavStringTargetEntity.Label", ex.Message);
        }

        [Fact]
        public void Metadata_导航对方为object_应抛出InvalidOperationException()
        {
            // object 是最常见的误用对方类型，构建期拒绝
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NavObjectTargetEntity>.Metadata);

            Assert.Contains("不是有效的实体类型", ex.Message);
            Assert.Contains("NavObjectTargetEntity.Payload", ex.Message);
        }

        [Fact]
        public void Metadata_导航对方为接口_应抛出InvalidOperationException()
        {
            // 接口无法实例化，不能作为导航对方实体
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NavInterfaceTargetEntity>.Metadata);

            Assert.Contains("不是有效的实体类型", ex.Message);
            Assert.Contains("NavInterfaceTargetEntity.Target", ex.Message);
        }

        [Fact]
        public void Metadata_导航对方为抽象类_应抛出InvalidOperationException()
        {
            // 抽象类无法实例化，不能作为导航对方实体
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NavAbstractTargetEntity>.Metadata);

            Assert.Contains("不是有效的实体类型", ex.Message);
            Assert.Contains("NavAbstractTargetEntity.Target", ex.Message);
        }

        [Fact]
        public void Metadata_导航对方为值类型_应抛出InvalidOperationException()
        {
            // 值类型没有实体引用语义，不能作为导航对方实体
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NavValueTypeTargetEntity>.Metadata);

            Assert.Contains("不是有效的实体类型", ex.Message);
            Assert.Contains("NavValueTypeTargetEntity.Score", ex.Message);
        }

        [Fact]
        public void Metadata_导航对方为委托_应抛出InvalidOperationException()
        {
            // 委托类型不是实体，构建期拒绝
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NavDelegateTargetEntity>.Metadata);

            Assert.Contains("不是有效的实体类型", ex.Message);
            Assert.Contains("NavDelegateTargetEntity.Callback", ex.Message);
        }

        #endregion

        #region 导航构建拒绝——集合形态与键列

        [Fact]
        public void Metadata_导航集合为Dictionary_应抛出InvalidOperationException()
        {
            // Dictionary<K,V> 不能接受 List<T> 赋值，1:N 集合仅支持 List<T> 及其可赋值泛型接口
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NavDictionaryEntity>.Metadata);

            Assert.Contains("的集合类型", ex.Message);
            Assert.Contains("不受支持", ex.Message);
            Assert.Contains("NavDictionaryEntity.Children", ex.Message);
        }

        [Fact]
        public void Metadata_导航键列配置转换器_应抛出InvalidOperationException()
        {
            // 键值在实体赋值与数据库参数两个场景间流转，应用转换器会破坏外键同步与装配正确性
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NavKeyConverterOwnerEntity>.Metadata);

            Assert.Contains("不支持自定义转换器", ex.Message);
            Assert.Contains("OtherKey 'OwnerId'", ex.Message);
        }

        [Fact]
        public void Metadata_导航键列配置EnumAsString_应抛出InvalidOperationException()
        {
            // 名称字符串存储与对方键的存储形态不一致，等值匹配会静默不命中
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NavKeyEnumOwnerEntity>.Metadata);

            Assert.Contains("不支持 EnumAsString", ex.Message);
            Assert.Contains("OtherKey 'OwnerId'", ex.Message);
        }

        [Fact]
        public void Metadata_导航键列配置IsJson_应抛出InvalidOperationException()
        {
            // 复杂类型 JSON 值没有单值等值匹配的键语义
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NavKeyJsonOwnerEntity>.Metadata);

            Assert.Contains("不支持 IsJson", ex.Message);
            Assert.Contains("OtherKey 'OwnerId'", ex.Message);
        }

        [Fact]
        public void Metadata_导航键列为并发令牌_应抛出InvalidOperationException()
        {
            // 并发令牌由框架乐观锁专用管理，不是关系键
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NavKeyTokenOwnerEntity>.Metadata);

            Assert.Contains("是并发令牌列", ex.Message);
            Assert.Contains("无键语义", ex.Message);
            Assert.Contains("OtherKey 'RowVersion'", ex.Message);
        }

        #endregion

        #region 导航构建拒绝——键名与推断

        [Fact]
        public void Metadata_ThisKey属性名不存在_应抛出InvalidOperationException()
        {
            // 显式指定的 ThisKey 在本方实体中找不到对应属性时，提示检查 [Navigation(ThisKey = ...)]
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NavThisKeyMissingEntity>.Metadata);

            Assert.Contains("ThisKey 'NoSuchColumn'", ex.Message);
            Assert.Contains("未找到对应的属性", ex.Message);
        }

        [Fact]
        public void Metadata_OtherKey属性名不存在_应抛出InvalidOperationException()
        {
            // 显式指定的 OtherKey 在对方实体中找不到对应属性时，提示检查 [Navigation(OtherKey = ...)]
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NavOtherKeyMissingEntity>.Metadata);

            Assert.Contains("OtherKey 'NoSuchColumn'", ex.Message);
            Assert.Contains("未找到对应的属性", ex.Message);
        }

        [Fact]
        public void Metadata_1对多本方无主键_应抛出InvalidOperationException()
        {
            // 1:N 集合的 ThisKey 约定取本方主键，本方无主键且未显式指定时构建期报出
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NavOneToManyNoPkOwnerEntity>.Metadata);

            Assert.Contains("为 1:N 集合，但本方实体", ex.Message);
            Assert.Contains("未定义主键", ex.Message);
        }

        [Fact]
        public void Metadata_1对1外键推断失败_应抛出InvalidOperationException()
        {
            // 1:1 引用的 ThisKey 约定取 "{导航属性名}Id"（ChildId），本方不存在该列时提示手动指定
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NavOneToOneInferEntity>.Metadata);

            Assert.Contains("1:1 引用外键属性 'ChildId' 未找到", ex.Message);
        }

        [Fact]
        public void Metadata_1对多集合外键推断失败_应抛出InvalidOperationException()
        {
            // 1:N 集合的 OtherKey 约定取对方实体中 "{本方类型名}Id" 外键列，不存在时提示手动指定
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NavCollectionInferEntity>.Metadata);

            Assert.Contains("1:N 集合外键属性", ex.Message);
            Assert.Contains("NavCollectionInferEntityId", ex.Message);
            Assert.Contains("未找到", ex.Message);
        }

        [Fact]
        public void Metadata_1对1对方无主键_应抛出InvalidOperationException()
        {
            // 1:1 引用的 OtherKey 约定取对方主键，对方未定义主键且未显式指定时构建期报出
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache<NavOneToOneTargetNoPkEntity>.Metadata);

            Assert.Contains("为 1:1 引用，但对方实体", ex.Message);
            Assert.Contains("未定义主键", ex.Message);
        }

        #endregion
    }
}
