namespace Xbc.DbAccess
{
    /// <summary>
    /// 参数守卫，统一参数校验的异常抛出，避免各处重复构造异常消息。
    /// <para>null 抛 <see cref="ArgumentNullException"/>，非 null 但非法抛 <see cref="ArgumentException"/>；
    /// net8.0 及以上委托 BCL 内置实现（异常消息为英文），低版本框架使用本文件内的等价实现（消息为中文）。</para>
    /// </summary>
    internal static class Guard
    {
        /// <summary>
        /// 校验参数非 null。
        /// </summary>
        /// <param name="value">参数值。</param>
        /// <param name="paramName">参数名（通常传 <c>nameof(...)</c>）。</param>
        internal static void ThrowIfNull(object value, string paramName)
        {
#if NET8_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(value, paramName);
#else
            if (value == null)
                throw new ArgumentNullException(paramName);
#endif
        }

        /// <summary>
        /// 校验 int 参数为正数（> 0），否则抛 <see cref="ArgumentOutOfRangeException"/>。
        /// </summary>
        /// <param name="value">参数值。</param>
        /// <param name="paramName">参数名（通常传 <c>nameof(...)</c>）。</param>
        internal static void ThrowIfNegativeOrZero(int value, string paramName)
        {
#if NET8_0_OR_GREATER
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, paramName);
#else
            if (value <= 0)
                throw new ArgumentOutOfRangeException(paramName, $"参数 '{paramName}' 必须为正数（实际值：{value}）。");
#endif
        }

        /// <summary>
        /// 校验字符串参数非 null 且非空。
        /// </summary>
        /// <param name="value">参数值。</param>
        /// <param name="paramName">参数名（通常传 <c>nameof(...)</c>），用于异常消息与 <see cref="ArgumentException.ParamName"/>。</param>
        internal static void ThrowIfNullOrEmpty(string value, string paramName)
        {
#if NET8_0_OR_GREATER
            ArgumentException.ThrowIfNullOrEmpty(value, paramName);
#else
            if (value == null)
                throw new ArgumentNullException(paramName);

            if (string.IsNullOrEmpty(value))
                throw new ArgumentException($"参数 '{paramName}' 不能为空。", paramName);
#endif
        }

        /// <summary>
        /// 校验字符串参数非 null 且非空白。
        /// </summary>
        /// <param name="value">参数值。</param>
        /// <param name="paramName">参数名（通常传 <c>nameof(...)</c>），用于异常消息与 <see cref="ArgumentException.ParamName"/>。</param>
        internal static void ThrowIfNullOrWhiteSpace(string value, string paramName)
        {
#if NET8_0_OR_GREATER
            ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
#else
            if (value == null)
                throw new ArgumentNullException(paramName);

            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"参数 '{paramName}' 不能为空或空白。", paramName);
#endif
        }

        /// <summary>
        /// 校验集合参数非 null 且包含至少一个元素：null 抛 <see cref="ArgumentNullException"/>，空集合抛 <see cref="ArgumentException"/>。
        /// <para>参数类型为 <see cref="IReadOnlyCollection{T}"/>：同时覆盖 List 等可变集合与 IReadOnlyList 等只读集合。</para>
        /// </summary>
        /// <typeparam name="T">元素类型。</typeparam>
        /// <param name="collection">集合参数。</param>
        /// <param name="paramName">参数名（通常传 <c>nameof(...)</c>）。</param>
        internal static void ThrowIfNullOrEmpty<T>(IReadOnlyCollection<T> collection, string paramName)
        {
            ThrowIfNull(collection, paramName);

            if (collection.Count == 0)
                throw new ArgumentException($"参数 '{paramName}' 不能为空集合。", paramName);
        }

        /// <summary>
        /// 物化集合并校验：非 null、非空集合、不含 null 元素，返回物化后的列表。
        /// <para>用于需要先物化延迟执行的 IEnumerable 再校验的调用方，避免多次枚举；传入的已是 List 时直接复用，不复制。</para>
        /// </summary>
        /// <typeparam name="T">元素类型（引用类型）。</typeparam>
        /// <param name="items">集合参数。</param>
        /// <param name="paramName">参数名（通常传 <c>nameof(...)</c>）。</param>
        /// <returns>物化后的列表。</returns>
        internal static List<T> ToValidatedList<T>(IEnumerable<T> items, string paramName) where T : class
        {
            ThrowIfNull(items, paramName);

            // 已是 List 时直接复用不复制；先物化再校验，避免延迟集合被多次枚举
            var list = items as List<T> ?? items.ToList();
            if (list.Count == 0)
                throw new ArgumentException($"参数 '{paramName}' 不能为空集合。", paramName);

            if (list.Any(x => x == null))
                throw new ArgumentException($"参数 '{paramName}' 不能包含 null 元素。", paramName);

            return list;
        }
    }
}
