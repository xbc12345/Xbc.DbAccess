using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Extensions
{
    /// <summary>
    /// FormattableString 插值参数化扩展测试：骨架解析（孔替换/花括号反转义/对齐格式说明符）、
    /// 字面量内孔拒绝、无孔退化、孔编号校验、经真实参数转换管道的包装委托与集合展开。
    /// </summary>
    public class DbAccessInterpolatedExtensionsTests
    {
        #region 测试桩

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

        /// <summary>带 @ 前缀的参数构建器桩（方言前缀约定与 SQL Server 一致）。</summary>
        private sealed class StubParameterBuilder : IDbParameterBuilder
        {
            public string ParameterPrefix => "@";

            public string GetParameterName(string name) => ParameterPrefix + name;

            public DbParameter CreateParameter(string name, object value, DbType? dbType = null)
            {
                return new StubParameter { ParameterName = GetParameterName(name), Value = value, DbType = dbType ?? 0 };
            }

            public DbParameter CreateParameter(string name, object value, DbType? dbType, ParameterDirection direction, int? size = null)
            {
                return new StubParameter { ParameterName = GetParameterName(name), Value = value, DbType = dbType ?? 0, Direction = direction, Size = size ?? 0 };
            }
        }

        /// <summary>构建仅注入参数构建器的 IDbAccess 桩（扩展方法内的真实转换管道全部可运行）。</summary>
        private static Mock<IDbAccess> CreateDbAccessStub()
        {
            var factory = new Mock<DbAccessProviderFactory>();
            factory.Setup(f => f.ParameterBuilder).Returns(new StubParameterBuilder());

            var dbAccess = new Mock<IDbAccess>();
            dbAccess.Setup(x => x.ProviderFactory).Returns(factory.Object);
            return dbAccess;
        }

        #endregion

        #region 骨架解析核心

        [Fact]
        public void BuildInterpolated_ReplacesHolesWithDialectPrefixedParameters()
        {
            var name = "张三";
            var (sql, parameters) = DbAccessInterpolatedExtensions.BuildInterpolated(
                CreateDbAccessStub().Object, $"SELECT * FROM T WHERE Age = {18} AND Name = {name}");

            Assert.Equal("SELECT * FROM T WHERE Age = @fs_0 AND Name = @fs_1", sql);
            Assert.Equal(2, parameters.Count);
            Assert.Equal(18, parameters["fs_0"]);
            Assert.Equal("张三", parameters["fs_1"]);
        }

        [Fact]
        public void BuildInterpolated_UnescapesLiteralBraces_JsonLiteralNotMisjudged()
        {
            // 骨架级 {{ }} 转义与 SQL 引用无关：单引号字面量内的 JSON 花括号同样反转义（{{"a":1}} → {"a":1}），
            // 且引用内的 " 不切换引用态（同引号才闭合），后续孔正常替换
            var (sql, parameters) = DbAccessInterpolatedExtensions.BuildInterpolated(
                CreateDbAccessStub().Object, FormattableStringFactory.Create("SELECT '{{\"a\":1}}' AS J FROM T WHERE A = {0}", new object[] { 5 }));

            Assert.Equal("SELECT '{\"a\":1}' AS J FROM T WHERE A = @fs_0", sql);
            Assert.Equal(5, parameters["fs_0"]);
        }

        [Fact]
        public void BuildInterpolated_HoleInsideLiteral_Throws()
        {
            // '%{0}%' 替换为参数引用后是普通文本不会绑定值——显式拒绝，引导把含通配符的整个模式作为孔值
            var ex = Assert.Throws<NotSupportedException>(
                () => DbAccessInterpolatedExtensions.BuildInterpolated(
                    CreateDbAccessStub().Object, $"SELECT * FROM T WHERE Name LIKE '%{"%张%"}'"));

            Assert.Contains("字符串字面量", ex.Message);
        }

        [Fact]
        public void BuildInterpolated_DuplicateHoleNumber_SharesSingleParameter()
        {
            // 手搭骨架重复引用同一孔编号：共享同一参数（编译器对源码重复表达式各发独立孔，重复编号仅手搭可达）
            var (sql, parameters) = DbAccessInterpolatedExtensions.BuildInterpolated(
                CreateDbAccessStub().Object, FormattableStringFactory.Create("{0} | {0}", new object[] { 1.5m }));

            Assert.Equal("@fs_0 | @fs_0", sql);
            Assert.Single(parameters);
            Assert.Equal(1.5m, parameters["fs_0"]);
        }

        [Fact]
        public void BuildInterpolated_AlignmentFormatSpecifiers_SkippedInText()
        {
            // 对齐与格式说明符（{0,5:N2}）无参数化语义：占位符仅替换孔位，说明符不参与文本
            var (sql, parameters) = DbAccessInterpolatedExtensions.BuildInterpolated(
                CreateDbAccessStub().Object, FormattableStringFactory.Create("{0,5:N2}", new object[] { 1.5m }));

            Assert.Equal("@fs_0", sql);
            Assert.Equal(1.5m, parameters["fs_0"]);
        }

        [Fact]
        public void BuildInterpolated_NoHoles_ReturnsUnescapedTextWithNullParameters()
        {
            var (sql, parameters) = DbAccessInterpolatedExtensions.BuildInterpolated(
                CreateDbAccessStub().Object, $"SELECT '{{x}}' FROM T");

            Assert.Equal("SELECT '{x}' FROM T", sql);
            Assert.Null(parameters);
        }

        [Fact]
        public void BuildInterpolated_NoHolesPlain_TextUnescapedViaBclFormatSemantics()
        {
            // 无孔形态委托 BCL：ToString = string.Format(Format)，{{ 与 }} 转义仍还原，0 参时文化无关
            FormattableString fs = $"SELECT 1 -- 123456";
            var (sql, parameters) = DbAccessInterpolatedExtensions.BuildInterpolated(
                CreateDbAccessStub().Object, fs);

            Assert.Equal(fs.ToString(), sql);
            Assert.Null(parameters);
        }

        [Fact]
        public void BuildInterpolated_NoHoles_ClosingBraceOnlyEscapes_Unescaped()
        {
            // 仅有闭花括号转义的骨架（快路径标记须含 }}）；连续转义单遍折叠（}}}} → }}，
            // 两趟顺序 Replace 会把上一趟输出再折叠一次，此处锁定单遍语义）
            var (closingOnly, _) = DbAccessInterpolatedExtensions.BuildInterpolated(
                CreateDbAccessStub().Object, $"}}");
            var (sequential, _) = DbAccessInterpolatedExtensions.BuildInterpolated(
                CreateDbAccessStub().Object, $"}}}}");

            Assert.Equal("}", closingOnly);
            Assert.Equal("}}", sequential);
        }

        [Fact]
        public void BuildInterpolated_HoleNumberOutOfRange_Throws()
        {
            // 手搭骨架（孔编号 3 但仅 1 个值）：骨架与值数组不一致显式失败
            var ex = Assert.Throws<ArgumentException>(
                () => DbAccessInterpolatedExtensions.BuildInterpolated(
                    CreateDbAccessStub().Object, FormattableStringFactory.Create("{3}", new object[] { 1 })));

            Assert.Contains("超出值数组长度", ex.Message);
        }

        [Fact]
        public void BuildInterpolated_NonSequentialHoleNumbers_MapByNumber()
        {
            // 手搭骨架允许孔编号乱序：按编号映射值（编译器产出的骨架恒为顺序编号，此处锁定解析语义）
            var (sql, parameters) = DbAccessInterpolatedExtensions.BuildInterpolated(
                CreateDbAccessStub().Object, FormattableStringFactory.Create("{1} = {0}", new object[] { "A", "B" }));

            Assert.Equal("@fs_1 = @fs_0", sql);
            Assert.Equal("A", parameters["fs_0"]);
            Assert.Equal("B", parameters["fs_1"]);
        }

        #endregion

        #region 包装委托与参数转换管道集成

        [Fact]
        public async Task ExecuteNonQueryInterpolatedAsync_DelegatesWithRewrittenTextAndConvertedParameters()
        {
            var dbAccess = CreateDbAccessStub();
            string capturedSql = null;
            DbParameter[] capturedParameters = null;
            dbAccess.Setup(x => x.ExecuteNonQueryAsync(It.IsAny<string>(), It.IsAny<DbParameter[]>(), It.IsAny<CancellationToken>()))
                .Returns<string, DbParameter[], CancellationToken>((sql, parameters, _) =>
                {
                    capturedSql = sql;
                    capturedParameters = parameters;
                    return Task.FromResult(7);
                });

            var affected = await DbAccessInterpolatedExtensions.ExecuteNonQueryInterpolatedAsync(
                dbAccess.Object, $"UPDATE T SET Name = {"李四"} WHERE Id = {9}");

            Assert.Equal(7, affected);
            // 经真实 ConvertToDbParameters 管道：字典已转为方言前缀参数
            Assert.Equal("UPDATE T SET Name = @fs_0 WHERE Id = @fs_1", capturedSql);
            Assert.Equal(2, capturedParameters.Length);
            Assert.Equal("@fs_0", capturedParameters[0].ParameterName);
            Assert.Equal("李四", capturedParameters[0].Value);
            Assert.Equal("@fs_1", capturedParameters[1].ParameterName);
            Assert.Equal(9, capturedParameters[1].Value);
        }

        [Fact]
        public async Task ExecuteNonQueryInterpolatedAsync_NullHole_BecomesDbNull()
        {
            var dbAccess = CreateDbAccessStub();
            DbParameter[] capturedParameters = null;
            dbAccess.Setup(x => x.ExecuteNonQueryAsync(It.IsAny<string>(), It.IsAny<DbParameter[]>(), It.IsAny<CancellationToken>()))
                .Returns<string, DbParameter[], CancellationToken>((_, parameters, _) =>
                {
                    capturedParameters = parameters;
                    return Task.FromResult(1);
                });

            await DbAccessInterpolatedExtensions.ExecuteNonQueryInterpolatedAsync(
                dbAccess.Object, $"UPDATE T SET Remark = {(string)null} WHERE Id = {1}");

            Assert.Null((object)capturedParameters[0].Value);
        }

        [Fact]
        public async Task ExecuteNonQueryInterpolatedAsync_CollectionHole_ExpandsToInParameterList()
        {
            // 集合孔复用字典参数形态的 IN 展开机制：占位符 @fs_0 原地展开为 (@fs_0_0, @fs_0_1)
            var ids = new List<int> { 3, 8 };
            var dbAccess = CreateDbAccessStub();
            string capturedSql = null;
            DbParameter[] capturedParameters = null;
            dbAccess.Setup(x => x.ExecuteNonQueryAsync(It.IsAny<string>(), It.IsAny<DbParameter[]>(), It.IsAny<CancellationToken>()))
                .Returns<string, DbParameter[], CancellationToken>((sql, parameters, _) =>
                {
                    capturedSql = sql;
                    capturedParameters = parameters;
                    return Task.FromResult(2);
                });

            await DbAccessInterpolatedExtensions.ExecuteNonQueryInterpolatedAsync(
                dbAccess.Object, $"DELETE FROM T WHERE Id IN ({ids})");

            Assert.Equal("DELETE FROM T WHERE Id IN (@fs_0_0, @fs_0_1)", capturedSql);
            Assert.Equal(2, capturedParameters.Length);
            Assert.Equal("@fs_0_0", capturedParameters[0].ParameterName);
            Assert.Equal(3, capturedParameters[0].Value);
            Assert.Equal("@fs_0_1", capturedParameters[1].ParameterName);
            Assert.Equal(8, capturedParameters[1].Value);
        }

        [Fact]
        public async Task ExecuteScalarInterpolatedAsync_Generic_ConvertsResultType()
        {
            var dbAccess = CreateDbAccessStub();
            dbAccess.Setup(x => x.ExecuteScalarAsync(It.IsAny<string>(), It.IsAny<DbParameter[]>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<object>(42L));

            var result = await DbAccessInterpolatedExtensions.ExecuteScalarInterpolatedAsync<int>(
                dbAccess.Object, $"SELECT COUNT(*) FROM T WHERE A = {1}");

            Assert.Equal(42, result);
        }

        #endregion
    }
}
