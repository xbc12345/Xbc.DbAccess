using System.Collections;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 为 <see cref="IDbAccess"/> 提供参数对象转换扩展（类似 Dapper 的匿名对象参数处理）：
    /// 将多种形态的参数对象统一转换为 <see cref="DbParameter"/> 数组，支持的形态详见 <see cref="ConvertToDbParameters"/>。
    /// <para>Text 命令下自动将集合参数展开为 IN 子句的多个参数（如 @Ids 展开为 @Ids_0、@Ids_1）。</para>
    /// </summary>
    internal static class DbParameterConversionExtensions
    {
        /// <summary>
        /// 将参数（支持匿名对象、字典、DbParameter、集合等类型）转换为 DbParameter 数组。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="param">参数对象。</param>
        /// <param name="sql">SQL 语句，集合展开时用于替换参数占位符。</param>
        /// <param name="commandType">命令类型。</param>
        /// <returns></returns>
        internal static DbParameter[] ConvertToDbParameters(this IDbAccess dbAccess, object param, ref string sql, CommandType commandType = CommandType.Text)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));

            if (param == null)
                return null;

            // 如果已经是 DbParameter 数组，直接返回
            if (param is DbParameter[] dbParams)
                return dbParams;

            // 如果是单个 DbParameter，包装成数组
            if (param is DbParameter dbParam)
                return new[] { dbParam };

            // 如果是 DbParameterCollection，直接返回数组
            if (param is DbParameterCollection dbParamCollection)
                return dbParamCollection.ToArray();

            // 如果是 IEnumerable<DbParameter>，例如：List<DbParameter> 等，直接返回数组
            if (param is IEnumerable<DbParameter> dbParamEnumerable)
                return dbParamEnumerable.ToArray();

            // 处理字典类型 (IDictionary)，包括 Dictionary<string, object>, Hashtable 等
            if (param is IDictionary dict)
                return ConvertFromDictionary(dbAccess, dict, commandType, ref sql);

            // 处理匿名对象（通过反射属性）
            return ConvertFromObject(dbAccess, param, commandType, ref sql);
        }

        /// <summary>
        /// 将字典参数（包括 Dictionary&lt;string, object&gt;, Hashtable 等）转换为参数数组。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="dict">字典参数。</param>
        /// <param name="commandType">命令类型。</param>
        /// <param name="sql">SQL 语句，集合展开时用于替换参数占位符。</param>
        /// <returns></returns>
        private static DbParameter[] ConvertFromDictionary(IDbAccess dbAccess, IDictionary dict, CommandType commandType, ref string sql)
        {
            var parameterCollection = dbAccess.NewParameters();

            // 先登记全部键名：集合展开生成的组合参数名（如 Ids_0）需与用户键名防撞（无论添加顺序），
            // 否则重名键会在添加时抛异常、或展开名被错位引用。
            // 经 Keys 集合取键名，规避 Cast<DictionaryEntry> 对 Dictionary<string,object> 枚举 KVP 条目的强转失败
            var allocator = new ParameterNameAllocator(dict.Keys.Cast<object>().Select(key => key?.ToString()));

            foreach (DictionaryEntry entry in dict)
            {
                var key = entry.Key?.ToString();
                if (string.IsNullOrEmpty(key))
                    continue;

                // 集合展开仅对 Text 命令有意义（存储过程的集合参数由过程自身处理）
                if (commandType == CommandType.Text && TryExpandCollection(key, entry.Value, parameterCollection, allocator, ref sql))
                    continue;

                parameterCollection.Add(key, entry.Value);
            }

            return parameterCollection.ToArray();
        }

        /// <summary>
        /// 将对象参数（通过反射公共实例属性）转换为参数数组。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="param">参数对象。</param>
        /// <param name="commandType">命令类型。</param>
        /// <param name="sql">SQL 语句，集合展开时用于替换参数占位符。</param>
        /// <returns></returns>
        private static DbParameter[] ConvertFromObject(IDbAccess dbAccess, object param, CommandType commandType, ref string sql)
        {
            var parameterCollection = dbAccess.NewParameters();
            var properties = param.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            // 先登记全部属性名：集合展开生成的组合参数名（如 Ids_0）需与用户属性名防撞（无论属性顺序），
            // 否则重名属性会在添加时抛异常、或展开名被错位引用
            var allocator = new ParameterNameAllocator(properties.Select(property => property.Name));

            foreach (var property in properties)
            {
                var name = property.Name;
                var value = property.GetValue(param);

                // 集合展开仅对 Text 命令有意义（存储过程的集合参数由过程自身处理）
                if (commandType == CommandType.Text && TryExpandCollection(name, value, parameterCollection, allocator, ref sql))
                    continue;

                parameterCollection.Add(name, value);
            }

            return parameterCollection.ToArray();
        }

        /// <summary>
        /// 尝试将集合参数展开为多个数据库参数，并替换 SQL 中的占位符。
        /// </summary>
        /// <param name="name">属性名称或字典键。</param>
        /// <param name="value">集合对象。</param>
        /// <param name="parameterCollection">参数集合。</param>
        /// <param name="allocator">本次参数构建共享的参数名分配器（已登记全部键名/属性名），保证展开名与用户参数互不冲突。</param>
        /// <param name="sql">SQL 语句，集合展开时用于替换参数占位符。</param>
        /// <returns></returns>
        private static bool TryExpandCollection(string name, object value, DbParameterCollection parameterCollection, ParameterNameAllocator allocator, ref string sql)
        {
            if (value == null)
                return false;

            // 排除字符串（字符串也是 IEnumerable<char>）
            // 排除字节数组与字符数组：它们虽实现 IEnumerable，但语义是二进制/文本块（如 BLOB 列）
            if (value is string || value is byte[] || value is char[])
                return false;

            // 检查是否为 IEnumerable（数组、List、HashSet 等都实现此接口）
            if (value is not IEnumerable enumerable)
                return false;

            var paramNames = new List<string>();
            var index = 0;

            // 参数名基名清理：为防御异常键值（含空格/非 ASCII 字符或以数字开头），
            // 统一走 ParameterNameSanitizer（顺带补齐 p_ 前缀等缺失场景）；
            // 参数名以索引组合（如 @Ids_0、@Ids_1），经分配器保证互不冲突
            foreach (var item in enumerable)
            {
                var itemDbParam = parameterCollection.ParameterBuilder.CreateParameter(allocator.Allocate($"{name}_{index}"), item);
                parameterCollection.Add(itemDbParam);
                paramNames.Add(itemDbParam.ParameterName);// 包含参数前缀的参数名，例如：@Ids_0
                index++;
            }

            var paramName = parameterCollection.ParameterBuilder.GetParameterName(name); // 完整的参数名，例如：@Ids
            if (!TryFindParameterPlaceholder(sql, paramName, out _))
                throw new ArgumentException($"SQL 语句中未包含参数名 '{paramName}'。", nameof(sql));

            // 空集合，IN (NULL) 不会匹配任何行，对于 UPDATE/DELETE 可能掩盖逻辑错误
            if (paramNames.Count == 0)
                throw new ArgumentException($"参数 '{name}' 是一个空集合，无法展开为 IN 子句，请确保集合中至少包含一个元素。", nameof(name));

            // 按词边界替换为展开后的参数列表，两种写法均支持：
            // - IN (@ids)：用户已写括号，占位符直接替换为列表
            // - IN @ids（Dapper 风格）：处于 IN 子句语境时自动补括号，方便 Dapper 用户迁移
            sql = ReplaceParameterPlaceholder(sql, paramName, string.Join(", ", paramNames));
            return true;
        }

        /// <summary>
        /// 从头引用感知地查找参数占位符（如 <c>@Ids</c>）的词边界匹配，命中时输出其起始位置：
        /// 单引号字符串字面量、双引号与反引号引用内的参数名文本不计入（如字面量 <c>'参考 @Ids 写法'</c>），
        /// 避免把字面量内容误当占位符改写而破坏 SQL 语义。
        /// <para>匹配要求占位符后紧跟非标识符字符或串尾，否则命中处是更长参数名的组成部分（如 @Ids 前缀命中 @IdsList）。</para>
        /// </summary>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="paramName">包含前缀的完整参数名，例如 @Ids。</param>
        /// <param name="position">命中的占位符起始位置（含参数前缀）；未找到时为 -1。</param>
        /// <returns>找到词边界匹配时返回 true。</returns>
        private static bool TryFindParameterPlaceholder(string sql, string paramName, out int position)
        {
            position = -1;
            char quote = '\0';// 当前所在引用类型：'\0'=引用外；'\''/"'/`'=对应引用内
            var i = 0;

            while (i <= sql.Length - paramName.Length)
            {
                var c = sql[i];
                var consumed = SqlFormatter.ConsumeQuoteChar(sql, i, c, ref quote);
                if (consumed > 0)
                {
                    i += consumed;// 引用上下文：不计入占位符匹配
                    continue;
                }

                // 引用外：完整参数名匹配 + 后词边界
                if (c == paramName[0]
                    && string.CompareOrdinal(sql, i, paramName, 0, paramName.Length) == 0
                    && (i + paramName.Length == sql.Length || !SqlIdentifierHelper.IsIdentifierChar(sql[i + paramName.Length])))
                {
                    position = i;
                    return true;
                }

                i++;
            }

            return false;
        }

        /// <summary>
        /// 将 SQL 中参数占位符的全部词边界匹配替换为指定内容（引用感知单遍扫描）；
        /// 是否为展开的参数列表自动补括号由 <see cref="NeedsListParentheses"/> 判定。
        /// </summary>
        /// <param name="sql">SQL 语句。</param>
        /// <param name="paramName">包含前缀的完整参数名，例如 @Ids。</param>
        /// <param name="replacement">替换内容。</param>
        /// <returns>替换后的 SQL。</returns>
        private static string ReplaceParameterPlaceholder(string sql, string paramName, string replacement)
        {
            // 不能整串 Replace：若 SQL 中存在以当前参数名为前缀的其他参数（如 @Id 与 @IdList），
            // 整串替换会破坏该参数名，产生损坏的 SQL；引用内的占位符文本（如字面量 '参考 @Ids 写法'）
            // 同样不能替换——单遍扫描维护引用状态，仅对引用外的词边界匹配替换
            // （分段 Append：补括号若用 $"({replacement})" 插值会先分配与列表等长的临时串再拷贝，大集合场景翻倍开销）
            var sb = new StringBuilder(sql.Length);
            var copyIndex = 0;
            char quote = '\0';
            var i = 0;

            while (i < sql.Length)
            {
                var c = sql[i];
                var consumed = SqlFormatter.ConsumeQuoteChar(sql, i, c, ref quote);
                if (consumed > 0)
                {
                    i += consumed;// 引用上下文：跳过（间隙由 copyIndex 原样拷贝）
                    continue;
                }

                // 引用外：完整参数名匹配 + 后词边界
                if (c == paramName[0]
                    && string.CompareOrdinal(sql, i, paramName, 0, paramName.Length) == 0
                    && (i + paramName.Length == sql.Length || !SqlIdentifierHelper.IsIdentifierChar(sql[i + paramName.Length])))
                {
                    sb.Append(sql, copyIndex, i - copyIndex);

                    // IN (@ids) 写法已被括号包裹，原样替换；IN @ids（Dapper 风格）自动补括号
                    if (NeedsListParentheses(sql, i))
                        sb.Append('(').Append(replacement).Append(')');
                    else
                        sb.Append(replacement);

                    i += paramName.Length;
                    copyIndex = i;
                    continue;
                }

                i++;
            }

            sb.Append(sql, copyIndex, sql.Length - copyIndex);
            return sb.ToString();
        }

        /// <summary>
        /// 判断展开后的参数列表（如 <c>@Ids_0, @Ids_1</c>）在占位符位置是否需要自动补括号：
        /// 占位符处于 IN 关键字之后（Dapper 风格写法 <c>IN @Ids</c>）且括号尚未包裹时需要。
        /// </summary>
        /// <param name="sql">SQL 语句（替换前的原文）。</param>
        /// <param name="placeholderIndex">占位符在 SQL 中的起始位置（含参数前缀）。</param>
        /// <returns>需要补括号时返回 true。</returns>
        private static bool NeedsListParentheses(string sql, int placeholderIndex)
        {
            // 向前跳过空白字符（含换行）
            var i = placeholderIndex - 1;
            while (i >= 0 && char.IsWhiteSpace(sql[i]))
            {
                i--;
            }

            if (i < 0)
                return false;

            // 已被括号包裹（IN (@Ids) 写法）：不重复补括号
            if (sql[i] == '(')
                return false;

            // 向前读取连续字母组成的单词段，要求恰为 IN 关键词（忽略大小写；NOT IN 中的 IN 同样命中）；
            // 长度前置 + 区域比较免分配：字母段更长（如 WITHIN、Valin）或更短（紧邻数字/下划线等非字母）均不命中
            var wordEnd = i;
            while (i >= 0 && char.IsLetter(sql[i]))
            {
                i--;
            }

            if (wordEnd - i != 2 || string.Compare(sql, i + 1, "IN", 0, 2, StringComparison.OrdinalIgnoreCase) != 0)
                return false;

            // 词边界：IN 前面不能是标识符字符（防止 Valin @Ids 一类的列名误判）
            if (i >= 0 && SqlIdentifierHelper.IsIdentifierChar(sql[i]))
                return false;

            return true;
        }
    }
}
