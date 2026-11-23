namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 标记属性不参与数据库映射，忽略该属性对应的列。
    /// <para>不能与 <see cref="ColumnAttribute"/> 或 <see cref="PrimaryKeyAttribute"/> 同时标记，否则抛出 <see cref="InvalidOperationException"/>。</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class NotMappedAttribute : Attribute
    {
    }
}
