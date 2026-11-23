using System.Text;

namespace Xbc.DbAccess.Common
{
    /// <summary>
    /// SQL 文本工具：格式化（统一缩进、尾部分号与空白清理）与词法判定（标识符字符）。
    /// </summary>
    internal static class SqlFormatter
    {
        /// <summary>
        /// 引用状态机单步：计算 SQL 在位置 <paramref name="i"/> 处的引用状态转移。覆盖字符串字面量与引用标识符
        /// （单引号/双引号/反引号，''/""/`` 双写转义），供「仅对引用外文本操作」的扫描场景共用——
        /// 整体缩进（<see cref="IndentLines"/>）、参数占位符替换、ORDER BY 子句检测等，引用词法规则单点维护于此。
        /// <para>使用约定：调用方在扫描循环内以 <c>var consumed = ConsumeQuoteChar(sql, i, sql[i], ref quote);</c> 调用，
        /// consumed 大于 0 表示该字符属于引用上下文（按 consumed 推进并跳过引用外动作）；返回 0 表示引用外普通字符，
        /// 调用方执行自身动作后推进 1。已知局限：方言特有转义（如 MySQL 的 <c>\'</c>）可能误判引用边界；
        /// SQL Server 方括号标识符未纳入引用识别，标识符内含引号字符（如 <c>[O'Brien]</c>）会误开引用状态。
        /// 框架 SQL 的值均参数化传递，上述形态属罕见场景。</para>
        /// </summary>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="i">当前扫描位置。</param>
        /// <param name="c">当前字符（sql[i]）。</param>
        /// <param name="quote">引用状态（调用方跨循环持有）：'\0'=引用外；'\''/"'/`'=对应引用内。</param>
        /// <returns>本次应消费的字符数：大于 0 为引用上下文字符数（双写转义对为 2）；0 为引用外普通字符。</returns>
        public static int ConsumeQuoteChar(string sql, int i, char c, ref char quote)
        {
            if (quote != '\0')
            {
                if (c == quote)
                {
                    // 双写转义的引号：两个引号字符一并消费，引用继续
                    if (i + 1 < sql.Length && sql[i + 1] == quote)
                        return 2;

                    quote = '\0';// 引用闭合
                }

                return 1;
            }

            // 引用起始字符：单引号（字符串字面量）、双引号（标准标识符 / MySQL 字符串）、反引号（MySQL 标识符）
            if (c == '\'' || c == '"' || c == '`')
            {
                quote = c;
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// 将多行 SQL 整体缩进：每一行（含首行）前置指定层数的缩进（每层 4 个空格），
        /// 供派生表包裹等嵌入场景使内嵌 SQL 与包裹层对齐嵌套层级。
        /// <para>词法感知（<see cref="ConsumeQuoteChar"/>）：引用内的换行不缩进、不转换（避免值漂移），
        /// 结构换行原样保留换行风格（不篡改 CRLF/LF）。已知局限见 <see cref="ConsumeQuoteChar"/>。</para>
        /// </summary>
        /// <param name="sql">SQL 语句（可多行）。</param>
        /// <param name="levels">缩进层数；小于 1 时不缩进，原引用返回。</param>
        /// <returns>整体缩进后的 SQL。</returns>
        public static string IndentLines(string sql, int levels = 1)
        {
            // 0/负层数语义为不缩进，直接原样返回（BCL 格式化函数惯例，如 string.PadLeft(0)）
            if (levels < 1)
                return sql;

            var indent = string.Empty;
            for (var l = 0; l < levels; l++)
            {
                indent += "    ";
            }

            // 单行 SQL 无需词法扫描（绝大多数调用形态）：Contains 向量化预筛后直接拼接，一次分配
            if (!sql.Contains('\n'))
                return indent + sql;

            // 多行：引用感知逐字符扫描，仅对引用外的换行追加缩进；换行字符原样输出（不转换 CRLF/LF 风格）
            var result = new StringBuilder(sql.Length + indent.Length * 4);
            result.Append(indent);

            char quote = '\0';
            var i = 0;
            while (i < sql.Length)
            {
                var c = sql[i];
                var consumed = ConsumeQuoteChar(sql, i, c, ref quote);
                if (consumed > 0)
                {
                    result.Append(sql, i, consumed);// 引用上下文字符原样输出
                    i += consumed;
                    continue;
                }

                result.Append(c);

                if (c == '\n')
                    result.Append(indent);

                i++;
            }

            return result.ToString();
        }

        /// <summary>
        /// 移除 SQL 首部空白；<paramref name="removeSemicolon"/> 为 true 时同时移除尾部空白与分号（无论交错顺序），否则仅移除尾部空白。
        /// </summary>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="removeSemicolon">是否连同尾部空白一并移除末尾分号。</param>
        /// <returns></returns>
        public static string TrimSql(string sql, bool removeSemicolon = true)
        {
            // 尾部空白与分号可能任意交错出现（如 "; \t ; \r"），且空白不限于 ASCII（如全角空格），
            // 单趟 TrimEnd 无法一次剪净；从尾部回扫至第一个既非空白也非分号的字符后一次截取
            var start = 0;
            var end = sql.Length - 1;

            while (start <= end && char.IsWhiteSpace(sql[start]))
            {
                start++;
            }

            if (removeSemicolon)
            {
                while (end >= start && (char.IsWhiteSpace(sql[end]) || sql[end] == ';'))
                {
                    end--;
                }
            }
            else
            {
                while (end >= start && char.IsWhiteSpace(sql[end]))
                {
                    end--;
                }
            }

            // 首尾均无需剪裁时返回原引用，避免热路径上无谓的整串拷贝（string.Trim 系列未剪裁时同样返回原实例）
            return start == 0 && end == sql.Length - 1
                ? sql
                : sql.Substring(start, end - start + 1);
        }
    }
}
