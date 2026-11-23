using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 实体元数据缓存（非泛型版本），通过反射解析实体类型的特性标注（[Table]/[PrimaryKey]/[Column]/[Navigation]），构建并缓存实体元数据。
    /// </summary>
    internal static class EntityMetadataCache
    {
        /// <summary>
        /// 有效的主键类型集合。
        /// </summary>
        private static readonly HashSet<Type> PrimaryKeyTypes = new HashSet<Type>
        {
            // 字符串、Guid
            typeof(string), typeof(Guid),
            // 32/64 位整数（小整数范围太小不适合做主键）。
            // uint/ulong 适合 MySQL 等原生支持无符号整型的提供程序；
            // SqlClient/Oracle 不支持 UInt 系列参数映射，首次执行时抛提供程序异常，此时请改用 int/long 主键（本集合保持宽松，由提供程序侧决定是否可用）
            typeof(int), typeof(uint), typeof(long), typeof(ulong),
        };

        /// <summary>
        /// 数值主键类型集合（主键类型全集中的 32/64 位整数）。
        /// <para>数值主键在按主键查找时接受可转换的值（如传入 long 而主键为 int）；string/Guid 主键要求严格类型匹配。</para>
        /// </summary>
        private static readonly HashSet<Type> NumericPrimaryKeyTypes = new HashSet<Type>
        {
            typeof(int), typeof(uint), typeof(long), typeof(ulong),
        };

        /// <summary>
        /// 有效的标识列类型集合（int/long/uint/ulong），与数值主键类型是独立约束。
        /// </summary>
        private static readonly HashSet<Type> IdentityColumnTypes = new HashSet<Type>
        {
            typeof(int), typeof(uint), typeof(long), typeof(ulong),
        };

        /// <summary>
        /// 有效的列类型集合。
        /// </summary>
        private static readonly HashSet<Type> PrimitiveColumnTypes = new HashSet<Type>
        {
            // 整数类型
            typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong),
            // 浮点类型
            typeof(float), typeof(double),
            // 其他基础类型
            typeof(char), typeof(bool),
            // 字符串
            typeof(string),
            // 日期时间相关类型
            typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan),
            // Guid
            typeof(Guid),
            // decimal
            typeof(decimal),
            // 字节数组（对应数据库的 varbinary/blob 类型）
            typeof(byte[]),
        };

        /// <summary>
        /// 元数据缓存字典：实体类型 → 实体元数据。首次访问时通过反射构建，后续直接从缓存读取。
        /// </summary>
        private static readonly ConcurrentDictionary<Type, EntityMetadata> MetadataCache = new ConcurrentDictionary<Type, EntityMetadata>();

        /// <summary>
        /// 当前线程正在构建中的实体元数据登记表（实体类型到列已就绪、导航属性尚在构建中的实例）。
        /// <para>用于打破自引用/循环引用导航的递归构建：递归查找对方类型正是当前构建类型时，主缓存尚无该条目，
        /// 命中登记表即可提前返回，避免无限递归栈溢出。</para>
        /// <para>线程局部存储保证仅同线程的递归查找可见，其他线程观察不到半成品实例。</para>
        /// </summary>
        [ThreadStatic]
        private static Dictionary<Type, EntityMetadata> buildingMetadata;

        #region 公共方法
        /// <summary>
        /// 获取实体元数据。若缓存中不存在则通过反射解析实体类型的特性标注并构建元数据。
        /// </summary>
        /// <param name="entityType">实体类型。</param>
        /// <returns>实体元数据。</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static EntityMetadata GetMetadata(Type entityType)
        {
            Guard.ThrowIfNull(entityType, nameof(entityType));

            // 命中当前线程构建中登记表（自引用/循环引用导航的递归查找）：
            // 返回列已就绪的实例，其主键/列信息足以满足递归方的键解析需要（递归方不读取导航集合）
            if (buildingMetadata != null && buildingMetadata.TryGetValue(entityType, out var building))
                return building;

            var metadata = MetadataCache.GetOrAdd(entityType, BuildEntityMetadata);

            // 返回规范实例前，将各导航属性的 TargetEntityMetadata 重指到主缓存中的规范实例（消除多线程首建竞态）
            NormalizeNavigationTargets(metadata);
            return metadata;
        }

        /// <summary>
        /// 将元数据中各导航属性的 <see cref="NavigationMetadata.TargetEntityMetadata"/> 重新指向主缓存中的规范实例。
        /// <para>多线程并发构建时，GetOrAdd 败者线程的实例不进主缓存，但可能被对方实体的导航属性持有；
        /// 下游缓存以 <see cref="ColumnMetadata"/> 引用相等为组合键，两份实例会造成缓存键分裂，故返回前统一重指。</para>
        /// <para>仅查主缓存（不触发构建）；对方类型尚在构建时保留原引用，待其完成后的下一次 GetMetadata 再规范化（尽力而为）。
        /// 泛型入口经 Lazy 一次性固化，若首击落在导航目标未进入主缓存的窗口内该引用不再修正，影响仅为缓存键分裂导致的重复编译，无正确性问题。</para>
        /// </summary>
        /// <param name="metadata">主缓存中的规范元数据实例。</param>
        private static void NormalizeNavigationTargets(EntityMetadata metadata)
        {
            var navigations = metadata.Navigations;
            if (navigations == null || navigations.Count == 0)
                return;

            foreach (var navigation in navigations)
            {
                if (MetadataCache.TryGetValue(navigation.TargetType, out var canonical) &&
                    !ReferenceEquals(canonical, navigation.TargetEntityMetadata))
                {
                    navigation.SetTargetEntityMetadata(canonical);
                }
            }
        }

        /// <summary>
        /// 获取实体对应的数据库表名。若未标记 [Table] 特性，默认使用实体类名的复数形式。
        /// </summary>
        /// <param name="entityType">实体类型。</param>
        /// <returns>表名。</returns>
        public static string GetTableName(Type entityType)
        {
            return GetMetadata(entityType).TableName;
        }

        /// <summary>
        /// 获取实体的所有列元数据（不含导航属性）。
        /// </summary>
        /// <param name="entityType">实体类型。</param>
        /// <returns>列元数据只读列表。</returns>
        public static IReadOnlyList<ColumnMetadata> GetColumns(Type entityType)
        {
            return GetMetadata(entityType).Columns;
        }

        /// <summary>
        /// 获取实体的主键列元数据。当前不支持复合主键，仅支持单列主键。
        /// </summary>
        /// <param name="entityType">实体类型。</param>
        /// <returns>主键列元数据，未定义主键时为 null。</returns>
        public static ColumnMetadata GetPrimaryKey(Type entityType)
        {
            return GetMetadata(entityType).PrimaryKey;
        }

        /// <summary>
        /// 获取实体的标识列（自增列）元数据。仅支持 int/long/uint/ulong 类型的标识列。
        /// </summary>
        /// <param name="entityType">实体类型。</param>
        /// <returns>标识列元数据，未定义标识列时为 null。</returns>
        public static ColumnMetadata GetIdentityColumn(Type entityType)
        {
            return GetMetadata(entityType).IdentityColumn;
        }

        /// <summary>
        /// 根据属性名获取列元数据。
        /// </summary>
        /// <param name="entityType">实体类型。</param>
        /// <param name="propertyName">属性名（C# 属性名，非数据库列名）。</param>
        /// <returns>列元数据，如果未找到则返回 null。</returns>
        public static ColumnMetadata GetColumn(Type entityType, string propertyName)
        {
            Guard.ThrowIfNullOrWhiteSpace(propertyName, nameof(propertyName));

            // 经元数据实例的属性名字典查找（构建时一次性建立），替代逐列线性扫描
            return GetMetadata(entityType).GetColumn(propertyName);
        }
        #endregion

        #region 实体元数据
        /// <summary>
        /// 构建实体元数据。解析实体类型的特性标注，构建表名、列元数据、主键、标识列和导航属性等完整映射信息。
        /// </summary>
        /// <param name="entityType">实体类型。</param>
        /// <returns>实体元数据。</returns>
        /// <exception cref="InvalidOperationException"></exception>
        private static EntityMetadata BuildEntityMetadata(Type entityType)
        {
            // 未闭合泛型（如 Entity<>）无法实例化也无法解析属性签名，单独报出明确错误（区别于其他无效类型）
            if (entityType.ContainsGenericParameters)
                throw new InvalidOperationException($"实体类型 '{entityType}' 不能是未闭合泛型，请使用封闭泛型类型。");

            // 验证实体类型是否有效
            if (!IsValidEntityType(entityType))
                throw new InvalidOperationException($"实体类型 '{entityType}' 必须是可实例化的引用类型，不能是接口、抽象类、值类型、string 或 object。");

            // 获取表名
            var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            var tableName = tableAttr?.Name ?? PluralizationHelper.ToPlural(entityType.Name);// 默认使用复数表名

            // 封闭泛型未显式指定表名时，默认表名取自类型 Name（含泛型参数记号 '`'，如 "Entity`1"），
            // 大概率不是期望的物理表名，且部分数据库不接受含 '`' 的标识符；构建期即抛出，强制显式指定 [Table]
            if (tableAttr?.Name == null && entityType.IsGenericType)
                throw new InvalidOperationException(
                    $"实体类型 '{entityType}' 是泛型类型且未显式指定 [Table] 表名，默认表名 '{tableName}' 含泛型参数记号，" +
                    $"可能不被数据库支持，请通过 [Table(\"...\")] 显式指定表名。");

            var builder = new EntityMetadata.Builder
            {
                EntityType = entityType,
                TableName = tableName,
                Schema = tableAttr?.Schema
            };

            // 只读属性与索引器不能进入映射路径，带映射特性属配置错误，构建期即报出
            var allProperties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            ValidateUnmappablePropertyAttributes(entityType, allProperties);

            var properties = allProperties.Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0);

            // 创建实体实例用于读取属性初始值，缺少公共无参构造函数时抛出明确异常
            object entityInstance;
            try
            {
                entityInstance = Activator.CreateInstance(entityType);
            }
            catch (MissingMethodException ex)
            {
                throw new InvalidOperationException($"实体类型 '{entityType}' 必须提供公共无参构造函数，才能构建实体元数据（读取属性初始值）。", ex);
            }
            catch (TargetInvocationException ex)
            {
                // 无参构造函数执行时抛出的异常会被 Activator 包装，还原为原始异常
                throw new InvalidOperationException($"实体类型 '{entityType}' 的无参构造函数执行时抛出异常，请检查构造函数实现。", ex.InnerException ?? ex);
            }

            // 构建列元数据并收集特殊列（主键/标识列/并发令牌列，含列名唯一性校验）
            var columns = BuildColumns(entityType, entityInstance, properties, builder);

            // 无主键仅告警不抛出：插入不依赖主键仍可用，Update/Delete/Find 首次调用时会抛出明确异常
            //if (builder.PrimaryKey == null)
            //    DiagnosticLog.Warn(nameof(EntityMetadataCache), "实体类型 '{EntityType}' 未定义主键：插入（Insert/InsertRange）可用，但 Update/Delete/Find 需要主键，首次调用这些方法时会抛出异常。", entityType.ToString());

            // 必须先设置列再构建导航属性：导航属性的键解析（ThisKey/OtherKey 查找）依赖本方实体的列元数据
            var metadata = builder.Build();
            metadata.SetColumns(columns);

            // 将列已就绪的实例登记到线程局部表，供自引用/循环引用导航的递归查找提前返回，构建完成后移除
            var building = buildingMetadata ?? (buildingMetadata = new Dictionary<Type, EntityMetadata>());
            building[entityType] = metadata;

            List<NavigationMetadata> navigations;
            try
            {
                navigations = BuildNavigationMetadata(metadata, properties);
            }
            finally
            {
                building.Remove(entityType);
            }

            metadata.SetNavigations(navigations);
            return metadata;
        }

        /// <summary>
        /// 检查指定的类型是否可作为实体类型。排除未闭合泛型、接口、抽象类、值类型、字符串、object、匿名类型、委托、数组和集合类型。
        /// </summary>
        /// <param name="entityType">要检查的类型。</param>
        /// <returns>是否为有效的实体类型。</returns>
        private static bool IsValidEntityType(Type entityType)
        {
            // 接口、抽象类、值类型
            if (entityType.IsInterface || entityType.IsAbstract || entityType.IsValueType)
                return false;

            // 未闭合泛型无法实例化也无法解析属性签名（顶层入口已单独报错，此处兜底覆盖导航对方类型等路径）
            if (entityType.ContainsGenericParameters)
                return false;

            // 字符串与 object（最常见的误用类型）
            if (entityType == typeof(string) || entityType == typeof(object))
                return false;

            // 匿名类型（编译器生成）
            if (TypeHelper.IsAnonymousType(entityType))
                return false;

            // 委托类型
            if (typeof(Delegate).IsAssignableFrom(entityType))
                return false;

            // 数组（IsArray 比 IsGenericEnumerable 快）
            if (entityType.IsArray)
                return false;

            // 实现了 IEnumerable<T> 或 IEnumerable 的类型
            if (TypeHelper.IsGenericEnumerable(entityType, out _) || typeof(System.Collections.IEnumerable).IsAssignableFrom(entityType))
                return false;

            return true;
        }

        /// <summary>
        /// 逐属性校验不可映射形态（只读、索引器）未标记映射特性，属配置错误，构建期即报出。
        /// <para>无映射特性的只读属性视为计算属性、裸索引器视为辅助成员，静默跳过（由调用方负责过滤）；
        /// [NotMapped] 属于显式忽略，允许。</para>
        /// </summary>
        private static void ValidateUnmappablePropertyAttributes(Type entityType, IEnumerable<PropertyInfo> properties)
        {
            var mappingAttributes = new[]
            {
                (typeof(PrimaryKeyAttribute), "[PrimaryKey]"),
                (typeof(ColumnAttribute), "[Column]"),
                (typeof(NavigationAttribute), "[Navigation]")
            };

            foreach (var property in properties)
            {
                var isReadOnly = !property.CanWrite;
                var isIndexer = property.GetIndexParameters().Length > 0;

                if (!isReadOnly && !isIndexer)
                    continue;

                foreach (var (attributeType, name) in mappingAttributes)
                {
                    if (property.GetCustomAttribute(attributeType) == null)
                        continue;

                    if (isIndexer)
                        throw new InvalidOperationException($"属性 '{entityType}.{property.Name}' 是索引器属性，不能标记 {name}：索引器没有单值列语义，无法映射为数据库列或导航属性。");

                    throw new InvalidOperationException($"属性 '{entityType}.{property.Name}' 标记了 {name}，{name} 要求属性可写，只读属性不能参与映射。");
                }
            }
        }

        /// <summary>
        /// 构建列元数据列表，同时完成列名唯一性校验（不区分大小写，冲突时点名双方），
        /// 并收集主键/标识列/并发令牌列到 builder（各自至多一个，重复定义即抛出）。
        /// </summary>
        /// <param name="entityType">实体类型，用于异常消息。</param>
        /// <param name="entityInstance">实体实例，用于读取属性初始值。</param>
        /// <param name="properties">实体类型的所有可读写属性。</param>
        /// <param name="builder">实体元数据构建器，特殊列收集的目标。</param>
        /// <returns>列元数据列表。</returns>
        private static List<ColumnMetadata> BuildColumns(Type entityType, object entityInstance, IEnumerable<PropertyInfo> properties, EntityMetadata.Builder builder)
        {
            var columns = new List<ColumnMetadata>();

            // 列名唯一性校验：不同属性映射到同一列（不区分大小写）会导致按列名查找命中错误属性、
            // INSERT/UPDATE 生成重复列，构建期即失败并点名冲突双方
            var columnNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in properties)
            {
                var column = BuildColumnMetadata(entityType, entityInstance, property);
                if (column == null)
                    continue;

                if (columnNames.TryGetValue(column.ColumnName, out var existingProperty))
                    throw new InvalidOperationException($"实体类型 '{entityType}' 的属性 '{existingProperty}' 与 '{property.Name}' 映射了重复的列名 '{column.ColumnName}'，请检查 [Column] 特性配置。");

                columnNames[column.ColumnName] = property.Name;

                if (column.IsPrimaryKey)
                {
                    if (builder.PrimaryKey != null)
                        throw new InvalidOperationException($"实体类型 '{entityType}' 定义了多个主键，当前不支持复合主键。");

                    builder.PrimaryKey = column;
                }

                if (column.IsIdentity)
                {
                    if (builder.IdentityColumn != null)
                        throw new InvalidOperationException($"实体类型 '{entityType}' 定义了多个标识列，通常数据库表只有一个标识列。");

                    builder.IdentityColumn = column;
                }

                if (column.IsConcurrencyToken)
                {
                    if (builder.ConcurrencyTokenColumn != null)
                        throw new InvalidOperationException($"实体类型 '{entityType}' 定义了多个乐观并发令牌列，每张表至多一个。");

                    builder.ConcurrencyTokenColumn = column;
                }

                columns.Add(column);
            }

            return columns;
        }
        #endregion

        #region 列元数据
        /// <summary>
        /// 构建列元数据。根据属性的特性标注（[PrimaryKey]、[Column]）和命名约定，解析属性与数据库列的映射关系。
        /// <para>返回 null 的情形：属性被 [NotMapped] 忽略，或属性类型非基础类型/枚举且未标记 [Column]。</para>
        /// <para>非基础类型属性标记了 [Column] 但未指定 ConverterType 时抛出异常（无法完成读写转换）。</para>
        /// <para>主键属性不允许标记 [Column] 特性，应使用 [PrimaryKey] 替代。</para>
        /// </summary>
        /// <param name="entityType">实体类型，用于异常消息。</param>
        /// <param name="entityInstance">实体实例，用于读取属性初始值。</param>
        /// <param name="property">要构建列元数据的属性信息。</param>
        /// <returns>列元数据，若属性被忽略或不可映射则返回 null。</returns>
        /// <exception cref="InvalidOperationException"></exception>
        internal static ColumnMetadata BuildColumnMetadata(Type entityType, object entityInstance, PropertyInfo property)
        {
            var displayName = $"{entityType}.{property.Name}";// 属性显示名，用于冲突类异常消息

            // 一次性获取属性级映射特性，互斥校验与后续分派共用
            var pkAttr = property.GetCustomAttribute<PrimaryKeyAttribute>();
            var columnAttr = property.GetCustomAttribute<ColumnAttribute>();
            var navigationAttr = property.GetCustomAttribute<NavigationAttribute>();
            var notMappedAttr = property.GetCustomAttribute<NotMappedAttribute>();

            // 属性级映射特性互斥校验（先于所有构建路径）
            ValidateAttributeExclusivity(displayName, pkAttr, columnAttr, navigationAttr, notMappedAttr);

            // [NotMapped] 表示不参与映射（列与导航），优先于命名约定，允许忽略符合主键命名规则的属性
            if (notMappedAttr != null)
                return null;

            // 判断是否为主键
            var isPrimaryKey = pkAttr != null || IsPrimaryKeyName(entityType, property.Name);
            if (isPrimaryKey)
            {
                // 拦截约定主键 + [Column]/[Navigation] 的组合：放行 [Navigation] 会使同一属性
                // 同时进入列集合与导航集合，首次装配即抛转型异常
                if (columnAttr != null || navigationAttr != null)
                    throw new InvalidOperationException(
                        $"属性 '{displayName}' 符合主键命名约定（被识别为主键），不能标记 [Column] 或 [Navigation]，" +
                        "主键列请使用 [PrimaryKey] 特性配置；导航属性请使用不符合主键命名约定的属性名。");

                return BuildPrimaryKeyColumn(entityType, entityInstance, property, pkAttr);
            }

            return BuildNonPrimaryKeyColumn(entityType, entityInstance, property, columnAttr);
        }

        /// <summary>
        /// 校验属性级映射特性（[PrimaryKey]/[Column]/[Navigation]/[NotMapped]）至多声明一个，
        /// 同时声明多个属配置错误，构建期报出，错误消息列出全部冲突特性。
        /// </summary>
        /// <param name="displayName">属性显示名（实体类型全名.属性名），用于异常消息。</param>
        /// <param name="pkAttr">主键特性（未声明为 null）。</param>
        /// <param name="columnAttr">列特性（未声明为 null）。</param>
        /// <param name="navigationAttr">导航特性（未声明为 null）。</param>
        /// <param name="notMappedAttr">不映射特性（未声明为 null）。</param>
        private static void ValidateAttributeExclusivity(string displayName, PrimaryKeyAttribute pkAttr, ColumnAttribute columnAttr, NavigationAttribute navigationAttr, NotMappedAttribute notMappedAttr)
        {
            var declared = new List<string>(4);
            if (pkAttr != null)
                declared.Add("[PrimaryKey]");

            if (columnAttr != null)
                declared.Add("[Column]");

            if (navigationAttr != null)
                declared.Add("[Navigation]");

            if (notMappedAttr != null)
                declared.Add("[NotMapped]");

            if (declared.Count > 1)
                throw new InvalidOperationException($"属性 '{displayName}' 同时声明了 {string.Join("、", declared)}，属性级映射特性至多声明一个。");
        }

        /// <summary>
        /// 检查属性名是否符合主键命名约定。
        /// <para>约定规则：属性名为 "Id" 或 "{TypeName}Id"（不区分大小写）时自动识别为主键。</para>
        /// <para>例如：Order 类的 "Id" 或 "OrderId" 属性会被自动识别为主键。</para>
        /// </summary>
        /// <param name="entityType">实体类型，用于构造约定主键名。</param>
        /// <param name="propertyName">属性名。</param>
        /// <returns>是否符合主键命名约定。</returns>
        private static bool IsPrimaryKeyName(Type entityType, string propertyName)
        {
            // 注意：嵌套类的 Name 仅返回自身类名（不含外层类前缀），如 Foo.Bar 的 Name 为 "Bar"，
            // 故 "{TypeName}Id" 约定按嵌套类自身的短名构造（如 "BarId"），与非嵌套类的行为保持一致
            return propertyName.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                   propertyName.Equals($"{entityType.Name}Id", StringComparison.OrdinalIgnoreCase);
        }

        #region 主键元数据
        /// <summary>
        /// 构建主键列元数据。校验主键的类型约束（不可空、有效主键类型、标识列类型）并装配元数据。
        /// </summary>
        /// <param name="entityType">实体类型，用于异常消息。</param>
        /// <param name="entityInstance">实体实例，用于读取属性初始值。</param>
        /// <param name="property">属性信息。</param>
        /// <param name="pkAttr">主键特性，可为 null（命名约定识别的主键）。</param>
        /// <returns>主键列元数据。</returns>
        private static ColumnMetadata BuildPrimaryKeyColumn(Type entityType, object entityInstance, PropertyInfo property, PrimaryKeyAttribute pkAttr)
        {
            var propertyType = property.PropertyType;

            // 主键不能为可空类型
            if (Nullable.GetUnderlyingType(propertyType) != null)
                throw new InvalidOperationException($"属性 '{entityType}.{property.Name}' 是主键列，不能为可空类型。");

            // 主键必须是基础类型（不支持枚举）
            if (!PrimaryKeyTypes.Contains(propertyType))
                throw new InvalidOperationException($"属性 '{entityType}.{property.Name}' 是主键列，其类型 '{GetTypeDisplayName(propertyType)}' 不是有效的主键类型。");

            // 标识列类型验证
            var isIdentity = pkAttr?.IsIdentity ?? false;
            if (isIdentity)
                ValidateIdentityColumnType(entityType, property);

            var builder = new ColumnMetadata.Builder
            {
                PropertyInfo = property,
                ColumnName = pkAttr?.Name ?? property.Name,
                IsPrimaryKey = true,
                IsIdentity = isIdentity,
                DbNullHandling = DbNullHandling.Default,
                // 透传 [PrimaryKey(DbType = ...)]：主键列同样可在写路径指定参数的数据库类型（如 string 主键指定 AnsiString）
                DbType = pkAttr != null ? NormalizeDbType(pkAttr.DbType) : null,
                ConverterType = null,
                ConvertFromMethod = null,
                ConvertToMethod = null,
                // entityInstance 已在 BuildEntityMetadata 中保证不为 null
                InitialValue = GetInitialValue(entityType, entityInstance, property),
            };

            return builder.Build();
        }
        #endregion

        #region 非主键元数据
        /// <summary>
        /// 构建非主键列元数据：类型去留判定、列级开关校验（互斥与适用性）、转换器解析全部通过后装配元数据。
        /// <para>返回 null 的情形：属性类型非基础类型/枚举且未标记 [Column]。</para>
        /// </summary>
        /// <param name="entityType">实体类型，用于异常消息。</param>
        /// <param name="entityInstance">实体实例，用于读取属性初始值。</param>
        /// <param name="property">属性信息。</param>
        /// <param name="columnAttr">列特性，可为 null（未标记时使用默认值）。</param>
        /// <returns>非主键列元数据，属性不可映射时为 null。</returns>
        private static ColumnMetadata BuildNonPrimaryKeyColumn(Type entityType, object entityInstance, PropertyInfo property, ColumnAttribute columnAttr)
        {
            var propertyType = property.PropertyType;
            var underlyingType = Nullable.GetUnderlyingType(propertyType);

            // 非基础类型/枚举的属性需要显式映射手段（转换器或 JSON 存储）
            if (!IsValidColumnType(underlyingType ?? propertyType))
            {
                // 未标记 [Column]：无法映射的属性（如自定义类型），过滤掉
                if (columnAttr == null)
                    return null;

                // JSON 列豁免：复杂类型可以 JSON 文本存储
                if (!columnAttr.IsJson && columnAttr.ConverterType == null)
                    throw new InvalidOperationException($"属性 '{entityType}.{property.Name}' 的类型不是基础类型，必须通过 [Column(ConverterType = ...)] 指定类型转换器或 [Column(IsJson = true)] 启用 JSON 存储。");
            }

            // 列级开关的组合校验（互斥 + 单开关适用性：标识列/并发令牌/枚举字符串/JSON/转换器）
            if (columnAttr != null)
                ValidateColumnStorageFlags(entityType, property, columnAttr, underlyingType ?? propertyType);

            // 校验转换器并解析双向转换方法（解析结果由列元数据持有，供读写路径反射调用）
            MethodInfo convertFromMethod = null;
            MethodInfo convertToMethod = null;
            if (columnAttr?.ConverterType != null)
                ValidateConverter(entityType, property, propertyType, columnAttr.ConverterType, out convertFromMethod, out convertToMethod);

            // [Column(DbType = ...)] 不做与属性类型的兼容性校验：各提供程序对 DbType 的宽容度差异大，
            // 不兼容的组合由提供程序在首次执行时报错，此处仅校验取值本身是 DbType 枚举
            var builder = new ColumnMetadata.Builder
            {
                PropertyInfo = property,
                ColumnName = columnAttr?.Name ?? property.Name,
                IsPrimaryKey = false,
                IsIdentity = columnAttr?.IsIdentity ?? false,
                IsConcurrencyToken = columnAttr?.IsConcurrencyToken ?? false,
                DbNullHandling = columnAttr?.DbNullHandling ?? DbNullHandling.Default,
                DbType = columnAttr != null ? NormalizeDbType(columnAttr.DbType) : null,
                ConverterType = columnAttr?.ConverterType,// 不强制要求实现 IDbValueConverter<T> 接口
                ConvertFromMethod = convertFromMethod,
                ConvertToMethod = convertToMethod,
                EnumAsString = columnAttr?.EnumAsString ?? false,
                IsJson = columnAttr?.IsJson ?? false,
                // entityInstance 已在 BuildEntityMetadata 中保证不为 null
                InitialValue = GetInitialValue(entityType, entityInstance, property),
            };

            return builder.Build();
        }

        /// <summary>
        /// 读取属性的 .NET 原始值作为初始值快照（SkipAssignment 判定的比较基准）。
        /// 属性 getter 抛出异常时附加实体/属性定位上下文，避免裸 TargetInvocationException 只能靠调用栈定位。
        /// </summary>
        private static object GetInitialValue(Type entityType, object entityInstance, PropertyInfo property)
        {
            try
            {
                return property.GetValue(entityInstance);
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException($"读取属性 '{entityType}.{property.Name}' 的初始值时，属性 getter 抛出异常（初始值用于值未变跳过赋值的判定基准），请检查属性初始化器实现。", ex.InnerException ?? ex);
            }
        }

        /// <summary>
        /// 检查类型是否为有效的列类型（基础类型或枚举，无需转换器即可映射）。
        /// </summary>
        private static bool IsValidColumnType(Type type)
        {
            return PrimitiveColumnTypes.Contains(type) || type.IsEnum;
        }

        /// <summary>
        /// 校验列级开关（列角色/存储格式）的组合约束。
        /// <para>列级开关至多其一：角色开关（IsConcurrencyToken/IsIdentity）的值由框架直接读写，
        /// 存储格式开关（EnumAsString/IsJson/ConverterType）只对应用侧读写的列生效，任意组合均语义矛盾；
        /// 三种存储格式之间是互斥的序列化方式。</para>
        /// <para>单开关适用性：IsConcurrencyToken 列必须为 byte[]；IsIdentity 列必须为数值类型；
        /// EnumAsString 仅用于枚举类型列；IsJson 属性必须是引用类型。</para>
        /// </summary>
        /// <param name="entityType">实体类型，用于异常消息。</param>
        /// <param name="property">属性信息，用于异常消息。</param>
        /// <param name="columnAttr">列特性。</param>
        /// <param name="effectiveType">属性的生效类型（可空类型取基础类型）。</param>
        private static void ValidateColumnStorageFlags(Type entityType, PropertyInfo property, ColumnAttribute columnAttr, Type effectiveType)
        {
            var displayName = $"{entityType}.{property.Name}";

            // 列级开关互斥：至多其一（收集声明后统一报出全部冲突项）
            var declaredFlags = new List<string>(5);
            if (columnAttr.IsConcurrencyToken)
                declaredFlags.Add("IsConcurrencyToken");

            if (columnAttr.IsIdentity)
                declaredFlags.Add("IsIdentity");

            if (columnAttr.EnumAsString)
                declaredFlags.Add("EnumAsString");

            if (columnAttr.IsJson)
                declaredFlags.Add("IsJson");

            if (columnAttr.ConverterType != null)
                declaredFlags.Add("ConverterType");

            if (declaredFlags.Count > 1)
                throw new InvalidOperationException($"属性 '{displayName}' 同时声明了 {string.Join("、", declaredFlags)}，列级开关至多其一。");

            // 单开关的适用性校验（互斥已由上面的声明收集保证，分支内只管各自约束）
            if (columnAttr.IsConcurrencyToken)
            {
                // 令牌值由框架生成 Guid 字节，类型必须为 byte[]（映射 varbinary(16)/BLOB 等二进制列）
                if (property.PropertyType != typeof(byte[]))
                    throw new InvalidOperationException($"属性 '{displayName}' 是并发令牌列，其类型 '{GetTypeDisplayName(property.PropertyType)}' 不是有效的类型（仅支持 byte[]）。");
            }
            else if (columnAttr.IsIdentity)
            {
                // 标识列类型：数据库自增仅支持 32/64 位整数
                ValidateIdentityColumnType(entityType, property);
            }
            else if (columnAttr.EnumAsString)
            {
                // EnumAsString 仅用于枚举类型列
                if (!effectiveType.IsEnum)
                    throw new InvalidOperationException($"属性 '{displayName}' 标记了 EnumAsString，但属性类型 '{GetTypeDisplayName(effectiveType)}' 不是枚举类型（可空枚举按基础枚举校验）。");
            }
            else if (columnAttr.IsJson)
            {
                // 值类型本可直接映射列，标记 IsJson 属误配
                if (property.PropertyType.IsValueType)
                    throw new InvalidOperationException($"属性 '{displayName}' 标记了 IsJson，但属性类型 '{GetTypeDisplayName(property.PropertyType)}' 是值类型，JSON 列属性必须是引用类型（复杂类型/集合）。");

                // string/byte[] 可直接映射列：误标会改变列存储形态（引号包裹/base64 编码），且禁用该列的文本匹配与排序能力，构建期显式拦截
                if (property.PropertyType == typeof(string) || property.PropertyType == typeof(byte[]))
                    throw new InvalidOperationException(
                        $"属性 '{displayName}' 标记了 IsJson，但属性类型 '{GetTypeDisplayName(property.PropertyType)}' 可直接映射为数据库列，无需 JSON 序列化；" +
                        "误标会改变列存储形态（引号包裹/base64 编码）并禁用文本匹配与排序等能力。");
            }
        }

        /// <summary>
        /// 校验自定义转换器类型并解析其转换方法，全部通过时输出两方向方法。
        /// <para>校验项：无参构造函数（构建元数据时实例化）、<c>ConvertFrom(object)</c> 与 <c>ConvertTo(T)</c>
        /// 公共实例方法（static 由 BindingFlags 排除、开放泛型方法定义由查找后判定）；
        /// ConvertFrom 返回类型须可赋给属性类型（可空属性按基础类型校验）；ConvertTo 按属性类型签名匹配。
        /// 按方法签名匹配，不强制要求实现 <see cref="IDbValueConverter{T}"/> 接口。</para>
        /// </summary>
        /// <param name="entityType">实体类型，用于异常消息。</param>
        /// <param name="property">属性信息，用于异常消息。</param>
        /// <param name="propertyType">属性类型，ConvertTo 的签名匹配目标。</param>
        /// <param name="converterType">转换器类型。</param>
        /// <param name="convertFromMethod">解析出的读取转换方法。</param>
        /// <param name="convertToMethod">解析出的写入转换方法。</param>
        /// <exception cref="InvalidOperationException">任一校验项不通过时抛出。</exception>
        private static void ValidateConverter(Type entityType, PropertyInfo property, Type propertyType, Type converterType, out MethodInfo convertFromMethod, out MethodInfo convertToMethod)
        {
            // 检查是否有无参构造函数
            if (converterType.GetConstructor(Type.EmptyTypes) == null)
                throw new InvalidOperationException($"属性 '{entityType}.{property.Name}' 的转换器类型 '{converterType}' 未定义无参构造函数。");

            // 检查 ConvertFrom(object) 公共实例方法：static 由 BindingFlags 排除（框架以实例调用并固化为
            // 编译委托），开放泛型方法定义（ConvertFrom<T>(object)）签名照常命中查找但无法调用，一并报出
            convertFromMethod = converterType.GetMethod("ConvertFrom", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(object) }, null);
            if (convertFromMethod == null || convertFromMethod.IsGenericMethodDefinition)
                throw new InvalidOperationException($"属性 '{entityType}.{property.Name}' 的转换器类型 '{converterType}' 未定义公共实例方法 ConvertFrom(object)。");

            // ConvertFrom 返回类型必须可赋给属性类型（可空属性按基础类型校验），否则读取路径运行期抛 InvalidCastException
            var assignmentTargetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (!assignmentTargetType.IsAssignableFrom(convertFromMethod.ReturnType))
                throw new InvalidOperationException(
                    $"属性 '{entityType}.{property.Name}' 的转换器类型 '{converterType}' 的 ConvertFrom 方法返回类型 " +
                    $"'{GetTypeDisplayName(convertFromMethod.ReturnType)}' 不能赋给属性类型 '{GetTypeDisplayName(propertyType)}'，ConvertFrom 返回值须可赋给属性类型。");

            // 检查是否有 ConvertTo(T) 公共实例方法（写入时转换），查找限定与排除规则同 ConvertFrom
            convertToMethod = converterType.GetMethod("ConvertTo", BindingFlags.Public | BindingFlags.Instance, null, new[] { propertyType }, null);
            if (convertToMethod == null || convertToMethod.IsGenericMethodDefinition)
                throw new InvalidOperationException($"属性 '{entityType}.{property.Name}' 的转换器类型 '{converterType}' 未定义公共实例方法 ConvertTo({GetTypeDisplayName(propertyType)})。");
        }
        #endregion

        #region 列元数据-通用方法
        /// <summary>
        /// 校验标识列的属性类型。仅支持 32 位和 64 位整数类型（int/long/uint/ulong），其他整数类型范围太小不适合做标识列。
        /// 主键列与非主键列的标识列共用本校验。
        /// </summary>
        private static void ValidateIdentityColumnType(Type entityType, PropertyInfo property)
        {
            if (!IdentityColumnTypes.Contains(property.PropertyType))
                throw new InvalidOperationException($"属性 '{entityType}.{property.Name}' 是标识列，其类型 '{GetTypeDisplayName(property.PropertyType)}' 不是有效的类型（仅支持 int/long/uint/ulong）。");
        }

        /// <summary>
        /// 获取类型在异常消息中的显示名：可空类型显示为 "基础类型?"（如 "Int32?"），
        /// 避免 <see cref="Type"/> 的 <c>Name</c> 显示内部名称（如 "Nullable`1"）。
        /// </summary>
        /// <param name="type">要显示的类型。</param>
        /// <returns>类型显示名。</returns>
        private static string GetTypeDisplayName(Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type);
            return underlyingType != null ? underlyingType.Name + "?" : type.Name;
        }

        /// <summary>
        /// 将特性声明的 <see cref="DbType"/> 归一为可空形式：null 表示未指定，按 .NET 类型推断。
        /// <para>特性实参不支持可空枚举（CS0655），且 <see cref="DbType.AnsiString"/> 枚举值为 0、
        /// 无法作哨兵区分"未指定"，故特性侧以 object 属性承载，此处校验取值为 <see cref="DbType"/> 枚举。</para>
        /// </summary>
        /// <param name="dbType">特性声明的数据库类型（null 或 <see cref="DbType"/> 枚举值）。</param>
        /// <returns>归一后的数据库类型，未指定时为 null。</returns>
        private static DbType? NormalizeDbType(object dbType)
        {
            // null（object 属性默认值）表示未指定，按 .NET 类型推断
            if (dbType == null)
                return null;

            // object 属性接受任意特性常量，非 DbType 取值（如误传字符串 "String"）构建期即报出
            if (dbType is not DbType value)
                throw new InvalidOperationException($"DbType 配置的值 '{dbType}' 不是有效的 System.Data.DbType 枚举值，请使用 DbType 枚举（如 DbType.String）。");

            return value;
        }

        /// <summary>
        /// 检查类型是否为数值主键类型（int/uint/long/ulong）。
        /// <para>数值主键在按主键查找（Find/FindAsync）时接受可转换的值（如传入 long 而主键为 int）；
        /// string/Guid 主键要求严格类型匹配。供 <see cref="EntitySet{T}"/> 的主键值校验与标识列类型检查共用。</para>
        /// </summary>
        /// <param name="type">要检查的类型。</param>
        /// <returns>是否为数值主键类型。</returns>
        internal static bool IsNumericPrimaryKeyType(Type type)
        {
            return NumericPrimaryKeyTypes.Contains(type);
        }
        #endregion
        #endregion

        #region 导航属性元数据
        /// <summary>
        /// 构建导航属性元数据列表。遍历实体类型的所有属性，为标记了 [Navigation] 特性的属性构建导航元数据。
        /// </summary>
        /// <param name="entityMetadata">本方实体的元数据。</param>
        /// <param name="properties">实体类型的所有可读写属性。</param>
        /// <returns>导航属性元数据列表。</returns>
        private static List<NavigationMetadata> BuildNavigationMetadata(EntityMetadata entityMetadata, IEnumerable<PropertyInfo> properties)
        {
            var navigations = new List<NavigationMetadata>();
            foreach (var property in properties)
            {
                var navAttr = property.GetCustomAttribute<NavigationAttribute>();
                if (navAttr == null)
                    continue;

                var nav = BuildNavigationMetadata(entityMetadata, property, navAttr);
                if (nav != null)
                    navigations.Add(nav);
            }

            return navigations;
        }

        /// <summary>
        /// 构建单个导航属性元数据。解析导航类型（1:1 引用或 1:N 集合）、对方实体类型、键映射（ThisKey/OtherKey），并验证导航属性的合法性。
        /// <para>导航属性不能同时标记 [Column] 或 [PrimaryKey] 特性。</para>
        /// <para>1:N 导航属性必须是 <see cref="List{T}"/> 或其可赋值的泛型接口（如 <see cref="IList{T}"/>、<see cref="ICollection{T}"/>）。</para>
        /// </summary>
        /// <param name="entityMetadata">本方实体的元数据。</param>
        /// <param name="property">导航属性信息。</param>
        /// <param name="navAttr">导航特性。</param>
        /// <returns>导航属性元数据。</returns>
        /// <exception cref="InvalidOperationException"></exception>
        private static NavigationMetadata BuildNavigationMetadata(EntityMetadata entityMetadata, PropertyInfo property, NavigationAttribute navAttr)
        {
            var entityType = entityMetadata.EntityType;
            var displayName = $"{entityType}.{property.Name}";

            // 解析导航形态（1:1 引用 / 1:N 集合）与对方类型（1:1 为实体类型，1:N 为集合元素类型）
            var (navigationType, targetType) = ResolveNavigationType(entityType, property);

            // 验证对方类型是否为有效的实体类型
            if (!IsValidEntityType(targetType))
                throw new InvalidOperationException($"导航属性 '{entityType}.{property.Name}' 的对方类型 '{targetType}' 不是有效的实体类型，请将其声明为具体实体类型或实体集合类型。");

            // 解析键属性：指定的直接查找，未指定的走约定推断
            // 1:N 集合：ThisKey=本方主键，OtherKey=对方外键；1:1 引用：ThisKey=本方外键，OtherKey=对方主键
            var targetMetadata = GetMetadata(targetType);
            var thisKeyColumn = string.IsNullOrEmpty(navAttr.ThisKey)
                ? InferThisKey(entityMetadata, property, navigationType)
                : FindThisKeyColumn(entityMetadata, property, navAttr.ThisKey);
            var otherKeyColumn = string.IsNullOrEmpty(navAttr.OtherKey)
                ? InferOtherKey(entityType, property, navigationType, targetMetadata)
                : FindOtherKeyColumn(entityType.ToString(), property, targetMetadata, navAttr.OtherKey);

            // 1:1 引用的 ThisKey 命中本方主键：约定推断路径由 InferThisKey 拦截自引用等误配；
            // 手动显式指定视为共享主键 1:1 模式（主键即外键，如 OrderDetail.OrderId）放行，
            // 写路径的外键同步（本方键列为默认值时回填对方键值）正是该模式的期望行为；
            // 记录诊断提示：误配会在运行期静默回填主键值，提示让模式启用可见
            if (!string.IsNullOrEmpty(navAttr.ThisKey) && thisKeyColumn.IsPrimaryKey)
                DiagnosticLog.Warn(nameof(EntityMetadataCache),
                    "导航属性 '{DisplayName}' 的 ThisKey '{ThisKey}' 命中本方主键，按共享主键 1:1 模式放行（主键即外键，写路径将在本方键列为默认值时回填对方键值）；若非有意配置请核对 ThisKey。",
                    displayName, navAttr.ThisKey);

            // 校验两端键列（转换器禁用 + 类型匹配）
            ValidateNavigationKeys(displayName, thisKeyColumn, otherKeyColumn);

            var builder = new NavigationMetadata.Builder
            {
                PropertyInfo = property,
                TargetType = targetType,
                NavigationType = navigationType,
                ThisKeyColumn = thisKeyColumn,
                OtherKeyColumn = otherKeyColumn,
                TargetEntityMetadata = targetMetadata,
            };

            return builder.Build();
        }

        /// <summary>
        /// 解析导航形态与对方类型：属性为集合（且非 string）时判定 1:N 集合（对方类型为元素类型），
        /// 否则判定 1:1 引用（对方类型为属性类型本身）。集合类型必须是泛型集合且能接受 List 赋值。
        /// </summary>
        /// <param name="entityType">实体类型，用于异常消息。</param>
        /// <param name="property">导航属性。</param>
        /// <returns>导航形态与对方类型的元组。</returns>
        private static (NavigationType NavigationType, Type TargetType) ResolveNavigationType(Type entityType, PropertyInfo property)
        {
            var propertyType = property.PropertyType;

            // 判断是 1:N（集合）还是 1:1（引用）
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType) && propertyType != typeof(string))
            {
                // 集合类型：必须是泛型集合，且能接受 List<T> 赋值
                if (!TypeHelper.IsGenericEnumerable(propertyType, out var elementType) ||
                    !propertyType.IsAssignableFrom(typeof(List<>).MakeGenericType(elementType)))
                {
                    throw new InvalidOperationException(
                        $"导航属性 '{entityType}.{property.Name}' 的集合类型 '{propertyType}' 不受支持，" +
                        $"请使用 List<T> 或 List<T> 可赋值的泛型接口（如 IList<T>、ICollection<T>）。");
                }

                return (NavigationType.OneToMany, elementType);
            }

            return (NavigationType.OneToOne, propertyType);
        }

        /// <summary>
        /// 校验导航两端键列的配置兼容性。
        /// <para>键列不支持自定义转换器：外键值会在属性赋值（SetValue）和数据库参数（GetValueForDb）两个场景间流转，
        /// 应用转换器会导致两者不一致，破坏外键同步与导航装配的正确性。</para>
        /// <para>键列不支持 EnumAsString/IsJson/IsConcurrencyToken：键的语义是单值等值匹配——EnumAsString 会令参数化形态
        /// （名称字符串）与对方键列存储形态（数值）不一致而静默不命中，IsJson 的复杂类型值无键语义，
        /// IsConcurrencyToken 是框架乐观锁专用的版本标记而非关系键。禁用清单是列级显式变换配置，
        /// 刻意不含全局转换器（键列经 GetValueForDb 参数化后全链路自洽，
        /// 见 <see cref="ColumnMetadata.HasConverterTransform"/> 的等值域说明）。</para>
        /// <para>两端键的 CLR 类型必须一致（可空包装视为与其基础类型一致）：导航装配按键值装箱相等匹配，
        /// int 主键配 long 外键等类型不匹配会静默装配失败（1:N 集合为空、1:1 引用为 null）。</para>
        /// </summary>
        /// <param name="displayName">导航属性显示名（实体类型全名.属性名），用于异常消息。</param>
        /// <param name="thisKeyColumn">本方键列。</param>
        /// <param name="otherKeyColumn">对方键列。</param>
        private static void ValidateNavigationKeys(string displayName, ColumnMetadata thisKeyColumn, ColumnMetadata otherKeyColumn)
        {
            ThrowIfInvalidNavigationKey(displayName, "ThisKey", thisKeyColumn);
            ThrowIfInvalidNavigationKey(displayName, "OtherKey", otherKeyColumn);

            var thisKeyUnderlyingType = Nullable.GetUnderlyingType(thisKeyColumn.PropertyType) ?? thisKeyColumn.PropertyType;
            var otherKeyUnderlyingType = Nullable.GetUnderlyingType(otherKeyColumn.PropertyType) ?? otherKeyColumn.PropertyType;
            if (thisKeyUnderlyingType != otherKeyUnderlyingType)
                throw new InvalidOperationException(
                    $"导航属性 '{displayName}' 的键类型不匹配：ThisKey '{thisKeyColumn.PropertyName}' 为 {GetTypeDisplayName(thisKeyColumn.PropertyType)}，" +
                    $"OtherKey '{otherKeyColumn.PropertyName}' 为 {GetTypeDisplayName(otherKeyColumn.PropertyType)}，两端键类型必须一致，请统一其中一端键属性的类型。");
        }

        /// <summary>
        /// 校验单端键列的形态资格（转换器/EnumAsString/IsJson/并发令牌均无键语义），命中时抛 <see cref="InvalidOperationException"/>。
        /// </summary>
        /// <param name="displayName">导航属性显示名（实体类型全名.属性名），用于异常消息。</param>
        /// <param name="keyName">键端名（ThisKey/OtherKey）。</param>
        /// <param name="column">本端键列。</param>
        private static void ThrowIfInvalidNavigationKey(string displayName, string keyName, ColumnMetadata column)
        {
            if (column.ConverterType != null)
                throw new InvalidOperationException($"导航属性 '{displayName}' 的 {keyName} '{column.PropertyName}' 不支持自定义转换器，请移除该键列的转换器配置。");

            if (column.EnumAsString)
                throw new InvalidOperationException($"导航属性 '{displayName}' 的 {keyName} '{column.PropertyName}' 不支持 EnumAsString：名称字符串存储与对方键的存储形态不一致，会静默不命中。");

            if (column.IsJson)
                throw new InvalidOperationException($"导航属性 '{displayName}' 的 {keyName} '{column.PropertyName}' 不支持 IsJson：键的语义是单值等值匹配，复杂类型 JSON 值无键语义。");

            if (column.IsConcurrencyToken)
                throw new InvalidOperationException($"导航属性 '{displayName}' 的 {keyName} '{column.PropertyName}' 是并发令牌列，令牌由框架乐观锁专用管理，无键语义。");
        }

        /// <summary>
        /// 在本方实体中查找 ThisKey 对应的列元数据。
        /// </summary>
        /// <param name="entityMetadata">本方实体的元数据。</param>
        /// <param name="property">导航属性信息，用于异常消息。</param>
        /// <param name="thisKey">ThisKey 属性名。</param>
        /// <returns>列元数据。</returns>
        /// <exception cref="InvalidOperationException"></exception>
        private static ColumnMetadata FindThisKeyColumn(EntityMetadata entityMetadata, PropertyInfo property, string thisKey)
        {
            var column = entityMetadata.GetColumn(thisKey);
            if (column == null)
                throw new InvalidOperationException(
                    $"导航属性 '{entityMetadata.EntityType}.{property.Name}' 的 ThisKey '{thisKey}' 在 '{entityMetadata.EntityType}' 中未找到对应的属性，" +
                    $"请检查 [Navigation(ThisKey = ...)] 指定的属性名。");

            return column;
        }

        /// <summary>
        /// 在对方实体中查找 OtherKey 对应的列元数据。
        /// </summary>
        /// <param name="ownerEntityTypeName">本方实体类型展示名（ToString），用于异常消息。</param>
        /// <param name="property">导航属性信息，用于异常消息。</param>
        /// <param name="targetMetadata">对方实体的元数据。</param>
        /// <param name="otherKey">OtherKey 属性名。</param>
        /// <returns>列元数据。</returns>
        /// <exception cref="InvalidOperationException"></exception>
        private static ColumnMetadata FindOtherKeyColumn(string ownerEntityTypeName, PropertyInfo property, EntityMetadata targetMetadata, string otherKey)
        {
            var column = targetMetadata.GetColumn(otherKey);
            if (column == null)
                throw new InvalidOperationException(
                    $"导航属性 '{ownerEntityTypeName}.{property.Name}' 的 OtherKey '{otherKey}' 在 '{targetMetadata.EntityType}' 中未找到对应的属性，" +
                    $"请检查 [Navigation(OtherKey = ...)] 指定的属性名。");

            return column;
        }

        /// <summary>
        /// 推断本方的键属性（ThisKey）。
        /// <para>1:N 集合：ThisKey 为本方主键（如 Customer.Id）。</para>
        /// <para>1:1 引用：ThisKey 为本方中名为 "{导航属性名}Id" 的外键（如 Order.Customer 导航的 Order.CustomerId）；
        /// 推断结果命中本方主键时抛 <see cref="InvalidOperationException"/>（自引用场景下 "{导航属性名}Id" 恰为主键名的约定误配）。</para>
        /// </summary>
        /// <param name="entityMetadata">本方实体的元数据。</param>
        /// <param name="property">导航属性信息。</param>
        /// <param name="navigationType">导航类型（1:1 引用或 1:N 集合）。</param>
        /// <returns>ThisKey 对应的列元数据。</returns>
        /// <exception cref="InvalidOperationException"></exception>
        private static ColumnMetadata InferThisKey(EntityMetadata entityMetadata, PropertyInfo property, NavigationType navigationType)
        {
            var entityType = entityMetadata.EntityType;

            if (navigationType == NavigationType.OneToMany)
            {
                if (entityMetadata.PrimaryKey == null)
                    throw new InvalidOperationException(
                        $"导航属性 '{entityType}.{property.Name}' 为 1:N 集合，但本方实体 '{entityType}' 未定义主键，" +
                        $"请定义主键或用 [Navigation(ThisKey = \"...\")] 显式指定本方键。");

                return entityMetadata.PrimaryKey;
            }
            else
            {
                var inferredThisKey = $"{property.Name}Id";
                var thisKeyColumn = entityMetadata.GetColumn(inferredThisKey);
                if (thisKeyColumn == null)
                {
                    throw new InvalidOperationException(
                        $"导航属性 '{entityType}.{property.Name}' 的 1:1 引用外键属性 '{inferredThisKey}' 未找到，" +
                        $"请使用 [Navigation(ThisKey = \"...\", OtherKey = \"...\")] 手动指定。");
                }

                // 约定推断命中本方主键（自引用场景下 "{导航属性名}Id" 恰为主键名）：
                // 该列是主键而非外键，装配会以本方主键匹配引用导致错误结果，构建期即报出
                if (thisKeyColumn.IsPrimaryKey)
                {
                    throw new InvalidOperationException(
                        $"导航属性 '{entityType}.{property.Name}' 的 1:1 约定推断命中了本方主键 '{thisKeyColumn.PropertyName}'（自引用场景下该列是主键而非外键），" +
                        $"请使用 [Navigation(ThisKey = \"...\", OtherKey = \"...\")] 显式指定外键。");
                }

                return thisKeyColumn;
            }
        }

        /// <summary>
        /// 推断对方实体的键属性（OtherKey）。
        /// <para>1:N 集合：OtherKey 为对方实体中名为 "{本方类型名}Id" 的外键（如 Customer.Orders 的 OtherKey 为 Order.CustomerId）；
        /// 推断结果命中对方主键时抛 <see cref="InvalidOperationException"/>（自引用场景下 "{本方类型名}Id" 恰为对方主键名的约定误配）。</para>
        /// <para>1:1 引用：OtherKey 为对方实体的主键（如 Order.Customer 的 OtherKey 为 Customer.Id）。</para>
        /// </summary>
        /// <param name="entityType">本方实体类型，用于构造约定外键名。</param>
        /// <param name="property">导航属性信息，用于异常消息。</param>
        /// <param name="navigationType">导航类型（1:1 引用或 1:N 集合）。</param>
        /// <param name="targetMetadata">对方实体的元数据。</param>
        /// <returns>OtherKey 对应的列元数据。</returns>
        /// <exception cref="InvalidOperationException"></exception>
        private static ColumnMetadata InferOtherKey(Type entityType, PropertyInfo property, NavigationType navigationType, EntityMetadata targetMetadata)
        {
            var targetType = targetMetadata.EntityType;

            if (navigationType == NavigationType.OneToMany)
            {
                var inferredForeignKey = $"{entityType.Name}Id";
                var foreignKeyColumn = targetMetadata.GetColumn(inferredForeignKey);
                if (foreignKeyColumn == null)
                {
                    throw new InvalidOperationException(
                       $"导航属性 '{entityType}.{property.Name}' 的 1:N 集合外键属性 '{targetType}.{inferredForeignKey}' 未找到，" +
                       $"请使用 [Navigation(ThisKey = \"...\", OtherKey = \"...\")] 手动指定。");
                }

                // 约定推断命中对方实体的主键（自引用场景下 "{本方类型名}Id" 恰为对方主键名）：
                // 该列是对方主键而非外键，装配会把本方实体自身作为唯一匹配元素，构建期即报出
                if (foreignKeyColumn.IsPrimaryKey)
                {
                    throw new InvalidOperationException(
                        $"导航属性 '{entityType}.{property.Name}' 的 1:N 约定推断命中了对方实体 '{targetType}' 的主键 '{foreignKeyColumn.PropertyName}'（自引用场景下该列是主键而非外键），" +
                        $"请使用 [Navigation(ThisKey = \"...\", OtherKey = \"...\")] 显式指定外键。");
                }

                return foreignKeyColumn;
            }
            else
            {
                if (targetMetadata.PrimaryKey == null)
                    throw new InvalidOperationException(
                        $"导航属性 '{entityType}.{property.Name}' 为 1:1 引用，但对方实体 '{targetType}' 未定义主键，" +
                        $"请定义主键或用 [Navigation(OtherKey = \"...\")] 显式指定对方键。");

                return targetMetadata.PrimaryKey;
            }
        }
        #endregion
    }
}
