using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 实体映射器核心，负责构建表达式树并缓存映射委托，实现高性能的 DbDataReader 到实体映射。
    /// <para>模块内部实现类：仅 EntityMapper / EntityMapper&lt;T&gt; / EntityMapperCachePolicy 可调用，
    /// 框架其余层一律经上述门面进入（含文档 cref 引用）。</para>
    /// </summary>
    internal static class EntityMapperCore
    {
        #region 委托缓存
        /// <summary>
        /// 实体映射委托缓存。键为实体类型、数据读取器架构哈希值与全局转换器注册表版本号的组合键，值为缓存条目。
        /// <para>同一实体类型在不同 SQL 查询的结果集下可能具有不同的列顺序和类型，因此需要按架构哈希值区分缓存。</para>
        /// <para>全局转换器版本号必须纳入缓存键：仅靠 <see cref="DbValueConverterRegistry.Changed"/> 事件清空缓存
        /// 存在"编译回写晚于清空"的失效窗口，旧语义委托会在清空后的缓存中复活；纳入版本号后，
        /// 变更前写入的旧版本条目不再被新版本键命中，窗口随新版本重建自然消除。</para>
        /// <para>每个键只编译一份委托；非泛型入口（导航装配、多结果集）通过委托协变强转为
        /// <see cref="Func{DbDataReader, Object}"/> 复用同一委托，避免 DynamicInvoke 反射与二次编译。</para>
        /// </summary>
        private static readonly ConcurrentDictionary<(Type EntityType, long SchemaHash, int ConverterVersion), CacheEntry> MapperCache
            = new ConcurrentDictionary<(Type EntityType, long SchemaHash, int ConverterVersion), CacheEntry>();

        /// <summary>
        /// 投影映射委托缓存（单列标量与位置构造多列共用：匿名类型/元组）。键为组合键 <see cref="ProjectionMapperKey"/>，值为缓存条目（编译后的投影委托 + 最后访问时间戳；单列为标量委托，多列为位置构造装配委托）。
        /// <para>实体类型不参与委托构建，仅用于 <see cref="ClearCache"/> 的按类型失效；同一实体同一列可存在不同结果类型的投影（如同一列分别按 int 与 long 投影），
        /// 结果类型必须纳入缓存键，否则跨类型强转委托会失败。</para>
        /// <para>列元数据必须纳入缓存键：同一列的不同转换器与 DBNull 处理配置会编译出不同委托，仅按列缓存会让首个配置固化，后续配置被静默忽略。
        /// 列元数据来自 <see cref="EntityMetadataCache"/> 的缓存实例，实例稳定，以引用相等语义参与键比较。</para>
        /// <para>各位置的字段类型必须纳入缓存键：强类型 getter 按字段类型选择（COUNT(*) 等聚合列的字段类型与属性类型不一致），
        /// 同一列经 CAST 等变换产生不同字段类型时委托形态不同，不区分会命中错误版本。</para>
        /// <para>与实体映射键不同，投影键不含结果集架构哈希：委托固定按位置逐列读取（单列即位置 0），读取行为完全由列元数据与对应位置字段类型决定，
        /// 与结果集包含多少列、顺序如何无关，同一列组跨查询形态共享同一份委托。入口处校验各位置列名与元数据一致，保证缓存命中时的读取目标正确。</para>
        /// <para>全局转换器版本号纳入缓存键的理由与 <see cref="MapperCache"/> 一致。</para>
        /// </summary>
        private static readonly ConcurrentDictionary<ProjectionMapperKey, CacheEntry> ProjectionMapperCache
            = new ConcurrentDictionary<ProjectionMapperKey, CacheEntry>();

        /// <summary>
        /// 缓存条目持有者：委托 + 最后访问时间戳（<see cref="StopwatchClock.ElapsedTicks"/>，库内共享单调时钟）。
        /// <para>时间戳仅作淘汰启发式（LRU/空闲过期）：命中路径 Volatile 写、清扫路径 Volatile 读
        /// （32 位进程下 long 写非原子，Volatile 保证清扫判断不读到撕裂值而误判热条目）；命中即续期，被命中的条目在后续清扫中必然存活。</para>
        /// </summary>
        private sealed class CacheEntry
        {
            public readonly Lazy<Delegate> Mapper;// 编译后的映射委托
            public long LastAccessTicks;// 最后访问时间戳

            public CacheEntry(Lazy<Delegate> value)
            {
                Mapper = value;
                LastAccessTicks = StopwatchClock.ElapsedTicks;
            }
        }

        /// <summary>
        /// 投影缓存键：列元数据、对应位置字段类型、成员绑定与绑定构造函数按序全等才视为同一键
        /// （单列投影为 1 个元素的特例），供 <see cref="ProjectionMapperCache"/> 使用；数组所有权契约见构造函数。
        /// <para>绑定构造函数纳入键的理由：命名类型带参构造的绑定结果由选择器 lambda 决定而非 TResult 静态结构
        /// （同一 TResult 的多构造类型可被不同 lambda 分别绑定，实参经转换剥离后列组可能完全相同），不区分会错命中其他构造编译的委托。</para>
        /// </summary>
        private readonly struct ProjectionMapperKey : IEquatable<ProjectionMapperKey>
        {
            private readonly Type entityType;
            private readonly Type resultType;
            private readonly ColumnMetadata[] columns;
            private readonly Type[] fieldTypes;
            private readonly MemberInfo[] members;// 命名类型初始化器绑定的目标成员（解析产物）；位置构造（匿名类型/元组/命名构造）/单列为 null
            private readonly ConstructorInfo constructor;// 命名类型带参构造绑定的构造函数；匿名类型/元组（TResult 唯一构造即绑定结果）与单列为 null
            private readonly int converterVersion;

            /// <summary>实体类型（供 ClearCache 按实体类型过滤缓存键；private 字段对包含类型不可见，须以属性暴露）。</summary>
            public Type EntityType => entityType;

            public ProjectionMapperKey(Type entityType, Type resultType, ColumnMetadata[] columns, Type[] fieldTypes, MemberInfo[] members, ConstructorInfo constructor, int converterVersion)
            {
                // 数组零克隆（所有权转移）：字典键在缓存中长期存活，「构造后不可变」由所有权契约保证而非复制——
                // 唯一调用方 GetOrAddProjectionMapper 构造键后不再读写传入数组（fieldTypes 为其本方法新建且仅在编译期读取，
                // columns/members 来自 plan 且解析层独占新建、构建后不再改写），持有原数组即持有不可变内容。
                // 若未来出现第二个调用方，须先满足同样的所有权契约（传入后不再触碰），否则恢复克隆
                this.entityType = entityType;
                this.resultType = resultType;
                this.columns = columns;
                this.fieldTypes = fieldTypes;
                this.members = members;
                this.constructor = constructor;
                this.converterVersion = converterVersion;
            }

            public bool Equals(ProjectionMapperKey other)
            {
                if (entityType != other.entityType
                    || resultType != other.resultType
                    || converterVersion != other.converterVersion
                    || columns.Length != other.columns.Length
                    || members == null != (other.members == null)
                    || constructor != other.constructor)
                {
                    return false;
                }

                for (int i = 0; i < columns.Length; i++)
                {
                    // 列元数据与成员绑定均为解析产物且 CLR 驻留缓存实例（ColumnMetadata 来自元数据缓存、
                    // MemberInfo 按 (类型, 成员) 缓存），引用即身份（引用快路径 + Equals 兜底）；
                    // Type 由 CLR 驻留（每类型全局唯一实例），== 为类型语义比较
                    if (!ReferenceEquals(columns[i], other.columns[i]) || fieldTypes[i] != other.fieldTypes[i])
                        return false;

                    // 成员绑定与列数组平行（解析层保证同长），两侧 null 形态已判等排除（同 null 或同非 null），
                    // 同 null 时短路跳过，以列数为准
                    if (members != null && !ReferenceEquals(members[i], other.members[i]))
                        return false;
                }

                return true;
            }

            public override bool Equals(object obj) => obj is ProjectionMapperKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = entityType.GetHashCode();
                    hash = hash * 31 + resultType.GetHashCode();
                    hash = hash * 31 + converterVersion;
                    hash = hash * 31 + (constructor?.GetHashCode() ?? 0);

                    for (int i = 0; i < columns.Length; i++)
                    {
                        hash = hash * 31 + RuntimeHelpers.GetHashCode(columns[i]);
                        hash = hash * 31 + fieldTypes[i].GetHashCode();

                        // 成员绑定与列数组平行（解析层保证同长），在第 i 列位置紧邻混入——哈希成分序与 Equals 的比较序同构
                        if (members != null)
                            hash = hash * 31 + RuntimeHelpers.GetHashCode(members[i]);
                    }

                    return hash;
                }
            }
        }
        #endregion

        #region 读取方法与反射缓存
        /// <summary>
        /// DbDataReader 强类型读取方法缓存，按方法名索引，用于表达式树构建时快速获取 MethodInfo（强类型方法比 GetValue + 转换性能更优）。
        /// </summary>
        private static readonly Dictionary<string, MethodInfo> DbDataReaderMethodCache = new Dictionary<string, MethodInfo>
        {
            ["IsDBNull"] = typeof(DbDataReader).GetMethod("IsDBNull", new[] { typeof(int) }),
            ["GetValue"] = typeof(DbDataReader).GetMethod("GetValue", new[] { typeof(int) }),
            ["GetString"] = typeof(DbDataReader).GetMethod("GetString", new[] { typeof(int) }),
            ["GetBoolean"] = typeof(DbDataReader).GetMethod("GetBoolean", new[] { typeof(int) }),
            ["GetInt32"] = typeof(DbDataReader).GetMethod("GetInt32", new[] { typeof(int) }),
            ["GetInt64"] = typeof(DbDataReader).GetMethod("GetInt64", new[] { typeof(int) }),
            ["GetInt16"] = typeof(DbDataReader).GetMethod("GetInt16", new[] { typeof(int) }),
            ["GetByte"] = typeof(DbDataReader).GetMethod("GetByte", new[] { typeof(int) }),
            ["GetDateTime"] = typeof(DbDataReader).GetMethod("GetDateTime", new[] { typeof(int) }),
            ["GetDecimal"] = typeof(DbDataReader).GetMethod("GetDecimal", new[] { typeof(int) }),
            ["GetDouble"] = typeof(DbDataReader).GetMethod("GetDouble", new[] { typeof(int) }),
            ["GetFloat"] = typeof(DbDataReader).GetMethod("GetFloat", new[] { typeof(int) }),
            ["GetGuid"] = typeof(DbDataReader).GetMethod("GetGuid", new[] { typeof(int) }),
            // ["GetStream"] = typeof(DbDataReader).GetMethod("GetStream", new[] { typeof(int) }),
        };

        /// <summary>
        /// .NET 类型到 DbDataReader 强类型读取方法名称的映射，用于表达式树构建时选择最优读取方法。
        /// <para>字节数组（byte[]）使用 GetValue 方法（无专用读取方法）；字符（char）不进本表，统一走
        /// <see cref="EntityValueConverter.ConvertFrom"/> 兜底（string → char 归一）。</para>
        /// </summary>
        private static readonly Dictionary<Type, string> TypeToReaderMethodNameCache = new Dictionary<Type, string>
        {
            [typeof(string)] = "GetString",
            [typeof(bool)] = "GetBoolean",
            [typeof(int)] = "GetInt32",
            [typeof(long)] = "GetInt64",
            [typeof(short)] = "GetInt16",
            [typeof(byte)] = "GetByte",
            [typeof(DateTime)] = "GetDateTime",
            [typeof(decimal)] = "GetDecimal",
            [typeof(double)] = "GetDouble",
            [typeof(float)] = "GetFloat",
            [typeof(Guid)] = "GetGuid",
            [typeof(byte[])] = "GetValue",  // 字节数组使用 GetValue 方法
            // [typeof(Stream)] = "GetStream",
        };

        /// <summary>
        /// 整数族位宽与符号表，判"源值集 ⊆ 目标值集"（无损宽化）的依据，类型 → (位宽, 是否符号)。
        /// </summary>
        private static readonly Dictionary<Type, KeyValuePair<int, bool>> IntegralRanks = new Dictionary<Type, KeyValuePair<int, bool>>
        {
            [typeof(sbyte)] = new KeyValuePair<int, bool>(1, true),
            [typeof(byte)] = new KeyValuePair<int, bool>(1, false),
            [typeof(short)] = new KeyValuePair<int, bool>(2, true),
            [typeof(ushort)] = new KeyValuePair<int, bool>(2, false),
            [typeof(int)] = new KeyValuePair<int, bool>(4, true),
            [typeof(uint)] = new KeyValuePair<int, bool>(4, false),
            [typeof(long)] = new KeyValuePair<int, bool>(8, true),
            [typeof(ulong)] = new KeyValuePair<int, bool>(8, false),
        };

        // EntityValueConverter.ConvertFrom 静态方法缓存，用于兜底类型转换的表达式树构建
        private static readonly MethodInfo EntityValueConverter_ConvertFromMethod = typeof(EntityValueConverter)
            .GetMethod(nameof(EntityValueConverter.ConvertFrom), new[] { typeof(object), typeof(Type) });

        // JsonColumnSerializer.Deserialize 静态方法缓存，用于 JSON 列读取的表达式树构建
        private static readonly MethodInfo JsonColumnSerializer_DeserializeMethod = typeof(JsonColumnSerializer)
            .GetMethod(nameof(JsonColumnSerializer.Deserialize), new[] { typeof(string), typeof(Type) });

        // NumericValueGuard.ConvertOrThrow 静态方法缓存，构建期按（字段类型，属性基础类型）实例化强类型
        // 行内守卫调用编进委托，行内无装箱（见 CreateNumericConversionExpression）
        private static readonly MethodInfo NumericValueGuard_ConvertOrThrowMethod = typeof(NumericValueGuard)
            .GetMethod(nameof(NumericValueGuard.ConvertOrThrow), BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

        // Func<object, object>.Invoke 方法缓存，用于全局转换器委托调用的表达式树构建
        private static readonly MethodInfo FuncObjectObject_InvokeMethod = typeof(Func<object, object>)
            .GetMethod("Invoke", new[] { typeof(object) });

        // InvalidCastException 字符串构造函数缓存，用于 DBNull 抛异常模式下的表达式树构建
        private static readonly ConstructorInfo InvalidCastExceptionConstructor = typeof(InvalidCastException)
            .GetConstructor(new[] { typeof(string) });
        #endregion

        /// <summary>
        /// 静态构造函数：订阅全局转换器注册表的变更事件，在转换器注册/取消注册/清空时联动失效全部映射委托缓存。
        /// <para>映射委托在构建时会固化当时的全局转换器分支，注册表变更后缓存不失效会导致后注册的转换器被静默忽略、后注销的转换器引发空引用。</para>
        /// </summary>
        static EntityMapperCore()
        {
            DbValueConverterRegistry.Changed += ClearAllCache;
        }

        #region 缓存失效
        /// <summary>
        /// 清理指定实体类型的映射委托缓存（含实体映射与投影映射）。
        /// </summary>
        /// <param name="entityType">要清理缓存的实体类型。</param>
        internal static void ClearCache(Type entityType)
        {
            // 两个缓存均为扁平组合键，按实体类型过滤移除；失效是罕见事件（元数据/转换器变更），遍历键的成本可忽略
            foreach (var key in MapperCache.Keys)
            {
                if (key.EntityType == entityType)
                    MapperCache.TryRemove(key, out _);
            }

            foreach (var key in ProjectionMapperCache.Keys)
            {
                if (key.EntityType == entityType)
                    ProjectionMapperCache.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// 清理所有实体类型的映射委托缓存（含实体映射与投影映射）。
        /// <para>全局转换器注册表变更时通过 <see cref="DbValueConverterRegistry.Changed"/> 事件自动触发。</para>
        /// </summary>
        internal static void ClearAllCache()
        {
            MapperCache.Clear();
            ProjectionMapperCache.Clear();
        }

        /// <summary>映射委托缓存条目数（诊断与测试用）。</summary>
        internal static int MapperCacheEntryCount => MapperCache.Count;

        /// <summary>投影映射缓存条目数（诊断与测试用）。</summary>
        internal static int ProjectionMapperCacheEntryCount => ProjectionMapperCache.Count;

        /// <summary>按当前策略立即清扫两个映射缓存，供 <see cref="EntityMapperCachePolicy.Sweep"/> 显式调用（插入期清扫由各入口自带）。</summary>
        internal static void SweepCaches()
        {
            SweepCache(MapperCache);
            SweepCache(ProjectionMapperCache);
        }

        /// <summary>
        /// 单缓存清扫：先移除空闲超期条目，仍超出上限时按最后访问时间移除最旧直至上限内。
        /// <para>被清扫条目若正被其他线程使用，持有引用不受影响，仅下次访问重新编译（单键单次编译语义不变）；
        /// 扫描与插入间无原子性，上限为软上限。</para>
        /// </summary>
        /// <param name="cache">目标缓存。</param>
        private static void SweepCache<TKey>(ConcurrentDictionary<TKey, CacheEntry> cache)
        {
            // 空闲过期：无条件全扫移除超期条目（命中即续期，刚被访问的条目时间戳新鲜，不会被误判；
            // Volatile 读保证 32 位进程下不读到撕裂时间戳）。过期时长换算为秒表原始刻度（防极端长 TimeSpan 溢出取半程钳制）
            var idleExpiration = EntityMapperCachePolicy.IdleExpiration;
            if (idleExpiration.HasValue)
            {
                var idleTicks = (long)Math.Min(idleExpiration.Value.TotalSeconds * StopwatchClock.Frequency, long.MaxValue / 2);
                var now = StopwatchClock.ElapsedTicks;

                foreach (var pair in cache)
                {
                    if (now - Volatile.Read(ref pair.Value.LastAccessTicks) >= idleTicks)
                        cache.TryRemove(pair.Key, out _);
                }
            }

            // 上限封顶：按最后访问时间移除最旧直至上限内（Ticks 越小越旧）
            var maxEntries = EntityMapperCachePolicy.MaxEntries;
            if (maxEntries.HasValue)
            {
                var overflow = cache.Count - maxEntries.Value;
                if (overflow > 0)
                {
                    var victims = cache
                        .OrderBy(pair => Volatile.Read(ref pair.Value.LastAccessTicks))
                        .Take(overflow)
                        .Select(pair => pair.Key)
                        .ToList();

                    foreach (var key in victims)
                    {
                        cache.TryRemove(key, out _);
                    }
                }
            }
        }
        #endregion

        #region 实体映射
        /// <summary>
        /// 获取或添加实体映射委托。先根据数据读取器架构计算哈希值，再从缓存中查找；若未命中则构建新的映射委托并加入缓存。
        /// </summary>
        /// <param name="entityType">实体类型。</param>
        /// <param name="reader">数据读取器，用于计算架构哈希值和首次构建时的列映射。</param>
        /// <returns>编译后的实体映射委托（从数据读取器到实体）。</returns>
        internal static Delegate GetOrAddMapper(Type entityType, DbDataReader reader)
        {
            var key = (EntityType: entityType,
                SchemaHash: GetSchemaHash(reader),
                ConverterVersion: DbValueConverterRegistry.Version);

            // TryGetValue 命中路径零闭包分配；miss 时预构造 Lazy（ExecutionAndPublication 保证单键单次编译），
            // 命中即续期（清理永远不动刚续期的热条目），续期后直接返回——清扫仅由插入路径触发
            if (MapperCache.TryGetValue(key, out var entry))
            {
                Volatile.Write(ref entry.LastAccessTicks, StopwatchClock.ElapsedTicks);
                return entry.Mapper.Value;
            }

            // miss：插入并编译（GetOrAdd 用无 lambda 重载避免每次调用的闭包分配；
            // CacheEntry 构造时间戳即本次访问的续期，其后的清扫决策基于完整访问状态）
            var lazy = new Lazy<Delegate>(() => CreateMapper(entityType, reader), LazyThreadSafetyMode.ExecutionAndPublication);
            entry = MapperCache.GetOrAdd(key, new CacheEntry(lazy));

            // 插入期清扫（miss = 编译新委托的低频时刻）：策略未设置（默认）时跳过，行为与无治理完全一致
            if (EntityMapperCachePolicy.MaxEntries.HasValue || EntityMapperCachePolicy.IdleExpiration.HasValue)
                SweepCache(MapperCache);

            return entry.Mapper.Value;
        }

        /// <summary>
        /// 计算数据读取器架构的哈希值。基于所有列的名称和类型生成 64 位 FNV-1a 哈希，用于区分不同 SQL 查询的结果集架构。
        /// </summary>
        /// <param name="reader">数据读取器。</param>
        /// <returns>架构哈希值。</returns>
        private static long GetSchemaHash(DbDataReader reader)
        {
            unchecked
            {
                // FNV-1a 64 位偏移基数与质数；64 位宽度降低大量列组合下的碰撞概率，避免不同架构共用同一映射委托导致错列赋值
                const ulong offsetBasis = 14695981039346656037;
                const ulong prime = 1099511628211;

                var hash = offsetBasis;
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    hash = (hash ^ (ulong)reader.GetName(i).GetHashCode()) * prime;
                    hash = (hash ^ (ulong)reader.GetFieldType(i).GetHashCode()) * prime;
                }

                return (long)hash;
            }
        }

        /// <summary>
        /// 创建实体映射委托。构建表达式树：new TEntity() → 逐列赋值 → 返回实体，编译为从数据读取器到实体的映射委托。
        /// </summary>
        /// <param name="entityType">实体类型。</param>
        /// <param name="reader">数据读取器，仅在首次创建列映射时使用。</param>
        /// <returns>编译后的实体映射委托。</returns>
        private static Delegate CreateMapper(Type entityType, DbDataReader reader)
        {
            var columnMapping = CreateColumnIndexMapping(entityType, reader);
            var readerParam = Expression.Parameter(typeof(DbDataReader), "reader");
            var body = BuildMapperBody(entityType, columnMapping, reader, readerParam);

            // 动态创建泛型委托类型 Func<DbDataReader, TEntity>；
            // 实体为引用类型时，该委托可经协变强转为 Func<DbDataReader, object>（非泛型门面调用点内联强转），无需单独编译对象版本
            // 最终生成表达式树（C# 伪代码）：reader => {
            //                                  var entity = new T();
            //                                  entity.Id = reader.GetInt32(0);
            //                                  entity.Name = reader.GetString(1);
            //                                  …;
            //                                  return entity; }
            var lambdaType = typeof(Func<,>).MakeGenericType(typeof(DbDataReader), entityType);
            return Expression.Lambda(lambdaType, body, readerParam).Compile();
        }

        /// <summary>
        /// 创建列索引到列元数据的映射。遍历数据读取器的所有列，将匹配到的实体属性列建立索引。
        /// <para>未匹配的列会输出调试警告，但不抛出异常（允许查询返回实体未映射的额外列）；
        /// 同一属性被多个结果集列匹配（结果集存在重复列名）时输出重复映射警告——按位置语义后列赋值覆盖前列，
        /// 同值重复无害，同名不同值（如 SELECT Name, Age AS Name）会静默错数据，依赖警告暴露。</para>
        /// </summary>
        /// <param name="entityType">实体类型。</param>
        /// <param name="reader">数据读取器。</param>
        /// <returns>列索引到列元数据的字典，键为数据读取器中的列序号。</returns>
        private static Dictionary<int, ColumnMetadata> CreateColumnIndexMapping(Type entityType, DbDataReader reader)
        {
            // 构建列名到列元数据的映射；列名唯一性由元数据构建时的判重保证（忽略大小写）
            var propertyMap = new Dictionary<string, ColumnMetadata>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in EntityMetadataCache.GetColumns(entityType))
            {
                propertyMap[column.ColumnName] = column;
            }

            var columnMapping = new Dictionary<int, ColumnMetadata>();
            var firstMappedIndex = new Dictionary<ColumnMetadata, int>();// 各属性首个映射索引（结果集重复列检测，元数据为引用身份）

            for (int i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);// 列名
                if (propertyMap.TryGetValue(columnName, out var columnMetadata))
                {
                    if (firstMappedIndex.TryGetValue(columnMetadata, out var firstIndex))
                        DiagnosticLog.Warn(nameof(EntityMapperCore),
                            "结果集第 {Index} 列 '{ColumnName}' 与第 {FirstIndex} 列重复映射到属性 '{PropertyName}'，后列赋值将覆盖前列，若非有意重复请核对 SELECT 列。",
                            i, columnName, firstIndex, columnMetadata.PropertyName);
                    else
                        firstMappedIndex[columnMetadata] = i;

                    columnMapping[i] = columnMetadata;
                }
                else
                {
                    DiagnosticLog.Warn(nameof(EntityMapperCore), "DbDataReader 中的列 '{ColumnName}' 未找到可映射的属性。", columnName);
                }
            }

            return columnMapping;
        }

        /// <summary>
        /// 构建映射委托的表达式树主体：new TEntity() → 逐列赋值 → 返回实体。
        /// </summary>
        /// <param name="entityType">实体类型。</param>
        /// <param name="columnMapping">列索引到列元数据的映射。</param>
        /// <param name="reader">数据读取器，仅在构建期间读取列字段类型（委托编译一次，读取不在行内）。</param>
        /// <param name="readerParam">数据读取器参数表达式。</param>
        /// <returns>表达式树主体。</returns>
        private static Expression BuildMapperBody(
            Type entityType,
            Dictionary<int, ColumnMetadata> columnMapping,
            DbDataReader reader,
            ParameterExpression readerParam)
        {
            var entityVar = Expression.Variable(entityType, "entity");
            var statements = new List<Expression>
            {
                // 实例化实体，生成：entity = new T()
                Expression.Assign(entityVar, Expression.New(entityType))
            };

            // 逐列生成赋值语句（传读取器实际字段类型，供 getter 选择参考聚合列等不一致场景），生成：entity.<属性> = <列读取表达式>
            foreach (var mapping in columnMapping)
            {
                var assignExpr = CreateAssignmentExpression(
                    readerParam,
                    entityVar,
                    mapping.Key,
                    mapping.Value,
                    reader.GetFieldType(mapping.Key));
                statements.Add(assignExpr);
            }

            // 返回实体，生成：return entity
            statements.Add(entityVar);
            return Expression.Block(new[] { entityVar }, statements);
        }

        /// <summary>
        /// 创建属性赋值表达式，根据 <see cref="DbNullHandling"/> 配置处理 DBNull 值。
        /// <list type="bullet">
        /// <item>DbNullHandling.Default：根据属性类型自动选择（可空类型与引用类型使用 SkipAssignment，非可空值类型使用 ProviderException）。</item>
        /// <item>DbNullHandling.SkipAssignment：if (!reader.IsDBNull(i)) entity.Prop = reader.GetValue(i);</item>
        /// <item>DbNullHandling.UseDefaultValue：entity.Prop = reader.IsDBNull(i) ? default(T) : reader.GetValue(i);</item>
        /// <item>DbNullHandling.ThrowException：if (reader.IsDBNull(i)) throw new InvalidCastException(); else entity.Prop = reader.GetValue(i);</item>
        /// <item>DbNullHandling.ProviderException：entity.Prop = reader.GetValue(i);（不检查 NULL，NULL 时由 ADO.NET 提供程序抛出原生异常；可空类型与引用类型退化为 SkipAssignment）</item>
        /// </list>
        /// </summary>
        /// <param name="readerParam">数据读取器参数表达式。</param>
        /// <param name="entityVar">实体变量表达式。</param>
        /// <param name="columnIndex">列索引。</param>
        /// <param name="columnMetadata">列元数据。</param>
        /// <param name="fieldType">读取器中该列的实际字段类型，供 getter 选择。</param>
        /// <returns>属性赋值表达式。</returns>
        private static Expression CreateAssignmentExpression(
            ParameterExpression readerParam,
            ParameterExpression entityVar,
            int columnIndex,
            ColumnMetadata columnMetadata,
            Type fieldType)
        {
            var propertyName = columnMetadata.PropertyName;
            var propertyType = columnMetadata.PropertyType;
            var propertyAccess = Expression.Property(entityVar, propertyName);// 生成：entity.Id
            var isDbNullCall = Expression.Call(
                readerParam,
                DbDataReaderMethodCache["IsDBNull"],
                Expression.Constant(columnIndex));// 生成：reader.IsDBNull(columnIndex)

            // 创建列值读取表达式（策略分派入口：列转换器/JSON/全局转换器/强类型直读/通用兜底，详见 CreateColumnReadExpression）
            var columnReadExpr = CreateColumnReadExpression(readerParam, columnIndex, columnMetadata, fieldType);

            // 根据 DbNullHandling 配置处理
            var dbNullHandling = ResolveDbNullHandling(columnMetadata);
            switch (dbNullHandling)
            {
                case DbNullHandling.SkipAssignment:// 跳过赋值，生成：if (!reader.IsDBNull(i)) entity.Prop = read
                    {
                        var assignExpr = Expression.Assign(propertyAccess, columnReadExpr);
                        return Expression.IfThen(Expression.Not(isDbNullCall), assignExpr);
                    }
                case DbNullHandling.UseDefaultValue:// 使用默认值，生成：entity.Prop = reader.IsDBNull(i) ? default(TProperty) : read
                    {
                        var conditionExpr = Expression.Condition(isDbNullCall, Expression.Default(propertyType), columnReadExpr);
                        return Expression.Assign(propertyAccess, conditionExpr);
                    }
                case DbNullHandling.ProviderException:// 由提供程序抛出异常（仅非可空值类型），生成：entity.Prop = read
                    {
                        // 不检查 IsDBNull，若数据库值为 NULL，由 ADO.NET 提供程序抛出原生异常
                        return Expression.Assign(propertyAccess, columnReadExpr);
                    }
                case DbNullHandling.ThrowException:// 抛出异常，生成：if (reader.IsDBNull(i)) throw new InvalidCastException(...); else entity.Prop = read
                    {
                        var throwExpr = CreateThrowInvalidCastExceptionExpression(propertyName);
                        var assignExpr = Expression.Assign(propertyAccess, columnReadExpr);
                        return Expression.IfThenElse(isDbNullCall, throwExpr, assignExpr);
                    }
                default:
                    throw new NotSupportedException($"不支持的 DbNullHandling 值：{dbNullHandling}。");
            }
        }
        #endregion

        #region DbNull 处理
        /// <summary>
        /// 解析列的实际 DBNull 处理策略，实体赋值与投影共用。
        /// <list type="bullet">
        /// <item>DbNullHandling.Default：根据属性类型自动选择（可空类型与引用类型使用 SkipAssignment，非可空值类型使用 ProviderException）。</item>
        /// <item>DbNullHandling.ProviderException：可空类型与引用类型列的 NULL 是合法值，不能跳过检查（否则合法 NULL 会触发提供程序异常），退化为 SkipAssignment。</item>
        /// </list>
        /// </summary>
        /// <param name="columnMetadata">列元数据。</param>
        /// <returns>解析后的实际 DBNull 处理策略。</returns>
        private static DbNullHandling ResolveDbNullHandling(ColumnMetadata columnMetadata)
        {
            var dbNullHandling = columnMetadata.DbNullHandling;

            // 能持有 null 的列：可空值类型（UnderlyingType 非 null）或引用类型
            //（引用类型无运行时可空标记，NRT 标注仅编译器层，一律视为可空，数据库 NULL 装进引用属性完全合法）
            bool canHoldNull = columnMetadata.UnderlyingType != null || !columnMetadata.PropertyType.IsValueType;

            // 处理 Default：根据类型自动选择
            if (dbNullHandling == DbNullHandling.Default)
            {
                // 能持有 null：跳过赋值（保持 null）
                // 非可空值类型：直接读取不检查 NULL，对齐 Dapper/EF Core 行为（NULL 由提供程序抛原生异常）；
                // 需要 NULL 映射为 default(T) 的列应显式声明 UseDefaultValue
                dbNullHandling = canHoldNull
                    ? DbNullHandling.SkipAssignment
                    : DbNullHandling.ProviderException;
            }

            // 处理 ProviderException 退化：能持有 null 的列其 NULL 是合法值，退化为跳过赋值
            if (dbNullHandling == DbNullHandling.ProviderException && canHoldNull)
                dbNullHandling = DbNullHandling.SkipAssignment;

            return dbNullHandling;
        }
        #endregion

        /// <summary>
        /// 创建抛出 InvalidCastException 的表达式，用于 DbNullHandling.ThrowException 模式。
        /// </summary>
        /// <param name="propertyName">属性名。</param>
        /// <param name="resultType">抛出表达式的类型：有返回值的 lambda（如标量投影）中 throw 必须带类型才能与值分支组成合法条件表达式；为 null 时生成无类型 throw。</param>
        /// <returns>抛出 InvalidCastException 的表达式。</returns>
        private static Expression CreateThrowInvalidCastExceptionExpression(string propertyName, Type resultType = null)
        {
            // 最终生成表达式树（C# 伪代码）：throw new InvalidCastException("属性 'X' 配置了 [Column(DbNullHandling = ThrowException)]，无法接受数据库中的 NULL 值。")；
            // 带 resultType 时为带返回类型的 throw
            var constantExpr = Expression.Constant($"属性 '{propertyName}' 配置了 [Column(DbNullHandling = DbNullHandling.ThrowException)]，无法接受数据库中的 NULL 值。");
            var newExceptionExpr = Expression.New(InvalidCastExceptionConstructor, constantExpr);
            return resultType == null
                ? Expression.Throw(newExceptionExpr)
                : Expression.Throw(newExceptionExpr, resultType);
        }

        #region 列值读取表达式
        /// <summary>
        /// 创建列值读取表达式（读取策略分派入口），按优先级选择最优读取策略：
        /// <list type="number">
        /// <item>列配置类型转换器（[Column(ConverterType)]），生成：(TProperty)converter.ConvertFrom(reader.GetValue(i))</item>
        /// <item>JSON 列，生成：(TProperty)JsonColumnSerializer.Deserialize(reader.GetString(i), typeof(TProperty))</item>
        /// <item>全局类型转换器（DbValueConverterRegistry），生成：(TProperty)globalConverter.Invoke(reader.GetValue(i))</item>
        /// <item>强类型直读（getter 按读取器实际字段类型选择，数值族不一致时归一、可空属性再包装），生成：reader.GetInt32(i) 等</item>
        /// <item>通用转换兜底，生成：(TProperty)EntityValueConverter.ConvertFrom(reader.GetValue(i), typeof(TProperty))</item>
        /// </list>
        /// </summary>
        /// <param name="readerParam">数据读取器参数表达式。</param>
        /// <param name="columnIndex">列索引。</param>
        /// <param name="columnMetadata">列元数据。</param>
        /// <param name="fieldType">读取器中该列的实际字段类型（委托构建时读取一次，不在行内）：为 null 时按属性类型选择。</param>
        /// <returns>值读取表达式。</returns>
        private static Expression CreateColumnReadExpression(ParameterExpression readerParam, int columnIndex, ColumnMetadata columnMetadata, Type fieldType)
        {
            var propertyType = columnMetadata.PropertyType;
            var underlyingType = columnMetadata.UnderlyingType ?? propertyType;

            // 1.如果有自定义类型转换器，优先使用
            if (columnMetadata.ConverterType != null)
                return CreateColumnConverterExpression(readerParam, columnIndex, columnMetadata);

            // 2.JSON 列：把数据库文本反序列化为属性类型（列级显式配置优先于按类型注册的全局转换器，与写路径优先级对称）
            if (columnMetadata.IsJson)
                return CreateJsonDeserializeExpression(readerParam, columnIndex, columnMetadata);

            // 3.检查全局数据库值转换器注册表
            var globalConverter = DbValueConverterRegistry.GetFromConverter(underlyingType);
            if (globalConverter != null)
                return CreateGlobalConverterExpression(readerParam, columnIndex, columnMetadata, globalConverter);

            // 4.强类型直接读取：getter 按读取器字段类型选择，聚合投影等场景字段类型与属性类型不一致
            //（如 COUNT(*) 返回 int 而属性是 long，按属性类型选 GetInt64 在 SqlClient/Npgsql 的严格匹配下行内抛
            // InvalidCastException）。一致时零变化；数值族不一致时按字段类型读取、由读取表达式完成数值提升；
            // 其余不一致组合（如 string 字段对 char/数值属性）getterType 置空，落到底部唯一兜底开箱归一
            var getterType = underlyingType;
            if (fieldType != null && fieldType != underlyingType)
            {
                // 数值族对按字段类型读取（读取表达式完成数值提升），非数值对置空、落到底部唯一兜底
                getterType = TypeHelper.IsNumeric(fieldType, includeNullable: false)
                    && TypeHelper.IsNumeric(underlyingType, includeNullable: false)
                    ? fieldType
                    : null;
            }

            if (getterType != null && TypeToReaderMethodNameCache.TryGetValue(getterType, out var methodName))
                return CreateDirectReadExpression(readerParam, methodName, columnIndex, columnMetadata);

            // 5.兜底：使用 EntityValueConverter.ConvertFrom 处理表外类型与跨族不一致组合（唯一出口）
            return CreateEntityValueConverterExpression(readerParam, columnIndex, columnMetadata);
        }

        /// <summary>
        /// 创建使用列配置类型转换器（[Column(ConverterType)]）的表达式（优先级最高）。
        /// </summary>
        /// <param name="readerParam">数据读取器参数表达式。</param>
        /// <param name="columnIndex">列索引。</param>
        /// <param name="columnMetadata">列元数据，包含转换器类型和 ConvertFrom 方法信息。</param>
        /// <returns>自定义转换器读取表达式。</returns>
        private static Expression CreateColumnConverterExpression(ParameterExpression readerParam, int columnIndex, ColumnMetadata columnMetadata)
        {
            var propertyType = columnMetadata.PropertyType;

            // 转换器实例常量（构建元数据时创建的无状态单例，与写路径 ConvertToDb 共用同一实例）
            var converterInstance = Expression.Constant(columnMetadata.ConverterInstance);

            // 读取值，生成：reader.GetValue(columnIndex)
            var getValueCall = Expression.Call(
                readerParam,
                DbDataReaderMethodCache["GetValue"],
                Expression.Constant(columnIndex));

            // 调用转换器，生成：converter.ConvertFrom(reader.GetValue(columnIndex))
            var convertFromCall = Expression.Call(converterInstance, columnMetadata.ConvertFromMethod, getValueCall);

            // 转换为属性类型
            // 最终生成表达式树（C# 伪代码）：(TProperty)converter.ConvertFrom(reader.GetValue(columnIndex))
            return Expression.Convert(convertFromCall, propertyType);
        }

        /// <summary>
        /// 创建 JSON 列反序列化的读取表达式。
        /// </summary>
        /// <param name="readerParam">数据读取器参数表达式。</param>
        /// <param name="columnIndex">列索引。</param>
        /// <param name="columnMetadata">列元数据。</param>
        /// <returns>值读取表达式。</returns>
        private static Expression CreateJsonDeserializeExpression(ParameterExpression readerParam, int columnIndex, ColumnMetadata columnMetadata)
        {
            var propertyType = columnMetadata.PropertyType;

            // 读取 JSON 文本，生成：reader.GetString(columnIndex)
            var getStringCall = Expression.Call(
                readerParam,
                DbDataReaderMethodCache["GetString"],
                Expression.Constant(columnIndex));

            // 反序列化，生成：JsonColumnSerializer.Deserialize(getStringCall, typeof(TProperty))
            var deserializeCall = Expression.Call(
                JsonColumnSerializer_DeserializeMethod,
                getStringCall,
                Expression.Constant(propertyType));

            // 最终生成表达式树（C# 伪代码）：(TProperty)JsonColumnSerializer.Deserialize(reader.GetString(columnIndex), typeof(TProperty))
            return Expression.Convert(deserializeCall, propertyType);
        }

        /// <summary>
        /// 创建使用全局类型转换器的表达式（优先级第二）。
        /// <para>转换器委托在构建时查表固化，配合 <see cref="DbValueConverterRegistry.Changed"/> 事件与缓存键版本号，注册表变更后委托整体重建。</para>
        /// </summary>
        /// <param name="readerParam">数据读取器参数表达式。</param>
        /// <param name="columnIndex">列索引。</param>
        /// <param name="columnMetadata">列元数据，用于获取属性类型和基础类型。</param>
        /// <param name="globalConverter">构建时查表得到的全局转换器委托。</param>
        /// <returns>全局转换器读取表达式。</returns>
        private static Expression CreateGlobalConverterExpression(
            ParameterExpression readerParam,
            int columnIndex,
            ColumnMetadata columnMetadata,
            Func<object, object> globalConverter)
        {
            var propertyType = columnMetadata.PropertyType;

            // 读取值，生成：reader.GetValue(columnIndex)
            var getValueCall = Expression.Call(readerParam,
                DbDataReaderMethodCache["GetValue"],
                Expression.Constant(columnIndex));

            // 委托常量调用 Invoke（构建时固化，变更经 Changed 事件 + 版本键整体失效），生成：globalConverter.Invoke(reader.GetValue(columnIndex))
            var convertFromCall = Expression.Call(
                Expression.Constant(globalConverter),
                FuncObjectObject_InvokeMethod,
                getValueCall);

            // 转换为属性类型
            // 最终生成表达式树（C# 伪代码）：(TProperty)globalConverter.Invoke(reader.GetValue(columnIndex))
            return Expression.Convert(convertFromCall, propertyType);
        }

        /// <summary>
        /// 创建使用 DbDataReader 强类型读取方法的表达式（优先级第三，性能最优）。
        /// </summary>
        /// <param name="readerParam">数据读取器参数表达式。</param>
        /// <param name="methodName">DbDataReader 读取方法名称（如 GetInt32、GetString 等）。</param>
        /// <param name="columnIndex">列索引。</param>
        /// <param name="columnMetadata">列元数据，用于判断是否为可空类型。</param>
        /// <returns>强类型读取表达式。</returns>
        private static Expression CreateDirectReadExpression(ParameterExpression readerParam, string methodName, int columnIndex, ColumnMetadata columnMetadata)
        {
            var propertyType = columnMetadata.PropertyType;
            var underlyingType = columnMetadata.UnderlyingType ?? propertyType;

            // 读取值，生成：reader.GetInt32(columnIndex)。getter 按字段类型选择时返回类型可能与目标不一致
            //（COUNT(*) 的 int getter 对 long 属性），故经后续两段转换归一（见第一步/第二步）
            var getMethodCall = Expression.Call(readerParam,
                 DbDataReaderMethodCache[methodName],
                 Expression.Constant(columnIndex));

            // 第一步：getter 返回类型 → 属性基础类型。类型对齐（绝大多数场景）时跳过；
            // 数值族不一致时经 CreateNumericConversionExpression 分流：无损宽化与浮点/decimal
            // 目标走原生 conv 节点（零开销），有损风险方向走行内守卫显式失败
            var readValue = getMethodCall.Type == underlyingType
                ? getMethodCall
                : CreateNumericConversionExpression(getMethodCall, underlyingType);

            // 第二步：属性基础类型 → 属性类型（可空包装，如 (int?)readValue）。
            // 必须与第一步分开：Expression.Convert 不支持"提升 + 包装"合并的一步转换，
            // 直接对 (int → long?) 会抛"No coercion operator is defined"，只能拆成两跳

            // 最终生成表达式树（C# 伪代码）：reader.GetInt32(columnIndex)，
            // 类型不一致时两段式（数值归一 + 可空包装），如 (long?)((long)reader.GetInt32(columnIndex))
            return propertyType == underlyingType
                ? readValue
                : Expression.Convert(readValue, propertyType);
        }

        /// <summary>
        /// 创建数值不匹配读取的转换表达式（构建期分流）：
        /// <para>浮点/decimal 目标与整数源的无损宽化（如 int → long）走原生转换；
        /// 有损风险方向（小数源 → 整数目标、整数窄化）走 <see cref="NumericValueGuard.ConvertOrThrow{TSource, TTarget}(TSource)"/>
        /// 行内守卫调用（原生 Convert 节点编译为 unchecked conv 指令，小数静默截断、窄化超范围静默丢高位，守卫改为显式失败）。</para>
        /// </summary>
        /// <param name="value">getter 调用表达式（返回类型为字段类型）。</param>
        /// <param name="targetType">属性基础类型（已解包可空）。</param>
        /// <returns>转换表达式。</returns>
        private static Expression CreateNumericConversionExpression(Expression value, Type targetType)
        {
            var sourceType = value.Type;

            // 浮点/decimal 目标：long → double 的 2^53 外舍入是浮点族固有行为而非值损坏，原生转换
            if (!TypeHelper.IsInteger(targetType, includeNullable: false))
                return Expression.Convert(value, targetType);

            // 整数源的无损宽化（源值集包含于目标值集，如 int → long、byte → short）：conv 节点零开销
            if (TypeHelper.IsInteger(sourceType, includeNullable: false) && IsLosslessIntegralWidening(sourceType, targetType))
                return Expression.Convert(value, targetType);

            // 有损风险方向（整数目标）：小数源（可能带小数部分）或整数窄化（可能超范围），经行内守卫显式失败
            var method = NumericValueGuard_ConvertOrThrowMethod.MakeGenericMethod(sourceType, targetType);
            return Expression.Call(method, value);
        }

        /// <summary>
        /// 判定整数源到整数目标是否为无损宽化（源值集包含于目标值集）。符号规则：
        /// 同号位宽不缩即可；有符号源不进无符号目标（负值无处安放）；无符号源进有符号目标需位宽更宽
        ///（目标一位作符号、正数空间减半，防 byte → sbyte、uint → int）。两个类型均为构建期静态类型。
        /// </summary>
        /// <param name="source">数据读取器报告的字段类型（getter 返回类型，构建期经 <c>reader.GetFieldType</c> 得知）。</param>
        /// <param name="target">实体属性的基础类型（已解包）。</param>
        private static bool IsLosslessIntegralWidening(Type source, Type target)
        {
            var s = IntegralRanks[source];
            var t = IntegralRanks[target];

            // 同号（值域随位宽嵌套）：位宽不缩即安全，如 int → long、byte → ushort
            if (s.Value == t.Value)
                return t.Key >= s.Key;

            // 跨符号：有符号源 → 无符号目标，负值无处安放，永不无损
            if (s.Value)
                return false;

            // 跨符号：无符号源 → 有符号目标，目标一位作符号、正数空间减半，须位宽更宽：
            // uint → long（8 > 4）宽化；uint → int（4 > 4 不成立）不是，3e9 超出 int 范围
            return t.Key > s.Key;
        }

        /// <summary>
        /// 创建使用 EntityValueConverter.ConvertFrom 的表达式（兜底方案，优先级最低）。
        /// <para>适用于枚举、DateTimeOffset、TimeSpan 等无专用 DbDataReader 读取方法的类型。</para>
        /// </summary>
        /// <param name="readerParam">数据读取器参数表达式。</param>
        /// <param name="columnIndex">列索引。</param>
        /// <param name="columnMetadata">列元数据，用于获取属性类型。</param>
        /// <returns>通用转换读取表达式。</returns>
        private static Expression CreateEntityValueConverterExpression(ParameterExpression readerParam, int columnIndex, ColumnMetadata columnMetadata)
        {
            var propertyType = columnMetadata.PropertyType;

            // 读取值，生成：reader.GetValue(columnIndex)
            var getValueCall = Expression.Call(readerParam,
                DbDataReaderMethodCache["GetValue"],
                Expression.Constant(columnIndex));

            // 调用 EntityValueConverter.ConvertFrom(value, targetType)
            var convertCall = Expression.Call(
                EntityValueConverter_ConvertFromMethod,
                getValueCall,
                Expression.Constant(propertyType));

            // 转换为属性类型
            // 最终生成表达式树（C# 伪代码）：(TProperty)EntityValueConverter.ConvertFrom(reader.GetValue(columnIndex), typeof(TProperty))
            return Expression.Convert(convertCall, propertyType);
        }
        #endregion

        #region 投影映射
        /// <summary>
        /// 获取或添加投影映射委托（<see cref="Func{DbDataReader, TResult}"/>）：单列投影（结果类型为标量）按位置 0 读取，
        /// 多列投影（结果类型为匿名类型）以构造函数逐列装配。委托按位置逐列读取：第 i 列用第 i 个列元数据
        /// （含各自的转换器与 DBNull 处理配置），与实体赋值的转换策略一致。
        /// <para>入口比对各位置列名与投影列元数据（忽略大小写），不一致仅记录诊断告警不抛异常：
        /// 列别名（SELECT Age AS A）与聚合列（SELECT COUNT(*) AS Total）的列名与投影属性名不同属合法形态；
        /// 告警保留了"元数据列与结果集列错位时静默读错列"的可观测性。</para>
        /// </summary>
        /// <typeparam name="T">投影源实体类型，用于区分不同实体的列配置。</typeparam>
        /// <typeparam name="TResult">投影结果类型：单列为属性类型，多列为匿名类型、元组（ValueTuple）、带参构造的命名类型（编译器按实参绑定的构造函数）或命名类型（无参构造 + 对象初始化器）。</typeparam>
        /// <param name="reader">数据读取器，列序与投影列一致。</param>
        /// <param name="plan">投影映射计划（结果形态 + 投影列组 + 成员绑定 + 绑定构造函数，构建期已解析并经互锁守卫）。</param>
        /// <returns>编译后的投影委托。</returns>
        internal static Func<DbDataReader, TResult> GetOrAddProjectionMapper<T, TResult>(DbDataReader reader, ProjectionPlan plan)
        {
            Guard.ThrowIfNull(reader, nameof(reader));
            Guard.ThrowIfNull(plan, nameof(plan));

            // 形态与结果类型一致性守卫：Positional 形态要求结果类型为匿名类型、元组或带绑定构造的命名类型（解析层按位置构造形态产出 Positional，
            // 此处防止调用方构造不一致组合——Scalar/MemberBinding 均要求非匿名、非元组且无绑定构造，同式覆盖）
            // 等价校验：两布尔必须同真同假（合法组合仅对角线两格），XOR 为真即错配
            var isPositionalShape = plan.ResultShape == ProjectionPlan.Shape.Positional;
            var isPositionalResult = TypeHelper.IsAnonymousType(typeof(TResult)) || TypeHelper.IsValueTuple(typeof(TResult)) || plan.Constructor != null;
            if (isPositionalShape != isPositionalResult)
                throw new InvalidOperationException($"投影形态（{plan.ResultShape}）与结果类型 '{typeof(TResult)}' 不一致：Positional 形态要求匿名类型、元组（ValueTuple）或带参构造的命名类型结果，其余形态要求非匿名、非元组且无绑定构造的结果。");

            var columns = plan.Columns;
            if (reader.FieldCount != columns.Length)
                throw new InvalidOperationException($"投影结果集的列数（{reader.FieldCount}）与投影列数（{columns.Length}）不一致，请核对投影列与查询形态。");

            // 逐列采集读取器字段类型并校验列名：委托固定按位置读取，列名不符仅告警
            //（硬校验会拒绝别名/聚合列等合法形态），防止错位时静默错数据完全不可见
            var fieldTypes = new Type[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                fieldTypes[i] = reader.GetFieldType(i);

                // 分组聚合的 COUNT(*) 列无源列元数据（裸读取），跳过列名校验
                if (columns[i] == null)
                    continue;

                var readerColumnName = reader.GetName(i);
                if (!string.Equals(readerColumnName, columns[i].ColumnName, StringComparison.OrdinalIgnoreCase))
                    DiagnosticLog.Warn(nameof(EntityMapperCore),
                        "投影列名与读取器第 {Index} 列不符：投影列为 '{ProjectionColumn}'，读取器列为 '{ReaderColumn}'，按位置契约读取。若非有意为之请核对查询与投影列。",
                        i, columns[i].ColumnName, readerColumnName);
            }

            var key = new ProjectionMapperKey(
                typeof(T),
                typeof(TResult),
                columns,
                fieldTypes,
                plan.Members,
                plan.Constructor,
                DbValueConverterRegistry.Version);

            // 命中即续期（清理永远不动刚续期的热条目），续期后直接返回——清扫仅由插入路径触发
            // （TryGetValue 命中路径零闭包分配；miss 时预构造 Lazy，同 GetOrAddMapper 的单键单次编译语义）
            if (ProjectionMapperCache.TryGetValue(key, out var entry))
            {
                Volatile.Write(ref entry.LastAccessTicks, StopwatchClock.ElapsedTicks);
                return (Func<DbDataReader, TResult>)entry.Mapper.Value;
            }

            // miss：插入并编译（CacheEntry 构造时间戳即本次访问的续期，其后的清扫决策基于完整访问状态）
            var lazy = new Lazy<Delegate>(() => CreateProjectionMapper<TResult>(plan, fieldTypes), LazyThreadSafetyMode.ExecutionAndPublication);
            entry = ProjectionMapperCache.GetOrAdd(key, new CacheEntry(lazy));

            // 插入期清扫（同 GetOrAddMapper：策略未设置时跳过）
            if (EntityMapperCachePolicy.MaxEntries.HasValue || EntityMapperCachePolicy.IdleExpiration.HasValue)
                SweepCache(ProjectionMapperCache);

            return (Func<DbDataReader, TResult>)entry.Mapper.Value;
        }

        /// <summary>
        /// 创建投影映射委托，编译为 <see cref="Func{DbDataReader, TResult}"/> 委托：按计划声明的结果形态分派——
        /// 命名类型初始化器（无参构造 + 按成员名赋值）、位置构造（匿名类型/元组的唯一公共构造函数或命名类型编译器绑定的带参构造，按位置映射）、单列标量（直读）。
        /// <para>组合合法性由 <see cref="ProjectionPlan"/> 构造守卫保证（形态 ⇔ 列数 ⇔ 成员绑定互锁），形态与结果类型的
        /// 一致性（Positional ⇔ 匿名类型、元组或带参构造命名类型）由 <see cref="GetOrAddProjectionMapper"/> 入口校验，本方法不再重复判定。</para>
        /// <para>逐列的值读取与 NULL 处理复用 <see cref="CreateScalarReadExpression"/>：转换器列、枚举名列、JSON 列按各自配置转换，
        /// 与实体赋值的语义一致；与实体赋值的差异：NULL 且策略为跳过赋值时返回目标类型默认值（投影无实体实例可承载初始值）。</para>
        /// </summary>
        /// <typeparam name="TResult">投影结果类型：单列为属性类型，多列为匿名类型、元组（ValueTuple）、带参构造的命名类型（编译器按实参绑定的构造函数）或命名类型（无参构造 + 对象初始化器）。</typeparam>
        /// <param name="plan">投影映射计划（结果形态 + 投影列组 + 成员绑定 + 绑定构造函数）。</param>
        /// <param name="fieldTypes">读取器各列的实际字段类型（getter 选择参考）。</param>
        /// <returns>编译后的投影委托。</returns>
        private static Func<DbDataReader, TResult> CreateProjectionMapper<TResult>(ProjectionPlan plan, Type[] fieldTypes)
        {
            var readerParam = Expression.Parameter(typeof(DbDataReader), "reader");
            var columns = plan.Columns;

            switch (plan.ResultShape)
            {
                case ProjectionPlan.Shape.MemberBinding:
                    {
                        // 命名类型初始化器分支：无参构造实例化后按成员绑定赋值——绑定序 = 列序，绑定目标（MemberInfo）是解析层
                        // 经 ReflectionHelper.ResolveWritableMember 的解析产物（可写属性优先、可写字段兜底的唯一解析点），此处直接绑定，
                        // 不再按名字二次推导（两层推导规则漂移的分歧隐患在结构上消除）。
                        // 赋值机制是成员 setter：无参构造没有参数可承载映射，「列 → 成员」须由 Members 显式携带。
                        // 示例（投影选择器表达式）：x => new Dto { Id = x.Id, Name = x.Name } ⇒ 
                        // reader => {
                        //      var dto = new Dto();
                        //      dto.Id   = reader.GetInt32(0);   // Members[0] = Id 属性：第 0 列的落点
                        //      dto.Name = reader.GetString(1);  // Members[1] = Name 属性
                        //      return dto; }

                        var parameterlessConstructor = typeof(TResult).GetConstructor(Type.EmptyTypes);
                        if (parameterlessConstructor == null)
                            throw new NotSupportedException($"命名类型投影的结果类型 '{typeof(TResult)}' 必须提供公共无参构造函数。");

                        var bindings = new MemberBinding[columns.Length];
                        for (int i = 0; i < columns.Length; i++)
                        {
                            // 解析层只产出带公共 setter 的属性或非 initonly 字段两种落点（互锁守卫保证计划合法），按形态取成员类型
                            var member = plan.Members[i];
                            var memberType = member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;

                            bindings[i] = Expression.Bind(member, CreateScalarReadExpression(readerParam, i, columns[i], fieldTypes[i], memberType));
                        }

                        var memberInitBody = Expression.MemberInit(Expression.New(parameterlessConstructor), bindings);
                        return Expression.Lambda<Func<DbDataReader, TResult>>(memberInitBody, readerParam).Compile();
                    }

                case ProjectionPlan.Shape.Positional:
                    {
                        // 位置构造分支：按构造参数位置映射列，【位置】即映射，无需成员名。
                        // 构造函数由解析层统一捕获固化进计划（命名构造：编译器绑定的 NewExpression.Constructor，多构造重载已各自消歧；
                        // 匿名/元组：类型唯一公共构造——匿名构造参数与属性一一对应是语言保证，元组构造参数与元素一一对应是 CLR 形态保证），
                        // plan 工厂互锁校验构造参数数 = 列数，此处直接采用。
                        // 示例（投影选择器表达式）：x => new { x.Id, x.Name } / x => ValueTuple.Create(x.Id, x.Name) / x => new Dto(x.Id, x.Name) ⇒ 
                        // reader => new TResult(reader.GetInt32(0), reader.GetString(1))
                        // 构造参数 i ← 第 i 列，构造体内自动赋给属性/元素 i

                        var constructor = plan.Constructor;
                        var constructorParameters = constructor.GetParameters();
                        var arguments = new Expression[columns.Length];

                        for (int i = 0; i < columns.Length; i++)
                        {
                            arguments[i] = CreateScalarReadExpression(readerParam, i, columns[i], fieldTypes[i], constructorParameters[i].ParameterType);
                        }

                        var newBody = Expression.New(constructor, arguments);
                        return Expression.Lambda<Func<DbDataReader, TResult>>(newBody, readerParam).Compile();
                    }

                case ProjectionPlan.Shape.Scalar:
                    {
                        // 标量分支：单列投影直接返回第 0 列读取表达式（列数已由计划的互锁守卫保证）。
                        // 列元数据为 null（分组聚合的 COUNT(*) 列，契约见 ProjectionPlan.Columns）→ 裸读取子分支，跳过列策略分派。
                        // 示例（投影选择器表达式）：x => x.Name               ⇒ reader => (string)reader.GetString(0)
                        //                      x => x.Id 的 COUNT(*) 聚合 ⇒ reader => reader.GetInt32(0)（裸读取）

                        var body = CreateScalarReadExpression(readerParam, 0, columns[0], fieldTypes[0], typeof(TResult));
                        return Expression.Lambda<Func<DbDataReader, TResult>>(body, readerParam).Compile();
                    }

                default:
                    throw new InvalidOperationException($"未知的投影结果形态：{plan.ResultShape}。");
            }
        }

        /// <summary>
        /// 构建单列读取表达式（标量投影与多列投影共用）：按列索引读取值并转换到目标类型，含 DBNull 处理分支。
        /// <para>值读取策略与实体赋值一致；与实体赋值的差异：NULL 且策略为跳过赋值时返回目标类型默认值（投影无实体实例可承载初始值）。</para>
        /// <para>投影语境的 NULL 语义以目标类型为准：非可空列投影到可承载 null 的目标类型（可空值类型/引用类型）时
        /// 放宽为默认值（得到 default 而非提供程序异常），属性可空性是实体赋值语境的锚点，投影无实体实例承载该语义。</para>
        /// </summary>
        /// <param name="readerParam">读取器参数表达式。</param>
        /// <param name="columnIndex">列索引。</param>
        /// <param name="columnMetadata">列元数据；null 表示无源列元数据（分组聚合的 COUNT(*) 列，走裸读取分支）。</param>
        /// <param name="fieldType">读取器该列的实际字段类型（getter 选择参考）。</param>
        /// <param name="targetType">读取值的转换目标类型（单列投影为结果类型，多列投影为构造参数类型）。</param>
        /// <returns>目标类型形态的列读取表达式。</returns>
        private static Expression CreateScalarReadExpression(ParameterExpression readerParam, int columnIndex, ColumnMetadata columnMetadata, Type fieldType, Type targetType)
        {
            // 无源列元数据的裸读取分支（分组聚合的 COUNT(*) 列）：reader.GetValue(i) 经实体值转换链归一到目标类型。
            // COUNT 结果恒非 NULL 的整数族，DBNull → default 仅为手搭形态兜底；与键列/聚合源列共用 DBNull 条件结构
            if (columnMetadata == null)
                return CreateRawScalarReadExpression(readerParam, columnIndex, targetType);

            var columnReadExpr = CreateColumnReadExpression(readerParam, columnIndex, columnMetadata, fieldType);// 列值读取表达式：按列配置分派策略（列转换器/JSON/全局转换器/强类型直读/通用兜底）

            // 值表达式（属性类型）→ 目标类型，生成：(TTarget)columnReadExpr；同型跳过；数值族（int→long 等）经数值转换合法；
            // 无转换路径的组合（如 string→Guid）在表达式构建时即抛底层 InvalidOperationException，在此包装为带列名与双类型上下文的异常
            Expression readExpr;
            try
            {
                readExpr = columnReadExpr.Type == targetType
                    ? columnReadExpr
                    : Expression.Convert(columnReadExpr, targetType);
            }
            catch (InvalidOperationException ex)
            {
                throw new NotSupportedException($"标量投影的结果类型 '{targetType}' 与列 '{columnMetadata.PropertyName}'（属性类型 '{columnMetadata.PropertyType}'）之间无可用的值转换，请使用与列类型匹配的投影结果类型。", ex);
            }

            // NULL 检查，生成：reader.IsDBNull(columnIndex)
            var isDbNullCall = Expression.Call(
                readerParam,
                DbDataReaderMethodCache["IsDBNull"],
                Expression.Constant(columnIndex));

            var dbNullHandling = ResolveDbNullHandling(columnMetadata);
            if (dbNullHandling == DbNullHandling.ProviderException
                && (Nullable.GetUnderlyingType(targetType) != null || !targetType.IsValueType))
                dbNullHandling = DbNullHandling.UseDefaultValue;

            switch (dbNullHandling)
            {
                case DbNullHandling.ThrowException:
                    // if (reader.IsDBNull(columnIndex)) throw new InvalidCastException(); else return read;
                    return Expression.IfThenElse(
                        isDbNullCall,
                        CreateThrowInvalidCastExceptionExpression(columnMetadata.PropertyName, targetType),
                        readExpr);
                case DbNullHandling.ProviderException:
                    // 不检查 IsDBNull，NULL 时由 ADO.NET 提供程序的类型化读取方法抛出原生异常
                    return readExpr;
                default:
                    // SkipAssignment / UseDefaultValue：NULL 时返回目标类型默认值
                    // 生成：reader.IsDBNull(columnIndex) ? default(TTarget) : readExpr
                    return Expression.Condition(isDbNullCall, Expression.Default(targetType), readExpr);
            }
        }

        /// <summary>
        /// 创建无源列元数据的裸读取表达式（分组聚合的 COUNT(*) 列）：reader.GetValue(i) 经实体值转换链归一到目标类型。
        /// <para>COUNT 结果恒非 NULL 的整数族（不同引擎返回 int/long/decimal 不一），转换链按 reader 实报类型归一；
        /// DBNull → default(targetType) 仅为手搭表达式形态兜底。</para>
        /// </summary>
        /// <param name="readerParam">读取器参数表达式。</param>
        /// <param name="columnIndex">列索引。</param>
        /// <param name="targetType">读取值的转换目标类型。</param>
        /// <returns>目标类型形态的裸读取表达式。</returns>
        private static Expression CreateRawScalarReadExpression(ParameterExpression readerParam, int columnIndex, Type targetType)
        {
            // 读取值，生成：reader.GetValue(0)
            var getValueCall = Expression.Call(readerParam,
                DbDataReaderMethodCache["GetValue"],
                Expression.Constant(columnIndex));

            // 调用实体值转换链并转换到目标类型，生成：(TTarget)EntityValueConverter.ConvertFrom(reader.GetValue(i), typeof(TTarget))
            var convertCall = Expression.Call(
                EntityValueConverter_ConvertFromMethod,
                getValueCall,
                Expression.Constant(targetType));
            var readExpr = Expression.Convert(convertCall, targetType);

            // NULL 检查，生成：reader.IsDBNull(i)
            var isDbNullCall = Expression.Call(
                readerParam,
                DbDataReaderMethodCache["IsDBNull"],
                Expression.Constant(columnIndex));

            // NULL 时返回目标类型默认值（COUNT 无 NULL，兜底形态保持与其他分支一致的 DBNull 结构）
            // 最终生成表达式树（C# 伪代码）：
            // reader.IsDBNull(i) 
            //  ? default(TTarget) 
            //  : (TTarget)EntityValueConverter.ConvertFrom(reader.GetValue(i), typeof(TTarget))
            return Expression.Condition(isDbNullCall, Expression.Default(targetType), readExpr);
        }
        #endregion
    }
}
