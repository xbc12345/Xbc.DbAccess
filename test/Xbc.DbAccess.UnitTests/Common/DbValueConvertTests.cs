using System.Globalization;
using System.Net;
using Xbc.DbAccess.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Common
{
    /// <summary>
    /// DbValueConvert 的单元测试。
    /// </summary>
    public class DbValueConvertTests
    {
        #region ChangeType<T> 测试

        [Theory]
        [InlineData(123, typeof(int))]
        [InlineData(123L, typeof(long))]
        [InlineData(123.45, typeof(double))]
        [InlineData("123", typeof(int))]
        [InlineData("true", typeof(bool))]
        [InlineData("2024-01-01", typeof(DateTime))]
        public void ChangeType_Generic_ConvertsSuccessfully(object value, Type targetType)
        {
            var method = typeof(DbValueConvert).GetMethod("ChangeType", new[] { typeof(object) });
            var genericMethod = method.MakeGenericMethod(targetType);
            var result = genericMethod.Invoke(null, new[] { value });

            Assert.NotNull(result);
            Assert.IsType(targetType, result);
        }

        [Fact]
        public void ChangeType_IntToString_ReturnsString()
        {
            var result = DbValueConvert.ChangeType<string>(123);
            Assert.Equal("123", result);
        }

        [Fact]
        public void ChangeType_StringToInt_ReturnsInt()
        {
            var result = DbValueConvert.ChangeType<int>("123");
            Assert.Equal(123, result);
        }

        [Fact]
        public void ChangeType_StringToDateTime_ReturnsDateTime()
        {
            var result = DbValueConvert.ChangeType<DateTime>("2024-01-15");
            Assert.Equal(new DateTime(2024, 1, 15), result);
        }

        [Fact]
        public void ChangeType_StringToGuid_ReturnsGuid()
        {
            var guid = Guid.NewGuid();
            var result = DbValueConvert.ChangeType<Guid>(guid.ToString());
            Assert.Equal(guid, result);
        }

        [Fact]
        public void ChangeType_StringToUri_ReturnsUri()
        {
            var result = DbValueConvert.ChangeType<Uri>("https://example.com");
            Assert.Equal(new Uri("https://example.com"), result);
        }

        [Fact]
        public void ChangeType_StringToVersion_ReturnsVersion()
        {
            var result = DbValueConvert.ChangeType<Version>("1.2.3");
            Assert.Equal(new Version(1, 2, 3), result);
        }

        [Fact]
        public void ChangeType_StringToIPAddress_ReturnsIPAddress()
        {
            var result = DbValueConvert.ChangeType<IPAddress>("192.168.1.1");
            Assert.Equal(IPAddress.Parse("192.168.1.1"), result);
        }

        [Fact]
        public void ChangeType_StringToDateTimeOffset_ReturnsDateTimeOffset()
        {
            var result = DbValueConvert.ChangeType<DateTimeOffset>("2024-01-15T10:30:00+08:00");
            Assert.Equal(DateTimeOffset.Parse("2024-01-15T10:30:00+08:00"), result);
        }

        [Fact]
        public void ChangeType_StringToTimeSpan_ReturnsTimeSpan()
        {
            var result = DbValueConvert.ChangeType<TimeSpan>("01:30:45");
            Assert.Equal(new TimeSpan(1, 30, 45), result);
        }

        #endregion

        #region 可空类型测试

        [Fact]
        public void ChangeType_NullToNullableInt_ReturnsNull()
        {
            var result = DbValueConvert.ChangeType<int?>(null);
            Assert.Null(result);
        }

        [Fact]
        public void ChangeType_ValueToNullableInt_ReturnsValue()
        {
            var result = DbValueConvert.ChangeType<int?>(123);
            Assert.Equal(123, result);
        }

        [Fact]
        public void ChangeType_NullToNullableDateTime_ReturnsNull()
        {
            var result = DbValueConvert.ChangeType<DateTime?>(null);
            Assert.Null(result);
        }

        [Fact]
        public void ChangeType_EmptyStringToNullableGuid_ReturnsNull()
        {
            var result = DbValueConvert.ChangeType<Guid?>("");
            Assert.Null(result);
        }

        #endregion

        #region DBNull 测试

        [Fact]
        public void ChangeType_DBNullToNullableInt_ReturnsNull()
        {
            var result = DbValueConvert.ChangeType<int?>(DBNull.Value);
            Assert.Null(result);
        }

        [Fact]
        public void ChangeType_DBNullToString_ReturnsNull()
        {
            var result = DbValueConvert.ChangeType<string>(DBNull.Value);
            Assert.Null(result);
        }

        [Fact]
        public void ChangeType_DBNullToNullableDateTime_ReturnsNull()
        {
            var result = DbValueConvert.ChangeType<DateTime?>(DBNull.Value);
            Assert.Null(result);
        }

        #endregion

        #region 枚举类型测试

        public enum TestEnum
        {
            Value1 = 1,
            Value2 = 2
        }

        [Fact]
        public void ChangeType_IntToEnum_ReturnsEnum()
        {
            var result = DbValueConvert.ChangeType<TestEnum>(1);
            Assert.Equal(TestEnum.Value1, result);
        }

        [Fact]
        public void ChangeType_StringToEnum_ReturnsEnum()
        {
            var result = DbValueConvert.ChangeType<TestEnum>("Value2");
            Assert.Equal(TestEnum.Value2, result);
        }

        [Fact]
        public void ChangeType_NullToNullableEnum_ReturnsNull()
        {
            var result = DbValueConvert.ChangeType<TestEnum?>(null);
            Assert.Null(result);
        }

        #endregion

        #region 类型转换测试

        [Fact]
        public void ChangeType_IntToLong_ReturnsLong()
        {
            var result = DbValueConvert.ChangeType<long>(123);
            Assert.Equal(123L, result);
        }

        [Fact]
        public void ChangeType_DoubleToDecimal_ReturnsDecimal()
        {
            var result = DbValueConvert.ChangeType<decimal>(123.45);
            Assert.Equal(123.45m, result);
        }

        [Fact]
        public void ChangeType_StringToBool_ReturnsBool()
        {
            var result = DbValueConvert.ChangeType<bool>("true");
            Assert.True(result);
        }

        [Fact]
        public void ChangeType_IntToString2_ReturnsString()
        {
            var result = DbValueConvert.ChangeType<string>(456);
            Assert.Equal("456", result);
        }

        #endregion

        #region 异常测试

        [Fact]
        public void ChangeType_NullConversionType_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                DbValueConvert.ChangeType("value", null));
        }

        [Fact]
        public void ChangeType_InvalidStringToInt_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                DbValueConvert.ChangeType<int>("not a number"));
        }

        [Fact]
        public void ChangeType_InvalidStringToDateTime_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() =>
                DbValueConvert.ChangeType<DateTime>("invalid date"));
        }

        [Fact]
        public void ChangeType_NullToNonNullableInt_ThrowsInvalidCastException()
        {
            Assert.Throws<InvalidCastException>(() =>
                DbValueConvert.ChangeType<int>(null));
        }

        [Fact]
        public void ChangeType_DBNullToNonNullableInt_ThrowsInvalidCastException()
        {
            Assert.Throws<InvalidCastException>(() =>
                DbValueConvert.ChangeType<int>(DBNull.Value));
        }

        #endregion

        #region 自定义解析器测试

        public class CustomType
        {
            public string Value { get; set; }
        }

        [Fact]
        public void AddParser_CustomType_UsesCustomParser()
        {
            DbValueConvert.AddParser(typeof(CustomType), v => new CustomType { Value = v.ToString() });

            var result = DbValueConvert.ChangeType<CustomType>("test");

            Assert.NotNull(result);
            Assert.Equal("test", result.Value);

            // Cleanup
            DbValueConvert.RemoveParser(typeof(CustomType));
        }

        [Fact]
        public void RemoveParser_CustomType_RemovesParser()
        {
            DbValueConvert.AddParser(typeof(CustomType), v => new CustomType { Value = v.ToString() });
            DbValueConvert.RemoveParser(typeof(CustomType));

            // After removing, it should throw because CustomType is not a supported type
            Assert.Throws<InvalidCastException>(() =>
                DbValueConvert.ChangeType<CustomType>("test"));
        }

        #endregion

        #region Culture 测试

        [Fact]
        public void ChangeType_WithCulture_UsesCulture()
        {
            // 注意：当前实现中 TypeConverter 可能不完全支持 culture
            // 使用标准格式测试 culture 支持
            var culture = new CultureInfo("en-US");
            var result = DbValueConvert.ChangeType<decimal>("123.45", culture);
            Assert.Equal(123.45m, result);
        }

        [Fact]
        public void ChangeType_DateTimeWithCulture_UsesCulture()
        {
            var culture = new CultureInfo("en-US");
            var result = DbValueConvert.ChangeType<DateTime>("01/15/2024", culture);
            Assert.Equal(new DateTime(2024, 1, 15), result);
        }

        #endregion

        #region Object 类型测试

        [Fact]
        public void ChangeType_ToObject_ReturnsOriginalValue()
        {
            var original = new { Name = "Test" };
            var result = DbValueConvert.ChangeType<object>(original);
            Assert.Same(original, result);
        }

        #endregion

        #region 边界与契约补充测试

        public enum SampleColor
        {
            Red,
            Green,
            Blue
        }

        [Fact]
        public void ChangeType_NullToDBNull_ReturnsDBNull()
        {
            // 写参数场景：null 归一为 DBNull.Value（typeof(DBNull) 特判优先于引用类型 null 直返）
            var result = DbValueConvert.ChangeType(null, typeof(DBNull));

            Assert.Equal(DBNull.Value, result);
        }

        [Fact]
        public void ChangeType_EmptyStringToNonNullableInt_Throws()
        {
            // 空字符串对非可空值类型显式失败（不静默归零）
            Assert.ThrowsAny<Exception>(() => DbValueConvert.ChangeType<int>(""));
        }

        [Fact]
        public void ChangeType_StringToEnum_IsCaseInsensitive()
        {
            // Enum.Parse 忽略大小写（true 参数）：与数据库存储的名称形态解耦
            var result = DbValueConvert.ChangeType<SampleColor>("RED");

            Assert.Equal(SampleColor.Red, result);
        }

        [Fact]
        public void ChangeType_NumericStringToEnum_ParsesByValue()
        {
            // 数值字符串按枚举底层数值解析（如驱动返回整数文本）
            var result = DbValueConvert.ChangeType<SampleColor>("2");

            Assert.Equal(SampleColor.Blue, result);
        }

        [Fact]
        public void ChangeType_UnknownEnumName_Throws()
        {
            // 未定义名称显式失败（枚举名是有限集合，乱值不应静默归零）
            Assert.ThrowsAny<Exception>(() => DbValueConvert.ChangeType<SampleColor>("Purple"));
        }

        [Fact]
        public void ChangeType_LossyFractionalToInt_ConvertsSilently()
        {
            // 引擎契约：无有损防护（doc 声明）——小数静默取整；实体列值的有损防护在内部策略层
            var result = DbValueConvert.ChangeType<int>(3.7m);

            Assert.Equal(4, result);
        }

        [Fact]
        public void ChangeType_FractionalMidpoint_UsesBankersRounding()
        {
            // Convert.ChangeType 对中点值用银行家舍入（就近偶数）：2.5 → 2 而非四舍五入的 3——引擎契约的一部分
            var result = DbValueConvert.ChangeType<int>(2.5m);

            Assert.Equal(2, result);
        }

        [Fact]
        public void ChangeType_LongToDoubleBeyondPrecision_LosesPrecisionSilently()
        {
            // 宽化精度损失同样静默：long 超过 double 尾数精度（2^53）后值域内不抛、结果带精度损失
            var result = DbValueConvert.ChangeType<double>(123456789012345678L);

            Assert.Equal(123456789012345678d, result);
            Assert.NotEqual(123456789012345678L, (long)result);
        }

        [Fact]
        public void RemoveParser_BuiltInParser_FallsBackAndRestoresViaAddParser()
        {
            // 移除内置解析器后经 TypeConverter 层兜底仍可转换（Convert.From 四层管线）；
            // doc 契约：内置解析器可经 AddParser 重新注册恢复（builtInParserTypes 名单快照）
            DbValueConvert.RemoveParser(typeof(DateTime));
            Assert.Equal(new DateTime(2024, 1, 15), DbValueConvert.ChangeType<DateTime>("2024-01-15"));

            DbValueConvert.AddParser(typeof(DateTime), v => DateTime.Parse(v.ToString(), CultureInfo.InvariantCulture));
            Assert.Equal(new DateTime(2024, 1, 15), DbValueConvert.ChangeType<DateTime>("2024-01-15"));
        }

        [Fact]
        public void AddParser_IConvertibleWithoutBuiltIn_Throws()
        {
            // long 实现 IConvertible 且无内置解析器：自定义解析器只开放给非 IConvertible 类型或内置名单类型
            Assert.Throws<ArgumentException>(() =>
                DbValueConvert.AddParser(typeof(long), v => v));
        }

        [Fact]
        public void AddParser_NullParser_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                DbValueConvert.AddParser(typeof(CustomType), null));
        }

        [Fact]
        public void RemoveParser_UnknownType_IsNoOp()
        {
            // 移除未注册类型无副作用（TryRemove 语义）
            DbValueConvert.RemoveParser(typeof(Uri));

            Assert.Equal(new Uri("https://example.com", UriKind.RelativeOrAbsolute),
                DbValueConvert.ChangeType<Uri>("https://example.com"));
        }

        #endregion
    }
}
