using System.Data;
using System.Data.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Extensions
{
    /// <summary>
    /// DbParameterCollection 的单元测试：添加/访问/移除、名称大小写不敏感与前缀归一化、
    /// 重复名守卫、输出/返回值参数与 ICollection 实现（mock 构建器消除方言差异）。
    /// </summary>
    public class DbParameterCollectionTests
    {
        #region 测试替身

        private sealed class MockParameter : DbParameter
        {
            public override DbType DbType { get; set; }
            public override ParameterDirection Direction { get; set; }
            public override bool IsNullable { get; set; }
            public override string ParameterName { get; set; }
            public override int Size { get; set; }
            public override string SourceColumn { get; set; }
            public override bool SourceColumnNullMapping { get; set; }
            public override object Value { get; set; }
            public override void ResetDbType()
            {
            }
        }

        private sealed class MockParameterBuilder : IDbParameterBuilder
        {
            public string ParameterPrefix => "@";

            public string GetParameterName(string name)
            {
                return name.StartsWith("@", StringComparison.Ordinal) ? name : "@" + name;
            }

            public DbParameter CreateParameter(string name, object value, DbType? dbType = null)
            {
                return CreateParameter(name, value, dbType, ParameterDirection.Input);
            }

            public DbParameter CreateParameter(string name, object value, DbType? dbType, ParameterDirection direction, int? size = null)
            {
                return new MockParameter
                {
                    ParameterName = GetParameterName(name),
                    Value = value ?? DBNull.Value,
                    DbType = dbType ?? DbType.Object,
                    Direction = direction,
                    Size = size ?? 0
                };
            }
        }

        #endregion

        [Fact]
        public void Constructor_NullBuilder_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new DbParameterCollection(null));
        }

        [Fact]
        public void Add_NameValue_ExposesBuilderAndCaseInsensitiveAccess()
        {
            var collection = new DbParameterCollection(new MockParameterBuilder());

            collection.Add("Name", "Tom");

            Assert.Equal(1, collection.Count);
            Assert.False(collection.IsReadOnly);
            Assert.IsType<MockParameterBuilder>(collection.ParameterBuilder);

            // 不区分大小写按名访问
            Assert.True(collection.Contains("name"));
            Assert.Equal("Tom", collection.GetValue("NAME"));

            var typed = collection.GetValue<string>("name");
            Assert.Equal("Tom", typed);
        }

        [Fact]
        public void Add_DbParameter_WithoutPrefix_KeyNormalizedWithBuilder()
        {
            var collection = new DbParameterCollection(new MockParameterBuilder());
            var parameter = new MockParameter { ParameterName = "Age", Value = 20 };// 手工构造不带前缀

            collection.Add(parameter);

            // 手工参数名经归一化存储键，与字符串添加路径一致
            Assert.True(collection.Contains("Age"));
            Assert.Equal(20, collection.GetValue<int>("age"));
        }

        [Fact]
        public void Add_DuplicateName_ThrowsArgumentException()
        {
            var collection = new DbParameterCollection(new MockParameterBuilder());
            collection.Add("Name", "Tom");

            Assert.Throws<ArgumentException>(() => collection.Add("name", "Jerry"));
        }

        [Fact]
        public void Add_WithDbTypeDirectionSize_CreatesParameter()
        {
            var collection = new DbParameterCollection(new MockParameterBuilder());

            collection.Add("Age", 20, DbType.Int32);
            collection.Add("Score", 0, DbType.Int32, ParameterDirection.Output, 100);

            Assert.Equal(2, collection.Count);
            var score = collection.Cast<DbParameter>().Single(p => p.ParameterName == "@Score");
            Assert.Equal(ParameterDirection.Output, score.Direction);
            Assert.Equal(100, score.Size);
        }

        [Fact]
        public void AddOutput_And_AddReturnValue_SetDirectionAndDefaultSize()
        {
            var collection = new DbParameterCollection(new MockParameterBuilder());

            collection.AddOutput("OutVal", DbType.String);
            collection.AddReturnValue("RetVal", DbType.Int32);

            var outParam = collection.Cast<DbParameter>().Single(p => p.ParameterName == "@OutVal");
            var retParam = collection.Cast<DbParameter>().Single(p => p.ParameterName == "@RetVal");
            Assert.Equal(ParameterDirection.Output, outParam.Direction);
            Assert.Equal(ParameterDirection.ReturnValue, retParam.Direction);
        }

        [Fact]
        public void Add_NullOrWhitespaceParameterName_Throws()
        {
            var collection = new DbParameterCollection(new MockParameterBuilder());

            Assert.Throws<ArgumentNullException>(() => collection.Add(new MockParameter()));
            Assert.Throws<ArgumentException>(() => collection.Add(new MockParameter { ParameterName = "  " }));
            Assert.Throws<ArgumentNullException>(() => collection.Add(null));
        }

        [Fact]
        public void GetValue_MissingOrNullName_Throws()
        {
            var collection = new DbParameterCollection(new MockParameterBuilder());

            Assert.Throws<ArgumentException>(() => collection.GetValue("Missing"));
            Assert.Throws<ArgumentNullException>(() => collection.GetValue(null));
            Assert.Throws<ArgumentNullException>(() => collection.GetValue<int>(null));
        }

        [Fact]
        public void TryGetValue_MissingOrNullName_ReturnsFalse()
        {
            var collection = new DbParameterCollection(new MockParameterBuilder());
            collection.Add("Age", 20);

            Assert.False(collection.TryGetValue("Missing", out var value));
            Assert.Equal(DBNull.Value, value);
            Assert.False(collection.TryGetValue(null, out _));
            Assert.False(collection.TryGetValue<int>("missing", out var typed));
            Assert.Equal(0, typed);
        }

        [Fact]
        public void TryGetValue_Generic_ConversionFailure_ReturnsFalse()
        {
            var collection = new DbParameterCollection(new MockParameterBuilder());
            collection.Add("Name", "Tom");

            Assert.False(collection.TryGetValue<int>("Name", out _));
            Assert.True(collection.TryGetValue<string>("name", out var value));
            Assert.Equal("Tom", value);
        }

        [Fact]
        public void GetValue_Generic_MissingName_ThrowsArgumentException()
        {
            var collection = new DbParameterCollection(new MockParameterBuilder());
            collection.Add("Name", "Tom");

            // 泛型取值对不存在的参数名同样抛出带参数名的异常
            var ex = Assert.Throws<ArgumentException>(() => collection.GetValue<int>("Missing"));
            Assert.Contains("@Missing", ex.Message);
            Assert.Equal("name", ex.ParamName);
        }

        [Fact]
        public void TryGetValue_ExistingParameter_ReturnsTrueAndValue()
        {
            var collection = new DbParameterCollection(new MockParameterBuilder());
            collection.Add("Age", 20);

            // 非泛型 TryGetValue 命中：返回 true 且输出参数值（不区分大小写）
            Assert.True(collection.TryGetValue("AGE", out var value));
            Assert.Equal(20, value);
        }

        [Fact]
        public void TryGetValue_Generic_EmptyName_ReturnsFalseWithDefault()
        {
            var collection = new DbParameterCollection(new MockParameterBuilder());
            collection.Add("Age", 20);

            // 泛型 TryGetValue 对空名返回 false，值保持类型默认
            Assert.False(collection.TryGetValue<int>(string.Empty, out var typed));
            Assert.Equal(0, typed);
        }

        [Fact]
        public void NonGenericGetEnumerator_EnumeratesParameters()
        {
            var collection = new DbParameterCollection(new MockParameterBuilder());
            collection.Add("Name", "Tom");
            collection.Add("Age", 20);

            // 显式接口实现：经非泛型 IEnumerable 枚举同样得到全部参数
            var enumerated = new List<DbParameter>();
            foreach (var parameter in (System.Collections.IEnumerable)collection)
                enumerated.Add((DbParameter)parameter);

            Assert.Equal(2, enumerated.Count);
            Assert.Contains(enumerated, p => p.ParameterName == "@Name");
            Assert.Contains(enumerated, p => p.ParameterName == "@Age");
        }

        [Fact]
        public void Contains_CopyTo_Remove_Clear_Enumerate()
        {
            var collection = new DbParameterCollection(new MockParameterBuilder());
            var first = new MockParameter { ParameterName = "@Name", Value = "Tom" };
            var second = new MockParameter { ParameterName = "@Age", Value = 20 };
            collection.Add(first);
            collection.Add(second);

            // Contains/Remove 按 DbParameter 实例（含手工无前缀参数名归一化）
            Assert.True(collection.Contains(first));
            var array = new DbParameter[4];
            collection.CopyTo(array, 1);
            Assert.Same(first, array[1]);
            Assert.Same(second, array[2]);

            Assert.True(collection.Remove(first));
            Assert.False(collection.Remove(first));
            Assert.True(collection.Remove("age"));
            Assert.Equal(0, collection.Count);

            Assert.Throws<ArgumentNullException>(() => collection.Remove((DbParameter)null));
            Assert.Throws<ArgumentNullException>(() => collection.Contains((DbParameter)null));
            Assert.Throws<ArgumentNullException>(() => collection.Remove((string)null));

            collection.Add("Name", "Tom");
            collection.Clear();
            Assert.Empty(collection);

            collection.Add("Name", "Tom");
            Assert.Single(collection, p => p.ParameterName == "@Name");
        }
    }
}
