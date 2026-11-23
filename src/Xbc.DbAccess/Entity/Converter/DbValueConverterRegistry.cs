using System.Collections.Concurrent;
namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 数据库值转换器注册表，管理全局自定义数据库值转换器，支持按类型注册和查找转换器。
    /// <para>全局转换器的优先级低于列级自定义转换器（[Column(ConverterType)]），高于默认类型转换。</para>
    /// </summary>
    public static class DbValueConverterRegistry
    {
        // 数据库值到属性值的转换器缓存：目标类型 -> 转换器委托
        private static readonly ConcurrentDictionary<Type, Func<object, object>> fromDbValueConverters
            = new ConcurrentDictionary<Type, Func<object, object>>();

        // 属性值到数据库值的转换器缓存：目标类型 -> 转换器委托
        private static readonly ConcurrentDictionary<Type, Func<object, object>> toDbValueConverters
            = new ConcurrentDictionary<Type, Func<object, object>>();

        // 注册表版本号：注册/取消注册/清空时递增（Interlocked 保证跨线程单调递增），
        // 供实体映射缓存纳入键，使版本变化后旧版本键下的缓存条目自然失效
        private static int version;

        /// <summary>
        /// 获取注册表的当前版本号。注册、取消注册或清空操作使其递增（未发生实际变更的操作不递增）。
        /// <para>实体映射缓存键纳入该版本号，消除"编译期间注册变更、旧语义委托回写缓存"的竞态窗口。</para>
        /// </summary>
        internal static int Version => version;

        /// <summary>
        /// 注册表发生变更（注册、取消注册、清空）后触发的事件。
        /// <para>供框架内部（如实体映射委托缓存）订阅，在转换器变更时联动失效已固化的缓存。</para>
        /// </summary>
        internal static event Action Changed;

        /// <summary>
        /// 触发注册表变更事件。
        /// </summary>
        private static void OnChanged()
        {
            Changed?.Invoke();
        }

        /// <summary>
        /// 注册数据库值转换器。
        /// </summary>
        /// <typeparam name="T">目标属性类型。可空类型按基础类型注册（与查询侧的解包口径一致，
        /// <c>Register&lt;int?&gt;</c> 与 <c>Register&lt;int&gt;</c> 互为同键）。</typeparam>
        /// <param name="converter">转换器实例。</param>
        public static void Register<T>(IDbValueConverter<T> converter)
        {
            Guard.ThrowIfNull(converter, nameof(converter));

            var key = ResolveKey(typeof(T));
            fromDbValueConverters[key] = dbValue => converter.ConvertFrom(dbValue);

            // null 提前映射为 DBNull：(T)null 对可空值类型是拆箱空引用，会抛 NullReferenceException
            toDbValueConverters[key] = value => value == null ? DBNull.Value : converter.ConvertTo((T)value);

            // 先写字典再递增版本（Interlocked 为全屏障）：读到新版本的线程必然看到已完成的字典写入
            Interlocked.Increment(ref version);
            OnChanged();
        }

        /// <summary>
        /// 注册数据库值转换器（使用委托）。
        /// </summary>
        /// <typeparam name="T">目标属性类型。可空类型按基础类型注册（与查询侧的解包口径一致，
        /// <c>Register&lt;int?&gt;</c> 与 <c>Register&lt;int&gt;</c> 互为同键）。</typeparam>
        /// <param name="convertFrom">数据库值转换为实体类型的函数。</param>
        /// <param name="convertTo">实体类型转换为数据库值的函数。</param>
        public static void Register<T>(Func<object, T> convertFrom, Func<T, object> convertTo)
        {
            Guard.ThrowIfNull(convertFrom, nameof(convertFrom));
            Guard.ThrowIfNull(convertTo, nameof(convertTo));

            var key = ResolveKey(typeof(T));
            fromDbValueConverters[key] = dbValue => convertFrom(dbValue);

            // null 提前映射为 DBNull：(T)null 对可空值类型是拆箱空引用，会抛 NullReferenceException
            toDbValueConverters[key] = value => value == null ? DBNull.Value : convertTo((T)value);

            // 先写字典再递增版本（Interlocked 为全屏障）：读到新版本的线程必然看到已完成的字典写入
            Interlocked.Increment(ref version);
            OnChanged();
        }

        /// <summary>
        /// 取消注册数据库值转换器。
        /// </summary>
        /// <typeparam name="T">目标属性类型。</typeparam>
        public static void Unregister<T>()
        {
            // 仅在确实移除了转换器时才递增版本并触发变更事件，避免无谓的映射缓存失效
            var key = ResolveKey(typeof(T));
            var removedFrom = fromDbValueConverters.TryRemove(key, out _);
            var removedTo = toDbValueConverters.TryRemove(key, out _);

            if (removedFrom || removedTo)
            {
                Interlocked.Increment(ref version);
                OnChanged();
            }
        }

        /// <summary>
        /// 获取数据库值转换为实体类型的转换器。
        /// </summary>
        /// <param name="type">目标属性类型（可空类型按基础类型查表，与注册口径一致）。</param>
        /// <returns>转换器委托，如果不存在则返回 null。</returns>
        public static Func<object, object> GetFromConverter(Type type)
        {
            Guard.ThrowIfNull(type, nameof(type));

            var key = ResolveKey(type);
            fromDbValueConverters.TryGetValue(key, out var converter);
            return converter;
        }

        /// <summary>
        /// 获取实体类型转换为数据库值的转换器。
        /// </summary>
        /// <param name="type">源属性类型（可空类型按基础类型查表，与注册口径一致）。</param>
        /// <returns>转换器委托，如果不存在则返回 null。</returns>
        public static Func<object, object> GetToConverter(Type type)
        {
            Guard.ThrowIfNull(type, nameof(type));

            var key = ResolveKey(type);
            toDbValueConverters.TryGetValue(key, out var converter);
            return converter;
        }

        /// <summary>
        /// 解析注册表键：可空值类型按基础类型归一。
        /// <para>注册/注销与查询共用此口径，框架内全部查询侧均以解包后的基础类型查表，
        /// 注册侧若以原样可空类型作键会永不命中，转换器静默失效。</para>
        /// </summary>
        private static Type ResolveKey(Type type)
        {
            return Nullable.GetUnderlyingType(type) ?? type;
        }

        /// <summary>
        /// 清空所有注册的转换器。
        /// </summary>
        public static void Clear()
        {
            // 空表时无需递增版本与触发变更事件，避免无谓的映射缓存失效
            if (fromDbValueConverters.Count == 0 && toDbValueConverters.Count == 0)
                return;

            fromDbValueConverters.Clear();
            toDbValueConverters.Clear();
            Interlocked.Increment(ref version);
            OnChanged();
        }
    }
}
