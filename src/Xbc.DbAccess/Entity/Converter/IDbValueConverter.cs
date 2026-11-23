namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 数据库值转换器接口，用于自定义数据库值与实体属性类型之间的双向转换。
    /// <para>注意：通过 [Column(ConverterType)] 指定为列转换器时，实现必须具有无参构造函数（构建实体元数据时校验）。</para>
    /// </summary>
    /// <typeparam name="T">目标属性类型。</typeparam>
    public interface IDbValueConverter<T>
    {
        /// <summary>
        /// 将数据库值转换为指定属性类型 <typeparamref name="T"/> 的值（读取时）。
        /// </summary>
        /// <param name="dbValue">数据库值。非可空值类型列声明 <see cref="DbNullHandling.Default"/>/
        /// <see cref="DbNullHandling.ProviderException"/>（不检查 NULL）时，NULL 会以 <see cref="DBNull.Value"/>
        /// 到达，实现需自行处理（返回默认值或抛出带上下文的异常）。</param>
        /// <returns>转换后的属性值。</returns>
        T ConvertFrom(object dbValue);

        /// <summary>
        /// 将指定属性类型 <typeparamref name="T"/> 的值转换为数据库值（写入时）。
        /// </summary>
        /// <param name="value">属性值。</param>
        /// <returns>转换后的数据库值。</returns>
        object ConvertTo(T value);
    }
}
