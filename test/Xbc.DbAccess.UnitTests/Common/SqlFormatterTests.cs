using Xbc.DbAccess.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Common
{
    /// <summary>
    /// SqlFormatter 的单元测试：ConsumeQuoteChar 引用状态机（缩进/参数替换/ORDER BY 检测共用的引用词法原语）、
    /// IndentLines 的词法感知缩进（字面量内换行不缩进、换行风格保留）与 TrimSql 的首尾清理。
    /// </summary>
    public class SqlFormatterTests
    {
        #region ConsumeQuoteChar（引用状态机）

        [Fact]
        public void ConsumeQuoteChar_OutsideQuote_ReturnsZeroAndKeepsState()
        {
            char quote = '\0';

            Assert.Equal(0, SqlFormatter.ConsumeQuoteChar("abc", 0, 'a', ref quote));
            Assert.Equal('\0', quote);// 引用外普通字符：状态不变
        }

        [Fact]
        public void ConsumeQuoteChar_QuoteStartEntersQuote_ReturnsOneAndSetsState()
        {
            char quote = '\0';

            Assert.Equal(1, SqlFormatter.ConsumeQuoteChar("'x", 0, '\'', ref quote));
            Assert.Equal('\'', quote);// 进入单引号引用
        }

        [Theory]
        [InlineData('\'')]
        [InlineData('"')]
        [InlineData('`')]
        public void ConsumeQuoteChar_InsideQuote_ReturnsOne(char quoteChar)
        {
            char quote = quoteChar;

            // 引用内的普通字符：消费 1，状态保持
            Assert.Equal(1, SqlFormatter.ConsumeQuoteChar("x", 0, 'x', ref quote));
            Assert.Equal(quoteChar, quote);
        }

        [Fact]
        public void ConsumeQuoteChar_DoubleEscapePair_ReturnsTwoAndKeepsQuoteOpen()
        {
            char quote = '\'';

            // '' 双写转义：两个字符一并消费，引用继续
            Assert.Equal(2, SqlFormatter.ConsumeQuoteChar("it''s", 2, '\'', ref quote));
            Assert.Equal('\'', quote);
        }

        [Fact]
        public void ConsumeQuoteChar_QuoteCloseExitsQuote_ReturnsOneAndClearsState()
        {
            char quote = '\'';

            Assert.Equal(1, SqlFormatter.ConsumeQuoteChar("x'", 1, '\'', ref quote));
            Assert.Equal('\0', quote);// 引用闭合
        }

        [Fact]
        public void ConsumeQuoteChar_OtherQuoteTypeInsideQuote_IsPlainChar()
        {
            char quote = '\'';

            // 单引号引用内的双引号：普通字符（不是边界也不是转义）
            Assert.Equal(1, SqlFormatter.ConsumeQuoteChar("'\"'", 1, '"', ref quote));
            Assert.Equal('\'', quote);
        }

        #endregion

        #region IndentLines

        [Fact]
        public void IndentLines_SingleLineSql_PrependsIndentOnly()
        {
            Assert.Equal("    SELECT * FROM Users", SqlFormatter.IndentLines("SELECT * FROM Users"));
        }

        [Fact]
        public void IndentLines_MultiLineLf_IndentsEveryLine()
        {
            var sql = "SELECT *\nFROM Users\nWHERE Id = 1";

            Assert.Equal("    SELECT *\n    FROM Users\n    WHERE Id = 1", SqlFormatter.IndentLines(sql));
        }

        [Fact]
        public void IndentLines_MultiLineCrLf_PreservesCrLfStyle()
        {
            // 换行风格原样保留（不篡改 CRLF/LF）
            var sql = "SELECT *\r\nFROM Users";

            Assert.Equal("    SELECT *\r\n    FROM Users", SqlFormatter.IndentLines(sql));
        }

        [Fact]
        public void IndentLines_NewlineInsideStringLiteral_LeftUntouched()
        {
            // 字面量内的换行不缩进、不转换——避免匹配值漂移
            var sql = "SELECT *\nFROM Users\nWHERE Memo = '第一行\r\n第二行'";

            Assert.Equal("    SELECT *\n    FROM Users\n    WHERE Memo = '第一行\r\n第二行'", SqlFormatter.IndentLines(sql));
        }

        [Fact]
        public void IndentLines_EscapedQuoteThenClose_RestartsIndentingAfterLiteral()
        {
            // '' 转义后闭合字面量，其后的结构换行恢复缩进
            var sql = "SELECT 'it''s a\r\nmemo' AS Memo\nFROM Users";

            Assert.Equal("    SELECT 'it''s a\r\nmemo' AS Memo\n    FROM Users", SqlFormatter.IndentLines(sql));
        }

        [Fact]
        public void IndentLines_QuotedIdentifierWithNewline_LeftUntouched()
        {
            // 双引号标识符（标准 SQL）内换行不缩进
            var sql = "SELECT *\nFROM T\nORDER BY \"my\ncolumn\"";

            Assert.Equal("    SELECT *\n    FROM T\n    ORDER BY \"my\ncolumn\"", SqlFormatter.IndentLines(sql));
        }

        [Fact]
        public void IndentLines_BacktickIdentifierWithNewline_LeftUntouched()
        {
            // 反引号标识符（MySQL）内换行不缩进
            var sql = "SELECT *\nFROM `my\ntable`";

            Assert.Equal("    SELECT *\n    FROM `my\ntable`", SqlFormatter.IndentLines(sql));
        }

        [Fact]
        public void IndentLines_DoubledQuoteInsideQuotes_ContinuesQuoting()
        {
            // "" 双写转义（标识符内的引号）：引用继续，其后结构换行恢复缩进
            var sql = "SELECT \"weird\"\"name\" AS C\nFROM T";

            Assert.Equal("    SELECT \"weird\"\"name\" AS C\n    FROM T", SqlFormatter.IndentLines(sql));
        }

        [Fact]
        public void IndentLines_ZeroOrNegativeLevels_ReturnsOriginalReference()
        {
            var sql = "SELECT *\nFROM T";

            Assert.Same(sql, SqlFormatter.IndentLines(sql, 0));
            Assert.Same(sql, SqlFormatter.IndentLines(sql, -1));
        }

        [Fact]
        public void IndentLines_EmptySql_ReturnsIndentOnly()
        {
            Assert.Equal("    ", SqlFormatter.IndentLines(string.Empty));
        }

        [Fact]
        public void IndentLines_LoneCarriageReturn_NotTreatedAsNewline()
        {
            // 单独 \r（老 Mac 经典换行）不触发缩进、原样保留（语义安全，仅该行不缩进）
            var sql = "SELECT 1\nFROM T\rWHERE X = 1";

            Assert.Equal("    SELECT 1\n    FROM T\rWHERE X = 1", SqlFormatter.IndentLines(sql));
        }

        [Fact]
        public void IndentLines_MixedLineEndings_EachStylePreserved()
        {
            // LF 与 CRLF 混用时各自风格保留
            var sql = "SELECT 1\nFROM T\r\nWHERE X = 1";

            Assert.Equal("    SELECT 1\n    FROM T\r\n    WHERE X = 1", SqlFormatter.IndentLines(sql));
        }

        [Fact]
        public void IndentLines_TrailingNewline_AppendsIndentAfterIt()
        {
            // 尾随换行后同样追加缩进（行为锁定：换行后一律缩进，无行上下文判断）
            Assert.Equal("    SELECT 1\n    ", SqlFormatter.IndentLines("SELECT 1\n"));
        }

        [Fact]
        public void IndentLines_BlankLines_IndentedLikeOtherLines()
        {
            // 空行与普通行一致：换行后一律追加缩进（产生带空格的空行）
            Assert.Equal("    SELECT 1\n    \n    FROM T", SqlFormatter.IndentLines("SELECT 1\n\nFROM T"));
        }

        [Fact]
        public void IndentLines_NPrefixUnicodeLiteral_TreatedAsQuoted()
        {
            // SQL Server 的 N'' Unicode 字面量：N 为普通字符、' 开引用，字面量内换行不缩进
            var sql = "SELECT N'中文\r\n值' AS C\nFROM T";

            Assert.Equal("    SELECT N'中文\r\n值' AS C\n    FROM T", SqlFormatter.IndentLines(sql));
        }

        [Fact]
        public void IndentLines_BacktickDoubledEscape_ContinuesQuoting()
        {
            // `` 双写转义（MySQL 标识符内的反引号）：引用继续，闭合后的结构换行恢复缩进
            var sql = "SELECT *\nFROM `my``tab\nle`\nWHERE X = 1";

            Assert.Equal("    SELECT *\n    FROM `my``tab\nle`\n    WHERE X = 1", SqlFormatter.IndentLines(sql));
        }

        [Fact]
        public void IndentLines_MultipleQuotedSegments_OnlyOutsideNewlinesIndented()
        {
            // 同一行多个引用段：字面量内换行不缩进，引用外的结构换行缩进
            var sql = "SELECT 'a\nb' AS X, 'c' AS Y\nFROM T";

            Assert.Equal("    SELECT 'a\nb' AS X, 'c' AS Y\n    FROM T", SqlFormatter.IndentLines(sql));
        }

        [Fact]
        public void IndentLines_MultipleLevels_AppliesNestedIndent()
        {
            var sql = "SELECT 1\nFROM T";

            Assert.Equal("        SELECT 1\n        FROM T", SqlFormatter.IndentLines(sql, 2));
        }

        [Fact]
        public void IndentLines_UnclosedLiteral_DegradesToNoIndentForRemainder()
        {
            // 字面量未闭合（残缺 SQL）安全降级：余文不加缩进、原样保留
            var sql = "SELECT 'unclosed\nFROM T";

            Assert.Equal("    SELECT 'unclosed\nFROM T", SqlFormatter.IndentLines(sql));
        }

        #endregion

        #region TrimSql

        [Fact]
        public void TrimSql_NoTrimNeeded_ReturnsSameReference()
        {
            var sql = "SELECT 1";

            Assert.Same(sql, SqlFormatter.TrimSql(sql));
        }

        [Theory]
        [InlineData("  SELECT 1", "SELECT 1")]
        [InlineData("SELECT 1  ", "SELECT 1")]
        [InlineData("SELECT 1;", "SELECT 1")]
        [InlineData("SELECT 1 ; \t ; \r", "SELECT 1")]
        [InlineData("\tSELECT 1; ", "SELECT 1")]
        public void TrimSql_TrimsLeadingWhitespaceAndTrailingSemicolonMix(string sql, string expected)
        {
            Assert.Equal(expected, SqlFormatter.TrimSql(sql));
        }

        [Fact]
        public void TrimSql_KeepTrailingSemicolon_RemovesWhitespaceOnly()
        {
            Assert.Equal("SELECT 1 ;", SqlFormatter.TrimSql("SELECT 1 ; ", removeSemicolon: false));
        }

        #endregion
    }
}
