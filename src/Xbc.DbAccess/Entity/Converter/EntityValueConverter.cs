using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 实体列值转换器静态类，提供实体属性值与数据库值之间的通用转换功能（框架实体读写/聚合路径内部使用）。
    /// <para>两个方向策略相反：<see cref="ConvertFrom"/>（数据库值 → 目标类型）有损映射<b>显式失败</b>，值已离开数据库、
    /// 无处保持原值；<see cref="ConvertTo(object, Type)"/>（属性值 → 目标列类型）有损/超范围<b>保持原值</b>交存储层裁决，
    /// 值尚未进入存储，int 列 = 3.7 自然不命中、超范围报错语义不变。
    /// <see cref="ConvertTo(object)"/> 为无损表示归一（枚举转底层整数、null 转 DBNull、其余直通）。
    /// 通用转换引擎见 <see cref="DbValueConvert.ChangeType(object, Type)"/>（无防护）；手写 SQL 标量结果的类型归一请直接使用它。</para>
    /// </summary>
    internal static class EntityValueConverter
    {
        /// <summary>
        /// 将数据库值转换为目标类型（<see cref="ConvertFrom(object, Type)"/> 的类型化便捷入口，守卫策略一致）。
        /// </summary>
        /// <typeparam name="T">目标类型。</typeparam>
        /// <param name="dbValue">数据库值。</param>
        /// <returns>转换后的属性值。</returns>
        public static T ConvertFrom<T>(object dbValue)
        {
            return (T)ConvertFrom(dbValue, typeof(T));
        }

        /// <summary>
        /// 将数据库值转换为目标类型。
        /// <para>有损舍入防护（与直读路径、谓词路径共用 <see cref="NumericValueGuard"/> 的统一判定）：小数源
        ///（double/float/decimal）映射到整数族/char 目标且值带非零小数部分时，<see cref="DbValueConvert.ChangeType(object, Type)"/>
        /// 会静默四舍五入（3.7 → 4）产生错数据。读取路径的值已离开数据库、无处保持原值，显式失败优于静默错数据。</para>
        /// </summary>
        /// <param name="dbValue">数据库值。</param>
        /// <param name="targetType">目标类型。</param>
        /// <returns>转换后的属性值。</returns>
        public static object ConvertFrom(object dbValue, Type targetType)
        {
            var runtimeType = dbValue.GetType();
            var underlyingTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

            // 有损舍入守卫（读取侧策略：显式失败；谓词侧对称地保持原值，见 ConvertTo(object, Type)）
            if (NumericValueGuard.IsLossyFractionalConversion(dbValue, runtimeType, underlyingTarget))
                throw new InvalidOperationException($"数据库返回值 '{dbValue}'（{runtimeType}）带非零小数部分，无法无损映射到目标类型 '{underlyingTarget}'，如需取整请在 SQL 中显式 CAST。");

            return DbValueConvert.ChangeType(dbValue, targetType);
        }

        /// <summary>
        /// 将属性值无损表示归一为数据库值：枚举转底层整数、null 转 <see cref="DBNull"/>、其余直通。
        /// <para>带目标列类型的对齐归一见重载 <see cref="ConvertTo(object, Type)"/>。</para>
        /// </summary>
        /// <param name="value">属性值。</param>
        /// <returns>数据库值。</returns>
        public static object ConvertTo(object value)
        {
            // 处理可空类型的 null 值（可空类型装箱后：无值时即为 null 引用，有值时为底层类型的装箱实例，不保留 Nullable 包装）
            if (value == null)
                return DBNull.Value;

            // 对于枚举类型，转换为底层整数类型
            var valueType = value.GetType();
            if (valueType.IsEnum)
                return Convert.ChangeType(value, Enum.GetUnderlyingType(valueType));

            // 对于其他类型，直接返回（假设可以直接存入数据库）
            // 如果需要复杂转换，应该使用自定义的 IDbValueConverter 实现
            return value;
        }

        /// <summary>
        /// 将属性值先对齐到目标列类型、再转换为数据库值（写方向守卫原语，与 <see cref="ConvertFrom"/> 策略相反）。
        /// <para>归一仅限无损形态：有损舍入与窄化超范围均保持原值，交数据库/驱动按原值裁决（判定集中于 <see cref="NumericValueGuard"/>）。</para>
        /// </summary>
        /// <param name="value">属性值（null 归一为 <see cref="DBNull"/>）。</param>
        /// <param name="targetType">目标列的 .NET 值类型（可空类型自动解包为基础类型）。</param>
        /// <returns>数据库值。</returns>
        public static object ConvertTo(object value, Type targetType)
        {
            if (value == null)
                return DBNull.Value;

            var runtimeType = value.GetType();
            var underlyingTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (runtimeType != underlyingTarget && runtimeType != typeof(object) && underlyingTarget != typeof(object))
            {
                // 数值族互转、数值 → char（includeNullable: false，装箱值的运行时类型不可能是 Nullable）。
                // 归一对象是表达式求值的类型提升值：如 char 列 == 77（'M' 提升为 int，不归一则字符列静默不命中）、int 列 == 3.7（提升为 double）
                if (TypeHelper.IsNumeric(runtimeType, includeNullable: false)
                    && (TypeHelper.IsNumeric(underlyingTarget, includeNullable: false) || underlyingTarget == typeof(char)))
                {
                    // 有损舍入防护：保持原值交存储层隐式比较，与手写 SQL 语义一致
                    if (NumericValueGuard.IsLossyFractionalConversion(value, runtimeType, underlyingTarget))
                        return value;

                    // 域内对齐转换本体：把提升后的运行时值对齐回目标列类型（int 闭包值 → short 列、77 → char、3.0 → int）。
                    // 能到达此处的小数源必为整数值（非零小数已被上方守卫拦截），整数窄化超范围经 OverflowException
                    // 兜底，与有损舍入同一策略：保持原值交存储层
                    try
                    {
                        return Convert.ChangeType(value, underlyingTarget);
                    }
                    catch (OverflowException)
                    {
                        // 窄化超范围：保持原值，报错语义交还存储层
                        return value;
                    }
                }

                // 弱类型集合（List<object> { "M" } 等）经非泛型 Contains 路径带入的 string 值给 char 列
                //（codes.Contains(u.Code)）：单字符字符串归一为 char
                if (underlyingTarget == typeof(char) && runtimeType == typeof(string))
                {
                    var text = (string)value;
                    if (text.Length == 1)
                        return text[0];
                }
            }

            // 类型对齐或归一范围外：无损表示归一（枚举转底层整数、其余直通）
            return ConvertTo(value);
        }
    }
}
