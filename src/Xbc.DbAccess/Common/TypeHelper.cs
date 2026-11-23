namespace Xbc.DbAccess.Common
{
    /// <summary>
    /// 类型工具类，提供类型判断的静态方法。
    /// </summary>
    internal static class TypeHelper
    {
        /// <summary>
        /// 检查类型是否为整数类型。
        /// </summary>
        /// <param name="type">要检查的类型。</param>
        /// <param name="includeNullable">是否包含可空类型。</param>
        /// <returns></returns>
        public static bool IsInteger(Type type, bool includeNullable = true)
        {
            Guard.ThrowIfNull(type, nameof(type));

            Type underlyingType;
            if (includeNullable)
                underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            else
                underlyingType = type;

            return underlyingType == typeof(int) ||
                   underlyingType == typeof(long) ||
                   underlyingType == typeof(short) ||
                   underlyingType == typeof(byte) ||
                   underlyingType == typeof(uint) ||
                   underlyingType == typeof(ulong) ||
                   underlyingType == typeof(ushort) ||
                   underlyingType == typeof(sbyte);
        }

        /// <summary>
        /// 检查类型是否为浮点数类型。
        /// </summary>
        /// <param name="type">要检查的类型。</param>
        /// <param name="includeNullable">是否包含可空类型。</param>
        /// <returns></returns>
        public static bool IsFloating(Type type, bool includeNullable = true)
        {
            Guard.ThrowIfNull(type, nameof(type));

            Type underlyingType;
            if (includeNullable)
                underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            else
                underlyingType = type;

            return underlyingType == typeof(double) || underlyingType == typeof(float);
        }

        /// <summary>
        /// 检查类型是否为 decimal 类型。
        /// </summary>
        /// <param name="type">要检查的类型。</param>
        /// <param name="includeNullable">是否包含可空类型。</param>
        /// <returns></returns>
        public static bool IsDecimal(Type type, bool includeNullable = true)
        {
            Guard.ThrowIfNull(type, nameof(type));

            Type underlyingType;
            if (includeNullable)
                underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            else
                underlyingType = type;

            return underlyingType == typeof(decimal);
        }

        /// <summary>
        /// 检查类型是否为数值类型（整数、浮点数或 decimal）。
        /// </summary>
        /// <param name="type">要检查的类型。</param>
        /// <param name="includeNullable">是否包含可空类型。</param>
        /// <returns></returns>
        public static bool IsNumeric(Type type, bool includeNullable = true)
        {
            return IsInteger(type, includeNullable) ||
                   IsFloating(type, includeNullable) ||
                   IsDecimal(type, includeNullable);
        }

        /// <summary>
        /// 检查类型是否为日期时间相关类型（DateTime、DateTimeOffset 或 TimeSpan）。
        /// </summary>
        /// <param name="type">要检查的类型。</param>
        /// <param name="includeNullable">是否包含可空类型。</param>
        /// <returns></returns>
        public static bool IsDateTime(Type type, bool includeNullable = true)
        {
            Guard.ThrowIfNull(type, nameof(type));

            Type underlyingType;
            if (includeNullable)
                underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            else
                underlyingType = type;

            return underlyingType == typeof(DateTime) ||
                   underlyingType == typeof(DateTimeOffset) ||
                   underlyingType == typeof(TimeSpan);
        }

        /// <summary>
        /// 检查类型是否为 Guid 类型。
        /// </summary>
        /// <param name="type">要检查的类型。</param>
        /// <param name="includeNullable">是否包含可空类型。</param>
        /// <returns></returns>
        public static bool IsGuid(Type type, bool includeNullable = true)
        {
            Guard.ThrowIfNull(type, nameof(type));

            Type underlyingType;
            if (includeNullable)
                underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            else
                underlyingType = type;

            return underlyingType == typeof(Guid);
        }

        /// <summary>
        /// 检查类型是否为泛型可枚举类型（排除 <see cref="string"/>）。
        /// </summary>
        /// <param name="type">要检查的类型。</param>
        /// <param name="elementType">可枚举类型的元素类型。</param>
        /// <returns></returns>
        public static bool IsGenericEnumerable(Type type, out Type elementType)
        {
            Guard.ThrowIfNull(type, nameof(type));

            // string 实现了 IEnumerable<char>，但不是集合类型
            if (type == typeof(string))
            {
                elementType = null;
                return false;
            }

            // GetInterfaces() 不包含类型自身：导航属性直接声明为 IEnumerable<T> 等接口类型时，
            // 接口本身不在返回结果中，需先检查类型自身（IReadOnlyList<T>/ICollection<T> 等实现
            // IEnumerable<T> 的接口不受影响，仍由 GetInterfaces 匹配）
            Type enumerableType;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                enumerableType = type;
            else
                enumerableType = type.GetInterfaces().FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            if (enumerableType != null)
            {
                elementType = enumerableType.GetGenericArguments().FirstOrDefault();
                return true;
            }
            else
            {
                elementType = null;
                return false;
            }
        }

        /// <summary>
        /// 检查类型是否为匿名类型（编译器生成的类型）。
        /// </summary>
        /// <param name="type">要检查的类型。</param>
        /// <returns></returns>
        public static bool IsAnonymousType(Type type)
        {
            Guard.ThrowIfNull(type, nameof(type));
            return type.Name.StartsWith("<>f__AnonymousType", StringComparison.Ordinal);
        }

        /// <summary>
        /// 检查类型是否为 System.ValueTuple 元组类型（泛型参数 1~8 的形态均判定为真：
        /// 1~7 元素为平铺形态，8 元素为 ValueTuple&lt;T1..T7, ValueTuple&lt;T1&gt;&gt; 的 TRest 嵌套形态）。
        /// <para>以泛型定义精确比较判定（类型名前缀与 struct 判定均不可靠）；0 元素的非泛型 System.ValueTuple 不予识别，
        /// 其零实参构造在解析层以空形态统一拒绝。</para>
        /// </summary>
        /// <param name="type">要检查的类型。</param>
        /// <returns></returns>
        public static bool IsValueTuple(Type type)
        {
            Guard.ThrowIfNull(type, nameof(type));

            if (!type.IsValueType || !type.IsGenericType)
                return false;

            var definition = type.GetGenericTypeDefinition();
            return definition == typeof(ValueTuple<>) ||
                   definition == typeof(ValueTuple<,>) ||
                   definition == typeof(ValueTuple<,,>) ||
                   definition == typeof(ValueTuple<,,,>) ||
                   definition == typeof(ValueTuple<,,,,>) ||
                   definition == typeof(ValueTuple<,,,,,>) ||
                   definition == typeof(ValueTuple<,,,,,,>) ||
                   definition == typeof(ValueTuple<,,,,,,,>);
        }

        /// <summary>
        /// 检查元组类型是否超出平铺装配范围：8 元素即 ValueTuple&lt;T1..T7, TRest&gt; 的 TRest 嵌套形态，
        /// 构造函数第 8 参为嵌套 ValueTuple 而非第 8 个元素，按列位置装配无法承载（投影层构建期显式拒绝）。
        /// </summary>
        /// <param name="type">要检查的类型。</param>
        /// <returns></returns>
        public static bool IsOverLengthValueTuple(Type type)
        {
            return IsValueTuple(type) && type.GetGenericArguments().Length == 8;
        }
    }
}
