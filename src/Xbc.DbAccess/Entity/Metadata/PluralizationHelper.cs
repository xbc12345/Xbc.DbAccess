namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 复数化辅助类，将英文单数名词转换为复数形式，用于实体类名到表名的默认映射。
    /// <para>复数化是尽力而为的约定推断，无法覆盖所有不规则单词；类名不符合常规复数规则时，
    /// 应显式指定表名，而非扩充词表。</para>
    /// </summary>
    internal static class PluralizationHelper
    {
        // 不规则复数形式映射
        private static readonly Dictionary<string, string> IrregularPlurals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["person"] = "people",
            ["man"] = "men",
            ["woman"] = "women",
            ["child"] = "children",
            ["tooth"] = "teeth",
            ["foot"] = "feet",
            ["mouse"] = "mice",
            ["goose"] = "geese",
            ["ox"] = "oxen",
            ["cactus"] = "cacti",
            ["focus"] = "foci",
            ["fungus"] = "fungi",
            ["nucleus"] = "nuclei",
            ["syllabus"] = "syllabi",
            ["analysis"] = "analyses",
            ["crisis"] = "crises",
            ["thesis"] = "theses",
            ["phenomenon"] = "phenomena",
            ["criterion"] = "criteria",
            ["datum"] = "data",
            ["index"] = "indices",
            ["matrix"] = "matrices",
            ["vertex"] = "vertices",
        };

        // 以 -o 结尾需要加 -es 的单词
        private static readonly HashSet<string> OEndings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hero", "potato", "tomato", "echo", "veto",
            "mosquito", "volcano", "tornado", "zero", "cargo"
        };

        // 不可数名词（不需要变复数）
        private static readonly HashSet<string> UncountableNouns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "equipment", "information", "rice", "money", "species", "series",
            "fish", "sheep", "deer", "moose", "swine", "bison", "buffalo",
            "shrimp", "squid", "trout", "salmon", "carp", "pike", "plankton",
            "water", "milk", "bread", "butter", "cheese", "meat", "beef",
            "pork", "mutton", "fruit", "food", "furniture", "luggage", "baggage",
            "homework", "housework", "news", "advice", "knowledge", "education",
            "research", "traffic", "weather", "music", "art", "love", "peace",
            "happiness", "health", "wealth", "courage", "bravery", "honesty",
            "justice", "patience", "energy", "time", "space", "work", "help",
        };

        /// <summary>
        /// 将单数名词转换为复数形式。
        /// </summary>
        /// <param name="word">单数名词。</param>
        /// <returns>复数形式。</returns>
        public static string ToPlural(string word)
        {
            if (string.IsNullOrEmpty(word))
                return word;

            // 检查是否是不可数名词
            if (UncountableNouns.Contains(word))
                return word;

            // 检查是否是不规则复数
            if (IrregularPlurals.TryGetValue(word, out var irregularPlural))
            {
                // 保留原始大小写：若原词首字母大写，复数形式也首字母大写
                if (word.Length > 0 && char.IsUpper(word[0]) && irregularPlural.Length > 0)
                    return char.ToUpperInvariant(irregularPlural[0]) + irregularPlural.Substring(1);

                return irregularPlural;
            }

            // 应用复数化规则
            return ApplyPluralRules(word);
        }

        /// <summary>
        /// 应用复数化规则。
        /// </summary>
        /// <param name="word">单词。</param>
        /// <returns>复数形式。</returns>
        private static string ApplyPluralRules(string word)
        {
            var lowerWord = word.ToLowerInvariant();

            // 规则 1: 以 s, x, z, ch, sh 结尾，加 -es
            if (lowerWord.EndsWith("s") ||
                lowerWord.EndsWith("x") ||
                lowerWord.EndsWith("z") ||
                lowerWord.EndsWith("ch") ||
                lowerWord.EndsWith("sh"))
            {
                return word + "es";
            }

            // 规则 2: 以辅音字母 + y 结尾，变 y 为 i 加 -es
            if (lowerWord.Length > 1 &&
                lowerWord.EndsWith("y") &&
                !IsVowel(lowerWord[lowerWord.Length - 2]))
            {
                return word.Substring(0, word.Length - 1) + "ies";
            }

            // 规则 3: 以 -fe 结尾，变 fe 为 v 加 -es
            if (lowerWord.EndsWith("fe"))
                return word.Substring(0, word.Length - 2) + "ves";

            // 规则 4: 以 -f 结尾（非 -ff），变 f 为 v 加 -es
            if (lowerWord.EndsWith("f") && !lowerWord.EndsWith("ff"))
                return word.Substring(0, word.Length - 1) + "ves";

            // 规则 5: 以 -o 结尾的特定单词，加 -es
            if (lowerWord.EndsWith("o") && OEndings.Contains(word))
                return word + "es";

            // 默认规则: 加 -s
            return word + "s";
        }

        /// <summary>
        /// 判断字符是否为元音字母。
        /// </summary>
        /// <param name="c">字符。</param>
        /// <returns>是否为元音。</returns>
        private static bool IsVowel(char c)
        {
            return "aeiou".IndexOf(char.ToLowerInvariant(c)) >= 0;
        }
    }
}
