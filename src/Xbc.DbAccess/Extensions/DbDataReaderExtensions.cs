using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 为 <see cref="DbDataReader"/> 提供按列名获取值的扩展方法。
    /// </summary>
    public static class DbDataReaderExtensions
    {
        /// <summary>
        /// 检查指定列的值是否为 <see cref="DBNull.Value"/>。
        /// </summary>
        /// <param name="reader">数据读取器。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static bool IsDBNull(this DbDataReader reader, string columnName)
        {
            Guard.ThrowIfNull(reader, nameof(reader));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="string"/> 类型。
        /// </summary>
        /// <param name="reader">数据读取器。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static string GetString(this DbDataReader reader, string columnName)
        {
            Guard.ThrowIfNull(reader, nameof(reader));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
                return null;

            return reader.GetString(ordinal);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="bool"/> 类型。
        /// </summary>
        /// <param name="reader">数据读取器。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static bool? GetBoolean(this DbDataReader reader, string columnName)
        {
            Guard.ThrowIfNull(reader, nameof(reader));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
                return null;

            return reader.GetBoolean(ordinal);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="byte"/> 类型。
        /// </summary>
        /// <param name="reader">数据读取器。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static byte? GetByte(this DbDataReader reader, string columnName)
        {
            Guard.ThrowIfNull(reader, nameof(reader));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
                return null;

            return reader.GetByte(ordinal);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="short"/> 类型。
        /// </summary>
        /// <param name="reader">数据读取器。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static short? GetInt16(this DbDataReader reader, string columnName)
        {
            Guard.ThrowIfNull(reader, nameof(reader));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
                return null;

            return reader.GetInt16(ordinal);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="int"/> 类型。
        /// </summary>
        /// <param name="reader">数据读取器。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static int? GetInt32(this DbDataReader reader, string columnName)
        {
            Guard.ThrowIfNull(reader, nameof(reader));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
                return null;

            return reader.GetInt32(ordinal);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="long"/> 类型。
        /// </summary>
        /// <param name="reader">数据读取器。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static long? GetInt64(this DbDataReader reader, string columnName)
        {
            Guard.ThrowIfNull(reader, nameof(reader));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
                return null;

            return reader.GetInt64(ordinal);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="float"/> 类型。
        /// </summary>
        /// <param name="reader">数据读取器。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static float? GetFloat(this DbDataReader reader, string columnName)
        {
            Guard.ThrowIfNull(reader, nameof(reader));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
                return null;

            return reader.GetFloat(ordinal);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="double"/> 类型。
        /// </summary>
        /// <param name="reader">数据读取器。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static double? GetDouble(this DbDataReader reader, string columnName)
        {
            Guard.ThrowIfNull(reader, nameof(reader));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
                return null;

            return reader.GetDouble(ordinal);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="decimal"/> 类型。
        /// </summary>
        /// <param name="reader">数据读取器。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static decimal? GetDecimal(this DbDataReader reader, string columnName)
        {
            Guard.ThrowIfNull(reader, nameof(reader));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
                return null;

            return reader.GetDecimal(ordinal);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="Guid"/> 类型。
        /// </summary>
        /// <param name="reader">数据读取器。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static Guid? GetGuid(this DbDataReader reader, string columnName)
        {
            Guard.ThrowIfNull(reader, nameof(reader));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
                return null;

            return reader.GetGuid(ordinal);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="DateTime"/> 类型。
        /// </summary>
        /// <param name="reader">数据读取器。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static DateTime? GetDateTime(this DbDataReader reader, string columnName)
        {
            Guard.ThrowIfNull(reader, nameof(reader));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
                return null;

            return reader.GetDateTime(ordinal);
        }

        /// <summary>
        /// 获取指定列的值并转为指定类型。
        /// <para>DBNull 语义与 <see cref="GetString"/> 等同族方法一致：可空/引用类型返回 null，非可空值类型抛异常。</para>
        /// </summary>
        /// <typeparam name="T">值的类型。</typeparam>
        /// <param name="reader">数据读取器。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static T GetFieldValue<T>(this DbDataReader reader, string columnName)
        {
            Guard.ThrowIfNull(reader, nameof(reader));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
            {
                var returnType = typeof(T);
                if (Nullable.GetUnderlyingType(returnType) != null || !returnType.IsValueType)
                    return default;

                throw new InvalidCastException($"无法将列 '{columnName}' 的 DBNull.Value 转换为 '{returnType}' 类型。");
            }

            return reader.GetFieldValue<T>(ordinal);
        }

        /// <summary>
        /// 获取指定列的值。
        /// </summary>
        /// <param name="reader">数据读取器。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static object GetValue(this DbDataReader reader, string columnName)
        {
            Guard.ThrowIfNull(reader, nameof(reader));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var ordinal = reader.GetOrdinal(columnName);
            return reader.GetValue(ordinal);
        }
    }
}
