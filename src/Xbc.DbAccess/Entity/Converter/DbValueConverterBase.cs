namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 数据库值转换器基类，提供默认的类型转换实现，自定义转换器可继承此类并重写转换方法。
    /// </summary>
    /// <typeparam name="T">目标属性类型。</typeparam>
    public abstract class DbValueConverterBase<T> : IDbValueConverter<T>
    {
        /// <summary>
        /// 将数据库值转换为目标类型。
        /// </summary>
        /// <param name="dbValue">数据库值。</param>
        /// <returns>转换后的属性值。</returns>
        public virtual T ConvertFrom(object dbValue)
        {
            return (T)EntityValueConverter.ConvertFrom(dbValue, typeof(T));
        }

        /// <summary>
        /// 将属性值转换为数据库值。
        /// </summary>
        /// <param name="value">属性值。</param>
        /// <returns>数据库值。</returns>
        public virtual object ConvertTo(T value)
        {
            return EntityValueConverter.ConvertTo(value);
        }
    }
}
