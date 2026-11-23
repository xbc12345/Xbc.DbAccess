using System.Data;
using System.Globalization;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 为 <see cref="DataRow"/> 提供按列名获取值的扩展方法。
    /// </summary>
    public static class DataRowExtensions
    {
        /// <summary>
        /// 获取指定列的值并转为 <see cref="string"/> 类型。
        /// </summary>
        /// <param name="row">数据行。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static string GetString(this DataRow row, string columnName)
        {
            Guard.ThrowIfNull(row, nameof(row));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var value = row[columnName];
            return value == DBNull.Value ? null : value.ToString();
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="bool"/> 类型。
        /// </summary>
        /// <param name="row">数据行。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static bool? GetBoolean(this DataRow row, string columnName)
        {
            Guard.ThrowIfNull(row, nameof(row));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var value = row[columnName];
            if (value == DBNull.Value)
                return null;

            return Convert.ToBoolean(value);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="byte"/> 类型。
        /// </summary>
        /// <param name="row">数据行。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static byte? GetByte(this DataRow row, string columnName)
        {
            Guard.ThrowIfNull(row, nameof(row));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var value = row[columnName];
            if (value == DBNull.Value)
                return null;

            return Convert.ToByte(value);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="short"/> 类型。
        /// </summary>
        /// <param name="row">数据行。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static short? GetInt16(this DataRow row, string columnName)
        {
            Guard.ThrowIfNull(row, nameof(row));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var value = row[columnName];
            if (value == DBNull.Value)
                return null;

            return Convert.ToInt16(value);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="int"/> 类型。
        /// </summary>
        /// <param name="row">数据行。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static int? GetInt32(this DataRow row, string columnName)
        {
            Guard.ThrowIfNull(row, nameof(row));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var value = row[columnName];
            if (value == DBNull.Value)
                return null;

            return Convert.ToInt32(value);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="long"/> 类型。
        /// </summary>
        /// <param name="row">数据行。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static long? GetInt64(this DataRow row, string columnName)
        {
            Guard.ThrowIfNull(row, nameof(row));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var value = row[columnName];
            if (value == DBNull.Value)
                return null;

            return Convert.ToInt64(value);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="float"/> 类型。
        /// </summary>
        /// <param name="row">数据行。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static float? GetFloat(this DataRow row, string columnName)
        {
            Guard.ThrowIfNull(row, nameof(row));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var value = row[columnName];
            if (value == DBNull.Value)
                return null;

            return Convert.ToSingle(value);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="double"/> 类型。
        /// </summary>
        /// <param name="row">数据行。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static double? GetDouble(this DataRow row, string columnName)
        {
            Guard.ThrowIfNull(row, nameof(row));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var value = row[columnName];
            if (value == DBNull.Value)
                return null;

            return Convert.ToDouble(value);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="decimal"/> 类型。
        /// </summary>
        /// <param name="row">数据行。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static decimal? GetDecimal(this DataRow row, string columnName)
        {
            Guard.ThrowIfNull(row, nameof(row));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var value = row[columnName];
            if (value == DBNull.Value)
                return null;

            return Convert.ToDecimal(value);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="Guid"/> 类型。
        /// </summary>
        /// <param name="row">数据行。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static Guid? GetGuid(this DataRow row, string columnName)
        {
            Guard.ThrowIfNull(row, nameof(row));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var value = row[columnName];
            if (value == DBNull.Value)
                return null;

            // 值已是 Guid 时直接返回，避免绕字符串解析
            return value is Guid guid ? guid : Guid.Parse(value.ToString());
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="DateTime"/> 类型。
        /// </summary>
        /// <param name="row">数据行。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static DateTime? GetDateTime(this DataRow row, string columnName)
        {
            return GetDateTime(row, columnName, null, null);
        }

        /// <summary>
        /// 获取指定列的值并使用指定格式转为 <see cref="DateTime"/> 类型。
        /// </summary>
        /// <param name="row">数据行。</param>
        /// <param name="columnName">列名。</param>
        /// <param name="format">日期时间格式字符串。</param>
        /// <param name="formatProvider">格式提供程序。</param>
        /// <returns></returns>
        public static DateTime? GetDateTime(this DataRow row, string columnName, string format, IFormatProvider formatProvider = null)
        {
            Guard.ThrowIfNull(row, nameof(row));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var value = row[columnName];
            if (value == DBNull.Value)
                return null;

            if (string.IsNullOrEmpty(format))
                return DateTime.Parse(value.ToString(), CultureInfo.InvariantCulture);// 数据库字符串值区域无关，使用固定文化解析
            else
                return DateTime.ParseExact(value.ToString(), format, formatProvider ?? CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="DateTimeOffset"/> 类型。
        /// </summary>
        /// <param name="row">数据行。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static DateTimeOffset? GetDateTimeOffset(this DataRow row, string columnName)
        {
            return GetDateTimeOffset(row, columnName, null, null);
        }

        /// <summary>
        /// 获取指定列的值并使用指定格式转为 <see cref="DateTimeOffset"/> 类型。
        /// </summary>
        /// <param name="row">数据行。</param>
        /// <param name="columnName">列名。</param>
        /// <param name="format">日期时间偏移格式字符串。</param>
        /// <param name="formatProvider">格式提供程序。</param>
        /// <returns></returns>
        public static DateTimeOffset? GetDateTimeOffset(this DataRow row, string columnName, string format, IFormatProvider formatProvider = null)
        {
            Guard.ThrowIfNull(row, nameof(row));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var value = row[columnName];
            if (value == DBNull.Value)
                return null;

            if (string.IsNullOrEmpty(format))
                return DateTimeOffset.Parse(value.ToString(), CultureInfo.InvariantCulture);// 数据库字符串值区域无关，使用固定文化解析
            else
                return DateTimeOffset.ParseExact(value.ToString(), format, formatProvider ?? CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// 获取指定列的值并转为 <see cref="TimeSpan"/> 类型。
        /// </summary>
        /// <param name="row">数据行。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        public static TimeSpan? GetTimeSpan(this DataRow row, string columnName)
        {
            return GetTimeSpan(row, columnName, null, null);
        }

        /// <summary>
        /// 获取指定列的值并使用指定格式转为 <see cref="TimeSpan"/> 类型。
        /// </summary>
        /// <param name="row">数据行。</param>
        /// <param name="columnName">列名。</param>
        /// <param name="format">时间跨度格式字符串。</param>
        /// <param name="formatProvider">格式提供程序。</param>
        /// <returns></returns>
        public static TimeSpan? GetTimeSpan(this DataRow row, string columnName, string format, IFormatProvider formatProvider = null)
        {
            Guard.ThrowIfNull(row, nameof(row));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var value = row[columnName];
            if (value == DBNull.Value)
                return null;

            if (string.IsNullOrEmpty(format))
                return TimeSpan.Parse(value.ToString(), CultureInfo.InvariantCulture);// 数据库字符串值区域无关，使用固定文化解析
            else
                return TimeSpan.ParseExact(value.ToString(), format, formatProvider ?? CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// 获取指定列的值并转为指定类型。
        /// </summary>
        /// <typeparam name="T">值的类型。</typeparam>
        /// <param name="row">数据行。</param>
        /// <param name="columnName">列名。</param>
        /// <returns></returns>
        /// <exception cref="InvalidCastException"></exception>
        public static T GetFieldValue<T>(this DataRow row, string columnName)
        {
            Guard.ThrowIfNull(row, nameof(row));
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));

            var returnType = typeof(T);
            var value = row[columnName];

            if (value == DBNull.Value)
            {
                if (Nullable.GetUnderlyingType(returnType) != null)// 可空类型
                    return default;
                else
                    throw new InvalidCastException($"无法将列 '{columnName}' 的 DBNull.Value 转换为 '{returnType}' 类型。");
            }

            // 值转换统一走 DbValueConvert，与其他值转换路径（实体映射、ExecuteScalar<T>）语义一致
            return (T)DbValueConvert.ChangeType(value, returnType);
        }
    }
}
