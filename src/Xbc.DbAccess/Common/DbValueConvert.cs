using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Net;

namespace Xbc.DbAccess.Common
{
    /// <summary>
    /// 数据库值转换工具，提供强化版的 <see cref="Convert.ChangeType(object,Type)"/> 类型转换功能，同时支持自定义类型解析器。
    /// <para>定位为通用转换引擎（无有损防护：小数 → 整数等有损转换静默执行），适合手写 SQL 标量结果的类型归一（如 COUNT → long），目标类型由调用方对齐。</para>
    /// </summary>
    public static class DbValueConvert
    {
        /// <summary>
        /// 内置的类型解析器字典。
        /// </summary>
        private static readonly ConcurrentDictionary<Type, Func<object, object>> parsers = new ConcurrentDictionary<Type, Func<object, object>>
        {
            // 数据库返回的字符串值（如日期时间字面量）为与区域无关的标准格式，统一使用不变文化解析，
            // 避免服务器区域设置（非公历日历、dd/MM 与 MM/dd 差异等）导致解析失败或得到错误结果
            [typeof(DateTime)] = v => DateTime.Parse(v.ToString(), CultureInfo.InvariantCulture),
            [typeof(DateTimeOffset)] = v => DateTimeOffset.Parse(v.ToString(), CultureInfo.InvariantCulture),
            [typeof(TimeSpan)] = v => TimeSpan.Parse(v.ToString(), CultureInfo.InvariantCulture),
            [typeof(Guid)] = v => Guid.Parse(v.ToString()),
            [typeof(IPAddress)] = v => IPAddress.Parse(v.ToString()),
            [typeof(Uri)] = v => new Uri(v.ToString(), UriKind.RelativeOrAbsolute),
            [typeof(Version)] = v => Version.Parse(v.ToString()),
        };

        /// <summary>
        /// 内置解析器覆盖的类型集合（自 <see cref="parsers"/> 快照）。这些类型可通过 <see cref="AddParser"/> 重新注册，
        /// 其余 IConvertible 类型不允许注册自定义解析器。
        /// </summary>
        private static readonly HashSet<Type> builtInParserTypes = new HashSet<Type>(parsers.Keys);

        /// <summary>
        /// 添加自定义类型解析器。已实现 <see cref="IConvertible"/> 且无内置解析器的类型不支持；
        /// 有内置解析器的类型（如 <see cref="DateTime"/>）可重新注册以覆盖或恢复。
        /// </summary>
        /// <param name="type">目标类型。</param>
        /// <param name="parser">解析函数。</param>
        public static void AddParser(Type type, Func<object, object> parser)
        {
            Guard.ThrowIfNull(type, nameof(type));

            if (typeof(IConvertible).IsAssignableFrom(type) && !builtInParserTypes.Contains(type))
                throw new ArgumentException($"不支持为实现了 'IConvertible' 接口且无内置解析器的类型 '{type}' 添加自定义解析器。", nameof(type));

            parsers[type] = parser ?? throw new ArgumentNullException(nameof(parser));
        }

        /// <summary>
        /// 移除类型解析器；内置解析器移除后可经 <see cref="AddParser"/> 重新注册恢复。
        /// </summary>
        /// <param name="type">要移除的类型。</param>
        public static void RemoveParser(Type type)
        {
            Guard.ThrowIfNull(type, nameof(type));
            parsers.TryRemove(type, out _);
        }

        /// <summary>
        /// 将数据库值转换为指定类型，默认使用不变文化解析字符串值。
        /// <para>无有损防护：小数 → 整数等有损转换静默执行（如 3.7 → 4），目标类型由调用方对齐。</para>
        /// </summary>
        /// <typeparam name="T">目标类型。</typeparam>
        /// <param name="value">要转换的值。</param>
        /// <returns></returns>
        public static T ChangeType<T>(object value)
        {
            return (T)ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 将数据库值转换为指定类型，使用指定的格式提供器。
        /// </summary>
        /// <typeparam name="T">目标类型。</typeparam>
        /// <param name="value">要转换的值。</param>
        /// <param name="provider">格式提供器。</param>
        /// <returns></returns>
        public static T ChangeType<T>(object value, IFormatProvider provider)
        {
            return (T)ChangeType(value, typeof(T), provider);
        }

        /// <summary>
        /// 将数据库值转换为指定类型，默认使用不变文化解析字符串值。
        /// <para>无有损防护：小数 → 整数等有损转换静默执行（如 3.7 → 4），目标类型由调用方对齐。</para>
        /// </summary>
        /// <param name="value">要转换的值。</param>
        /// <param name="conversionType">目标类型。</param>
        /// <returns></returns>
        public static object ChangeType(object value, Type conversionType)
        {
            return ChangeType(value, conversionType, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 将数据库值转换为指定类型，使用指定的格式提供器。
        /// </summary>
        /// <param name="value">要转换的值。</param>
        /// <param name="conversionType">目标类型。</param>
        /// <param name="provider">格式提供器。</param>
        /// <returns></returns>
        /// <exception cref="InvalidCastException"></exception>
        public static object ChangeType(object value, Type conversionType, IFormatProvider provider)
        {
            Guard.ThrowIfNull(conversionType, nameof(conversionType));

            // 如果目标类型是 object，直接返回原值
            if (conversionType == typeof(object))
                return value;

            // 处理可空类型
            Type targetType = conversionType;
            bool isNullable = false;
            Type underlyingType = Nullable.GetUnderlyingType(conversionType);
            if (underlyingType != null)
            {
                targetType = underlyingType;
                isNullable = true;
            }

            // 处理 null 值
            if (value == null)
            {
                if (targetType == typeof(DBNull))
                    return DBNull.Value;// null 转换为 DBNull.Value
                else if (!targetType.IsValueType || isNullable)
                    return null;// 非值类型或可空类型，直接返回 null
                else
                    throw new InvalidCastException($"无法将 null 转换为类型 '{conversionType}'。");
            }

            // 如果值已经是目标类型，直接返回
            if (value.GetType() == targetType)
                return value;

            // 处理 DBNull
            if (value is DBNull)
            {
                if (targetType == typeof(string))
                    return null;// DBNull 转换为 null (string)
                else if (isNullable)
                    return null;// DBNull 转换为 null (可空类型)
                else
                    throw new InvalidCastException($"无法将 DBNull 转换为类型 '{conversionType}'。");
            }

            // 处理空字符串 - 对于可空类型返回 null
            if (value is string str && string.IsNullOrEmpty(str))
            {
                if (isNullable)
                    return null;

                // 对于非可空类型，继续尝试转换（可能会抛出异常）
            }

            // 1. 枚举类型
            if (targetType.IsEnum)
                return Enum.Parse(targetType, value.ToString(), true);

            // 2. 内置解析器
            if (parsers.TryGetValue(targetType, out var parser))
                return parser(value);

            // 3. TypeConverter
            var converter = TypeDescriptor.GetConverter(targetType);
            if (converter != null && converter.CanConvertFrom(value.GetType()))
                return converter.ConvertFrom(value);

            // 4. 标准 Convert.ChangeType
            return Convert.ChangeType(value, targetType, provider);
        }
    }
}
