using System.Data;
using System.Data.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests
{
    /// <summary>
    /// DbParameterBuilderBase 的单元测试：显式 size 优先、输出类方向的字符串/二进制参数默认长度 4000、
    /// 非字符串类型与输入方向不应用默认值（stub 工厂消除方言差异）。
    /// </summary>
    public class DbParameterBuilderBaseTests
    {
        #region 测试替身

        private sealed class StubParameter : DbParameter
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

        private sealed class StubProviderFactory : DbProviderFactory
        {
            public override DbParameter CreateParameter() => new StubParameter();
        }

        private sealed class StubParameterBuilder : DbParameterBuilderBase
        {
            public StubParameterBuilder()
                : base("@")
            {
            }

            public override DbProviderFactory ProviderFactory => new StubProviderFactory();
        }

        #endregion

        [Fact]
        public void CreateParameter_ExplicitSize_IsApplied()
        {
            var builder = new StubParameterBuilder();

            // 显式指定长度（如超长字符串/二进制输出参数）优先，不应用默认值
            var parameter = builder.CreateParameter("P", DBNull.Value, DbType.String, ParameterDirection.Output, 100);

            Assert.Equal("@P", parameter.ParameterName);
            Assert.Equal(ParameterDirection.Output, parameter.Direction);
            Assert.Equal(100, parameter.Size);
        }

        [Theory]
        [InlineData(DbType.String)]
        [InlineData(DbType.AnsiString)]
        [InlineData(DbType.StringFixedLength)]
        [InlineData(DbType.AnsiStringFixedLength)]
        [InlineData(DbType.Binary)]
        public void CreateParameter_OutputStringLikeTypesWithoutSize_UsesDefaultSize4000(DbType dbType)
        {
            var builder = new StubParameterBuilder();

            // 输出类方向的字符串/二进制参数无法从值推导长度，应用跨库安全的默认长度 4000
            var parameter = builder.CreateParameter("P", DBNull.Value, dbType, ParameterDirection.Output);

            Assert.Equal(4000, parameter.Size);
        }

        [Theory]
        [InlineData(DbType.Int32)]
        [InlineData(DbType.Object)]
        public void CreateParameter_OutputNonStringTypeWithoutSize_KeepsProviderDefaultSize(DbType dbType)
        {
            var builder = new StubParameterBuilder();

            // 非字符串/二进制类型不应用默认长度（stub 参数 Size 保持 0）
            var parameter = builder.CreateParameter("P", DBNull.Value, dbType, ParameterDirection.Output);

            Assert.Equal(0, parameter.Size);
        }

        [Fact]
        public void CreateParameter_InputDirectionWithoutSize_KeepsProviderDefaultSize()
        {
            var builder = new StubParameterBuilder();

            // 输入方向可从值推导长度，不应用默认值
            var parameter = builder.CreateParameter("P", "Tom", DbType.String, ParameterDirection.Input);

            Assert.Equal(0, parameter.Size);
            Assert.Equal("Tom", parameter.Value);
        }

        [Fact]
        public void CreateParameter_NullValue_ConvertedToDBNull()
        {
            var builder = new StubParameterBuilder();

            var parameter = builder.CreateParameter("P", null, DbType.Int32);

            Assert.Equal(DBNull.Value, parameter.Value);
        }

        [Fact]
        public void GetParameterName_AddsOrKeepsPrefix()
        {
            var builder = new StubParameterBuilder();

            Assert.Equal("@Name", builder.GetParameterName("Name"));
            Assert.Equal("@Name", builder.GetParameterName("@Name"));// 已带前缀不重复添加
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void GetParameterName_NullOrWhitespace_Throws(string name)
        {
            var builder = new StubParameterBuilder();

            Assert.ThrowsAny<ArgumentException>(() => builder.GetParameterName(name));
        }

        [Fact]
        public void Constructor_NullPrefix_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new NullPrefixBuilder());
        }

        private sealed class NullPrefixBuilder : DbParameterBuilderBase
        {
            public NullPrefixBuilder()
                : base(null)
            {
            }

            public override DbProviderFactory ProviderFactory => new StubProviderFactory();
        }
    }
}
