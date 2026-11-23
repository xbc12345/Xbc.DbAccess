using System.Text;

namespace Xbc.DbAccess.Common
{
    /// <summary>
    /// 参数名清理器：将任意基名（列名、字典键等）转换为各 provider 公认的合法参数名基名，规则详见 <see cref="CreateSafeName"/>。
    /// 极端情况下不同基名清理后可能相同——仅当一方的非法字符序列转义后恰好与另一方的字面 "u+4位十六进制" 片段吻合
    /// （如 "My Col" 转义为 "Myu0020Col" 与字面命名 "Myu0020Col"），此时命令构建因参数重名显式失败。
    /// </summary>
    internal static class ParameterNameSanitizer
    {
        /// <summary>
        /// 生成合法的参数名基名：非合法字符替换为 Unicode 码点转义（如中文字符按 U+540D → u540d），
        /// 首字符非 ASCII 字母时补 p_ 前缀（如 "1Price" → "p_1Price"）。
        /// <para>Unicode 字母逐字符转义（而非统一替换为下划线）：中文列名共存是常态场景，统一替换会让
        /// 不同的中文基名清理后完全相同（如「名称」「状态」同为 p___），既依赖分配器序号后缀兜底而失去可辨识性，
        /// 也会让不经分配器的直传参数路径直接重名失败；码点转义保留基名的可辨识性且天然防撞。</para>
        /// </summary>
        /// <param name="baseName">参数名基名（通常是列名或字典键）。</param>
        /// <returns>清理后的参数名基名，空基名返回 "p_"。</returns>
        public static string CreateSafeName(string baseName)
        {
            if (string.IsNullOrEmpty(baseName))
                return "p_";

            var sb = new StringBuilder(baseName.Length + 2);
            foreach (var c in baseName)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')
                {
                    // 仅 ASCII 字母、数字与下划线是各 provider 公认的参数名字符，原样保留
                    sb.Append(c);
                }
                else
                {
                    // 其余字符（含 Unicode 字母与空格等）按 Unicode 码点转义为 ASCII 安全形态：
                    // 「名」U+540D → u540d。转义后必然 ASCII 合法，且不同字符转义结果不同，保留可辨识性
                    sb.Append('u').Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            // 参数名必须以 ASCII 字母开头。转义形态以 u 开头天然合法；但数字/下划线开头的基名
            // （首字符属合法字符集而原样保留，不走转义）仍需补前缀，如 "1Price" → "p_1Price"、"_name" → "p__name"
            var first = sb[0];
            if (!(first >= 'a' && first <= 'z') && !(first >= 'A' && first <= 'Z'))
                sb.Insert(0, "p_");

            return sb.ToString();
        }
    }
}
