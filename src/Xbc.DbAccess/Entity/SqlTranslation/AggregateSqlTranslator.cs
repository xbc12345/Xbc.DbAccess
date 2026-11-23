using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 分组聚合表达式翻译器，分组查询（GroupBy/Having/Select/排序）的表达式解析与 SQL 片段产出单点。
    /// <para>lambda 参数用 BCL 的 <see cref="IGrouping{TKey, TElement}"/>：聚合方法编译为 <see cref="Enumerable"/> 静态调用
    /// （按 DeclaringType 形态匹配白名单），g.Key 为普通属性访问。键选择器复用 <see cref="MemberMappingParser"/> 三形态分派，
    /// 成员解析落点是实体列元数据（含扁平视图实体声明的连接列），列资格守卫（JSON/并发令牌/转换器变换拒绝）与排序路径对齐。</para>
    /// <para>聚合 SQL 片段在 Select/排序收集期即产出（方言相关，构造查询时方言已确定）；HAVING 常量/闭包值经参数化，
    /// 与 WhereClauseTranslator 同款机制（<see cref="ParameterNameAllocator"/> 与源参数共享名域，闭包值一律参数化、永不内联）。</para>
    /// </summary>
    internal static class AggregateSqlTranslator
    {
        #region 键选择器解析

        /// <summary>
        /// 解析 GroupBy 键选择器为键列组：裸成员访问（None 形态，单键列）、匿名类型（Positional）与对象初始化器（Initializer），
        /// 配对解释与守卫由 <see cref="MemberMappingParser"/> 单点执行，成员解析经 <see cref="ResolveGuardedColumn"/> 落到实体列元数据。
        /// 元组与带参构造形态显式拒绝（复合键的成员名是 g.Key.X 的寻址落点，两者均无成员名可承载寻址语义）。
        /// </summary>
        /// <param name="keySelector">键选择器表达式（如 <c>x =&gt; new {{ x.CustomerId, x.Status }}</c>）。</param>
        /// <param name="metadata">实体元数据。</param>
        /// <returns>键列组（按选择器配对序）。</returns>
        /// <exception cref="NotSupportedException">键为计算表达式/常量、零配对空键、元组或带参构造形态、成员非映射列或列资格守卫未通过。</exception>
        internal static List<GroupedKeyColumn> ParseKeySelector(LambdaExpression keySelector, EntityMetadata metadata)
        {
            Guard.ThrowIfNull(keySelector, nameof(keySelector));
            Guard.ThrowIfNull(metadata, nameof(metadata));

            if (keySelector.Parameters.Count != 1)
                throw new NotSupportedException($"分组键表达式只支持一个参数，实际包含 {keySelector.Parameters.Count} 个。");

            var body = keySelector.Body.StripConversions();
            var kind = MemberMappingParser.TryParse(body, out var mappings);

            // None 形态 = 单成员键（裸成员访问，g.Key 即键列本身）
            if (kind == MemberMappingKind.None)
            {
                var column = ResolveGuardedColumn(metadata, Expression.Lambda(body, keySelector.Parameters[0]), "分组键");
                return new List<GroupedKeyColumn> { new GroupedKeyColumn(column, null) };
            }

            // 元组键显式拒绝：复合键的成员名是 g.Key.X 的寻址落点，元组形态（Create 调用）不携带元素名，寻址语义无法成立
            if (kind == MemberMappingKind.Tuple)
                throw new NotSupportedException("分组键选择器不支持元组形态（x => ValueTuple.Create(...)），请使用匿名类型或对象初始化器。");

            // 带参构造键显式拒绝：构造实参不携带成员名（位置即映射），g.Key.X 的寻址落点不成立，与元组键同一立场
            if (kind == MemberMappingKind.Constructor)
                throw new NotSupportedException("分组键选择器不支持带参构造形态（x => new T(...)），请使用匿名类型或对象初始化器。");

            if (mappings.Count == 0)
                throw new NotSupportedException("分组键选择器未包含任何列，分组键至少需要一列（如 x => new { x.CustomerId, x.Status }）。");

            // 复合键（匿名类型/初始化器）：成员名是 g.Key.X 的寻址落点
            var keyColumns = new List<GroupedKeyColumn>(mappings.Count);
            foreach (var (memberName, valueExpression) in mappings)
            {
                var column = ResolveGuardedColumn(metadata, Expression.Lambda(valueExpression, keySelector.Parameters[0]), "分组键");
                keyColumns.Add(new GroupedKeyColumn(column, memberName));
            }

            return keyColumns;
        }

        #endregion

        #region Select 与排序目标解析

        /// <summary>
        /// 解析分组投影选择器为输出列组与成员绑定：单列形态（g.Key / g.Key.X / 聚合调用）、匿名类型与元组多列（Positional 位置映射）、
        /// 命名类型带参构造多列（Positional + 编译器绑定的构造函数）与命名类型初始化器（绑定序 + 成员名落点）。
        /// 复合 g.Key 仅支持作为整个投影结果（展开为全部键列，匿名键走位置构造映射、命名键走成员绑定）；
        /// 多列选择器内的复合 g.Key 拒绝（位置映射无法承载展开）。
        /// </summary>
        /// <param name="selector">分组投影选择器表达式。</param>
        /// <param name="keyColumns">分组键列组。</param>
        /// <param name="resultType">投影结果类型（复合键整体出列时决定成员绑定形态）。</param>
        /// <param name="metadata">实体元数据。</param>
        /// <param name="sqlDialect">SQL 方言（聚合片段引用）。</param>
        /// <returns>输出列组（列序 = 结果集列序）、命名类型初始化器各列绑定的目标成员（匿名/元组/单列为 null，
        /// 解析层经 <see cref="ReflectionHelper.ResolveWritableMember"/> 解析并校验可写）、
        /// 命名类型带参构造绑定的构造函数（其余形态为 null）与投影结果形态。</returns>
        /// <exception cref="NotSupportedException">投影项不是键列引用或聚合调用、复合键整体出列形态非法、带参构造的构造函数归属/参数数不符（手搭树）、初始化器绑定目标不可写或重复绑定。</exception>
        internal static (GroupedOutputColumn[] Columns, MemberInfo[] Members, ConstructorInfo Constructor, ProjectionPlan.Shape Shape) ParseSelectSelector(
            LambdaExpression selector,
            List<GroupedKeyColumn> keyColumns,
            Type resultType,
            EntityMetadata metadata,
            ISqlDialect sqlDialect)
        {
            Guard.ThrowIfNull(selector, nameof(selector));
            Guard.ThrowIfNullOrEmpty(keyColumns, nameof(keyColumns));
            Guard.ThrowIfNull(metadata, nameof(metadata));
            Guard.ThrowIfNull(sqlDialect, nameof(sqlDialect));

            if (selector.Parameters.Count != 1)
                throw new NotSupportedException($"分组投影表达式只支持一个参数，实际包含 {selector.Parameters.Count} 个。");

            var groupParameter = selector.Parameters[0];
            var body = selector.Body.StripConversions();
            var kind = MemberMappingParser.TryParse(body, out var mappings);

            // None 形态：g.Key 整体（复合键展开为键列组）或单列（g.Key.X / 聚合调用）
            if (kind == MemberMappingKind.None)
            {
                if (IsGroupKeyAccess(body, groupParameter))
                {
                    // 复合键整体出列：匿名键走位置构造（Positional），命名键走成员绑定（Members 非空）
                    var (wholeColumns, wholeMembers) = ResolveWholeKeyOutputs(keyColumns, resultType);
                    return (wholeColumns, wholeMembers, null, wholeMembers == null ? ProjectionPlan.Shape.Positional : ProjectionPlan.Shape.MemberBinding);
                }

                return (new[] { ResolveSingleOutput(body, groupParameter, keyColumns, metadata, sqlDialect) }, null, null, ProjectionPlan.Shape.Scalar);
            }

            if (mappings.Count == 0)
                throw new NotSupportedException("分组投影表达式未包含任何列，初始化器、匿名类型或元组至少需要一列（键列引用或聚合调用）。");

            // 元组的 TRest 嵌套守卫：8 元素即 ValueTuple<T1..T7, TRest>，构造第 8 参为嵌套 ValueTuple 而非元素，位置装配无法承载，构建期即拒绝
            if (kind == MemberMappingKind.Tuple && TypeHelper.IsOverLengthValueTuple(resultType))
                throw new NotSupportedException("暂不支持 8 元素及以上的元组分组投影（TRest 嵌套构造），请使用 7 元素以内的元组或匿名类型。");

            var columns = new GroupedOutputColumn[mappings.Count];
            if (kind == MemberMappingKind.Positional || kind == MemberMappingKind.Tuple || kind == MemberMappingKind.Constructor)
            {
                // 匿名类型、元组与命名类型带参构造：位置映射，成员名不参与映射（匿名类型由编译器保证属性名唯一且只读，经构造函数赋值；
                // 元组无元素名可提取，经构造函数位置赋值；带参构造的构造函数由编译器按实参绑定，构造参数与实参一一对应，位置即映射）
                ConstructorInfo constructor = null;
                if (kind == MemberMappingKind.Constructor)
                {
                    constructor = ((NewExpression)body).Constructor;

                    // 构造函数须归属结果类型自身：newobj 按 DeclaringType 精确创建实例，基类构造无法产出结果类型实例兑现 lambda 返回契约；
                    // 编译器绑定（g => new TResult(...)）恒满足，手搭表达式树绑定基类/无关类型构造在构建期即拒绝
                    if (constructor.DeclaringType != resultType)
                        throw new NotSupportedException($"分组投影构造函数声明类型 '{constructor.DeclaringType}' 与结果类型 '{resultType}' 不一致：构造函数须归属结果类型自身（编译器按实参绑定保证，基类构造无法构造出结果类型实例）。");

                    // 手搭表达式树可令实参数与构造参数数不符（编译器绑定保证一致），构建期即拒绝
                    if (constructor.GetParameters().Length != mappings.Count)
                        throw new NotSupportedException($"分组投影构造函数的参数数（{constructor.GetParameters().Length}）与实参数（{mappings.Count}）不一致，请核对投影选择器。");
                }

                for (var i = 0; i < mappings.Count; i++)
                {
                    columns[i] = ResolveSingleOutput(mappings[i].ValueExpression, groupParameter, keyColumns, metadata, sqlDialect);
                }

                return (columns, null, constructor, ProjectionPlan.Shape.Positional);
            }

            // 命名类型初始化器：成员是结果落点（绑定序 = 列序），须可写且不可重复绑定（与实体投影同规矩）；
            // 解析产物（MemberInfo）固化进 plan，mapper 直接绑定（解析规则单点在 ReflectionHelper.ResolveWritableMember）
            var members = new MemberInfo[mappings.Count];
            var boundMembers = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < mappings.Count; i++)
            {
                var (memberName, valueExpression) = mappings[i];

                var member = ReflectionHelper.ResolveWritableMember(resultType, memberName);
                if (member == null)
                    throw new NotSupportedException($"成员 '{memberName}' 不是分组投影结果类型 '{resultType}' 的可写公共属性或字段，无法用于投影赋值。");

                if (!boundMembers.Add(memberName))
                    throw new NotSupportedException($"分组投影表达式对成员 '{memberName}' 重复赋值。");

                columns[i] = ResolveSingleOutput(valueExpression, groupParameter, keyColumns, metadata, sqlDialect);
                members[i] = member;
            }

            return (columns, members, null, ProjectionPlan.Shape.MemberBinding);
        }

        /// <summary>
        /// 解析分组排序选择器为排序目标组：g.Key 整体（复合键展开为全部键列同向）、g.Key.X（单键列）或聚合调用（别名引用）。
        /// </summary>
        /// <param name="keySelector">排序键选择器表达式。</param>
        /// <param name="keyColumns">分组键列组。</param>
        /// <param name="metadata">实体元数据。</param>
        /// <param name="sqlDialect">SQL 方言（聚合片段引用）。</param>
        /// <param name="descending">是否降序。</param>
        /// <returns>排序目标组（一次调用可产出多个目标，如复合键展开）。</returns>
        /// <exception cref="NotSupportedException">排序键不是分组键引用或聚合调用。</exception>
        internal static List<GroupedOrderingTarget> ParseOrderingSelector(
            LambdaExpression keySelector,
            List<GroupedKeyColumn> keyColumns,
            EntityMetadata metadata,
            ISqlDialect sqlDialect,
            bool descending)
        {
            Guard.ThrowIfNull(keySelector, nameof(keySelector));
            Guard.ThrowIfNullOrEmpty(keyColumns, nameof(keyColumns));
            Guard.ThrowIfNull(metadata, nameof(metadata));
            Guard.ThrowIfNull(sqlDialect, nameof(sqlDialect));

            if (keySelector.Parameters.Count != 1)
                throw new NotSupportedException($"分组排序表达式只支持一个参数，实际包含 {keySelector.Parameters.Count} 个。");

            var groupParameter = keySelector.Parameters[0];
            var body = keySelector.Body.StripConversions();

            // g.Key 整体：复合键展开为全部键列同向排序（单成员键等价于键列引用）
            if (IsGroupKeyAccess(body, groupParameter))
            {
                return keyColumns
                    .Select(key => new GroupedOrderingTarget(key.Column, null, descending))
                    .ToList();
            }

            // g.Key.X（含可空 .Value 解包）
            if (TryResolveKeyMember(body, groupParameter, keyColumns, out var keyColumn))
                return new List<GroupedOrderingTarget> { new GroupedOrderingTarget(keyColumn, null, descending) };

            // 聚合调用（排序引用由构建期分配的别名承载）
            if (body is MethodCallExpression call)
            {
                var fragment = TranslateAggregateCall(call, groupParameter, metadata, sqlDialect);
                if (fragment != null)
                    return new List<GroupedOrderingTarget> { new GroupedOrderingTarget(null, fragment, descending) };
            }

            throw new NotSupportedException(
                $"分组排序键 '{keySelector.Body}' 仅支持分组键引用（g.Key / g.Key.X）或聚合调用（g.Count() / g.Sum(x => x.A) 等），不支持计算表达式与常量。");
        }

        /// <summary>
        /// 解析单个投影项为键列引用或聚合片段：g.Key.X（含可空 .Value 解包）、单成员键的 g.Key 与 Enumerable 白名单聚合调用。
        /// </summary>
        /// <param name="expression">投影项表达式。</param>
        /// <param name="groupParameter">分组 lambda 参数（g）。</param>
        /// <param name="keyColumns">分组键列组。</param>
        /// <param name="metadata">实体元数据。</param>
        /// <param name="sqlDialect">SQL 方言。</param>
        /// <returns>输出列。</returns>
        /// <exception cref="NotSupportedException">投影项不是键列引用或聚合调用；多列语境的复合 g.Key 整体引用。</exception>
        private static GroupedOutputColumn ResolveSingleOutput(
            Expression expression,
            ParameterExpression groupParameter,
            List<GroupedKeyColumn> keyColumns,
            EntityMetadata metadata,
            ISqlDialect sqlDialect)
        {
            var stripped = expression.StripConversions();

            // g.Key.X 键列引用
            if (TryResolveKeyMember(stripped, groupParameter, keyColumns, out var keyColumn))
                return new GroupedOutputColumn(keyColumn, null);

            // g.Key 整体：单成员键合法（g.Key 即键列）；复合键在多列投影内拒绝（位置映射无法承载展开）
            if (IsGroupKeyAccess(stripped, groupParameter))
            {
                if (keyColumns.Count == 1 && keyColumns[0].MemberName == null)
                    return new GroupedOutputColumn(keyColumns[0].Column, null);

                throw new NotSupportedException(
                    "多列投影内不支持复合分组键整体引用（g.Key），位置映射无法承载键展开；请展开为 g.Key.X 逐成员引用，或以 Select(g => g.Key) 单独出键对象。");
            }

            // Enumerable 白名单聚合调用
            if (stripped is MethodCallExpression call)
            {
                var fragment = TranslateAggregateCall(call, groupParameter, metadata, sqlDialect);
                if (fragment != null)
                    return new GroupedOutputColumn(fragment.Column, fragment);
            }

            throw new NotSupportedException(
                $"分组投影项 '{expression}' 仅支持分组键引用（g.Key / g.Key.X）或聚合调用（g.Count() / g.Sum(x => x.A) 等），不支持常量、计算表达式或其他方法调用。");
        }

        /// <summary>
        /// 分组键整体出列（Select(g => g.Key)）：单成员标量键输出单列（g.Key 即键列本身，Members 为 null）；
        /// 复合键展开为全部键列，匿名键走位置构造映射（Members 为 null）、命名键走成员绑定（Members = 键成员名的解析产物）。
        /// </summary>
        /// <param name="keyColumns">分组键列组。</param>
        /// <param name="resultType">投影结果类型（须与键类型一致，编译器保证；手搭表达式树不一致时由映射器报错）。</param>
        /// <returns>输出列组与成员绑定组。</returns>
        /// <exception cref="NotSupportedException">命名键成员不是结果类型的可写公共属性或字段。</exception>
        private static (GroupedOutputColumn[] Columns, MemberInfo[] Members) ResolveWholeKeyOutputs(
            List<GroupedKeyColumn> keyColumns,
            Type resultType)
        {
            if (keyColumns.Count == 1 && keyColumns[0].MemberName == null)
                return (new[] { new GroupedOutputColumn(keyColumns[0].Column, null) }, null);

            var columns = keyColumns.Select(key => new GroupedOutputColumn(key.Column, null)).ToArray();
            if (TypeHelper.IsAnonymousType(resultType))
                return (columns, null);

            // 命名键：成员名解析为绑定目标（可写属性优先、可写字段兜底）——g.Key 整体出列与多列初始化器同一规矩，
            // 解析产物固化进计划（原先此路径不做可写校验、延迟到 mapper 编译期按名重推才失败，现提前到构建期显式拒绝）
            var members = new MemberInfo[keyColumns.Count];
            for (var i = 0; i < keyColumns.Count; i++)
            {
                var member = ReflectionHelper.ResolveWritableMember(resultType, keyColumns[i].MemberName);
                if (member == null)
                    throw new NotSupportedException($"成员 '{keyColumns[i].MemberName}' 不是分组投影结果类型 '{resultType}' 的可写公共属性或字段，无法用于投影赋值。");

                members[i] = member;
            }

            return (columns, members);
        }

        /// <summary>
        /// 匹配 Enumerable 白名单聚合调用（Count/LongCount/Sum/Average/Max/Min，按声明类型精确匹配）并翻译为聚合片段。
        /// <para>非白名单调用返回 null 交回调用方；白名单形态的问题（接收者非分组参数、条件计数、缺选择器、非数值列）抛精确异常。</para>
        /// </summary>
        /// <param name="call">方法调用表达式。</param>
        /// <param name="groupParameter">分组 lambda 参数（g）。</param>
        /// <param name="metadata">实体元数据。</param>
        /// <param name="sqlDialect">SQL 方言。</param>
        /// <returns>聚合片段；非白名单调用返回 null。</returns>
        internal static AggregateFragment TranslateAggregateCall(
            MethodCallExpression call,
            ParameterExpression groupParameter,
            EntityMetadata metadata,
            ISqlDialect sqlDialect)
        {
            if (!call.Method.IsStatic || call.Method.DeclaringType != typeof(Enumerable))
                return null;

            var methodName = call.Method.Name;
            if (methodName != "Count" && methodName != "LongCount"
                && methodName != "Sum" && methodName != "Average"
                && methodName != "Max" && methodName != "Min")
                return null;

            // 接收者必须是分组参数本身：g.Details.Sum(...) 等嵌套聚合（导航集合）显式拒绝
            if (call.Arguments.Count == 0 || call.Arguments[0].StripConversions() != groupParameter)
                throw new NotSupportedException(
                    $"聚合调用 '{call}' 的接收者必须是分组参数 g，不支持嵌套聚合（如 g.Details.Sum(...)）；聚合只作用于分组本身。");

            switch (methodName)
            {
                case "Count":
                case "LongCount":
                    if (call.Arguments.Count != 1)
                        throw new NotSupportedException("条件计数 g.Count(x => ...) 不支持（CASE WHEN 形态留后续版本），请使用不带条件的 g.Count()。");

                    return new AggregateFragment("COUNT(*)", null);

                case "Sum":
                case "Average":
                case "Max":
                case "Min":
                    if (call.Arguments.Count != 2)
                        throw new NotSupportedException(
                            $"g.{methodName}() 需要列选择器（如 g.{methodName}(x => x.A)），无选择器形态在实体类型上不可编译，请显式指定聚合列。");

                    if (call.Arguments[1].StripConversions() is not LambdaExpression selector || selector.Parameters.Count != 1)
                        throw new NotSupportedException($"g.{methodName} 的选择器必须是单参数 lambda（如 x => x.A），实际为 '{call.Arguments[1]}'。");

                    var column = ResolveGuardedColumn(metadata, selector, $"聚合 {methodName}");
                    if (methodName == "Sum" || methodName == "Average")
                    {
                        var valueType = column.UnderlyingType ?? column.PropertyType;
                        if (!TypeHelper.IsNumeric(valueType, includeNullable: false))
                            throw new NotSupportedException(
                                $"属性 '{column.PropertyName}' 的类型 '{valueType}' 不是数值类型，不支持 {methodName} 聚合（仅支持数值列；MAX/MIN 无此限制）。");
                    }

                    var quoted = sqlDialect.QuoteIdentifier(column.ColumnName);
                    var sql = methodName == "Sum" ? $"SUM({quoted})"
                        : methodName == "Max" ? $"MAX({quoted})"
                        : methodName == "Min" ? $"MIN({quoted})"
                        : sqlDialect.BuildAverageExpression(quoted);   // 与 AverageAsync 同约定：SQL Server 整型列裸 AVG 会截断小数
                    return new AggregateFragment(sql, column);

                default:
                    return null;
            }
        }

        #endregion

        #region 分组键引用识别

        /// <summary>
        /// 判断表达式是否为分组键整体访问（g.Key）：成员名 Key、声明类型为 <see cref="IGrouping{TKey, TElement}"/> 且接收者为分组参数。
        /// </summary>
        /// <param name="expression">待识别表达式（已剥离转换节点）。</param>
        /// <param name="groupParameter">分组 lambda 参数（g）。</param>
        /// <returns>是 g.Key 访问时返回 true。</returns>
        private static bool IsGroupKeyAccess(Expression expression, ParameterExpression groupParameter)
        {
            if (expression is not MemberExpression member || member.Member.Name != "Key")
                return false;

            // 按声明类型精确过滤（IGrouping<,>.Key 是接口上唯一的实例属性），避免自定义同名成员误匹配
            var declaringType = member.Member.DeclaringType;
            if (declaringType == null || !declaringType.IsGenericType || declaringType.GetGenericTypeDefinition() != typeof(IGrouping<,>))
                return false;

            // 静态成员（Expression == null，如 DateTime.Now）不可能挂在分组参数上
            if (member.Expression == null)
                return false;

            return member.Expression.StripConversions() == groupParameter;
        }

        /// <summary>
        /// 尝试将 g.Key.X（含可空 .Value 解包）解析为键列：复合键按成员名寻址、单成员键的 g.Key 即键列本身。
        /// <para>非 g.Key 根的表达式返回 false 交回调用方；g.Key 根但成员不可解析时抛出精确异常。</para>
        /// </summary>
        /// <param name="expression">待解析表达式（已剥离转换节点）。</param>
        /// <param name="groupParameter">分组 lambda 参数（g）。</param>
        /// <param name="keyColumns">分组键列组。</param>
        /// <param name="column">解析出的键列元数据。</param>
        /// <returns>命中 g.Key.X 形态时返回 true。</returns>
        private static bool TryResolveKeyMember(Expression expression, ParameterExpression groupParameter, List<GroupedKeyColumn> keyColumns, out ColumnMetadata column)
        {
            column = null;

            if (expression is not MemberExpression member)
                return false;

            // 可空键成员的 .Value 解包（g.Key.Score.Value → g.Key.Score）
            if (member.Member.Name == "Value"
                && member.Member.DeclaringType != null
                && member.Member.DeclaringType.IsGenericType
                && member.Member.DeclaringType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                if (member.Expression == null || member.Expression.StripConversions() is not MemberExpression inner)
                    return false;

                member = inner;
            }

            // 解包后恰为 g.Key 整体：可空单成员键的 g.Key.Value 落到键列（编译器允许 Nullable 键的 .Value 访问）；
            // 其余整体形态交回调用方（复合键展开或拒绝）
            if (IsGroupKeyAccess(member, groupParameter))
            {
                if (keyColumns.Count == 1 && keyColumns[0].MemberName == null && keyColumns[0].Column.UnderlyingType != null)
                {
                    column = keyColumns[0].Column;
                    return true;
                }

                return false;
            }

            // g.Key.X：内层必须是 g.Key（静态成员 Expression == null 不可能是 g.Key 成员）
            if (member.Expression == null || !IsGroupKeyAccess(member.Expression.StripConversions(), groupParameter))
                return false;

            var memberName = member.Member.Name;
            var matched = keyColumns.FirstOrDefault(key => key.MemberName == memberName);
            if (matched != null)
            {
                column = matched.Column;
                return true;
            }

            if (keyColumns.Count == 1 && keyColumns[0].MemberName == null)
                throw new NotSupportedException(
                    $"分组键为单成员时 g.Key 即键列本身（类型 '{keyColumns[0].Column.PropertyType}'），不支持再访问成员 '{memberName}'。");

            throw new NotSupportedException(
                $"成员 '{memberName}' 不是分组键的成员，分组键成员访问仅支持键选择器中声明的成员（g.Key.{memberName} 无法寻址）。");
        }

        #endregion

        #region 列资格守卫

        /// <summary>
        /// 解析实体成员表达式为经过资格验证的映射列：单级成员访问校验与未映射拒绝由 <see cref="EntityMetadata.ResolveColumn"/> 执行，
        /// 多级路径的根为导航属性时给出导航专属引导（设计文档 §3.4），
        /// 列资格守卫（JSON 存储属性/并发令牌/存储变换属性拒绝，枚举映射语义确定放行）与排序/聚合路径对齐。
        /// </summary>
        /// <param name="metadata">实体元数据。</param>
        /// <param name="selector">单成员访问 lambda（由调用方包装修复）。</param>
        /// <param name="usage">用途名（异常消息上下文，如「分组键」「聚合 Sum」）。</param>
        /// <returns>命中的列元数据。</returns>
        private static ColumnMetadata ResolveGuardedColumn(EntityMetadata metadata, LambdaExpression selector, string usage)
        {
            ColumnMetadata column;
            try
            {
                column = metadata.ResolveColumn(selector);
            }
            catch (ArgumentException ex) when (IsNavigationRootFailure(metadata, selector))
            {
                // 导航路径（x.Customer.Name）无法推导手写 SQL 结果集列名（别名契约不可知、同名列撞名无法消解），
                // 拒绝并引导连接列以列形态声明进视图实体
                throw new NotSupportedException(
                    $"'{selector.Body}' 是导航路径，不支持作为{usage}——导航路径无法推导结果集列名，请将连接列以列形态声明进视图实体（如 [Column(\"CustomerName\")]），表达式按属性访问引用。", ex);
            }

            // JSON 存储属性参与分组/聚合的是序列化文本而非结构化内容，显式禁止（与 WHERE 侧禁止对齐）
            if (column.IsJson)
                throw new NotSupportedException($"属性 '{column.PropertyName}' 启用了 JSON 存储（[Column(IsJson = true)]），不支持{usage}，参与分组/聚合的是序列化文本而非结构化内容。");

            // 并发令牌由框架乐观锁专用管理（换发与乐观锁条件均在框架内部），不参与分组/聚合
            if (column.IsConcurrencyToken)
                throw new NotSupportedException($"属性 '{column.PropertyName}' 标记为并发令牌（[Column(IsConcurrencyToken = true)]），由框架乐观锁专用管理，不支持{usage}。");

            // 转换器守卫（JSON 已在上方拦下）：存储值与属性值不同构，分组/聚合语义不可信
            if (column.HasConverterTransform)
                throw new NotSupportedException($"属性 '{column.PropertyName}' 的存储值经自定义转换器变换，存储值与属性值不同构，不支持{usage}，请改用存储类型一致的属性。");

            return column;
        }

        /// <summary>
        /// 判断列解析失败是否源于导航根的多级路径（x.Customer.Name 形态）：根段解析为导航属性且路径多级时返回 true。
        /// </summary>
        private static bool IsNavigationRootFailure(EntityMetadata metadata, LambdaExpression selector)
        {
            try
            {
                var accessor = MemberAccessor.Create(selector);
                if (!accessor.IsDeepPath)
                    return false;

                var rootSegment = accessor.MemberPath.Split('.')[0];
                return metadata.GetNavigation(rootSegment) != null;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region HAVING 谓词翻译

        /// <summary>
        /// 翻译 HAVING 谓词（<c>Expression&lt;Func&lt;IGrouping&lt;TKey,T&gt;,bool&gt;&gt;</c>）为条件片段与参数清单。
        /// <para>支持：聚合与常量/闭包值的比较（含操作数交换、与 null 的 =/!= 特判）、键列与常量/闭包值的比较、键列间比较、
        /// 逻辑组合（&amp;&amp;/||/!）；常量谓词与布尔键列裸谓词拒绝（WhereClauseTranslator 同款守卫）。</para>
        /// <para>每次翻译新建实例（片段与参数在实例上累积）；调用方对多个谓词的片段按 AND 合并并各自包裹括号。</para>
        /// </summary>
        /// <param name="predicate">HAVING 谓词表达式。</param>
        /// <param name="metadata">实体元数据（聚合列解析）。</param>
        /// <param name="keyColumns">分组键列组。</param>
        /// <param name="sqlDialect">SQL 方言。</param>
        /// <param name="parameterBuilder">参数构建器。</param>
        /// <param name="allocator">参数名分配器（与同语句其他参数共享名域）。</param>
        /// <returns>翻译结果。</returns>
        internal static HavingTranslation TranslateHaving(
            LambdaExpression predicate,
            EntityMetadata metadata,
            List<GroupedKeyColumn> keyColumns,
            ISqlDialect sqlDialect,
            IDbParameterBuilder parameterBuilder,
            ParameterNameAllocator allocator)
        {
            Guard.ThrowIfNull(predicate, nameof(predicate));
            Guard.ThrowIfNull(metadata, nameof(metadata));
            Guard.ThrowIfNullOrEmpty(keyColumns, nameof(keyColumns));
            Guard.ThrowIfNull(sqlDialect, nameof(sqlDialect));
            Guard.ThrowIfNull(parameterBuilder, nameof(parameterBuilder));
            Guard.ThrowIfNull(allocator, nameof(allocator));

            var translator = new HavingTranslator(metadata, keyColumns, sqlDialect, parameterBuilder, allocator);
            return translator.Translate(predicate);
        }

        /// <summary>HAVING 谓词翻译结果：条件片段与参数清单（片段在调用方按 AND 合并并包裹括号）。
        /// 仅在本翻译器与调用方之间流转、无独立文件消费方，故作为嵌套类型保留（与跨文件产物模型区分）。</summary>
        internal sealed class HavingTranslation
        {
            public string Fragment { get; set; }

            public List<DbParameter> Parameters { get; set; }
        }

        /// <summary>
        /// HAVING 谓词翻译器：分组谓词的递归翻译（比较运算识别键列/聚合操作数、值侧编译求值后参数化）。
        /// </summary>
        private sealed class HavingTranslator
        {
            private readonly EntityMetadata metadata;
            private readonly ISqlDialect sqlDialect;
            private readonly IDbParameterBuilder parameterBuilder;
            private readonly ParameterNameAllocator allocator;
            private readonly Dictionary<string, ColumnMetadata> keyMemberMap;// 复合键成员名 → 键列
            private readonly ColumnMetadata singleKeyColumn;// 单成员键列（g.Key 即键列），复合键为 null
            private readonly StringBuilder sql = new StringBuilder();
            private readonly List<DbParameter> parameters = new List<DbParameter>();
            private ParameterExpression groupParameter;
            private ParameterReferenceDetector referenceDetector;

            // 单次翻译守卫：片段与参数在实例字段上累积，二次调用会拼出语义错误的 SQL（与 WhereClauseTranslator 同规矩）
            private bool translated;

            public HavingTranslator(
                EntityMetadata metadata,
                List<GroupedKeyColumn> keyColumns,
                ISqlDialect sqlDialect,
                IDbParameterBuilder parameterBuilder,
                ParameterNameAllocator allocator)
            {
                this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
                this.sqlDialect = sqlDialect ?? throw new ArgumentNullException(nameof(sqlDialect));
                this.parameterBuilder = parameterBuilder ?? throw new ArgumentNullException(nameof(parameterBuilder));
                this.allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));

                keyMemberMap = new Dictionary<string, ColumnMetadata>(StringComparer.Ordinal);
                foreach (var key in keyColumns)
                {
                    if (key.MemberName != null)
                        keyMemberMap[key.MemberName] = key.Column;
                }

                singleKeyColumn = keyColumns.Count == 1 && keyColumns[0].MemberName == null
                    ? keyColumns[0].Column
                    : null;
            }

            /// <summary>
            /// 翻译谓词为 HAVING 条件片段与参数清单（实例仅支持单次调用）。
            /// </summary>
            public HavingTranslation Translate(LambdaExpression predicate)
            {
                if (translated)
                    throw new InvalidOperationException("HAVING 翻译器实例仅支持单次 Translate 调用（片段与参数在实例字段上累积），请每次翻译新建实例。");

                if (predicate.Parameters.Count != 1)
                    throw new NotSupportedException($"HAVING 条件表达式只支持一个参数，实际包含 {predicate.Parameters.Count} 个。");

                translated = true;
                groupParameter = predicate.Parameters[0];
                referenceDetector = new ParameterReferenceDetector(groupParameter);
                VisitPredicate(predicate.Body);

                // 兜底断言：谓词未产出有效条件时显式拦截（空 HAVING 会被调用方拼出非法 SQL）
                if (sql.Length == 0)
                    throw new NotSupportedException($"HAVING 条件表达式 '{predicate.Body}' 未产出有效的 SQL 条件。");

                return new HavingTranslation { Fragment = sql.ToString(), Parameters = parameters };
            }

            /// <summary>
            /// 按谓词位置访问表达式：仅比较运算、逻辑组合与逻辑非可作谓词；布尔键列裸谓词、常量谓词与其他形态拒绝。
            /// </summary>
            private void VisitPredicate(Expression expression)
            {
                var stripped = expression.StripConversions();

                if (stripped is BinaryExpression binary)
                {
                    switch (binary.NodeType)
                    {
                        case ExpressionType.AndAlso:
                        case ExpressionType.OrElse:
                            // 逻辑运算总是加括号，保证与 C# 运算符优先级一致的嵌套语义
                            sql.Append('(');
                            VisitPredicate(binary.Left);
                            sql.Append(binary.NodeType == ExpressionType.AndAlso ? " AND " : " OR ");
                            VisitPredicate(binary.Right);
                            sql.Append(')');
                            return;

                        case ExpressionType.Equal:
                        case ExpressionType.NotEqual:
                        case ExpressionType.LessThan:
                        case ExpressionType.LessThanOrEqual:
                        case ExpressionType.GreaterThan:
                        case ExpressionType.GreaterThanOrEqual:
                            TranslateComparison(binary);
                            return;

                        default:
                            throw new NotSupportedException(
                                $"HAVING 条件不支持二元运算 '{binary.NodeType}'，仅支持比较运算（==/!=/&lt;/&lt;=/&gt;/&gt;=）与逻辑运算（&amp;&amp;/||）——聚合上的算术运算不属于支持范围。");
                    }
                }

                if (stripped.NodeType == ExpressionType.Not && stripped is UnaryExpression unary)
                {
                    sql.Append("NOT (");
                    VisitPredicate(unary.Operand);
                    sql.Append(')');
                    return;
                }

                throw new NotSupportedException(
                    $"HAVING 条件不支持 '{expression}'，仅支持键列/聚合与常量或闭包值的比较运算及 &&/||/! 组合（布尔键列请显式比较，如 g.Key.IsActive == true）。");
            }

            /// <summary>
            /// 翻译比较运算：识别键列/聚合操作数，处理键列对键列、操作数交换、null 特判与参数化。
            /// </summary>
            private void TranslateComparison(BinaryExpression node)
            {
                var left = TryResolveOperand(node.Left);
                var right = TryResolveOperand(node.Right);

                if (left != null && right != null)
                {
                    // 聚合间比较显式拒绝（SUM(A) == COUNT(B) 等），与聚合间算术运算同属能力边界；
                    // 聚合与键列的比较合法（HAVING SUM(A) >= B），与键列对键列一样直拼
                    if (left.IsAggregate && right.IsAggregate)
                        throw new NotSupportedException(
                            "HAVING 不支持聚合间比较（两侧均为聚合，如 g.Count() == g.Sum(x => x.A)）——聚合只能与常量/闭包值或键列比较，聚合间运算留后续版本。");

                    // 分组列间比较（键列对键列、聚合对键列）：[A] = [B]，无需参数
                    sql.Append(left.Sql).Append(' ').Append(GetComparisonOperator(node.NodeType)).Append(' ').Append(right.Sql);
                    return;
                }

                GroupOperand operand;
                Expression valueExpression;
                string comparisonOperator;

                if (left != null)
                {
                    operand = left;
                    valueExpression = node.Right;
                    comparisonOperator = GetComparisonOperator(node.NodeType);
                }
                else if (right != null)
                {
                    // 操作数交换：5 <= g.Count() 翻译为 g.Count() >= 5，保证键列/聚合在左侧、值在右侧
                    operand = right;
                    valueExpression = node.Left;
                    comparisonOperator = FlipComparisonOperator(GetComparisonOperator(node.NodeType));
                }
                else
                {
                    // 两侧均未解析出键列/聚合：引用分组参数的（算术运算、嵌套成员路径等）是能力边界，
                    // 完全不引用分组参数的条件（两侧均为常量/闭包）则是调用方误用（恒定条件，无分组参与）
                    var leftReferencesGroup = referenceDetector.Detect(node.Left);
                    var rightReferencesGroup = referenceDetector.Detect(node.Right);
                    if (leftReferencesGroup || rightReferencesGroup)
                    {
                        var referencingSide = leftReferencesGroup ? node.Left : node.Right;
                        throw new NotSupportedException(
                            $"HAVING 条件不支持引用分组参数的子表达式 '{referencingSide}'（如聚合上的算术运算、嵌套成员路径），仅支持键列/聚合与常量/闭包值的直接比较。");
                    }

                    throw new NotSupportedException(
                        $"HAVING 条件 '{node}' 的两侧均为常量或闭包表达式（不引用分组键与聚合），过滤结果与数据库无关，请使用键列或聚合的比较条件。");
                }

                var value = Evaluate(valueExpression);

                // 与 null 的等于/不等于比较翻译为 IS NULL / IS NOT NULL（SUM 全 NULL 值组返回 NULL，是合法形态）
                if (value == null || value == DBNull.Value)
                {
                    if (comparisonOperator == "=")
                    {
                        sql.Append(operand.Sql).Append(" IS NULL");
                        return;
                    }

                    if (comparisonOperator == "<>")
                    {
                        sql.Append(operand.Sql).Append(" IS NOT NULL");
                        return;
                    }

                    throw new InvalidOperationException("键列/聚合不能与 null 做大小比较（SQL 中结果恒为 UNKNOWN，条件永不成立），仅支持 ==/!= 与 null 比较。");
                }

                // 参数化：键列与带源列的聚合按列转换链与 DbType 参数化；COUNT(*) 无源列，值原样交提供程序推断
                DbType? dbType;
                object dbValue;
                if (operand.Column != null)
                {
                    dbValue = operand.Column.ConvertToDb(value);
                    dbType = operand.Column.DbType;
                }
                else
                {
                    dbValue = value;
                    dbType = null;
                }

                var parameter = parameterBuilder.CreateParameter(
                    allocator.Allocate(operand.ParameterBaseName),
                    dbValue,
                    dbType);
                parameters.Add(parameter);

                sql.Append(operand.Sql).Append(' ').Append(comparisonOperator).Append(' ').Append(parameter.ParameterName);
            }

            /// <summary>
            /// 尝试将比较操作数解析为分组操作数：聚合调用、g.Key 整体（单成员键）与 g.Key.X 键列引用。
            /// <para>返回 null 表示操作数与分组无关（常量/闭包），交由求值路径参数化；
            /// 引用分组参数但不属于受支持形态的表达式显式抛出。</para>
            /// </summary>
            private GroupOperand TryResolveOperand(Expression expression)
            {
                var stripped = expression.StripConversions();

                // 方法调用：引用分组参数的按聚合白名单匹配，其余引用形态精确拒绝；不引用分组参数的（闭包方法调用）交由求值
                if (stripped is MethodCallExpression call)
                {
                    if (!referenceDetector.Detect(call))
                        return null;

                    var fragment = TranslateAggregateCall(call, groupParameter, metadata, sqlDialect);
                    if (fragment != null)
                        return new GroupOperand(fragment, fragment.Column?.ColumnName ?? "Count");

                    throw new NotSupportedException(
                        $"HAVING 条件不支持方法 '{call.Method.DeclaringType.Name}.{call.Method.Name}' 的调用，仅支持分组聚合（g.Count()/g.LongCount()/g.Sum/g.Average/g.Max/g.Min）。");
                }

                if (stripped is MemberExpression member)
                {
                    // g.Key 整体作操作数：单成员键合法（即键列）；复合键不能整体参与比较（无单一列落点）
                    if (IsGroupKeyAccess(member, groupParameter))
                    {
                        if (singleKeyColumn != null)
                            return new GroupOperand(sqlDialect.QuoteIdentifier(singleKeyColumn.ColumnName), singleKeyColumn, singleKeyColumn.ColumnName);

                        throw new NotSupportedException(
                            "HAVING 不支持复合分组键整体比较（g.Key），请比较具体键成员（如 g.Key.CustomerId == 5）；对键列的条件与前置 Where 语义等价，也可移到 GroupBy 之前。");
                    }

                    // g.Key.X 键列引用（含可空 .Value 解包）
                    if (TryResolveKeyMember(member, groupParameter, KeyColumnsFromMap(), out var keyColumn))
                        return new GroupOperand(sqlDialect.QuoteIdentifier(keyColumn.ColumnName), keyColumn, keyColumn.ColumnName);

                    // 引用分组参数但不属于键引用形态（如 g.GetHashCode()）：精确拒绝，优于落入常量谓词误导
                    if (referenceDetector.Detect(member))
                        throw new NotSupportedException(
                            $"HAVING 条件不支持成员表达式 '{member}'，仅支持分组键引用（g.Key / g.Key.X）与聚合调用。");

                    return null;
                }

                return null;
            }

            /// <summary>
            /// 以键成员映射构造键列组供 <see cref="TryResolveKeyMember"/> 寻址（单成员键以 MemberName null 呈现）。
            /// </summary>
            private List<GroupedKeyColumn> KeyColumnsFromMap()
            {
                var columns = new List<GroupedKeyColumn>(keyMemberMap.Count + (singleKeyColumn != null ? 1 : 0));
                if (singleKeyColumn != null)
                    columns.Add(new GroupedKeyColumn(singleKeyColumn, null));

                foreach (var pair in keyMemberMap)
                    columns.Add(new GroupedKeyColumn(pair.Value, pair.Key));

                return columns;
            }

            /// <summary>
            /// 求值可求值子树：不引用分组参数的子树编译求值后由调用方参数化（闭包变量一律参数化、永不内联进 SQL）。
            /// </summary>
            private object Evaluate(Expression expression)
            {
                if (referenceDetector.Detect(expression))
                    throw new NotSupportedException(
                        $"HAVING 条件不支持引用分组参数的子表达式 '{expression}'（如算术运算、嵌套成员路径），仅支持键列/聚合与常量/闭包值的直接比较。");

                // DynamicInvoke 在 .NET Framework 上会把用户代码抛出的异常包装为 TargetInvocationException，解包还原原始异常
                try
                {
                    return Expression.Lambda(expression).Compile().DynamicInvoke();
                }
                catch (TargetInvocationException ex)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
                    throw;// 不可达，保持方法有返回路径
                }
            }

            /// <summary>
            /// 获取比较运算符的 SQL 文本。
            /// </summary>
            private static string GetComparisonOperator(ExpressionType nodeType)
            {
                switch (nodeType)
                {
                    case ExpressionType.Equal:
                        return "=";
                    case ExpressionType.NotEqual:
                        return "<>";
                    case ExpressionType.LessThan:
                        return "<";
                    case ExpressionType.LessThanOrEqual:
                        return "<=";
                    case ExpressionType.GreaterThan:
                        return ">";
                    case ExpressionType.GreaterThanOrEqual:
                        return ">=";
                    default:
                        throw new NotSupportedException($"比较运算 '{nodeType}' 不受支持。");
                }
            }

            /// <summary>
            /// 翻转比较运算符（操作数交换时使用）：5 &lt;= g.Count() 等价于 g.Count() &gt;= 5。
            /// </summary>
            private static string FlipComparisonOperator(string comparisonOperator)
            {
                switch (comparisonOperator)
                {
                    case "<":
                        return ">";
                    case "<=":
                        return ">=";
                    case ">":
                        return "<";
                    case ">=":
                        return "<=";
                    default:
                        return comparisonOperator;
                }
            }

            /// <summary>
            /// 分组操作数：键列引用或聚合片段（HAVING 在组级作用域求值，聚合片段无需别名）。
            /// </summary>
            private sealed class GroupOperand
            {
                public GroupOperand(AggregateFragment fragment, string parameterBaseName)
                {
                    Sql = fragment.Sql;
                    Column = fragment.Column;
                    IsAggregate = true;
                    ParameterBaseName = parameterBaseName;
                }

                public GroupOperand(string sql, ColumnMetadata column, string parameterBaseName)
                {
                    Sql = sql;
                    Column = column;
                    IsAggregate = false;
                    ParameterBaseName = parameterBaseName;
                }

                /// <summary>操作数 SQL 文本（键列方言引用或聚合片段）。</summary>
                public string Sql { get; }

                /// <summary>源列元数据（参数化用）；COUNT 类聚合与无源列形态为 null。</summary>
                public ColumnMetadata Column { get; }

                /// <summary>是否为聚合操作数（聚合间比较守卫依据）。</summary>
                public bool IsAggregate { get; }

                /// <summary>参数名基名（键列/聚合源列名为列名，COUNT 为 Count）。</summary>
                public string ParameterBaseName { get; }
            }
        }

        #endregion
    }
}
