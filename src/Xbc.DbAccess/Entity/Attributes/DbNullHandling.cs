namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 指定当数据库值为 NULL 时的处理方式。
    /// </summary>
    public enum DbNullHandling
    {
        /// <summary>
        /// 默认行为，根据属性类型自动选择处理方式。
        /// <para>非可空值类型：不检查 NULL 直接读取，若数据库值为 NULL 由 ADO.NET 提供程序抛出原生异常（与 Dapper、EF Core 行为一致）。
        /// 例外：枚举、<see cref="DateTimeOffset"/>、<see cref="TimeSpan"/> 等无专用提供程序读取方法的类型经框架转换层
        /// （EntityValueConverter）读取，NULL 时抛出框架的 <see cref="System.InvalidCastException"/>；
        /// 带 <c>[Column(ConverterType)]</c> 转换器的列会把 DBNull 递给转换器的 ConvertFrom。</para>
        /// <para>可空类型与引用类型：跳过赋值（保持属性为初始值）。需要非可空列容忍 NULL 并映射为 default(T) 时，显式声明 <see cref="UseDefaultValue"/>。</para>
        /// </summary>
        Default = 0,

        /// <summary>
        /// 不管属性类型是否为可空类型，都抛出异常。
        /// </summary>
        ThrowException = 1,

        /// <summary>
        /// 使用类型的默认值，即 default(T)。
        /// <para>需显式声明：默认行为已不包含此语义（见 <see cref="Default"/>）。
        /// 性能提示：相比默认行为，会在每列读取前增加一次 IsDBNull 检查，批量查询场景有约百分之十几到二十的开销。</para>
        /// </summary>
        UseDefaultValue = 2,

        /// <summary>
        /// 跳过赋值，保持属性为初始值。
        /// </summary>
        SkipAssignment = 3,

        /// <summary>
        /// 不检查 NULL，直接读取值；若数据库值为 NULL，由 ADO.NET 提供程序抛出原生异常
        ///（例外同 <see cref="Default"/>：经框架转换层读取的类型抛框架的 InvalidCastException，带转换器的列把 DBNull 递给转换器）。
        /// <para>仅对非可空值类型列生效，省去每次读取的 IsDBNull 检查开销，适用于带有 NOT NULL 约束的列；
        /// 非可空值类型列的默认行为与此相同，无需显式声明。可空类型与引用类型列无法跳过检查（NULL 是合法值），退化为 <see cref="SkipAssignment"/> 行为。</para>
        /// </summary>
        ProviderException = 4,
    }
}
