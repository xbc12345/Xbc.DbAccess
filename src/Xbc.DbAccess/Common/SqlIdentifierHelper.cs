namespace Xbc.DbAccess.Common
{
    /// <summary>
    /// SQL 标识符工具：标识符字符的词法判定（词边界判定的统一约定，关键词检测、参数占位符匹配等场景共用）
    /// 与内部标识符生成（带前缀的唯一短标识符，如 q_1a2b3c4d，用作子查询别名、保存点名称等，避免与语句中已有标识符冲突）。
    /// </summary>
    internal static class SqlIdentifierHelper
    {
        /// <summary>
        /// 是否为 SQL 标识符字符（Unicode 字母、数字、下划线）——词边界判定的统一约定，关键词检测、参数占位符匹配等场景共用。
        /// <para>刻意采用 Unicode 字母集而非 ASCII：用户可能以中文等书写标识符（如参数名 @名称），按 ASCII 判断会误断词边界。</para>
        /// </summary>
        /// <param name="c">待判定字符。</param>
        /// <returns>是标识符字符时返回 true。</returns>
        public static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>
        /// 生成带前缀的唯一短标识符。部分数据库标识符长度有限，随机段默认仅取 8 位十六进制。
        /// </summary>
        /// <param name="prefix">标识符前缀。</param>
        /// <param name="length">随机段长度。默认 8 位，最大 16 位。</param>
        /// <returns>格式为“前缀_随机段”的短标识符。</returns>
        public static string Generate(string prefix, int length = 8)
        {
            if (length > 16)
                length = 16;

            return $"{prefix}_{Guid.NewGuid().ToString("N").Substring(0, length)}";
        }
    }
}
