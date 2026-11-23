using System;
using System.Collections.Generic;
using Xbc.DbAccess;
using Xbc.DbAccess.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Sql
{
    /// <summary>
    /// SqlBuilderBase 默认实现的窗口构建与 SELECT/INSERT 拼接测试（变异测试 NoCoverage 补齐）：
    /// SQL Server / Oracle 各自重写了窗口构建，基类默认实现（OFFSET/FETCH 标准形态与 ROW_NUMBER 兼容形态）
    /// 作为「未重写方言的回退路径」用 stub 方言直接锁定产出形态，防止死分支悄悄劣化。
    /// </summary>
    public class SqlBuilderBaseWindowTests
    {
        /// <summary>支持 OFFSET/FETCH 的 stub 方言。</summary>
        private class OffsetFetchDialect : SqlDialectBase
        {
            public override bool SupportsSchema => false;
            public override int MaxParameterCount => 999;
            public override int MaxRowsPerInsertValues => int.MaxValue;
            public override bool TreatsEmptyStringAsNull => false;

            public override string QuoteIdentifier(string identifier) => identifier;
            public override string QuoteTableName(string schema, string tableName) => tableName;
            public override InsertIdentityMode GetInsertIdentityMode(DbServerInfo serverInfo) => InsertIdentityMode.SelectLastInsertId;
            public override string SelectLastInsertId(string columnName) => throw new NotSupportedException();
            public override string GetDatePartExpression(string quotedColumn, DatePart part) => throw new NotSupportedException();
            public override string BuildAverageExpression(string quotedColumn) => "AVG(" + quotedColumn + ")";

            public override bool SupportsOffsetFetch(DbServerInfo serverInfo) => true;
        }

        /// <summary>不支持 OFFSET/FETCH 的 stub 方言（走 ROW_NUMBER 兼容回退）。</summary>
        private sealed class RowNumberDialect : OffsetFetchDialect
        {
            public override bool SupportsOffsetFetch(DbServerInfo serverInfo) => false;
        }

        /// <summary>输出子句钩子的 stub 构建器：BeforeValues 钩子返回 OUTPUT 形态。</summary>
        private sealed class BeforeValuesBuilder : SqlBuilderBase
        {
            public override ISqlDialect SqlDialect { get; } = new OffsetFetchDialect();

            protected override string BuildInsertOutputClauseBeforeValues(InsertIdentityContext identity)
                => "OUTPUT INSERTED.Id";
        }

        /// <summary>默认窗口构建（不重写 BuildWindowSqlCore）的 stub 构建器。</summary>
        private sealed class DefaultWindowBuilder : SqlBuilderBase
        {
            private readonly ISqlDialect dialect;

            public DefaultWindowBuilder(ISqlDialect dialect)
            {
                this.dialect = dialect;
            }

            public override ISqlDialect SqlDialect => dialect;
        }

        [Fact]
        public void BuildSelectSql_Distinct_AndOrderBy()
        {
            // DISTINCT 关键字与 ORDER BY 子句的拼接（distinct/orderBy 双路径）
            var builder = new DefaultWindowBuilder(new OffsetFetchDialect());

            var sql = builder.BuildSelectSql("Users", new List<string> { "Id", "Name" }, orderBy: "Name DESC", distinct: true);

            Assert.Contains("SELECT DISTINCT", sql);
            Assert.Contains("Id, Name", sql);
            Assert.Contains("ORDER BY", sql);
            Assert.Contains("Name DESC", sql);
        }

        [Fact]
        public void BuildInsertSql_OutputClauseBeforeValues()
        {
            // BeforeValues 钩子命中：OUTPUT 子句位于列清单与 VALUES 之间
            var builder = new BeforeValuesBuilder();
            var identity = InsertIdentityContext.ForRetrieval("Id");

            var sql = builder.BuildInsertSql("Users", new List<string> { "Name" }, new List<string> { "@p0" }, identity);

            var valuesIndex = sql.IndexOf("VALUES", StringComparison.Ordinal);
            var outputIndex = sql.IndexOf("OUTPUT INSERTED.Id", StringComparison.Ordinal);
            var columnsIndex = sql.IndexOf("(Name)", StringComparison.Ordinal);
            Assert.True(columnsIndex >= 0 && valuesIndex > columnsIndex && outputIndex > columnsIndex && outputIndex < valuesIndex,
                $"OUTPUT 应位于列清单与 VALUES 之间，实际：{sql}");
        }

        [Fact]
        public void BuildWindowSql_NegativeSkip_Throws()
        {
            var builder = new DefaultWindowBuilder(new OffsetFetchDialect());

            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => builder.BuildWindowSql("SELECT * FROM Users", skip: -1, take: 10, orderBy: "Id"));

            Assert.Contains("不能为负数", ex.Message);
        }

        [Fact]
        public void BuildWindowSql_OffsetFetchFallback_Form()
        {
            // 未重写窗口构建的方言 + 支持 OFFSET/FETCH：基类标准形态（派生表 + 尾部 OFFSET/FETCH）
            var builder = new DefaultWindowBuilder(new OffsetFetchDialect());

            var sql = builder.BuildWindowSql("SELECT Id FROM Users", skip: 3, take: 5, orderBy: "Id DESC");

            Assert.Contains("SELECT * FROM (", sql);
            Assert.Contains("SELECT Id FROM Users", sql);
            Assert.Contains("ORDER BY Id DESC", sql);
            Assert.Contains("OFFSET 3 ROWS", sql);
            Assert.Contains("FETCH NEXT 5 ROWS ONLY", sql);
        }

        [Fact]
        public void BuildWindowSql_RowNumberFallback_Form_AndEndRow()
        {
            // 未重写窗口构建的方言 + 不支持 OFFSET/FETCH：ROW_NUMBER 兼容形态，行号窗口边界 = [offset+1, offset+fetch]
            var builder = new DefaultWindowBuilder(new RowNumberDialect());

            var sql = builder.BuildWindowSql("SELECT Id FROM Users", skip: 2, take: 5, orderBy: "Id");

            Assert.Contains("ROW_NUMBER() OVER (ORDER BY Id)", sql);
            Assert.Contains("> 2", sql);
            // 结束行号 = offset + fetch = 7
            Assert.Contains("<= 7", sql);
        }

        [Fact]
        public void BuildWindowSql_RowNumberFallback_SuppressesTrailingOrder_WhenWrapped()
        {
            // 包裹场景（appendRowOrder=false）：省略尾部 ORDER BY（行序由外层保证）
            var builder = new DefaultWindowBuilder(new RowNumberDialect());

            var sql = builder.BuildWindowSql("SELECT Id FROM Users", skip: 0, take: 5, orderBy: "Id", appendRowOrder: false);

            // ROW_NUMBER 计算序保留，但不得有窗口外的尾部 ORDER BY
            Assert.Contains("ROW_NUMBER()", sql);
            var afterWhere = sql.Substring(sql.IndexOf("WHERE", StringComparison.Ordinal));
            Assert.DoesNotContain("ORDER BY", afterWhere);
        }

        [Fact]
        public void BuildWindowSql_AliasesArePrefixed()
        {
            // 派生表别名带 q/rn 前缀且防撞（实体列名恰为 q/rn 时不与派生表列名冲突）
            var builder = new DefaultWindowBuilder(new RowNumberDialect());

            var sql = builder.BuildWindowSql("SELECT Id FROM Users", skip: 0, take: 5, orderBy: "Id");

            // 产出含 q_/rn_ 前缀别名（SqlIdentifierHelper.Generate 的基名）
            Assert.Matches("AS rn_\\w+", sql);
            Assert.Matches("\\) q_\\w+", sql);
        }
    }
}
