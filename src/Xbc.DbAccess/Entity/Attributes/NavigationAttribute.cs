namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 导航属性特性，用于标记实体间的关联关系。
    /// <para>1:N 集合：属性类型为 <see cref="List{T}"/>，或可接受 <see cref="List{T}"/> 赋值的泛型接口（如 <see cref="IList{T}"/>、<see cref="ICollection{T}"/>），
    /// 本方持有多个对方实体（如 <c>Customer.Orders</c>）；约定推断下 ThisKey 为本方主键，OtherKey 为对方实体中名为 "{本方类型名}Id" 的外键。</para>
    /// <para>1:1 引用：属性类型为单个实体类，本方持有一个对方实体引用（如 <c>Order.Customer</c>）；
    /// 约定推断下 ThisKey 为本方中名为 "{属性名}Id" 的外键，OtherKey 为对方主键。不支持多对多（ManyToMany）；
    /// 约定不适用时请手动指定 <see cref="ThisKey"/> 和 <see cref="OtherKey"/>。</para>
    /// <para>配置陷阱：1:1 引用的 ThisKey 应为本方外键列，若误配到本方主键，写路径的外键同步会把引用实体的
    /// OtherKey 键值回填到本方主键，破坏主键语义。ThisKey 命中本方主键的守卫仅覆盖约定推断路径（拦截自引用等
    /// 意外误配，构建时抛出异常）；手动显式指定视为共享主键 1:1 模式（主键即外键，如 OrderDetail.OrderId）放行，
    /// 写路径的外键同步正是该模式的期望行为。</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class NavigationAttribute : Attribute
    {
        /// <summary>
        /// 本方实体中的键属性名。为空时按约定推断。
        /// <para>1:N 集合时为本方主键（如 Customer.Id），1:1 引用时为本方外键（如 Order.CustomerId）。</para>
        /// </summary>
        public string ThisKey { get; set; }

        /// <summary>
        /// 对方实体中的键属性名。为空时按约定推断。
        /// <para>1:N 集合时为对方外键（如 Order.CustomerId），1:1 引用时为对方主键（如 Customer.Id）。</para>
        /// </summary>
        public string OtherKey { get; set; }
    }
}
