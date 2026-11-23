using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 导航属性装配器，将已查询到的对方实体按键值匹配装配到本方实体的导航属性上；1:N 集合与 1:1 引用的
    /// 键映射与装配细节分别详见 <see cref="PopulateChildren"/> 与 <see cref="PopulateReferences"/>。
    /// <para>读取对方实体（DbDataReader → 实体列表）由调用方完成，本类只做内存中的键值匹配与赋值，
    /// 装配逻辑与查询批次无关，适用于分批查询后的统一装配。</para>
    /// </summary>
    internal static class NavigationAssembler
    {
        /// <summary>
        /// 泛型列表工厂缓存（元素类型到其列表构造委托的映射），避免装配时逐本方实体反射创建列表。
        /// </summary>
        private static readonly ConcurrentDictionary<Type, Func<IList>> ListFactoryCache
            = new ConcurrentDictionary<Type, Func<IList>>();

        /// <summary>
        /// 获取指定元素类型的列表工厂，首次访问时编译对应元素类型列表的无参构造表达式并缓存。
        /// </summary>
        /// <param name="elementType">列表元素类型。</param>
        /// <returns>创建对应元素类型列表实例的委托。</returns>
        private static Func<IList> GetListFactory(Type elementType)
        {
            return ListFactoryCache.GetOrAdd(elementType, t =>
            {
                var listType = typeof(List<>).MakeGenericType(t);
                var newExpr = Expression.New(listType);
                return Expression.Lambda<Func<IList>>(newExpr).Compile();
            });
        }

        /// <summary>
        /// 将对方实体装配到本方实体的 1:N 集合导航属性上：ThisKeyColumn 为本方主键，OtherKeyColumn 为对方外键，
        /// 对方外键值等于本方主键值的记录归入对应本方实体。
        /// <para>未匹配到对方实体的本方实体，其导航属性被赋值为空列表（而非 null）；
        /// 对方外键值为 null 的记录不参与分组；本方主键值为 null 时跳过装配，导航属性保持原值（含先前持有的旧列表——残留不清空）。
        /// 本方主键重复的父实体各自得到内容相同的独立列表（主键应唯一，属畸形数据下的表现）。</para>
        /// <para>分组键未命中任何本方主键的对方实体不装配到任何本方实体，并经 <see cref="DiagnosticLog"/> 输出警告。
        /// 内存装配按 .NET 装箱值相等匹配：string 键可能因数据库排序规则大小写不敏感而 .NET 装配区分大小写导致丢配；
        /// 数值键的本方与对方属性须为同一类型宽度（int 与 long 的装箱值不相等，宽度不一致会使数据库 IN 查询正常取回、内存装配丢配）。</para>
        /// </summary>
        /// <param name="parents">已加载的本方实体列表。</param>
        /// <param name="navigationMetadata">导航属性元数据，包含键列映射和对方实体类型信息。</param>
        /// <param name="children">已从 DbDataReader 映射的对方实体列表（可跨多个查询批次累积）。</param>
        internal static void PopulateChildren(
            IReadOnlyList<object> parents,
            NavigationMetadata navigationMetadata,
            IReadOnlyList<object> children)
        {
            // 先收集本方主键值集合（null 跳过），供分组过滤与未命中统计共用，整体保持 O(n)
            var parentKeys = new HashSet<object>();
            foreach (var parent in parents)
            {
                var primaryKeyValue = navigationMetadata.ThisKeyColumn.GetValue(parent);
                if (primaryKeyValue != null)
                    parentKeys.Add(primaryKeyValue);
            }

            // 按对方外键值分组对方实体，例如 Customer.Orders 导航：Order.CustomerId -> List<Order>；
            // 外键值为 null 的不参与分组；外键值未命中任何本方主键的计入未匹配数、不进入分组
            var childrenByKey = new Dictionary<object, List<object>>(parents.Count);
            int unmatchedCount = 0;
            foreach (var child in children)
            {
                var foreignKeyValue = navigationMetadata.OtherKeyColumn.GetValue(child);
                if (foreignKeyValue == null)
                    continue;

                if (!parentKeys.Contains(foreignKeyValue))
                {
                    unmatchedCount++;
                    continue;
                }

                if (!childrenByKey.TryGetValue(foreignKeyValue, out var list))
                {
                    list = new List<object>();
                    childrenByKey[foreignKeyValue] = list;
                }

                list.Add(child);
            }

            // 存在未命中本方主键的对方实体时输出诊断警告（无未匹配的常规路径不产生日志开销）
            if (unmatchedCount > 0)
            {
                DiagnosticLog.Warn(nameof(NavigationAssembler),
                    "1:N 集合导航属性 '{NavigationProperty}' 装配有 {UnmatchedCount} 个对方实体的外键值未命中任何本方主键，这些实体未装配到任何本方实体；" +
                    "可能原因：string 键因数据库排序规则大小写不敏感而 .NET 装配区分大小写导致丢配，" +
                    "或本方主键与对方外键的数值类型宽度不一致（如 int 与 long，装箱值不相等导致丢配）。",
                    navigationMetadata.PropertyName, unmatchedCount);
            }

            // 装配到本方实体
            var listFactory = GetListFactory(navigationMetadata.TargetType);
            foreach (var parent in parents)
            {
                var primaryKeyValue = navigationMetadata.ThisKeyColumn.GetValue(parent);
                if (primaryKeyValue == null)
                    continue;

                // 创建 List<TargetType> 实例，逐个添加匹配的对方实体
                var navList = listFactory();
                if (childrenByKey.TryGetValue(primaryKeyValue, out var childList))
                {
                    foreach (var child in childList)
                    {
                        navList.Add(child);
                    }
                }

                // 赋值到本方实体的导航属性
                navigationMetadata.SetValue(parent, navList);
            }
        }

        /// <summary>
        /// 将引用实体装配到本方实体的 1:1 引用导航属性上：ThisKeyColumn 为本方外键，OtherKeyColumn 为对方主键，
        /// 对方主键值等于本方外键值的记录为匹配的引用实体。
        /// <para>若对方存在重复主键，仅首条记录被装配，后续重复项被忽略（不告警）；未匹配到引用实体的本方实体，其导航属性保持默认值 null。</para>
        /// <para>主键值未命中任何本方外键的引用实体不装配到任何本方实体，并经 <see cref="DiagnosticLog"/> 输出警告
        ///（丢配形态与 1:N 集合一致：string 大小写规则、数值键类型宽度不一致）。</para>
        /// </summary>
        /// <param name="parents">已加载的本方实体列表。</param>
        /// <param name="navigationMetadata">导航属性元数据，包含键列映射和对方实体类型信息。</param>
        /// <param name="references">已从 DbDataReader 映射的引用实体列表（可跨多个查询批次累积）。</param>
        internal static void PopulateReferences(
            IReadOnlyList<object> parents,
            NavigationMetadata navigationMetadata,
            IReadOnlyList<object> references)
        {
            // 先收集本方外键值集合（null 跳过），供索引过滤与未命中统计共用，整体保持 O(n)
            var parentKeys = new HashSet<object>();
            foreach (var parent in parents)
            {
                var foreignKeyValue = navigationMetadata.ThisKeyColumn.GetValue(parent);
                if (foreignKeyValue != null)
                    parentKeys.Add(foreignKeyValue);
            }

            // 按对方主键值建立索引（重复主键取第一条，1:1 引用不应有重复数据）；
            // 主键值为 null 的不参与索引；未命中任何本方外键的引用实体计入未匹配数、不进入索引
            var refByKey = new Dictionary<object, object>(parents.Count);
            int unmatchedCount = 0;
            foreach (var reference in references)
            {
                var primaryKeyValue = navigationMetadata.OtherKeyColumn.GetValue(reference);
                if (primaryKeyValue == null)
                    continue;

                if (!parentKeys.Contains(primaryKeyValue))
                {
                    unmatchedCount++;
                    continue;
                }

                if (!refByKey.ContainsKey(primaryKeyValue))
                    refByKey[primaryKeyValue] = reference;
            }

            // 存在未命中本方外键的引用实体时输出诊断警告（无未匹配的常规路径不产生日志开销）
            if (unmatchedCount > 0)
            {
                DiagnosticLog.Warn(nameof(NavigationAssembler),
                    "1:1 引用导航属性 '{NavigationProperty}' 装配有 {UnmatchedCount} 个引用实体的主键值未命中任何本方外键，这些实体未装配到任何本方实体；" +
                    "可能原因：string 键因数据库排序规则大小写不敏感而 .NET 装配区分大小写导致丢配，" +
                    "或本方外键与对方主键的数值类型宽度不一致（如 int 与 long，装箱值不相等导致丢配）。",
                    navigationMetadata.PropertyName, unmatchedCount);
            }

            // 装配到本方实体
            foreach (var parent in parents)
            {
                var foreignKeyValue = navigationMetadata.ThisKeyColumn.GetValue(parent);
                if (foreignKeyValue == null)
                    continue;

                if (refByKey.TryGetValue(foreignKeyValue, out var reference))
                    navigationMetadata.SetValue(parent, reference);
            }
        }
    }
}
