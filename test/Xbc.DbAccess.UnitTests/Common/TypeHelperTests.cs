using Xbc.DbAccess.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Common
{
    /// <summary>
    /// TypeHelper 的单元测试。
    /// </summary>
    public class TypeHelperTests
    {
        #region IsInteger 测试

        [Theory]
        [InlineData(typeof(int), true)]
        [InlineData(typeof(long), true)]
        [InlineData(typeof(short), true)]
        [InlineData(typeof(byte), true)]
        [InlineData(typeof(uint), true)]
        [InlineData(typeof(ulong), true)]
        [InlineData(typeof(ushort), true)]
        [InlineData(typeof(sbyte), true)]
        [InlineData(typeof(float), false)]
        [InlineData(typeof(double), false)]
        [InlineData(typeof(decimal), false)]
        [InlineData(typeof(string), false)]
        [InlineData(typeof(DateTime), false)]
        public void IsInteger_VariousTypes_ReturnsExpected(Type type, bool expected)
        {
            var result = TypeHelper.IsInteger(type);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(typeof(int?), true)]
        [InlineData(typeof(long?), true)]
        [InlineData(typeof(short?), true)]
        [InlineData(typeof(float?), false)]
        [InlineData(typeof(double?), false)]
        public void IsInteger_NullableTypes_ReturnsExpected(Type type, bool expected)
        {
            var result = TypeHelper.IsInteger(type);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(typeof(int?), false)]
        [InlineData(typeof(long?), false)]
        [InlineData(typeof(int), true)]
        public void IsInteger_ExcludeNullable_ReturnsExpected(Type type, bool expected)
        {
            var result = TypeHelper.IsInteger(type, includeNullable: false);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void IsInteger_NullType_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => TypeHelper.IsInteger(null));
        }

        #endregion

        #region IsValueTuple 测试

        [Theory]
        [InlineData(typeof((int, string)), true)]
        [InlineData(typeof((int Id, string Name)), true)]
        [InlineData(typeof((int A, int B, int C, int D, int E, int F, int G)), true)]
        [InlineData(typeof((int A, int B, int C, int D, int E, int F, int G, int H)), true)]// 8 元素：ValueTuple<T1..T7, ValueTuple<int>> 的 TRest 嵌套形态
        [InlineData(typeof(ValueTuple), false)]// 0 元素非泛型形态：不在识别范围（零实参构造由解析层空形态拒绝）
        [InlineData(typeof((int, string)?), false)]// 可空元组包装
        [InlineData(typeof(int), false)]
        [InlineData(typeof(string), false)]
        [InlineData(typeof(DateTime), false)]
        [InlineData(typeof(Guid), false)]
        [InlineData(typeof(TypeHelperProbeStruct), false)]// 非 ValueTuple 的普通 struct
        public void IsValueTuple_VariousTypes_ReturnsExpected(Type type, bool expected)
        {
            Assert.Equal(expected, TypeHelper.IsValueTuple(type));
        }

        [Fact]
        public void IsValueTuple_NullType_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => TypeHelper.IsValueTuple(null));
        }

        [Theory]
        [InlineData(typeof((int, string)), false)]
        [InlineData(typeof((int A, int B, int C, int D, int E, int F, int G)), false)]// 7 元素：平铺上限内
        [InlineData(typeof((int A, int B, int C, int D, int E, int F, int G, int H)), true)]// 8 元素：TRest 嵌套形态
        [InlineData(typeof(int), false)]
        public void IsOverLengthValueTuple_VariousTypes_ReturnsExpected(Type type, bool expected)
        {
            Assert.Equal(expected, TypeHelper.IsOverLengthValueTuple(type));
        }

        /// <summary>IsValueTuple 反例载体：普通 struct。</summary>
        private struct TypeHelperProbeStruct
        {
            public int A;
        }

        #endregion

        #region IsFloating 测试

        [Theory]
        [InlineData(typeof(float), true)]
        [InlineData(typeof(double), true)]
        [InlineData(typeof(int), false)]
        [InlineData(typeof(decimal), false)]
        [InlineData(typeof(string), false)]
        public void IsFloating_VariousTypes_ReturnsExpected(Type type, bool expected)
        {
            var result = TypeHelper.IsFloating(type);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(typeof(float?), true)]
        [InlineData(typeof(double?), true)]
        [InlineData(typeof(int?), false)]
        public void IsFloating_NullableTypes_ReturnsExpected(Type type, bool expected)
        {
            var result = TypeHelper.IsFloating(type);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void IsFloating_NullType_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => TypeHelper.IsFloating(null));
        }

        #endregion

        #region IsDecimal 测试

        [Theory]
        [InlineData(typeof(decimal), true)]
        [InlineData(typeof(int), false)]
        [InlineData(typeof(float), false)]
        [InlineData(typeof(double), false)]
        public void IsDecimal_VariousTypes_ReturnsExpected(Type type, bool expected)
        {
            var result = TypeHelper.IsDecimal(type);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(typeof(decimal?), true)]
        [InlineData(typeof(int?), false)]
        public void IsDecimal_NullableTypes_ReturnsExpected(Type type, bool expected)
        {
            var result = TypeHelper.IsDecimal(type);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void IsDecimal_NullType_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => TypeHelper.IsDecimal(null));
        }

        #endregion

        #region IsNumeric 测试

        [Theory]
        [InlineData(typeof(int), true)]
        [InlineData(typeof(long), true)]
        [InlineData(typeof(float), true)]
        [InlineData(typeof(double), true)]
        [InlineData(typeof(decimal), true)]
        [InlineData(typeof(string), false)]
        [InlineData(typeof(DateTime), false)]
        public void IsNumeric_VariousTypes_ReturnsExpected(Type type, bool expected)
        {
            var result = TypeHelper.IsNumeric(type);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(typeof(int?), true)]
        [InlineData(typeof(float?), true)]
        [InlineData(typeof(decimal?), true)]
        [InlineData(typeof(string), false)]
        public void IsNumeric_NullableTypes_ReturnsExpected(Type type, bool expected)
        {
            var result = TypeHelper.IsNumeric(type);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void IsNumeric_NullType_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => TypeHelper.IsNumeric(null));
        }

        #endregion

        #region IsDateTime 测试

        [Theory]
        [InlineData(typeof(DateTime), true)]
        [InlineData(typeof(DateTimeOffset), true)]
        [InlineData(typeof(TimeSpan), true)]
        [InlineData(typeof(string), false)]
        [InlineData(typeof(int), false)]
        public void IsDateTime_VariousTypes_ReturnsExpected(Type type, bool expected)
        {
            var result = TypeHelper.IsDateTime(type);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(typeof(DateTime?), true)]
        [InlineData(typeof(DateTimeOffset?), true)]
        [InlineData(typeof(int?), false)]
        public void IsDateTime_NullableTypes_ReturnsExpected(Type type, bool expected)
        {
            var result = TypeHelper.IsDateTime(type);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void IsDateTime_NullType_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => TypeHelper.IsDateTime(null));
        }

        #endregion

        #region IsGuid 测试

        [Theory]
        [InlineData(typeof(Guid), true)]
        [InlineData(typeof(string), false)]
        [InlineData(typeof(int), false)]
        public void IsGuid_VariousTypes_ReturnsExpected(Type type, bool expected)
        {
            var result = TypeHelper.IsGuid(type);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(typeof(Guid?), true)]
        [InlineData(typeof(int?), false)]
        public void IsGuid_NullableTypes_ReturnsExpected(Type type, bool expected)
        {
            var result = TypeHelper.IsGuid(type);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void IsGuid_NullType_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => TypeHelper.IsGuid(null));
        }

        #endregion

        #region IsGenericEnumerable 测试

        [Fact]
        public void IsGenericEnumerable_List_ReturnsTrue()
        {
            var result = TypeHelper.IsGenericEnumerable(typeof(List<int>), out var elementType);
            Assert.True(result);
            Assert.Equal(typeof(int), elementType);
        }

        [Fact]
        public void IsGenericEnumerable_Array_ReturnsTrue()
        {
            var result = TypeHelper.IsGenericEnumerable(typeof(int[]), out var elementType);
            // Arrays implement IEnumerable<T>
            Assert.True(result);
            Assert.Equal(typeof(int), elementType);
        }

        [Fact]
        public void IsGenericEnumerable_String_ReturnsFalse()
        {
            var result = TypeHelper.IsGenericEnumerable(typeof(string), out var elementType);
            Assert.False(result);
            Assert.Null(elementType);
        }

        [Fact]
        public void IsGenericEnumerable_NonEnumerable_ReturnsFalse()
        {
            var result = TypeHelper.IsGenericEnumerable(typeof(int), out var elementType);
            Assert.False(result);
            Assert.Null(elementType);
        }

        [Fact]
        public void IsGenericEnumerable_Dictionary_ReturnsTrue()
        {
            var result = TypeHelper.IsGenericEnumerable(typeof(Dictionary<string, int>), out var elementType);
            Assert.True(result);
            Assert.Equal(typeof(KeyValuePair<string, int>), elementType);
        }

        #endregion
    }
}
