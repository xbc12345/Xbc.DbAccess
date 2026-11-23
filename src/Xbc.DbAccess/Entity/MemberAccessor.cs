using System.Linq.Expressions;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 成员表达式访问器，提供从 Lambda 表达式解析成员路径、成员名称和类型等信息的能力。
    /// <para>支持单级和多级属性访问路径的解析，用于实体属性映射和导航属性选择。</para>
    /// </summary>
    internal sealed class MemberAccessor
    {
        private readonly ParameterExpression parameterExpression;
        private readonly MemberExpression memberExpression;

        /// <summary>
        /// 成员访问路径。
        /// </summary>
        public string MemberPath { get; }

        /// <summary>
        /// 是否为多级访问路径。
        /// </summary>
        public bool IsDeepPath => MemberPath.Contains(".");

        /// <summary>
        /// 成员名称。如果为多级访问路径，返回最终成员名称。
        /// </summary>
        public string MemberName => memberExpression.Member.Name;

        /// <summary>
        /// 成员类型（属性的返回类型）。
        /// </summary>
        public Type MemberType => memberExpression.Type;

        /// <summary>
        /// 参数类型（Lambda 表达式的参数类型）。
        /// </summary>
        public Type ParameterType => parameterExpression.Type;

        /// <summary>
        /// 参数表达式。
        /// </summary>
        public ParameterExpression Parameter => parameterExpression;

        /// <summary>
        /// 成员表达式。
        /// </summary>
        public MemberExpression Member => memberExpression;

        private MemberAccessor(ParameterExpression parameter, MemberExpression member, string memberPath)
        {
            parameterExpression = parameter;
            memberExpression = member;
            MemberPath = memberPath;
        }

        /// <summary>
        /// 使用参数类型和成员访问路径创建成员访问器。
        /// </summary>
        /// <param name="parameterType">参数类型（Lambda 表达式的参数类型）。</param>
        /// <param name="memberPath">成员访问路径。</param>
        /// <returns></returns>
        public static MemberAccessor Create(Type parameterType, string memberPath)
        {
            Guard.ThrowIfNull(parameterType, nameof(parameterType));
            Guard.ThrowIfNullOrWhiteSpace(memberPath, nameof(memberPath));

            var memberNames = memberPath.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (memberNames.Length == 0)
                throw new ArgumentException($"无效的成员访问路径：{memberPath}。", nameof(memberPath));

            var parameter = Expression.Parameter(parameterType, "x");
            var member = Expression.PropertyOrField(parameter, memberNames[0]);

            for (int i = 1; i < memberNames.Length; i++)
            {
                member = Expression.PropertyOrField(member, memberNames[i]);
            }

            return new MemberAccessor(parameter, member, memberPath);
        }

        /// <summary>
        /// 使用参数类型和成员访问路径创建成员访问器。
        /// </summary>
        public static MemberAccessor Create<T>(string memberPath)
        {
            return Create(typeof(T), memberPath);
        }

        /// <summary>
        /// 使用 Lambda 表达式创建成员访问器。
        /// </summary>
        public static MemberAccessor Create(LambdaExpression selector)
        {
            Guard.ThrowIfNull(selector, nameof(selector));

            if (selector.Parameters.Count == 0)
                throw new ArgumentException("成员选择器必须包含至少一个参数。", nameof(selector));

            // 剥离外层类型转换节点（枚举键、可空值类型装箱等 Convert/ConvertChecked/TypeAs 包裹），非成员访问形态报无效选择器
            var member = selector.Body.StripConversions() as MemberExpression;
            if (member == null)
                throw new ArgumentException($"无效的成员选择器：{selector.Body}。", nameof(selector));

            // 校验成员链的根是 Lambda 参数：x => x.A.B 合法（根为参数 x）；
            // 闭包成员链（如 x => other.A）的根是闭包常量，不属于实体属性的访问路径，须报清晰错误
            var root = member;
            while (root.Expression is MemberExpression parent)
            {
                root = parent;
            }

            if (root.Expression is not ParameterExpression || root.Expression != selector.Parameters[0])
                throw new ArgumentException($"成员选择器的根必须是 Lambda 参数（如 x => x.{member.Member.Name}），不支持闭包成员链：{selector.Body}。", nameof(selector));

            var parameter = selector.Parameters[0];
            var memberPath = GetMemberPath(member);

            return new MemberAccessor(parameter, member, memberPath);
        }

        /// <summary>
        /// 使用强类型成员选择器创建成员访问器。
        /// </summary>
        public static MemberAccessor Create<T, TMember>(Expression<Func<T, TMember>> selector)
        {
            return Create(selector as LambdaExpression);
        }

        /// <summary>
        /// 获取成员访问路径。
        /// </summary>
        private static string GetMemberPath(MemberExpression member)
        {
            Guard.ThrowIfNull(member, nameof(member));

            var memberNames = new List<string>();
            var current = member;

            while (current != null)
            {
                memberNames.Insert(0, current.Member.Name);
                current = current.Expression as MemberExpression;
            }

            return string.Join(".", memberNames);
        }
    }
}
