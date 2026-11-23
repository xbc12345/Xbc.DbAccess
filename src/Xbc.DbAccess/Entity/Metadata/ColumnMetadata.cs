using System.Collections.Concurrent;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 列元数据：实体属性与数据库列的映射单元，承载列配置（列名、主键/标识列、<c>DbType</c>、DBNull 处理）
    /// 与属性行为（编译的读写委托、初始值、转换器链、自增 ID 回填）。经 <see cref="Builder"/> 构建后不可修改。
    /// </summary>
    internal sealed class ColumnMetadata
    {
        /// <summary>
        /// 值类型默认值缓存，避免默认值判定热路径的反射分配。
        /// </summary>
        private static readonly ConcurrentDictionary<Type, object> defaultValueCache = new ConcurrentDictionary<Type, object>();

        private readonly PropertyInfo propertyInfo;// 属性信息
        private readonly Func<object, object> getter;// 编译的属性读取委托，替代反射 GetValue
        private readonly Action<object, object> setter;// 编译的属性写入委托，替代反射 SetValue
        private Func<object, object> convertToFunc;// 编译的转换器写入委托，替代反射 Invoke

        /// <summary>
        /// 属性名。
        /// </summary>
        public string PropertyName => propertyInfo.Name;

        /// <summary>
        /// 属性类型。
        /// </summary>
        public Type PropertyType => propertyInfo.PropertyType;

        /// <summary>
        /// 若属性类型为可空值类型，则为其基础类型；否则为 null。
        /// </summary>
        public Type UnderlyingType { get; private set; }

        /// <summary>
        /// 列名。
        /// </summary>
        public string ColumnName { get; private set; }

        /// <summary>
        /// 是否为主键。
        /// </summary>
        public bool IsPrimaryKey { get; private set; }

        /// <summary>
        /// 是否为标识列。
        /// </summary>
        public bool IsIdentity { get; private set; }

        /// <summary>
        /// 是否为乐观并发令牌列（每实体至多一列，见 <see cref="EntityMetadata.ConcurrencyTokenColumn"/>）。
        /// </summary>
        public bool IsConcurrencyToken { get; private set; }

        /// <summary>
        /// 当数据库值为 DBNull 时的处理方式。
        /// </summary>
        public DbNullHandling DbNullHandling { get; private set; }

        /// <summary>
        /// 属性的初始值（如 <c>int Age { get; set; } = 18</c> 为 18，而非 0），用于 SkipAssignment 场景。
        /// 实体类型缺少公共无参构造函数时构建元数据抛 <see cref="InvalidOperationException"/>。
        /// </summary>
        public object InitialValue { get; private set; }

        /// <summary>
        /// 数据库列类型，用于指定属性对应的数据库列的实际类型。
        /// 若未指定，默认根据 .NET 类型自动推断。
        /// </summary>
        public DbType? DbType { get; private set; }

        /// <summary>
        /// 自定义类型转换器类型。
        /// </summary>
        public Type ConverterType { get; private set; }

        /// <summary>
        /// 自定义类型转换器实例（构建元数据时创建，读写路径共用）。
        /// </summary>
        public object ConverterInstance { get; private set; }

        /// <summary>
        /// 自定义类型转换器的 ConvertFrom 方法信息。
        /// </summary>
        public MethodInfo ConvertFromMethod { get; private set; }

        /// <summary>
        /// 自定义类型转换器的 ConvertTo 方法信息。
        /// </summary>
        public MethodInfo ConvertToMethod { get; private set; }

        /// <summary>
        /// 枚举列是否以名称字符串存储（写入 <see cref="Enum.ToString()"/>，读取按名称解析）。
        /// </summary>
        public bool EnumAsString { get; private set; }

        /// <summary>
        /// 列是否以 JSON 文本存储复杂类型属性（写入序列化字符串，读取按属性类型反序列化）。
        /// </summary>
        public bool IsJson { get; private set; }

        /// <summary>
        /// 列的存储值是否经转换器变换（列级 <c>[Column(ConverterType)]</c> 或全局注册），此时存储值与属性值不同构。
        /// <para>消费场景：排序/聚合键解析与 SQL 函数/文本匹配守卫按本属性拦截（存储值序与属性值序可能不一致，
        /// 函数作用于存储文本的结果与按属性值语义不一致）；等值/比较/IN 等值域形态不受限，
        /// 参数经 <c>ConvertToDb</c> 与存储值同域比较，语义自洽。</para>
        /// <para>JSON 列与并发令牌在两个消费场景均由各自专属检查先行拒绝（排序/聚合守卫、WHERE 列操作数守卫），不经本判据；
        /// 枚举（名称字符串/数值）映射语义确定不触发。全局转换器按 <see cref="DbValueConverterRegistry"/> 当前注册状态动态判定。</para>
        /// </summary>
        public bool HasConverterTransform => ConverterType != null
            || DbValueConverterRegistry.GetToConverter(UnderlyingType ?? PropertyType) != null;

        private ColumnMetadata(PropertyInfo propertyInfo, object converterInstance)
        {
            this.propertyInfo = propertyInfo ?? throw new ArgumentNullException(nameof(propertyInfo));
            ConverterInstance = converterInstance;

            // 构建时编译属性读写委托，替代调用时的反射 GetValue/SetValue（写路径与装配路径的性能优化）。
            // 编译产物等价于手写代码：
            //   getter = instance => (object)((Person)instance).Name;
            //   setter = (instance, value) => ((Person)instance).Name = (string)value;
            // 每个属性仅在构建元数据时编译一次，之后每次读写都是普通委托调用，无反射开销

            // 委托签名以 object 传递实例与值（元数据不感知具体实体类型）
            var instanceParameter = Expression.Parameter(typeof(object), "instance");
            var valueParameter = Expression.Parameter(typeof(object), "value");

            // 将 object 实例转换为属性声明类型，等价于 ((Person)instance)。
            // 使用 DeclaringType 而非实体类型：属性可能声明在基类上，派生类实例转到基类引用后同样可以访问
            var castInstance = Expression.Convert(instanceParameter, propertyInfo.DeclaringType);

            // getter：读取属性后装箱为 object（值类型属性需要装箱），等价于 (object)((Person)instance).Name
            var getterBody = Expression.Convert(Expression.Property(castInstance, propertyInfo), typeof(object));
            getter = Expression.Lambda<Func<object, object>>(getterBody, instanceParameter).Compile();

            // setter：等价于 ((Person)instance).Name = (string)value
            var setterBody = Expression.Assign(
                Expression.Property(castInstance, propertyInfo),// 赋值左侧：类型化实例上的属性
                Expression.Convert(valueParameter, propertyInfo.PropertyType));// 赋值右侧：将传入的 object 拆箱/转换为属性类型
            setter = Expression.Lambda<Action<object, object>>(setterBody, instanceParameter, valueParameter).Compile();
        }

        /// <summary>
        /// 编译自定义转换器 <see cref="ConvertToMethod"/> 的写入调用委托，由 <see cref="Builder.Build"/> 在全部属性初始化完成后调用一次。
        /// </summary>
        internal void CompileConvertToDelegate()
        {
            if (ConvertToMethod == null)
                return;

            // 编译产物等价于手写代码：value => (object)converterInstance.ConvertTo((TParam)value)
            // 转换器实例常量须显式转换到方法声明类型才能作为调用目标；
            // 值参数按方法参数类型（而非属性类型）转换：装箱 Nullable 与装箱基础类型同构，
            // 方法签名按基础类型声明，直接转换到方法参数类型才能完成拆箱
            var valueParameter = Expression.Parameter(typeof(object), "value");
            var body = Expression.Convert(
                Expression.Call(
                    Expression.Convert(Expression.Constant(ConverterInstance), ConvertToMethod.DeclaringType),
                    ConvertToMethod,
                    Expression.Convert(valueParameter, ConvertToMethod.GetParameters()[0].ParameterType)),
                typeof(object));

            convertToFunc = Expression.Lambda<Func<object, object>>(body, valueParameter).Compile();
        }

        /// <summary>
        /// 读取实体的属性原始值（.NET 类型，不应用任何转换器）。
        /// </summary>
        /// <param name="entity">实体对象。</param>
        /// <returns>属性的 .NET 原始值。</returns>
        public object GetValue(object entity)
        {
            return getter(entity);
        }

        /// <summary>
        /// 将 .NET 原始值直接设置到实体属性（不应用任何转换器）。
        /// </summary>
        /// <param name="entity">实体对象。</param>
        /// <param name="value">要设置的 .NET 原始值。</param>
        public void SetValue(object entity, object value)
        {
            setter(entity, value);
        }

        /// <summary>
        /// 判断实体上本列的属性值是否为该列类型的默认值（如 int 的 0，引用类型的 null）。
        /// <para>可空值类型按基础类型比较默认值：装箱的可空值类型非 null 时即基础类型实例（int? 的 0 装箱为 int），
        /// 以 Nullable 创建默认值会得到 null，误把 0 判为非默认值。</para>
        /// </summary>
        /// <param name="entity">实体对象。</param>
        /// <returns>值为 null 或该列类型的默认值时返回 true。</returns>
        public bool IsDefaultValue(object entity)
        {
            var value = getter(entity);
            if (value == null)
                return true;

            var type = UnderlyingType ?? PropertyType;
            if (type.IsValueType)
                return value.Equals(defaultValueCache.GetOrAdd(type, Activator.CreateInstance));

            return false;
        }

        /// <summary>
        /// 获取实体属性值（已转换为数据库值）：读属性原值后经 <see cref="ConvertToDb"/> 转换（含主键短路）。
        /// </summary>
        /// <param name="entity">实体对象。</param>
        /// <returns>转换后的数据库值。</returns>
        public object GetValueForDb(object entity)
        {
            var propertyValue = getter(entity);
            return ConvertToDb(propertyValue);
        }

        /// <summary>
        /// 回填插入后的自增 ID 到实体，仅在数值类型间做兼容转换后直接赋值，不走转换器链。
        /// </summary>
        /// <param name="entity">实体对象。</param>
        /// <param name="dbValue">数据库返回的自增 ID（不会是 DBNull）。</param>
        public void BackfillIdentity(object entity, object dbValue)
        {
            // 部分提供程序的自增 ID 读取结果与属性类型不一致（如 SQLite LAST_INSERT_ROWID() 恒返回 Int64），需在数值类型间做兼容转换后再赋值
            if (dbValue != null && !PropertyType.IsInstanceOfType(dbValue) && dbValue is IConvertible)
                dbValue = Convert.ChangeType(dbValue, UnderlyingType ?? PropertyType);

            setter(entity, dbValue);
        }

        /// <summary>
        /// 将属性值转换为数据库值。
        /// <para>主键列值原样返回（含 null，调用方以 ?? DBNull 兜底）；非主键列依次应用自定义转换器、
        /// 列级存储开关（EnumAsString/IsJson）、全局转换器与标准类型归一。</para>
        /// </summary>
        /// <param name="propertyValue">属性值。</param>
        /// <returns>转换后的数据库值。</returns>
        public object ConvertToDb(object propertyValue)
        {
            if (IsPrimaryKey)
                return propertyValue;

            // null 值统一转换为 DBNull：可空值类型为 null 时，转换器入口的强制转换（拆箱）会抛 NullReferenceException
            if (propertyValue == null)
                return DBNull.Value;

            // 1. 优先使用自定义转换器（构建期已固化为编译委托）
            if (convertToFunc != null)
                return convertToFunc(propertyValue);

            // 2. 列级存储格式开关（显式列配置优先于按类型注册的全局转换器）
            if (EnumAsString)
            {
                // 谓词路径传入的值可能已被表达式编译器底层化为整数（Convert 节点求值结果），按列的枚举类型归一后取名称
                var enumValue = propertyValue as Enum;
                if (enumValue == null)
                    enumValue = (Enum)Enum.ToObject(UnderlyingType ?? PropertyType, propertyValue);

                return enumValue.ToString();
            }

            if (IsJson)
                return JsonColumnSerializer.Serialize(propertyValue, PropertyType);

            // 3. 使用全局转换器（使用 UnderlyingType 以匹配可空类型的底层类型）
            var globalConverter = DbValueConverterRegistry.GetToConverter(UnderlyingType ?? PropertyType);
            if (globalConverter != null)
                return globalConverter(propertyValue);

            // 4. 标准类型归一：值可能经表达式求值的类型提升偏离列类型（char 列 == 'A' 提升为 int、int 列 == 3.7
            //    以 double 到达），先按列类型无损归一；有损舍入与窄化超范围保持原值交存储层裁决
            return EntityValueConverter.ConvertTo(propertyValue, UnderlyingType ?? PropertyType);
        }

        /// <summary>
        /// 列元数据构建器，用于在 <see cref="EntityMetadataCache"/> 中构建不可变的元数据实例。
        /// </summary>
        internal sealed class Builder
        {
            public PropertyInfo PropertyInfo { get; set; }
            public string ColumnName { get; set; }
            public bool IsPrimaryKey { get; set; }
            public bool IsIdentity { get; set; }
            public bool IsConcurrencyToken { get; set; }
            public DbNullHandling DbNullHandling { get; set; }
            public object InitialValue { get; set; }
            public DbType? DbType { get; set; }
            public Type ConverterType { get; set; }
            public MethodInfo ConvertFromMethod { get; set; }
            public MethodInfo ConvertToMethod { get; set; }
            public bool EnumAsString { get; set; }
            public bool IsJson { get; set; }

            public ColumnMetadata Build()
            {
                // 获取属性底层类型（处理可空类型）
                var underlyingType = Nullable.GetUnderlyingType(PropertyInfo.PropertyType);

                // 创建转换器实例：无参构造缺失与构造执行异常分别报出带转换器类型上下文的明确异常
                object converterInstance = null;
                if (ConverterType != null)
                {
                    try
                    {
                        converterInstance = Activator.CreateInstance(ConverterType);
                    }
                    catch (MissingMethodException ex)
                    {
                        throw new InvalidOperationException($"转换器类型 '{ConverterType}' 必须提供公共无参构造函数，才能创建转换器实例。", ex);
                    }
                    catch (TargetInvocationException ex)
                    {
                        throw new InvalidOperationException($"转换器类型 '{ConverterType}' 的无参构造函数执行时抛出异常，请检查构造函数实现。", ex.InnerException ?? ex);
                    }
                }

                var metadata = new ColumnMetadata(PropertyInfo, converterInstance)
                {
                    UnderlyingType = underlyingType,
                    ColumnName = ColumnName,
                    IsPrimaryKey = IsPrimaryKey,
                    IsIdentity = IsIdentity,
                    IsConcurrencyToken = IsConcurrencyToken,
                    DbNullHandling = DbNullHandling,
                    InitialValue = InitialValue,
                    DbType = DbType,
                    ConverterType = ConverterType,
                    ConvertFromMethod = ConvertFromMethod,
                    ConvertToMethod = ConvertToMethod,
                    EnumAsString = EnumAsString,
                    IsJson = IsJson,
                };

                // 属性全部初始化完成后固化转换委托（依赖 ConverterInstance 与 ConvertToMethod 已就位）
                metadata.CompileConvertToDelegate();
                return metadata;
            }
        }
    }
}
