using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 实体映射器核心的守卫与特殊转换分支覆盖测试（Stryker NoCoverage 补测，纯读取器桩不依赖数据库）：
    /// 非泛型入口值类型拒绝、投影列数/成员绑定数一致性校验、按类型清缓存、未映射列告警直通、
    /// ThrowException 空值抛错（实体路径与投影路径）、byte 字段到 short/sbyte 属性的宽化分流
    ///（无损宽化走 conv 节点、跨符号窄化进行内守卫）、命名类型/匿名类型投影委托构建的形态校验。
    /// </summary>
    public class EntityMapperCoreCoverageTests
    {
        #region 测试实体与投影结果类型

        /// <summary>基础实体载体（Id、Age 两列）。</summary>
        public class ProbeEntity
        {
            public int Id { get; set; }

            public int Age { get; set; }
        }

        /// <summary>ThrowException 空值处理载体：Remark 列声明 NULL 抛 InvalidCastException。</summary>
        public class ThrowProbeEntity
        {
            public int Id { get; set; }

            [XbcDbEntity.Column(DbNullHandling = XbcDbEntity.DbNullHandling.ThrowException)]
            public string Remark { get; set; }
        }

        /// <summary>宽化分流载体：short 属性为无损宽化目标，sbyte 属性为跨符号窄化目标。</summary>
        public class WideningProbeEntity
        {
            public short NarrowTarget { get; set; }

            public sbyte TightTarget { get; set; }
        }

        /// <summary>命名类型投影的无参构造缺失载体（仅有带参构造）。</summary>
        public class NoDefaultCtorProbe
        {
            public NoDefaultCtorProbe(int a)
            {
                A = a;
            }

            public int A { get; set; }
        }

        /// <summary>非匿名命名类型载体（Members 为 null 时多列投影应被拒绝）。</summary>
        public class NamedMultiColProbe
        {
            public int A { get; set; }

            public int B { get; set; }
        }

        #endregion

        #region 读取器桩

        /// <summary>构建单行双列读取器：Id(int) 与 Age(int)，Age 值固定 21。</summary>
        private static Mock<DbDataReader> CreateIdAgeReader(int fieldCount, params (string Name, Type FieldType)[] extraColumns)
        {
            var mockReader = new Mock<DbDataReader>();
            mockReader.Setup(r => r.FieldCount).Returns(fieldCount);
            mockReader.Setup(r => r.GetName(0)).Returns("Id");
            mockReader.Setup(r => r.GetName(1)).Returns("Age");
            mockReader.Setup(r => r.GetFieldType(0)).Returns(typeof(int));
            mockReader.Setup(r => r.GetFieldType(1)).Returns(typeof(int));
            mockReader.Setup(r => r.GetInt32(0)).Returns(1);
            mockReader.Setup(r => r.GetInt32(1)).Returns(21);

            for (var i = 0; i < extraColumns.Length; i++)
            {
                var index = 2 + i;
                mockReader.Setup(r => r.GetName(index)).Returns(extraColumns[i].Name);
                mockReader.Setup(r => r.GetFieldType(index)).Returns(extraColumns[i].FieldType);
            }

            mockReader.SetupSequence(r => r.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
            return mockReader;
        }

        /// <summary>构建 byte 字段双列读取器：NarrowTarget 与 TightTarget 的字段类型均为 byte（GetByte 值 200 与指定值）。</summary>
        private static Mock<DbDataReader> CreateByteSourceReader(byte tightValue)
        {
            var mockReader = new Mock<DbDataReader>();
            mockReader.Setup(r => r.FieldCount).Returns(2);
            mockReader.Setup(r => r.GetName(0)).Returns("NarrowTarget");
            mockReader.Setup(r => r.GetName(1)).Returns("TightTarget");
            mockReader.Setup(r => r.GetFieldType(0)).Returns(typeof(byte));
            mockReader.Setup(r => r.GetFieldType(1)).Returns(typeof(byte));
            mockReader.Setup(r => r.GetByte(0)).Returns(200);
            mockReader.Setup(r => r.GetByte(1)).Returns(tightValue);
            mockReader.SetupSequence(r => r.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
            return mockReader;
        }

        /// <summary>构建单列读取器（列名与字段类型可指定），供投影委托构建的守卫测试使用。</summary>
        private static Mock<DbDataReader> CreateSingleColumnReader(string columnName, Type fieldType, bool dbNull = false)
        {
            var mockReader = new Mock<DbDataReader>();
            mockReader.Setup(r => r.FieldCount).Returns(1);
            mockReader.Setup(r => r.GetName(0)).Returns(columnName);
            mockReader.Setup(r => r.GetFieldType(0)).Returns(fieldType);
            mockReader.Setup(r => r.IsDBNull(0)).Returns(dbNull);
            return mockReader;
        }

        /// <summary>匿名类型 TResult 的通道：TResult 由 resultTemplate 推断（不参与构建）；实体类型经 Type 反射闭合双泛型内部入口。</summary>
        private static Func<DbDataReader, TResult> CallProjection<TResult>(DbDataReader reader, XbcDbEntity.ProjectionPlan plan, Type entityType, TResult resultTemplate)
        {
            _ = resultTemplate;
            try
            {
                return (Func<DbDataReader, TResult>)typeof(XbcDbEntity.EntityMapperCore)
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(m => m.Name == "GetOrAddProjectionMapper" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2)
                    .MakeGenericMethod(entityType, typeof(TResult))
                    .Invoke(null, new object[] { reader, plan });
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                // 反射调用剥包装：还原内部入口抛出的原始异常类型
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw;
            }
        }

        #endregion

        #region 非泛型入口守卫

        [Fact]
        public async Task EntityMapper_MapAsync_ValueTypeEntity_Throws()
        {
            // 值类型实体在元数据构建层即被拒绝（必须是可实例化的引用类型），映射入口不重复守卫，经 MapAsync 走通真实入口
            var reader = new Mock<DbDataReader>();
            reader.Setup(r => r.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => XbcDbEntity.EntityMapper.MapAsync(typeof(int), reader.Object));

            Assert.Contains("值类型", ex.Message);
        }

        #endregion

        #region 投影入口一致性校验

        [Fact]
        public void GetOrAddProjectionMapper_FieldCountMismatch_Throws()
        {
            // 投影结果集列数（2）与投影列数（1）不一致：入口处显式失败
            var ageColumn = XbcDbEntity.EntityMetadataCache<ProbeEntity>.GetColumn(nameof(ProbeEntity.Age));

            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMapperCore.GetOrAddProjectionMapper<ProbeEntity, int>(
                    CreateIdAgeReader(2).Object, XbcDbEntity.ProjectionPlan.Scalar(new[] { ageColumn })));

            Assert.Contains("列数", ex.Message);
        }

        [Fact]
        public void ProjectionPlan_MemberCountMismatch_Throws()
        {
            // 命名类型分支：投影列数（1）与成员绑定数（2）不一致——互锁守卫在计划构造时即显式失败（守卫已从 mapper 上移到计划）
            var ageColumn = XbcDbEntity.EntityMetadataCache<ProbeEntity>.GetColumn(nameof(ProbeEntity.Age));

            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.ProjectionPlan.MemberBinding(
                    new[] { ageColumn },
                    new[] { typeof(ProbeEntity).GetProperty(nameof(ProbeEntity.Id)), typeof(ProbeEntity).GetProperty(nameof(ProbeEntity.Age)) }));

            Assert.Contains("成员绑定数", ex.Message);
        }

        [Fact]
        public void GetOrAddProjectionMapper_TupleResult_AssemblesByPosition()
        {
            // 元组结果类型与 Positional 计划一致：构造函数随解析层统一固化进计划（此处直接构建计划，显式携带唯一公共构造），
            // 经构造按位置装配（与匿名类型同一装配分支）
            var idColumn = XbcDbEntity.EntityMetadataCache<ProbeEntity>.GetColumn(nameof(ProbeEntity.Id));
            var ageColumn = XbcDbEntity.EntityMetadataCache<ProbeEntity>.GetColumn(nameof(ProbeEntity.Age));

            var mapper = CallProjection(CreateIdAgeReader(2).Object,
                XbcDbEntity.ProjectionPlan.Positional(new[] { idColumn, ageColumn }, typeof(ValueTuple<int, int>).GetConstructors()[0]), typeof(ProbeEntity), (0, 0));

            var tuple = mapper(CreateIdAgeReader(2).Object);
            Assert.Equal(1, tuple.Item1);
            Assert.Equal(21, tuple.Item2);
        }

        [Fact]
        public void GetOrAddProjectionMapper_ScalarPlanWithTupleResult_Throws()
        {
            // 形态与结果类型一致性守卫：Scalar 计划配元组结果 → 显式失败
            var ageColumn = XbcDbEntity.EntityMetadataCache<ProbeEntity>.GetColumn(nameof(ProbeEntity.Age));

            var ex = Assert.Throws<InvalidOperationException>(
                () => CallProjection(CreateSingleColumnReader("Age", typeof(int)).Object, XbcDbEntity.ProjectionPlan.Scalar(new[] { ageColumn }), typeof(ProbeEntity), ValueTuple.Create(0)));

            Assert.Contains("不一致", ex.Message);
        }

        [Fact]
        public void GetOrAddProjectionMapper_OverLengthTupleResult_Throws()
        {
            // 8 元 TRest 计划在工厂互锁即被拒（构造参数数 8 ≠ 投影列数 1，建计划即失败）；
            // TRest 专属守卫现位于解析层与分组投影入口，mapper 不再重复
            var ageColumn = XbcDbEntity.EntityMetadataCache<ProbeEntity>.GetColumn(nameof(ProbeEntity.Age));
            var overLengthTemplate = ValueTuple.Create(0, 0, 0, 0, 0, 0, 0, 0);// 8 元素元组

            var ex = Assert.Throws<ArgumentException>(
                () => CallProjection(CreateSingleColumnReader("Age", typeof(int)).Object,
                    XbcDbEntity.ProjectionPlan.Positional(new[] { ageColumn }, overLengthTemplate.GetType().GetConstructors()[0]), typeof(ProbeEntity), overLengthTemplate));

            Assert.Contains("参数数", ex.Message);
        }

        [Fact]
        public void GetOrAddProjectionMapper_NoParameterlessConstructor_Throws()
        {
            // 命名类型投影按成员赋值填充，须提供公共无参构造函数
            var reader = CreateSingleColumnReader("Age", typeof(int));
            var ageColumn = XbcDbEntity.EntityMetadataCache<ProbeEntity>.GetColumn(nameof(ProbeEntity.Age));

            var ex = Assert.Throws<NotSupportedException>(
                () => XbcDbEntity.EntityMapperCore.GetOrAddProjectionMapper<ProbeEntity, NoDefaultCtorProbe>(
                    reader.Object,
                    XbcDbEntity.ProjectionPlan.MemberBinding(new[] { ageColumn }, new[] { typeof(NoDefaultCtorProbe).GetProperty(nameof(NoDefaultCtorProbe.A)) })));

            Assert.Contains("无参构造", ex.Message);
        }

        [Fact]
        public void GetOrAddProjectionMapper_AnonymousConstructorMismatch_Throws()
        {
            // 匿名类型按构造参数位置映射：构造参数数（2）与投影列数（1）不一致在建计划时即拒绝
            // （互锁守卫已上移至计划工厂，mapper 直接采用计划携带的构造函数）
            var reader = CreateSingleColumnReader("Age", typeof(int));
            var ageColumn = XbcDbEntity.EntityMetadataCache<ProbeEntity>.GetColumn(nameof(ProbeEntity.Age));
            var anonymousTemplate = new { A = 0, B = string.Empty };

            var ex = Assert.Throws<ArgumentException>(
                () => CallProjection(reader.Object,
                    XbcDbEntity.ProjectionPlan.Positional(new[] { ageColumn }, anonymousTemplate.GetType().GetConstructors()[0]), typeof(ProbeEntity), anonymousTemplate));

            Assert.Contains("不一致", ex.Message);
        }

        [Fact]
        public void ProjectionPlan_NonAnonymousMultiColumn_Throws()
        {
            // 非匿名类型的多列标量形态不可达（命名类型走成员绑定、匿名类型走位置构造），
            // 计划构造的互锁守卫应显式拒绝（原 mapper 收尾分支校验已上移至计划）
            var ageColumn = XbcDbEntity.EntityMetadataCache<ProbeEntity>.GetColumn(nameof(ProbeEntity.Age));
            var idColumn = XbcDbEntity.EntityMetadataCache<ProbeEntity>.GetColumn(nameof(ProbeEntity.Id));

            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.ProjectionPlan.Scalar(new[] { ageColumn, idColumn }));

            Assert.Contains("只允许单列", ex.Message);
        }

        #endregion

        #region 缓存失效与未映射列

        [Fact]
        public void ClearCache_ProjectionEntriesRemoved_RebuildWorks()
        {
            // 按实体类型清理投影委托缓存：清理后重建的委托应与原委托为不同实例且行为一致
            var reader = CreateSingleColumnReader("Age", typeof(int));
            reader.Setup(r => r.GetInt32(0)).Returns(21);
            var ageColumn = XbcDbEntity.EntityMetadataCache<ProbeEntity>.GetColumn(nameof(ProbeEntity.Age));

            var mapperBefore = XbcDbEntity.EntityMapperCore.GetOrAddProjectionMapper<ProbeEntity, int>(
                reader.Object, XbcDbEntity.ProjectionPlan.Scalar(new[] { ageColumn }));
            Assert.Equal(21, mapperBefore(reader.Object));

            XbcDbEntity.EntityMapperCore.ClearCache(typeof(ProbeEntity));

            var mapperAfter = XbcDbEntity.EntityMapperCore.GetOrAddProjectionMapper<ProbeEntity, int>(
                reader.Object, XbcDbEntity.ProjectionPlan.Scalar(new[] { ageColumn }));

            Assert.NotSame(mapperBefore, mapperAfter);
            Assert.Equal(21, mapperAfter(reader.Object));
        }

        [Fact]
        public async Task MapList_UnmappedReaderColumn_WarnsButMapsMappedColumns()
        {
            // 结果集包含实体未映射的额外列（Ghost）：告警但不抛异常，已映射列照常赋值
            var reader = CreateIdAgeReader(3, ("Ghost", typeof(int)));

            var result = await XbcDbEntity.EntityMapper<ProbeEntity>.MapListAsync(reader.Object);

            var entity = Assert.Single(result);
            Assert.Equal(1, entity.Id);
            Assert.Equal(21, entity.Age);
        }

        #endregion

        #region ThrowException 空值处理（实体路径与投影路径）

        [Fact]
        public async Task MapList_ThrowExceptionColumn_Null_ThrowsInvalidCast()
        {
            // 实体赋值路径：ThrowException 列遇 NULL，映射委托抛带属性名上下文的 InvalidCastException
            var reader = CreateIdAgeReader(2);
            reader.Setup(r => r.GetName(1)).Returns("Remark");
            reader.Setup(r => r.GetFieldType(1)).Returns(typeof(string));
            reader.Setup(r => r.IsDBNull(1)).Returns(true);

            var ex = await Assert.ThrowsAsync<InvalidCastException>(
                () => XbcDbEntity.EntityMapper<ThrowProbeEntity>.MapListAsync(reader.Object));

            Assert.Contains("Remark", ex.Message);
            Assert.Contains("ThrowException", ex.Message);
        }

        // 说明：标量投影（Select(u => u.Remark)）的 ThrowException NULL 抛错不经 GetOrAddProjectionMapper
        //（该入口的 TResult 是实体/匿名/命名 DTO 形态，string 标量无对应的命名绑定形态），其语义已由
        // 上方实体赋值路径的 MapList_ThrowExceptionColumn_Null_ThrowsInvalidCast 覆盖。

        #endregion

        #region byte 字段宽化分流（无损宽化与跨符号窄化守卫）

        [Fact]
        public async Task MapList_ByteField_ShortPropertyLosslessWidening_MapsValue()
        {
            // byte 字段（无符号）→ short 属性（有符号）：位宽更宽为无损宽化，走原生 conv 节点直接转换；
            // sbyte 属性在范围内时经行内守卫正常映射
            var reader = CreateByteSourceReader(tightValue: 5);

            var result = await XbcDbEntity.EntityMapper<WideningProbeEntity>.MapListAsync(reader.Object);

            var entity = Assert.Single(result);
            Assert.Equal(200, entity.NarrowTarget);
            Assert.Equal(5, entity.TightTarget);
        }

        [Fact]
        public async Task MapList_ByteField_SbytePropertyOutOfRange_Throws()
        {
            // byte 字段 → sbyte 属性：同位宽跨符号不构成无损宽化，进行内守卫；
            // 200 超出 sbyte 范围应显式失败，而非 unchecked conv 静默回绕成负数
            var reader = CreateByteSourceReader(tightValue: 200);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => XbcDbEntity.EntityMapper<WideningProbeEntity>.MapListAsync(reader.Object));

            Assert.Contains("超出目标类型", ex.Message);
        }

        #endregion
    }
}
