using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 数值有损转换防护的共用判定，谓词参数化（<see cref="ColumnMetadata.ConvertToDb"/>）、读取直收行内守卫
    ///（映射委托构建期分流后编进委托）与兜底转换（<see cref="EntityValueConverter.ConvertFrom"/>）三处共用。
    /// <para>防护对象：小数源（double/float/decimal）带非零小数部分映射到整数族/char 目标（3.7 → 4 的静默舍入）、
    /// 整数窄化超范围；在范围内的窄化与整数值的小数源（3.0 → 3）为无损，正常映射。</para>
    /// <para>谓词侧判定有损时保持原值交数据库隐式比较（与手写 SQL 语义一致）；读取侧显式失败，抛带值与双类型的异常。</para>
    /// </summary>
    internal static class NumericValueGuard
    {
        /// <summary>
        /// 判断数值映射是否为"带非零小数部分的浮点/decimal 值 → 整数族（或 char）目标"的有损舍入，仅此时返回 true。
        /// <para>谓词侧（<see cref="ColumnMetadata.ConvertToDb"/>）此时保持原值交数据库隐式比较；读取侧（<see cref="EntityValueConverter.ConvertFrom"/>）显式抛异常。</para>
        /// </summary>
        internal static bool IsLossyFractionalConversion(object value, Type runtimeType, Type targetType)
        {
            bool targetIntegralLike = TypeHelper.IsInteger(targetType, includeNullable: false) || targetType == typeof(char);
            if (!targetIntegralLike)
                return false;

            if (runtimeType != typeof(decimal) && runtimeType != typeof(double) && runtimeType != typeof(float))
                return false;

            return HasNonZeroFraction(value);
        }

        /// <summary>
        /// 行内校验并转换数值读取值（编进委托，每行一次调用、无装箱）：无损则转换；
        /// 小数源到整数目标校验小数部分为零，窄化经 <see cref="Convert.ChangeType(object, Type)"/> 的 checked 语义拦截超范围值。
        /// </summary>
        /// <typeparam name="TSource">字段读取类型（getter 返回类型）。</typeparam>
        /// <typeparam name="TTarget">属性基础类型（构建期分流保证为整数族）。</typeparam>
        /// <param name="value">读取值。</param>
        /// <returns>转换后的属性基础类型值。</returns>
        /// <exception cref="InvalidOperationException">值带非零小数部分，或窄化超出目标类型可表示范围。</exception>
        internal static TTarget ConvertOrThrow<TSource, TTarget>(TSource value)
            where TSource : struct
            where TTarget : struct
        {
            // 小数源 → 整数目标（构建期分流保证整数目标才走守卫）：小数部分非零即有损
            if (HasNonZeroFraction(value))
                throw new InvalidOperationException($"数据库返回值 '{value}'（{typeof(TSource)}）带非零小数部分，无法无损映射到目标类型 '{typeof(TTarget)}'，如需取整请在 SQL 中显式 CAST。");

            // ChangeType 为 checked 转换，超范围抛 OverflowException，包装上下文重新抛出
            try
            {
                return (TTarget)Convert.ChangeType(value, typeof(TTarget));
            }
            catch (OverflowException ex)
            {
                throw new InvalidOperationException($"数据库返回值 '{value}' 超出目标类型 '{typeof(TTarget)}' 的可表示范围。", ex);
            }
        }

        /// <summary>
        /// 判断装箱数值是否带非零小数部分（装箱兜底路径用）。
        /// </summary>
        internal static bool HasNonZeroFraction(object value)
        {
            if (value is double d)
                return d != Math.Truncate(d);

            if (value is float f)
                return f != Math.Truncate(f);

            if (value is decimal m)
                return m != Math.Truncate(m);

            return false;
        }

        /// <summary>
        /// 判断泛型数值是否带非零小数部分（读取行内热路径用，编进委托无装箱，整数源恒为 false）。
        /// </summary>
        private static bool HasNonZeroFraction<TSource>(TSource value)
            where TSource : struct
        {
            if (typeof(TSource) == typeof(double))
            {
                var d = (double)(object)value;
                return d != Math.Truncate(d);
            }

            if (typeof(TSource) == typeof(float))
            {
                var f = (float)(object)value;
                return f != Math.Truncate(f);
            }

            if (typeof(TSource) == typeof(decimal))
            {
                var m = (decimal)(object)value;
                return m != Math.Truncate(m);
            }

            return false;
        }
    }
}
