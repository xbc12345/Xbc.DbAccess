using System.Linq.Expressions;
using System.Reflection;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 导航属性元数据，描述实体间的关联关系，包括导航类型、对方实体、键映射等信息。
    /// </summary>
    internal sealed class NavigationMetadata
    {
        private readonly PropertyInfo propertyInfo;
        private readonly Func<object, object> getter;// 编译的属性读取委托，替代反射 GetValue
        private readonly Action<object, object> setter;// 编译的属性写入委托，替代反射 SetValue

        /// <summary>
        /// 导航属性名。
        /// </summary>
        public string PropertyName => propertyInfo.Name;

        /// <summary>
        /// 导航属性关系类型。
        /// </summary>
        public NavigationType NavigationType { get; private set; }

        /// <summary>
        /// 对方实体类型。
        /// <para>OneToOne（1:1 引用）时即对方实体类。</para>
        /// <para>OneToMany（1:N 集合）时为集合元素类型。</para>
        /// </summary>
        public Type TargetType { get; private set; }

        /// <summary>
        /// 对方实体的元数据。
        /// </summary>
        public EntityMetadata TargetEntityMetadata { get; private set; }

        /// <summary>
        /// 本方实体中键属性对应的列元数据。
        /// <para>1:N 集合时为本方主键（如 Customer.Id），1:1 引用时为本方外键（如 Order.CustomerId）。</para>
        /// </summary>
        public ColumnMetadata ThisKeyColumn { get; private set; }

        /// <summary>
        /// 对方实体中键属性对应的列元数据。
        /// <para>1:N 集合时为对方外键（如 Order.CustomerId），1:1 引用时为对方主键（如 Customer.Id）。</para>
        /// </summary>
        public ColumnMetadata OtherKeyColumn { get; private set; }

        /// <summary>
        /// 本方实体中的键属性名。
        /// </summary>
        public string ThisKey => ThisKeyColumn.PropertyName;

        /// <summary>
        /// 对方实体中的键属性名。
        /// </summary>
        public string OtherKey => OtherKeyColumn.PropertyName;

        private NavigationMetadata(PropertyInfo propertyInfo)
        {
            this.propertyInfo = propertyInfo ?? throw new ArgumentNullException(nameof(propertyInfo));

            // 构建时编译属性读写委托，替代调用时的反射 GetValue/SetValue（导航装配路径的性能优化），
            // 模式与 ColumnMetadata 一致。编译产物等价于手写代码：
            //   getter = instance => (object)((Order)instance).Customer;
            //   setter = (instance, value) => ((Order)instance).Customer = (Customer)value;
            // 每个导航属性仅在构建元数据时编译一次，之后每次读写都是普通委托调用，无反射开销

            // 委托签名以 object 传递实例与值（元数据不感知具体实体类型）
            var instanceParameter = Expression.Parameter(typeof(object), "instance");
            var valueParameter = Expression.Parameter(typeof(object), "value");

            // 将 object 实例转换为属性声明类型，等价于 ((Order)instance)。
            // 使用 DeclaringType 而非实体类型：属性可能声明在基类上，派生类实例转为基类引用后同样可以访问
            var castInstance = Expression.Convert(instanceParameter, propertyInfo.DeclaringType);

            // getter：读取属性后装箱为 object（值类型属性需要装箱），等价于 (object)((Order)instance).Customer
            var getterBody = Expression.Convert(Expression.Property(castInstance, propertyInfo), typeof(object));
            getter = Expression.Lambda<Func<object, object>>(getterBody, instanceParameter).Compile();

            // setter：等价于 ((Order)instance).Customer = (Customer)value
            var setterBody = Expression.Assign(
                Expression.Property(castInstance, propertyInfo),// 赋值左侧：类型化实例上的属性
                Expression.Convert(valueParameter, propertyInfo.PropertyType));// 赋值右侧：将传入的 object 拆箱/转换为属性类型
            setter = Expression.Lambda<Action<object, object>>(setterBody, instanceParameter, valueParameter).Compile();
        }

        /// <summary>
        /// 读取实体的导航属性原始值（.NET 类型，不应用任何转换器）。
        /// </summary>
        /// <param name="entity">实体对象。</param>
        /// <returns>导航属性的 .NET 原始值。</returns>
        public object GetValue(object entity)
        {
            return getter(entity);
        }

        /// <summary>
        /// 将 .NET 原始值直接设置到实体的导航属性（不应用任何转换器）。
        /// </summary>
        /// <param name="entity">实体对象。</param>
        /// <param name="value">要设置的 .NET 原始值。</param>
        public void SetValue(object entity, object value)
        {
            setter(entity, value);
        }

        /// <summary>
        /// 重新指向对方实体的规范元数据实例。仅供 <see cref="EntityMetadataCache"/> 在构建后规范化导航目标引用时调用。
        /// <para>多线程并发构建时，GetOrAdd 败者线程的实例不进主缓存，但可能被对方实体的导航属性持有；
        /// 下游缓存以 <see cref="ColumnMetadata"/> 引用相等为组合键，两份实例会造成缓存键分裂，故统一重指到规范实例。</para>
        /// </summary>
        /// <param name="value">对方实体在主缓存中的规范元数据实例。</param>
        internal void SetTargetEntityMetadata(EntityMetadata value)
        {
            TargetEntityMetadata = value;
        }

        /// <summary>
        /// 导航属性元数据构建器，用于在 <see cref="EntityMetadataCache"/> 中构建不可变的元数据实例。
        /// </summary>
        internal sealed class Builder
        {
            public PropertyInfo PropertyInfo { get; set; }
            public NavigationType NavigationType { get; set; }
            public Type TargetType { get; set; }
            public EntityMetadata TargetEntityMetadata { get; set; }
            public ColumnMetadata ThisKeyColumn { get; set; }
            public ColumnMetadata OtherKeyColumn { get; set; }

            public NavigationMetadata Build()
            {
                return new NavigationMetadata(PropertyInfo)
                {
                    NavigationType = NavigationType,
                    TargetType = TargetType,
                    TargetEntityMetadata = TargetEntityMetadata,
                    ThisKeyColumn = ThisKeyColumn,
                    OtherKeyColumn = OtherKeyColumn,
                };
            }
        }
    }
}
