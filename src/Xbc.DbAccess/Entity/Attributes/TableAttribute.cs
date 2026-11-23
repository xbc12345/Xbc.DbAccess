namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 表特性，用于指定实体类对应的数据库表名和架构名。
    /// <para>若未标记此特性，默认使用实体类名的复数形式作为表名。</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class TableAttribute : Attribute
    {
        /// <summary>
        /// 表名。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 架构名。
        /// </summary>
        public string Schema { get; set; }

        /// <summary>
        /// 初始化表特性，并指定表名。
        /// </summary>
        /// <param name="name">表名。</param>
        public TableAttribute(string name)
        {
            Guard.ThrowIfNullOrWhiteSpace(name, nameof(name));
            Name = name;
        }
    }
}
