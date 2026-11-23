using System.Linq.Expressions;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 成员名映射形态：表达式体的识别结果。
    /// </summary>
    internal enum MemberMappingKind
    {
        /// <summary>非映射形态（单列成员访问、常量、计算表达式、ListInit 等），分支语义由调用方决定。</summary>
        None,

        /// <summary>对象初始化器（MemberInit 的成员绑定），配对按绑定序排列，成员名为绑定目标（结果落点/映射列）。</summary>
        Initializer,

        /// <summary>
        /// 匿名类型裸构造（NewExpression 的 Members 与 Arguments 一一对应，编译器生成），
        /// 配对按构造参数序排列；零实参为退化形态。
        /// <para>命名取「位置」而非「匿名」：零实参退化形态对任意类型的 NewExpression 都成立（不判匿名性），
        /// 且与 <see cref="Tuple"/> 的区分止于表达式写法——两者下游共用同一位置装配（ProjectionPlan.Shape.Positional）。</para>
        /// </summary>
        Positional,

        /// <summary>
        /// 元组构造（<see cref="ValueTuple"/> 的 Create 调用，或手工构建的 ValueTuple 构造函数 New），
        /// 配对按元素序排列，成员名恒为 null——元组字面量在表达式树中不可编译（CS8143），Create 形态亦不携带元素名。
        /// </summary>
        Tuple,

        /// <summary>
        /// 命名类型带参构造（x => new T(x.Id, x.Name)）：编译器已按实参类型绑定具体构造函数（多构造重载由 Roslyn 各自消歧），
        /// 配对按实参序排列，成员名恒为 null——构造实参不携带成员名，位置即映射（与 <see cref="Tuple"/> 同款式）。
        /// </summary>
        Constructor
    }

    /// <summary>
    /// 「成员名 → 表达式」映射形态的统一解析器，供投影列解析与 SET 赋值翻译共用：
    /// 识别对象初始化器、匿名类型、元组与命名类型带参构造四种形态，产出 (成员名, 右侧表达式) 配对序列；
    /// 带参构造 + 初始化器混合形态、非简单赋值绑定两类守卫在此单点执行，
    /// 配对解释（投影：右侧解析列、成员名作落点；SET：成员名映射列、右侧求值）与空形态消息归调用方。
    /// </summary>
    internal static class MemberMappingParser
    {
        /// <summary>
        /// 尝试将表达式体解析为成员名映射形态并枚举配对。
        /// </summary>
        /// <param name="body">表达式体（调用方已按需剥离外层转换节点，本方法不再处理转换节点）。</param>
        /// <param name="mappings">配对序列：<see cref="MemberMappingKind.Initializer"/> 按绑定序、<see cref="MemberMappingKind.Positional"/> 按构造参数序、<see cref="MemberMappingKind.Tuple"/> 与 <see cref="MemberMappingKind.Constructor"/> 按实参序（成员名恒 null）；空形态为空列表。</param>
        /// <returns>形态识别结果；<see cref="MemberMappingKind.None"/> 时 <paramref name="mappings"/> 为 null。</returns>
        public static MemberMappingKind TryParse(Expression body, out List<(string MemberName, Expression ValueExpression)> mappings)
        {
            Guard.ThrowIfNull(body, nameof(body));

            switch (body)
            {
                case MemberInitExpression memberInit:
                    // 带参构造的实参无法映射到成员名配对，静默忽略会让调用方误以为实参已生效，显式失败
                    if (memberInit.NewExpression.Arguments.Count > 0)
                        throw new NotSupportedException("不支持带参数的构造函数（x => new T(...) { ... }），请使用无参构造初始化器或匿名类型。");

                    mappings = new List<(string MemberName, Expression ValueExpression)>(memberInit.Bindings.Count);
                    foreach (var binding in memberInit.Bindings)
                    {
                        if (binding.BindingType != MemberBindingType.Assignment)
                            throw new NotSupportedException($"不支持成员绑定类型 '{binding.BindingType}'，仅支持简单赋值绑定。");

                        // 比如 x=> new T() { Id = x.Id, Name = x.Name }
                        // 则 mappings 为 (Id, x.Id) (Name, x.Name)
                        var memberAssignment = (MemberAssignment)binding;
                        mappings.Add((memberAssignment.Member.Name, memberAssignment.Expression));
                    }

                    return MemberMappingKind.Initializer;

                case NewExpression newExpression:
                    // 零实参为退化形态（手工构建表达式树可达，如 Expression.New(无参构造)）：不判类型直接以空配对返回，
                    // 让"空形态"消息先行——该形态对任意类型的零实参 New 统一成立
                    if (newExpression.Arguments.Count == 0)
                    {
                        mappings = new List<(string MemberName, Expression ValueExpression)>();
                        return MemberMappingKind.Positional;
                    }

                    // 匿名类型：编译器保证 Members 与 Arguments 一一对应且构造体纯赋值，位置配对安全
                    // 比如 x=> new { x.Id, x.Name }
                    // 则 mappings 为 (Id, x.Id) (Name, x.Name)
                    if (TypeHelper.IsAnonymousType(newExpression.Type))
                    {
                        mappings = new List<(string MemberName, Expression ValueExpression)>(newExpression.Arguments.Count);
                        for (int i = 0; i < newExpression.Arguments.Count; i++)
                        {
                            mappings.Add((newExpression.Members[i].Name, newExpression.Arguments[i]));
                        }

                        return MemberMappingKind.Positional;
                    }

                    // 元组构造（手工构建表达式树可达，如 Expression.New(ValueTuple<,...> 构造函数, ...)）：与 Create 调用形态同语义，
                    // 按元素序位置配对，成员名恒为 null（表达式树与结果类型反射均无元素名可提取）
                    if (TypeHelper.IsValueTuple(newExpression.Type))
                    {
                        mappings = new List<(string MemberName, Expression ValueExpression)>(newExpression.Arguments.Count);
                        foreach (var argument in newExpression.Arguments)
                        {
                            mappings.Add((null, argument));
                        }

                        return MemberMappingKind.Tuple;
                    }

                    // 命名类型带参构造（x => new T(x.Id, x.Name)）：编译器已按实参类型绑定具体构造函数
                    // （多构造重载由 Roslyn 在编译期各自消歧，NewExpression.Constructor 即绑定结果），
                    // 按实参序位置配对，成员名恒为 null（构造实参不携带成员名，位置即映射——与元组形态同款式）
                    mappings = new List<(string MemberName, Expression ValueExpression)>(newExpression.Arguments.Count);
                    foreach (var argument in newExpression.Arguments)
                    {
                        mappings.Add((null, argument));
                    }

                    return MemberMappingKind.Constructor;

                case MethodCallExpression methodCall:
                    // 元组构造在表达式树中的唯一可编译形态：ValueTuple.Create(...)——元组字面量被编译器禁止（CS8143），
                    // 按元素序位置配对，成员名恒为 null（该形态不携带元素名，元素名反射亦不可得）
                    if (methodCall.Method.DeclaringType == typeof(ValueTuple) && methodCall.Method.Name == "Create")
                    {
                        mappings = new List<(string MemberName, Expression ValueExpression)>(methodCall.Arguments.Count);
                        foreach (var argument in methodCall.Arguments)
                        {
                            mappings.Add((null, argument));
                        }

                        return MemberMappingKind.Tuple;
                    }

                    mappings = null;
                    return MemberMappingKind.None;

                default:
                    mappings = null;
                    return MemberMappingKind.None;
            }
        }
    }
}
