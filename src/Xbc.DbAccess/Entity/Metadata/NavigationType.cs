namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 导航属性关系类型，从声明方（本方）实体的视角描述属性形态：
    /// 持有对方单个实体引用为 <see cref="OneToOne"/>，持有对方实体集合为 <see cref="OneToMany"/>。
    /// </summary>
    internal enum NavigationType
    {
        /// <summary>
        /// 一对一引用：属性类型为单个实体类，本方持有一个对方实体的引用。
        /// <para>键映射：ThisKey 为本方外键，OtherKey 为对方主键。
        /// 如 <c>Order.Customer</c>，Order 的 CustomerId 指向 Customer 主键。</para>
        /// </summary>
        OneToOne,

        /// <summary>
        /// 一对多集合：属性类型为实体集合，本方持有多个对方实体。
        /// <para>键映射：ThisKey 为本方主键，OtherKey 为对方外键。
        /// 如 <c>Customer.Orders</c>，Order 的 CustomerId 指向 Customer 主键。</para>
        /// <para>不支持多对多：如需表达，请通过中间实体加两个 1:N 导航属性显式建模。</para>
        /// </summary>
        OneToMany,
    }
}
