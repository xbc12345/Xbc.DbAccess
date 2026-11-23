using System;
using System.Linq.Expressions;
using Xbc.DbAccess.Entity;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// MemberAccessor 的单元测试。
    /// </summary>
    public class MemberAccessorTests
    {
        #region Create 测试

        [Fact]
        public void Create_SimpleProperty_ReturnsAccessor()
        {
            var accessor = MemberAccessor.Create<TestClass>("Name");

            Assert.NotNull(accessor);
            Assert.Equal("Name", accessor.MemberName);
            Assert.Equal("Name", accessor.MemberPath);
            Assert.Equal(typeof(string), accessor.MemberType);
            Assert.Equal(typeof(TestClass), accessor.ParameterType);
            Assert.False(accessor.IsDeepPath);
        }

        [Fact]
        public void Create_NestedProperty_ReturnsAccessor()
        {
            var accessor = MemberAccessor.Create<TestClass>("Nested.Name");

            Assert.NotNull(accessor);
            Assert.Equal("Name", accessor.MemberName);
            Assert.Equal("Nested.Name", accessor.MemberPath);
            Assert.Equal(typeof(string), accessor.MemberType);
            Assert.True(accessor.IsDeepPath);
        }

        [Fact]
        public void Create_DeeplyNestedProperty_ReturnsAccessor()
        {
            var accessor = MemberAccessor.Create<TestClass>("Nested.Deep.Value");

            Assert.NotNull(accessor);
            Assert.Equal("Value", accessor.MemberName);
            Assert.Equal("Nested.Deep.Value", accessor.MemberPath);
            Assert.Equal(typeof(int), accessor.MemberType);
            Assert.True(accessor.IsDeepPath);
        }

        [Fact]
        public void Create_Field_ReturnsAccessor()
        {
            var accessor = MemberAccessor.Create<TestClass>("FieldValue");

            Assert.NotNull(accessor);
            Assert.Equal("FieldValue", accessor.MemberName);
            Assert.Equal(typeof(int), accessor.MemberType);
        }

        [Fact]
        public void Create_NullDeclaringType_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                MemberAccessor.Create(null, "Name"));
        }

        [Fact]
        public void Create_NullMemberPath_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                MemberAccessor.Create<TestClass>(null));
        }

        [Fact]
        public void Create_EmptyMemberPath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                MemberAccessor.Create<TestClass>(""));
        }

        [Fact]
        public void Create_InvalidProperty_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                MemberAccessor.Create<TestClass>("NonExistentProperty"));
        }

        [Fact]
        public void Create_InvalidNestedPath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                MemberAccessor.Create<TestClass>("Nested.NonExistent"));
        }

        [Fact]
        public void Create_InvalidMemberPath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                MemberAccessor.Create<TestClass>("."));
        }

        #endregion

        #region 属性访问测试

        [Fact]
        public void MemberExpression_SimpleProperty_ReturnsExpression()
        {
            var accessor = MemberAccessor.Create<TestClass>("Name");

            Assert.NotNull(accessor.Member);
            Assert.Equal("Name", accessor.Member.Member.Name);
        }

        [Fact]
        public void ParameterExpression_ReturnsParameter()
        {
            var accessor = MemberAccessor.Create<TestClass>("Name");

            Assert.NotNull(accessor.Parameter);
            Assert.Equal(typeof(TestClass), accessor.Parameter.Type);
        }

        #endregion

        #region 类型测试

        [Fact]
        public void Type_IntProperty_ReturnsIntType()
        {
            var accessor = MemberAccessor.Create<TestClass>("Age");
            Assert.Equal(typeof(int), accessor.MemberType);
        }

        [Fact]
        public void Type_NullableIntProperty_ReturnsNullableIntType()
        {
            var accessor = MemberAccessor.Create<TestClass>("NullableAge");
            Assert.Equal(typeof(int?), accessor.MemberType);
        }

        [Fact]
        public void Type_StringProperty_ReturnsStringType()
        {
            var accessor = MemberAccessor.Create<TestClass>("Name");
            Assert.Equal(typeof(string), accessor.MemberType);
        }

        [Fact]
        public void Type_NestedProperty_ReturnsNestedType()
        {
            var accessor = MemberAccessor.Create<TestClass>("Nested");
            Assert.Equal(typeof(NestedClass), accessor.MemberType);
        }

        #endregion

        #region 嵌套检测测试

        [Fact]
        public void IsNested_SimpleProperty_ReturnsFalse()
        {
            var accessor = MemberAccessor.Create<TestClass>("Name");
            Assert.False(accessor.IsDeepPath);
        }

        [Fact]
        public void IsNested_NestedProperty_ReturnsTrue()
        {
            var accessor = MemberAccessor.Create<TestClass>("Nested.Name");
            Assert.True(accessor.IsDeepPath);
        }

        [Fact]
        public void IsNested_DeeplyNestedProperty_ReturnsTrue()
        {
            var accessor = MemberAccessor.Create<TestClass>("Nested.Deep.Value");
            Assert.True(accessor.IsDeepPath);
        }

        #endregion

        #region 参数类型测试

        [Fact]
        public void ParameterType_SimpleProperty_ReturnsParameterType()
        {
            var accessor = MemberAccessor.Create<TestClass>("Name");
            Assert.Equal(typeof(TestClass), accessor.ParameterType);
        }

        [Fact]
        public void ParameterType_NestedProperty_ReturnsParameterType()
        {
            var accessor = MemberAccessor.Create<TestClass>("Nested.Name");
            Assert.Equal(typeof(TestClass), accessor.ParameterType);
        }

        #endregion

        #region 成员路径测试

        [Fact]
        public void MemberPath_SimpleProperty_ReturnsPropertyName()
        {
            var accessor = MemberAccessor.Create<TestClass>("Name");
            Assert.Equal("Name", accessor.MemberPath);
        }

        [Fact]
        public void MemberPath_NestedProperty_ReturnsFullPath()
        {
            var accessor = MemberAccessor.Create<TestClass>("Nested.Name");
            Assert.Equal("Nested.Name", accessor.MemberPath);
        }

        #endregion

        #region 成员名称测试

        [Fact]
        public void MemberName_SimpleProperty_ReturnsPropertyName()
        {
            var accessor = MemberAccessor.Create<TestClass>("Name");
            Assert.Equal("Name", accessor.MemberName);
        }

        [Fact]
        public void MemberName_NestedProperty_ReturnsLastPropertyName()
        {
            var accessor = MemberAccessor.Create<TestClass>("Nested.Deep.Value");
            Assert.Equal("Value", accessor.MemberName);
        }

        #endregion

        #region 表达式编译测试

        [Fact]
        public void CreateLambda_SimpleProperty_CanCompileAndExecute()
        {
            var accessor = MemberAccessor.Create<TestClass>("Name");
            var lambda = Expression.Lambda<Func<TestClass, string>>(
                accessor.Member,
                accessor.Parameter
            );

            var func = lambda.Compile();
            var obj = new TestClass { Name = "Test" };

            Assert.Equal("Test", func(obj));
        }

        [Fact]
        public void CreateLambda_NestedProperty_CanCompileAndExecute()
        {
            var accessor = MemberAccessor.Create<TestClass>("Nested.Name");
            var lambda = Expression.Lambda<Func<TestClass, string>>(
                accessor.Member,
                accessor.Parameter
            );

            var func = lambda.Compile();
            var obj = new TestClass
            {
                Nested = new NestedClass { Name = "NestedTest" }
            };

            Assert.Equal("NestedTest", func(obj));
        }

        #endregion

        #region 测试辅助类

        public class TestClass
        {
            public string Name { get; set; }
            public int Age { get; set; }
            public int? NullableAge { get; set; }
            public NestedClass Nested { get; set; }
            public int FieldValue;
        }

        public class NestedClass
        {
            public string Name { get; set; }
            public DeepClass Deep { get; set; }
        }

        public class DeepClass
        {
            public int Value { get; set; }
        }

        #endregion
    }
}
