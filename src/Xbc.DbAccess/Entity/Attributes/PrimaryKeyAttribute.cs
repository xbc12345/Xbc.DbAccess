using System.Data;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 标记属性为主键列。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class PrimaryKeyAttribute : Attribute
    {
        /// <summary>
        /// 列名，未指定时默认使用属性名。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 是否为标识列（自增列），默认为 false。
        /// </summary>
        public bool IsIdentity { get; set; }

        /// <summary>
        /// 主键列的数据库类型。
        /// <para>string/Guid 主键可借此指定列类型（如 <see cref="DbType.AnsiString"/>，匹配 varchar 列避免隐式转换），
        /// 用于写路径（插入/更新/删除/查找）生成数据库参数时指定参数的数据库类型；查询映射按 .NET 类型推断，不受此配置影响。</para>
        /// <para><b>取值约束：仅接受 <see cref="System.Data.DbType"/> 枚举值。</b>
        /// null（默认）表示未指定，按 .NET 类型推断；赋值必须为枚举成员，赋其他类型（如字符串字面量 <c>"AnsiString"</c>、
        /// 整数常量）会在构建元数据时抛 <see cref="InvalidOperationException"/>。</para>
        /// <para>声明为 object 而非枚举：特性实参类型不支持可空枚举（CS0655，声明为 <c>DbType?</c> 后任何
        /// <c>[PrimaryKey(DbType = ...)]</c> 使用处都编译失败），而非可空枚举的默认值 0 恰为
        /// <see cref="DbType.AnsiString"/>、会让未配置的主键默认 AnsiString，object + null 是保留枚举书写语法的唯一方案。</para>
        /// </summary>
        public object DbType { get; set; }

        /// <summary>
        /// 初始化 <see cref="PrimaryKeyAttribute"/> 类的新实例。
        /// </summary>
        public PrimaryKeyAttribute()
        {
        }

        /// <summary>
        /// 初始化 <see cref="PrimaryKeyAttribute"/> 类的新实例，并指定列名。
        /// </summary>
        /// <param name="name">列名。</param>
        public PrimaryKeyAttribute(string name)
        {
            Guard.ThrowIfNullOrWhiteSpace(name, nameof(name));
            Name = name;
        }
    }
}
