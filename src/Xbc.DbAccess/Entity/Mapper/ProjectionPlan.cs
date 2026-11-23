using System.Reflection;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 投影映射计划：解析层产出的投影构建期契约（结果形态 + 投影列 + 成员绑定 + 绑定构造函数），投影查询链（解析层 → 投影查询对象 → mapper）的统一传递载体。
    /// <para>投影结果形态由解析层在解析选择器时即已确定（裸成员访问 = 标量、匿名类型/元组 = 位置构造、命名类型带参构造 = 位置构造 + 绑定构造函数、命名类型初始化器 = 成员绑定），
    /// 以枚举显式声明取代「成员绑定可空性 + 列数 + 匿名类型判定」的多信号隐式组合推断。</para>
    /// <para>互锁守卫收进构造函数：非法组合（如 MemberBinding 缺成员绑定、Scalar 多列、Positional 携带成员绑定）在构建期即抛出，
    /// mapper 收到的计划必为合法组合；数组由解析层独占新建（不与外部共享），计划不复制以保持轻量。</para>
    /// </summary>
    internal sealed class ProjectionPlan
    {
        /// <summary>投影结果形态。</summary>
        internal enum Shape
        {
            /// <summary>单列标量：直接返回第 0 列读取表达式（结果类型即列值类型）。</summary>
            Scalar,

            /// <summary>位置构造类型：按构造参数位置映射（匿名类型/元组的唯一公共构造函数，或命名类型编译器绑定的带参构造，含单列形态，构造参数序 = 列序）。</summary>
            Positional,

            /// <summary>命名类型初始化器：无参构造 + 按成员绑定赋值（Members 绑定序 = 列序）。</summary>
            MemberBinding
        }

        /// <summary>获取投影结果形态。</summary>
        internal Shape ResultShape { get; }

        /// <summary>获取投影列元数据组（列序 = 结果集列序）；分组聚合的 COUNT(*) 列为 null（mapper 走裸读取分支）。</summary>
        internal ColumnMetadata[] Columns { get; }

        /// <summary>获取命名类型初始化器各列绑定的目标成员（绑定序 = 列序）；仅 <see cref="Shape.MemberBinding"/> 形态非空。</summary>
        internal MemberInfo[] Members { get; }

        /// <summary>
        /// 获取命名类型带参构造投影绑定的构造函数（编译器按实参类型绑定的结果，实参序 = 列序）；
        /// 仅 <see cref="Shape.Positional"/> 形态的命名构造形态非 null，匿名类型/元组形态为 null。
        /// </summary>
        internal ConstructorInfo Constructor { get; }

        private ProjectionPlan(Shape shape, ColumnMetadata[] columns, MemberInfo[] members, ConstructorInfo constructor)
        {
            switch (shape)
            {
                case Shape.Scalar:
                    if (columns.Length != 1)
                        throw new InvalidOperationException($"标量投影计划只允许单列，实际投影列数：{columns.Length}。");

                    if (members != null)
                        throw new InvalidOperationException("标量投影计划不应携带成员绑定。");

                    if (constructor != null)
                        throw new InvalidOperationException("标量投影计划不应携带绑定构造函数。");

                    break;

                case Shape.Positional:
                    if (members != null)
                        throw new InvalidOperationException("位置构造投影计划（匿名类型/元组/命名构造）按构造参数位置映射，不应携带成员绑定。");

                    break;

                case Shape.MemberBinding:
                    if (members == null || members.Length != columns.Length)
                        throw new InvalidOperationException($"成员绑定投影计划的成员绑定数（{members?.Length ?? 0}）与投影列数（{columns.Length}）不一致。");

                    if (constructor != null)
                        throw new InvalidOperationException("成员绑定投影计划不应携带绑定构造函数。");

                    break;

                default:
                    throw new InvalidOperationException($"未知的投影结果形态：{shape}。");
            }

            ResultShape = shape;
            Columns = columns;
            Members = members;
            Constructor = constructor;
        }

        /// <summary>创建单列标量投影计划（单列成员访问等非映射形态）。</summary>
        /// <param name="columns">投影列元数据（恰为 1 个元素）。</param>
        /// <returns>投影映射计划。</returns>
        internal static ProjectionPlan Scalar(ColumnMetadata[] columns)
        {
            return new ProjectionPlan(Shape.Scalar, columns, null, null);
        }

        /// <summary>创建位置构造投影计划（匿名类型/元组/命名类型带参构造，构造参数序 = 列序）。</summary>
        /// <param name="columns">投影列元数据组。</param>
        /// <param name="constructor">构造函数（命名构造为编译器绑定结果；匿名/元组为类型唯一公共构造，由解析层捕获固化）。</param>
        /// <returns>投影映射计划。</returns>
        internal static ProjectionPlan Positional(ColumnMetadata[] columns, ConstructorInfo constructor)
        {
            Guard.ThrowIfNull(constructor, nameof(constructor));

            // 互锁守卫：构造参数序 = 列序是位置装配的前提，构建期即拒绝（错位若穿透到首次执行才暴露，难排查）
            if (constructor.GetParameters().Length != columns.Length)
                throw new ArgumentException($"投影构造函数的参数数（{constructor.GetParameters().Length} 个）与投影列数（{columns.Length} 个）不一致。", nameof(constructor));

            return new ProjectionPlan(Shape.Positional, columns, null, constructor);
        }

        /// <summary>创建命名类型初始化器成员绑定投影计划（绑定序 = 列序）。</summary>
        /// <param name="columns">投影列元数据组。</param>
        /// <param name="members">各列绑定的目标成员（与列序一一对应）。</param>
        /// <returns>投影映射计划。</returns>
        internal static ProjectionPlan MemberBinding(ColumnMetadata[] columns, MemberInfo[] members)
        {
            return new ProjectionPlan(Shape.MemberBinding, columns, members, null);
        }
    }
}
