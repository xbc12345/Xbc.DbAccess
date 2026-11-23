using System.Data;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 列特性，用于指定属性对应的数据库列名、类型转换、DBNull 处理等映射配置。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class ColumnAttribute : Attribute
    {
        /// <summary>
        /// 列名。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 是否为标识列（自增列），默认为 false。
        /// </summary>
        public bool IsIdentity { get; set; }

        /// <summary>
        /// 是否为乐观并发令牌列，默认为 false。
        /// <para>令牌列的 CLR 类型必须为 <see cref="byte"/>[]（映射 varbinary(16)/BLOB 等二进制列），
        /// 且不能与 <see cref="IsIdentity"/> 同时使用；每张表至多一个令牌列，重复声明在构建元数据时抛出异常。</para>
        /// <para>启用后：插入时由框架生成初始 Guid 字节；实体版更新/删除在 WHERE 中附带令牌等值条件做乐观并发校验，
        /// 更新成功后自动生成并回填新令牌；令牌不匹配或目标行不存在时抛出并发冲突异常（详见 <see cref="DbConcurrencyException"/>）；
        /// 条件更新/删除（谓词版）与 Upsert 不做令牌校验。</para>
        /// </summary>
        public bool IsConcurrencyToken { get; set; }

        /// <summary>
        /// 当数据库值为 DBNull 时的处理方式，默认为 <see cref="DbNullHandling.Default"/>。
        /// </summary>
        public DbNullHandling DbNullHandling { get; set; } = DbNullHandling.Default;

        /// <summary>
        /// 自定义数据库值转换器类型。
        /// 转换器类型须定义无参构造函数、ConvertFrom(object) 和 ConvertTo(T) 方法用于双向转换，
        /// 按方法签名匹配，不强制要求实现 <see cref="IDbValueConverter{T}"/> 接口。
        /// </summary>
        public Type ConverterType { get; set; }

        /// <summary>
        /// 数据库列类型，用于指定属性对应的数据库列的实际类型。
        /// <para><b>取值约束：仅接受 <see cref="System.Data.DbType"/> 枚举值。</b>
        /// null（默认）表示未指定，框架按 .NET 类型自动推断；赋值必须为枚举成员（如 <c>DbType.String</c>），
        /// 赋其他类型（如字符串字面量 <c>"String"</c>、整数常量）会在构建元数据时抛 <see cref="InvalidOperationException"/>。</para>
        /// <para>声明为 object 而非枚举：特性实参类型不支持可空枚举（CS0655，声明为 <c>DbType?</c> 后任何
        /// <c>[Column(DbType = ...)]</c> 使用处都编译失败），而非可空枚举的默认值 0 恰为
        /// <see cref="DbType.AnsiString"/>、会让所有未配置列默认 AnsiString，object + null 是保留枚举书写语法的唯一方案。</para>
        /// </summary>
        public object DbType { get; set; }

        /// <summary>
        /// 枚举列是否以名称字符串存储，默认为 false（存底层整数）。
        /// <para>为 true 时写入 <see cref="Enum.ToString()"/> 的名称字符串，读取按名称解析（忽略大小写）；
        /// 仅枚举类型列（含可空枚举）可启用，不能与 <see cref="IsJson"/> 或 <see cref="ConverterType"/> 同时使用。</para>
        /// </summary>
        public bool EnumAsString { get; set; }

        /// <summary>
        /// 列是否以 JSON 文本存储复杂类型属性（对象、集合等）。
        /// <para>为 true 时写入 System.Text.Json 序列化的字符串，读取按属性类型反序列化；
        /// 列的数据库类型应为文本类型（如 TEXT/NVARCHAR），不能与 <see cref="EnumAsString"/> 或 <see cref="ConverterType"/> 同时使用，
        /// 也不支持参与 WHERE 条件。</para>
        /// </summary>
        public bool IsJson { get; set; }

        /// <summary>
        /// 初始化列特性的默认实例。
        /// </summary>
        public ColumnAttribute()
        {
        }

        /// <summary>
        /// 初始化列特性，并指定列名。
        /// </summary>
        /// <param name="name">列名。</param>
        public ColumnAttribute(string name)
        {
            Guard.ThrowIfNullOrWhiteSpace(name, nameof(name));
            Name = name;
        }
    }
}
