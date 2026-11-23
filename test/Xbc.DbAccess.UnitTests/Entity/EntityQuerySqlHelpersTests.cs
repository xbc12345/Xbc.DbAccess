using Xbc.DbAccess.Entity;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// EntityQuerySqlHelpers 共享辅助的单元测试：包裹前基础 SQL 准备（含交错分号/空白清理与手写顶层 ORDER BY 检测）、
    /// ContainsOrderByClause 的引用感知检测（字符串字面量内的「ORDER BY」文本不计入，词边界与跨行空白规则）。
    /// </summary>
    public class EntityQuerySqlHelpersTests
    {
        [Fact]
        public void PrepareWrappableBaseSql_TrimsSemicolonAndWhitespace()
        {
            var baseSql = EntityQuerySqlHelpers.PrepareWrappableBaseSql("  SELECT * FROM T;  \n");

            Assert.Equal("SELECT * FROM T", baseSql);
        }

        [Fact]
        public void PrepareWrappableBaseSql_WithTopLevelOrderBy_ThrowsInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() => EntityQuerySqlHelpers.PrepareWrappableBaseSql("SELECT * FROM T ORDER BY Id"));
        }

        [Fact]
        public void PrepareWrappableBaseSql_TrimsInterleavedSemicolonsAndWhitespace()
        {
            // 尾部分号与空白交错（TrimSql 回扫一次移除任意交错组合）
            var baseSql = EntityQuerySqlHelpers.PrepareWrappableBaseSql("  SELECT * FROM T;\r;\t");

            Assert.Equal("SELECT * FROM T", baseSql);
        }

        #region ContainsOrderByClause 引用感知检测

        [Theory]
        [InlineData("SELECT * FROM T ORDER BY Id")]
        [InlineData("select * from t order by id")]// 大小写不敏感
        [InlineData("SELECT * FROM T\r\nORDER\r\nBY Id")]// ORDER 与 BY 之间跨行空白
        [InlineData("SELECT * FROM T WHERE Memo = 'x' ORDER BY Id")]// 字面量之后
        [InlineData("SELECT * FROM T WHERE Memo = 'it''s' ORDER BY Id")]// '' 转义闭合后
        public void ContainsOrderByClause_StructureOrderBy_Detected(string sql)
        {
            Assert.True(EntityQuerySqlHelpers.ContainsOrderByClause(sql));
        }

        [Theory]
        [InlineData("SELECT * FROM T")]// 无 ORDER BY
        [InlineData("SELECT * FROM T WHERE Memo = '请 ORDER BY 我'")]// 字面量内的 ORDER BY 不计入
        [InlineData("SELECT * FROM T WHERE Memo = \"排序 ORDER BY 规则\"")]// 双引号内（MySQL 字符串）
        [InlineData("SELECT * FROM T WHERE Memo = `排序 ORDER BY 规则`")]// 反引号内（MySQL 标识符）
        [InlineData("SELECT * FROM T WHERE Memo = 'ORDERED'")]// 词尾紧贴标识符（ORDERED）
        [InlineData("SELECT * FROM T WHERE Memo = 'REORDER BY'")]// 词首紧贴标识符（REORDER）
        [InlineData("SELECT * FROM T WHERE Memo = 'ORDER BYE'")]// BY 词尾紧贴标识符
        [InlineData("SELECT * FROM T WHERE Memo = '排序' ORDER")]// 末尾孤立 ORDER（无后续 BY）
        public void ContainsOrderByClause_NoStructureOrderBy_NotDetected(string sql)
        {
            Assert.False(EntityQuerySqlHelpers.ContainsOrderByClause(sql));
        }

        [Fact]
        public void ContainsOrderByClause_LiteralAndStructureMixed_Detected()
        {
            // 字面量内的 ORDER BY 不计入，字面量外的结构 ORDER BY 正常命中
            var sql = "SELECT * FROM T WHERE Memo = 'ORDER BY 说明' ORDER BY Id";

            Assert.True(EntityQuerySqlHelpers.ContainsOrderByClause(sql));
        }

        #endregion
    }
}
