using System.Reflection;

namespace Xbc.DbAccess.Common
{
    /// <summary>
    /// 反射原语工具：成员查找类通用方法（返回 <see cref="MemberInfo"/> 等解析产物、带绑定策略的解析）。
    /// <para>本类承载成员级解析原语；实体元数据扫描等领域构建逻辑归各自模块，不在此收敛。</para>
    /// </summary>
    internal static class ReflectionHelper
    {
        /// <summary>
        /// 解析类型上的可写公共实例成员（可写属性优先、可写字段兜底），供命名类型初始化器投影的绑定落点解析单点使用。
        /// <para>解析产物（<see cref="MemberInfo"/>）由解析层固化进投影计划、mapper 直接绑定，
        /// 规则仅此一处——两层按名重复推导的分歧隐患在结构上消除。</para>
        /// </summary>
        /// <param name="type">投影结果类型。</param>
        /// <param name="memberName">成员名（编译期标识符）。</param>
        /// <returns>带公共 setter 的属性或非 initonly 公共字段；不存在可写成员时为 null（拒绝语义归调用方）。</returns>
        public static MemberInfo ResolveWritableMember(Type type, string memberName)
        {
            Guard.ThrowIfNull(type, nameof(type));
            Guard.ThrowIfNullOrWhiteSpace(memberName, nameof(memberName));

            var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.GetSetMethod(nonPublic: false) != null)
                return property;

            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
            return field != null && !field.IsInitOnly ? field : null;
        }
    }
}
