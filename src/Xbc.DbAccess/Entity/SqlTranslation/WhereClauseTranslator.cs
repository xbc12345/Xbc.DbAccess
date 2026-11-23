using System.Collections;
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
    /// 谓词表达式（<c>Expression&lt;Func&lt;T,bool&gt;&gt;</c>）到 SQL WHERE 片段的翻译器，条件 UPDATE/DELETE 与查询、聚合、keyset 分页过滤的共享基础设施。
    /// <para>支持的表达式形态与异常类型详见 <see cref="Translate"/>；SQL 方言（标识符引用、参数上限）与参数构建器经构造函数注入，本类提供程序无关。</para>
    /// <para>实例仅支持单次 <see cref="Translate"/>（WHERE 文本与参数在实例字段上累积，重复调用抛 <see cref="InvalidOperationException"/>），调用方每次翻译新建实例。</para>
    /// </summary>
    internal sealed class WhereClauseTranslator : ExpressionVisitor
    {
        private readonly EntityMetadata metadata;
        private readonly ISqlDialect sqlDialect;
        private readonly IDbParameterBuilder parameterBuilder;
        private readonly ParameterNameAllocator parameterNameAllocator;
        private readonly StringBuilder sql = new StringBuilder();
        private readonly List<DbParameter> parameters = new List<DbParameter>();
        private ParameterExpression lambdaParameter;

        // 单次翻译守卫：WHERE 文本与参数在实例字段上累积，二次调用会拼出语义错误的 SQL（非崩溃而是错数据），见 Translate
        private bool translated;

        // 检测器在单次翻译内随 lambdaParameter 一并初始化，跨多次求值复用（见 Translate）
        private ParameterReferenceDetector parameterReferenceDetector;

        /// <summary>
        /// 初始化 WHERE 条件翻译器。
        /// </summary>
        /// <param name="metadata">实体元数据。</param>
        /// <param name="sqlDialect">SQL 方言（提供标识符引用与单语句参数上限）。</param>
        /// <param name="parameterBuilder">参数构建器（创建带提供程序前缀的 DbParameter，SQL 文本直接引用其 ParameterName）。</param>
        /// <param name="parameterNameAllocator">参数名分配器（与 SET 子句共享时须传入同一实例）。</param>
        public WhereClauseTranslator(
            EntityMetadata metadata,
            ISqlDialect sqlDialect,
            IDbParameterBuilder parameterBuilder,
            ParameterNameAllocator parameterNameAllocator)
        {
            this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            this.sqlDialect = sqlDialect ?? throw new ArgumentNullException(nameof(sqlDialect));
            this.parameterBuilder = parameterBuilder ?? throw new ArgumentNullException(nameof(parameterBuilder));
            this.parameterNameAllocator = parameterNameAllocator ?? throw new ArgumentNullException(nameof(parameterNameAllocator));
        }

        #region 翻译入口
        /// <summary>
        /// 翻译谓词表达式为 WHERE 条件片段与参数清单。
        /// <para>支持：比较运算（含可空列，两侧均可为实体列）、逻辑运算（&amp;&amp;/||/!）、布尔列与可空列 HasValue 直接作谓词、
        /// HasValue 的布尔比较（<c>x.Score.HasValue == true</c> 化简为 IS [NOT] NULL）、可空列 .Value 解包（<c>x.IsApproved.Value == true</c> 按内层列翻译）、
        /// 与 null 的 ==/!=（翻译为 IS [NOT] NULL）、集合 Contains（翻译为 IN 展开，含 Enumerable.Contains 扩展调用形式）、
        /// 字符串 Contains/StartsWith/EndsWith（翻译为 LIKE + ESCAPE 转义，% 、_ 与 [ 防注入）、
        /// string.IsNullOrEmpty/IsNullOrWhiteSpace（方言将空串视为 NULL 时退化为纯 NULL 判断）、
        /// 字符串列 ToLower/ToUpper/Trim/TrimStart/TrimEnd 与值的比较（列侧发射对应 SQL 函数，值侧应用相同变换后参数化）、
        /// DateTime/DateTimeOffset 列的 Year/Month/Day/Date/Hour/Minute/Second 日期部分比较（方言函数提取，仅变换列侧）、
        /// 操作数交换与比较符翻转（18 &lt; x.Age 翻译为 x.Age &gt; 18）、可求值子树折叠（不引用 lambda 参数的子树先求值再参数化，
        /// 闭包变量一律参数化、永不内联）。</para>
        /// <para>不支持（抛 <see cref="NotSupportedException"/>）：投影、Join、子查询、聚合等谓词内嵌套形态（分组查询是独立查询链点
        /// 而非谓词内形态，见 <see cref="IEntityQuery{T}"/> 的 GroupBy）、实体列上的算术运算、
        /// 引用实体参数的嵌套成员路径、导航属性、JSON 列参与比较、白名单之外的方法调用（含链式调用与带 CultureInfo 的
        /// ToLower/ToUpper）、常量谓词。不支持的能力与表达式/列形态一律抛 <see cref="NotSupportedException"/>（状态类错误如实例重复 Translate 仍抛 <see cref="InvalidOperationException"/>）。
        /// "算术运算/嵌套成员"的限制仅针对引用实体参数的形态，值侧的 <c>x.Age == 1 + 1</c> 与闭包嵌套成员属可求值子树折叠范围。</para>
        /// <para>已知边缘差异（C# 与 SQL 三值逻辑对 NULL 的行为不同）：可空列 != 值在 C# 中对 null 为 true，SQL 下 NULL 行
        /// 不命中（需包含时显式组合 <c>x.Field == null || x.Field != 值</c>）；NOT 包裹的比较与双可空列相等同族
        ///（C# 命中、SQL 不命中）；可空列 .Value 与字符串实例方法在 C# 中对 null 抛异常，翻译为 SQL 后 NULL 行静默排除；
        /// byte[] 列与列装箱比较在 C# 中是引用比较（恒 false），翻译为 SQL 值比较（与用户意图一致）；
        /// 字符串比较的大小写敏感性取决于数据库排序规则（大小写不敏感排序规则的数据库中 x.Name == "abc" 命中 "ABC"，
        /// C# ordinal 比较不命中）。</para>
        /// </summary>
        /// <param name="predicate">谓词表达式，如 <c>x => x.Age &gt; 18</c>。</param>
        /// <returns>翻译结果。</returns>
        public WhereClauseTranslation Translate(LambdaExpression predicate)
        {
            Guard.ThrowIfNull(predicate, nameof(predicate));

            // 单次使用契约的运行时守卫：二次调用会把两段条件拼成语义错误的 WHERE（静默错数据），转为显式异常
            if (translated)
                throw new InvalidOperationException("WhereClauseTranslator 实例仅支持单次 Translate 调用（WHERE 文本与参数在实例字段上累积，重复调用会拼接出语义错误的 SQL），请每次翻译新建实例。");

            if (predicate.Parameters.Count != 1)
                throw new NotSupportedException($"WHERE 条件表达式只支持一个参数，实际包含 {predicate.Parameters.Count} 个。");

            translated = true;
            lambdaParameter = predicate.Parameters[0];
            parameterReferenceDetector = new ParameterReferenceDetector(lambdaParameter);
            VisitPredicate(predicate.Body);

            // 兜底断言：未覆写的节点形态会静默产出空 SQL（基类 Visit 仅递归子节点），空 WHERE 会被调用方
            // 当作"无条件"省略 WHERE 子句（查询静默全表、条件写发出非法 SQL），在此显式拦截
            if (sql.Length == 0)
                throw new NotSupportedException($"WHERE 条件表达式 '{predicate.Body}' 未产出有效的 SQL 条件。");

            return new WhereClauseTranslation
            {
                WhereClause = sql.ToString(),
                Parameters = parameters
            };
        }
        #endregion

        #region 表达式访问
        /// <summary>
        /// 按谓词位置访问表达式：布尔列直接作谓词（如 <c>x => x.IsActive</c>，含 AND/OR/NOT 的操作数）时翻译为
        /// <c>[Col] = @p</c> 的参数化比较；其余表达式走常规访问。
        /// </summary>
        /// <param name="expression">谓词位置的表达式。</param>
        private void VisitPredicate(Expression expression)
        {
            // 可空列 HasValue 直接作谓词（x.Score.HasValue → IS NOT NULL）：须在 TryGetColumn 之前识别，
            // HasValue 是两级成员链，落入列解析会触发嵌套成员路径检查
            if (TryTranslateHasValue(expression, negated: false))
                return;

            var column = TryGetColumn(expression);
            if (column != null && (column.PropertyType == typeof(bool) || column.UnderlyingType == typeof(bool)))
            {
                // 部分方言的布尔列（如 bit）不能直接作谓词，统一翻译为参数化比较形式，对所有方言合法
                var parameter = parameterBuilder.CreateParameter(
                    parameterNameAllocator.Allocate(column.ColumnName),
                    column.ConvertToDb(true),
                    column.DbType);
                parameters.Add(parameter);

                // 无需括号包裹：= 的优先级高于 NOT/AND/OR，任何谓词位置都安全
                sql.Append($"{sqlDialect.QuoteIdentifier(column.ColumnName)} = {parameter.ParameterName}");
                return;
            }

            // 谓词位置的闭包根成员（闭包布尔变量 x => flag）：过滤结果与数据库无关，给出常量谓词同款清晰异常
            //（比较/方法调用等形态两侧均非列时另有精确守卫，不在此拦截）
            if (column == null && expression.StripConversions() is MemberExpression && !parameterReferenceDetector.Detect(expression))
                throw new NotSupportedException($"WHERE 条件不支持常量谓词 '{expression}'（不引用实体列的闭包变量或常量，过滤结果与数据库无关），请使用比较运算或布尔列。");

            // 谓词位置解析到非布尔列（如带 implicit operator bool 的自定义结构体列经 Convert 透传到达）：
            // 裸列名不是合法的 SQL 谓词，显式拒绝（布尔列已在前置分支处理）
            if (column != null)
                throw new NotSupportedException($"属性 '{column.PropertyName}' 不是布尔类型，不能直接作谓词，请使用比较运算（如 x.{column.PropertyName} == 值）。");

            Visit(expression);
        }

        /// <inheritdoc/>
        protected override Expression VisitBinary(BinaryExpression node)
        {
            switch (node.NodeType)
            {
                case ExpressionType.AndAlso:
                case ExpressionType.OrElse:
                    // 逻辑运算总是加括号，保证与 C# 运算符优先级一致的嵌套语义；操作数按谓词位置访问
                    sql.Append('(');
                    VisitPredicate(node.Left);
                    sql.Append(node.NodeType == ExpressionType.AndAlso ? " AND " : " OR ");
                    VisitPredicate(node.Right);
                    sql.Append(')');
                    return node;

                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                    // 比较运算的操作数按值翻译（布尔列比较走 [Col] = @p 的常规比较路径）
                    TranslateComparison(node);
                    return node;

                default:
                    throw new NotSupportedException($"WHERE 条件不支持二元运算 '{node.NodeType}'，仅支持比较运算（==/!=/&lt;/&lt;=/&gt;/&gt;=）与逻辑运算（&amp;&amp;/||）。");
            }
        }

        /// <inheritdoc/>
        protected override Expression VisitUnary(UnaryExpression node)
        {
            switch (node.NodeType)
            {
                case ExpressionType.Not:
                    // !x.Score.HasValue 直接翻译为 IS NULL，避免 NOT (col IS NOT NULL) 的双重否定
                    if (TryTranslateHasValue(node.Operand, negated: true))
                        return node;

                    // IsNullOrEmpty/IsNullOrWhiteSpace 的译文自带括号分组，NOT 直接前缀即可；其余操作数一律括号包裹
                    if (IsNullCheckMethod(node.Operand))
                    {
                        sql.Append("NOT ");
                        VisitPredicate(node.Operand);
                    }
                    else
                    {
                        sql.Append("NOT (");
                        VisitPredicate(node.Operand);
                        sql.Append(')');
                    }

                    return node;

                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                    // 顶层类型转换（如布尔列的可空转换、枚举比较的基类型转换）直接透传
                    Visit(node.Operand);
                    return node;

                default:
                    throw new NotSupportedException($"WHERE 条件不支持一元运算 '{node.NodeType}'，仅支持逻辑非（!）。");
            }
        }

        /// <summary>
        /// 判断表达式是否为 string.IsNullOrEmpty/IsNullOrWhiteSpace 调用。
        /// </summary>
        /// <param name="expression">待检测表达式。</param>
        /// <returns>是空值判断方法调用时返回 true。</returns>
        private static bool IsNullCheckMethod(Expression expression)
        {
            var call = expression.StripConversions() as MethodCallExpression;
            return call != null
                && call.Method.IsStatic
                && call.Method.DeclaringType == typeof(string)
                && (call.Method.Name == "IsNullOrEmpty" || call.Method.Name == "IsNullOrWhiteSpace");
        }

        /// <inheritdoc/>
        protected override Expression VisitMember(MemberExpression node)
        {
            var column = TryGetColumn(node);
            if (column != null)
            {
                sql.Append(sqlDialect.QuoteIdentifier(column.ColumnName));
                return node;
            }

            throw new NotSupportedException($"WHERE 条件不支持成员表达式 '{node}'，仅支持实体列、常量与闭包变量的比较。");
        }

        /// <inheritdoc/>
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var methodName = node.Method.Name;

            // string.IsNullOrEmpty/IsNullOrWhiteSpace 静态方法：参数必须是直连字符串列（x.Name），
            // 方法调用结果等其他表达式形态不在支持范围
            if (node.Method.IsStatic
                && node.Method.DeclaringType == typeof(string)
                && (methodName == "IsNullOrEmpty" || methodName == "IsNullOrWhiteSpace")
                && node.Arguments.Count == 1)
            {
                var stringColumn = TryGetColumn(node.Arguments[0]);
                if (stringColumn == null)
                    throw new NotSupportedException($"string.{methodName} 的参数必须是直连的字符串实体列（如 x => string.{methodName}(x.Name)），不支持闭包变量、方法调用结果等非直连列形态，实际参数为 '{node.Arguments[0]}'。");

                // 转换器列参与 SQL 函数/文本匹配的统一守卫（等值/比较/IN 等值域形态不受限）
                ThrowIfConverterColumn(stringColumn, $"string.{methodName} 判空");

                // 空字符串判断：x.Name.IsNullOrEmpty() → [Name] IS NULL
                TranslateNullOrEmptyCheck(stringColumn, methodName == "IsNullOrWhiteSpace");
                return node;
            }

            // Enumerable.Contains 扩展调用（按声明类型精确匹配，自定义静态 Contains 不误入）：
            // ids.Contains(x.Id) 编译为 Enumerable.Contains(ids, x.Id)
            if (node.Method.IsStatic
                && node.Method.DeclaringType == typeof(Enumerable)
                && methodName == "Contains"
                && node.Arguments.Count == 2)
            {
                var elementColumn = TryGetColumn(node.Arguments[1]);
                if (elementColumn == null)
                    throw new NotSupportedException($"Enumerable.Contains 的元素必须是直连实体列（如 ids.Contains(x.Id)），不支持其他表达式形态，实际元素为 '{node.Arguments[1]}'。");

                TranslateIn(node.Arguments[0], elementColumn);
                return node;
            }

            if (!node.Method.IsStatic && node.Arguments.Count == 1)
            {
                // 集合 Contains（实例方法形式）：list.Contains(x.Id) → IN 展开。
                // 接收者必须是可枚举集合且不是 string：string 的实例 Contains 是子串包含语义，不能按元素展开
                if (methodName == "Contains"
                    && node.Object.Type != typeof(string)
                    && typeof(IEnumerable).IsAssignableFrom(node.Object.Type))
                {
                    var elementColumn = TryGetColumn(node.Arguments[0]);
                    if (elementColumn == null)
                        throw new NotSupportedException($"集合 Contains 的元素必须是直连实体列（如 list.Contains(x.Id)），不支持其他表达式形态，实际元素为 '{node.Arguments[0]}'。");

                    TranslateIn(node.Object, elementColumn);
                    return node;
                }

                // 字符串方法（按声明类型精确匹配 string，自定义同名方法不误入为 LIKE）：
                // x.Name.Contains/StartsWith/EndsWith("...") → LIKE
                if (node.Method.DeclaringType == typeof(string)
                    && (methodName == "Contains" || methodName == "StartsWith" || methodName == "EndsWith"))
                {
                    var receiverColumn = TryGetColumn(node.Object);
                    if (receiverColumn == null)
                        throw new NotSupportedException($"字符串方法 '{methodName}' 的接收者必须是字符串实体列（如 x.Name.{methodName}(\"abc\")），实际接收者为 '{node.Object}'。");

                    var pattern = methodName == "Contains" ? LikePattern.Contains
                        : methodName == "StartsWith" ? LikePattern.StartsWith
                        : LikePattern.EndsWith;
                    TranslateLike(receiverColumn, node.Arguments[0], pattern);
                    return node;
                }
            }

            throw CreateUnsupportedMethodException(node);
        }

        /// <inheritdoc/>
        protected override Expression VisitConstant(ConstantExpression node)
        {
            throw new NotSupportedException($"WHERE 条件不支持常量谓词 '{node.Value}'，请使用比较运算或布尔列。");
        }

        /// <inheritdoc/>
        protected override Expression VisitTypeBinary(TypeBinaryExpression node)
        {
            // x is T（含恒真/可降形态，编译器仅警告不拦截）：若放行，基类递归子节点会静默产出空 WHERE 或裸列名
            throw new NotSupportedException($"WHERE 条件不支持类型测试 '{node}'（x is T），请使用列的比较条件。");
        }

        /// <inheritdoc/>
        protected override Expression VisitConditional(ConditionalExpression node)
        {
            // 三元谓词：若放行，基类递归三个子表达式会产出无分隔符的拼接垃圾 SQL（值侧三元属可求值子树，不受影响）
            throw new NotSupportedException($"WHERE 条件不支持条件表达式 '{node}'（?: 三元运算），请使用逻辑运算（&amp;&amp;/||）组合条件。");
        }

        /// <inheritdoc/>
        protected override Expression VisitInvocation(InvocationExpression node)
        {
            // 委托调用（含内联 lambda 即时调用）：递归会误把内层 lambda 体当 WHERE 子树翻译，产生张冠李戴的异常
            throw new NotSupportedException($"WHERE 条件不支持委托调用 '{node}'。");
        }

        /// <inheritdoc/>
        protected override Expression VisitParameter(ParameterExpression node)
        {
            // 裸 lambda 参数（如拆箱形态 (bool)(object)x 剥离转换后到达此处）：不是任何受支持的谓词形态
            throw new NotSupportedException($"WHERE 条件不支持未组合的裸参数 '{node}'。");
        }
        #endregion

        #region 可空 HasValue 处理
        /// <summary>
        /// 尝试把可空列的 HasValue 访问翻译为 IS [NOT] NULL：x.Score.HasValue → [Score] IS NOT NULL，
        /// NOT 语境（!x.Score.HasValue）→ [Score] IS NULL。
        /// <para>仅在谓词位置识别（含 AND/OR/NOT 的操作数）；比较位置的 HasValue 布尔比较由
        /// <see cref="TryTranslateHasValueComparison"/> 处理。未命中该模式时返回 false，交回常规路径处理。</para>
        /// </summary>
        /// <param name="expression">谓词位置表达式。</param>
        /// <param name="negated">是否处于 NOT 语境。</param>
        /// <returns>命中 HasValue 模式并完成翻译时返回 true。</returns>
        private bool TryTranslateHasValue(Expression expression, bool negated)
        {
            var column = TryGetHasValueColumn(expression);
            if (column == null)
                return false;

            sql.Append('(')
                .Append(sqlDialect.QuoteIdentifier(column.ColumnName))
                .Append(negated ? " IS NULL)" : " IS NOT NULL)");
            return true;
        }

        /// <summary>
        /// 识别可空列的 HasValue 成员访问并返回内层列（声明类型为 <see cref="Nullable{T}"/> 且内层为实体列时命中；
        /// 内层为闭包变量等非实体列时不按 HasValue 处理，交回值侧求值）。
        /// </summary>
        /// <param name="expression">待识别表达式。</param>
        /// <returns>内层列元数据；非 HasValue 形态、声明类型非可空或内层非实体列时返回 null。</returns>
        private ColumnMetadata TryGetHasValueColumn(Expression expression)
        {
            var member = expression.StripConversions() as MemberExpression;
            if (member == null || member.Member.Name != "HasValue")
                return null;

            // HasValue 仅存在于 Nullable<> 类型上，按声明类型过滤避免自定义同名成员的误匹配
            var declaringType = member.Member.DeclaringType;
            if (declaringType == null || !declaringType.IsGenericType || declaringType.GetGenericTypeDefinition() != typeof(Nullable<>))
                return null;

            return TryGetColumn(member.Expression.StripConversions());
        }

        /// <summary>
        /// 尝试把比较位置的 HasValue 布尔比较（<c>x.Score.HasValue == true</c>、<c>x.Score.HasValue != false</c>，
        /// 值侧可为常量或闭包布尔变量）化简为 IS [NOT] NULL，与谓词位置的 HasValue 翻译语义一致
        /// （Equal(true) / NotEqual(false) → IS NOT NULL，反之 IS NULL）。
        /// </summary>
        /// <param name="node">比较二元表达式。</param>
        /// <returns>命中 HasValue 比较模式并完成翻译时返回 true；值侧引用实体参数（如两个可空列的 HasValue 对比）时返回 false 交回常规路径。</returns>
        private bool TryTranslateHasValueComparison(BinaryExpression node)
        {
            if (node.NodeType != ExpressionType.Equal && node.NodeType != ExpressionType.NotEqual)
                return false;

            var leftColumn = TryGetHasValueColumn(node.Left);
            var rightColumn = TryGetHasValueColumn(node.Right);
            if (leftColumn == null && rightColumn == null)
                return false;

            var column = leftColumn ?? rightColumn;
            var valueExpression = leftColumn != null ? node.Right : node.Left;

            // 值侧必须可求值（常量或闭包布尔变量）；引用实体参数的（两侧均为 HasValue 的对比）不支持
            if (parameterReferenceDetector.Detect(valueExpression))
                return false;

            var value = Evaluate(valueExpression);
            if (value is not bool flag)
                throw new InvalidOperationException($"HasValue 比较的值必须是布尔常量或闭包布尔变量（如 x.Score.HasValue == true），实际为 '{value?.GetType()?.ToString() ?? "null"}'。");

            // Equal(true) / NotEqual(false) → IS NOT NULL；Equal(false) / NotEqual(true) → IS NULL
            var isNotNull = node.NodeType == ExpressionType.Equal ? flag : !flag;

            sql.Append('(')
                .Append(sqlDialect.QuoteIdentifier(column.ColumnName))
                .Append(isNotNull ? " IS NOT NULL)" : " IS NULL)");
            return true;
        }
        #endregion

        #region 比较运算翻译
        /// <summary>
        /// 翻译比较运算：识别列侧与值侧，处理列对列、操作数交换、null 特判与参数化；
        /// 列侧支持三种形态：直连列（x.Age）、字符串变换（x.Name.ToLower() / x.Name.Trim()）与日期部分提取（x.CreatedAt.Year）。
        /// </summary>
        /// <param name="node">比较二元表达式。</param>
        private void TranslateComparison(BinaryExpression node)
        {
            // 比较位置的 HasValue 布尔比较（x.Score.HasValue == true）：化简为 IS [NOT] NULL
            if (TryTranslateHasValueComparison(node))
                return;

            var left = TryResolveColumnOperand(node.Left);
            var right = TryResolveColumnOperand(node.Right);

            if (left != null && right != null)
            {
                // 直连列对直连列：[A] = [B]，无需参数
                if (left.Kind == ColumnOperandKind.Plain && right.Kind == ColumnOperandKind.Plain)
                {
                    sql.Append($"{left.Sql} {GetComparisonOperator(node.NodeType)} {right.Sql}");
                    return;
                }

                // 字符串变换与日期部分提取作用于列之后，函数结果与另一列的比较没有稳定的翻译语义，显式禁止
                throw new NotSupportedException($"比较运算 '{node.NodeType}' 的一侧是字符串变换或日期部分表达式 '{(left.Kind != ColumnOperandKind.Plain ? node.Left : node.Right)}'，这类操作数不支持列对列比较，仅支持与常量/闭包变量的值比较。");
            }

            ColumnOperand operand;
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
                // 操作数交换：18 < x.Age 翻译为 x.Age > 18，保证列在左侧、值在右侧
                operand = right;
                valueExpression = node.Left;
                comparisonOperator = FlipComparisonOperator(GetComparisonOperator(node.NodeType));
            }
            else
            {
                // 两侧均未解析出实体列：仍引用实体参数的（算术运算、嵌套成员路径等）是能力边界，
                // 完全不引用实体参数的条件（两侧均为常量/闭包）则是调用方误用（恒定条件，无实体参与）
                var leftReferencesParameter = parameterReferenceDetector.Detect(node.Left);
                var rightReferencesParameter = parameterReferenceDetector.Detect(node.Right);
                if (leftReferencesParameter || rightReferencesParameter)
                {
                    var referencingSide = leftReferencesParameter ? node.Left : node.Right;
                    throw new NotSupportedException($"WHERE 条件不支持引用实体参数的子表达式 '{referencingSide}'（如算术运算、嵌套成员路径或列间引用），仅支持实体列与常量/闭包变量的直接比较。");
                }

                throw new NotSupportedException($"比较运算 '{node.NodeType}' 的操作数必须至少有一侧为实体列，两侧均为常量或闭包表达式的条件无法翻译。");
            }

            if (operand.Kind == ColumnOperandKind.StringTransform)
            {
                TranslateStringTransformComparison(operand, valueExpression, comparisonOperator);
                return;
            }

            if (operand.Kind == ColumnOperandKind.DatePart)
            {
                TranslateDatePartComparison(operand, valueExpression, comparisonOperator);
                return;
            }

            var value = Evaluate(valueExpression);

            // 与 null 的等于/不等于比较翻译为 IS NULL / IS NOT NULL（SQL 中 "= null" 恒为 UNKNOWN，永远不会命中）
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

                throw new InvalidOperationException($"属性 '{operand.Column.PropertyName}' 不能与 null 做大小比较（SQL 中结果恒为 UNKNOWN，条件永不成立）。");
            }

            // 先注释掉，后续再考虑是否需要
            // // 枚举名称字符串列（EnumAsString）仅 ==/!=/IN 按名称双射语义自洽；大小比较时 SQL 按字典序与 C# 按枚举数值序
            // // 不一致（如 Normal(0) < Locked(1) 但 'Normal' > 'Locked'），显式拒绝
            // if (operand.Column.EnumAsString && comparisonOperator != "=" && comparisonOperator != "<>")
            //     throw new NotSupportedException($"属性 '{operand.Column.PropertyName}' 以枚举名称字符串存储（EnumAsString），不支持大小比较（名称的字典序与枚举数值序不一致），仅支持 ==/!= 与 IN。");

            var parameter = parameterBuilder.CreateParameter(
                parameterNameAllocator.Allocate(operand.Column.ColumnName),
                operand.Column.ConvertToDb(value),
                operand.Column.DbType);
            parameters.Add(parameter);
            sql.Append($"{operand.Sql} {comparisonOperator} {parameter.ParameterName}");
        }

        /// <summary>
        /// 翻译字符串变换列的比较（如 LOWER([Name]) = @p、LTRIM(RTRIM([Name])) = @p）：值侧应用与列侧相同的 C# 变换后再参数化。
        /// <para>已知边缘差异：SQL 的 LTRIM/RTRIM 仅去除空格，C# Trim 族去除全部空白字符。</para>
        /// </summary>
        /// <param name="operand">字符串变换的列操作数。</param>
        /// <param name="valueExpression">值侧表达式（常量或闭包变量）。</param>
        /// <param name="comparisonOperator">比较运算符（字符串无大小比较运算，实际只可能为 = 或 &lt;&gt;）。</param>
        private void TranslateStringTransformComparison(ColumnOperand operand, Expression valueExpression, string comparisonOperator)
        {
            var value = Evaluate(valueExpression);

            // C# 侧对 null 列调用变换方法会抛 NullReferenceException，条件既非恒真也非恒假，无法给出语义等价的 SQL
            if (value == null || value == DBNull.Value)
                throw new InvalidOperationException($"属性 '{operand.Column.PropertyName}' 的字符串变换比较值不能为 null。");

            var text = value as string;
            if (text == null)
                throw new InvalidOperationException($"属性 '{operand.Column.PropertyName}' 的字符串变换比较值必须是字符串，实际为 '{value.GetType()}'。");

            // 值侧应用与列侧相同的 C# 变换：用户书写 x.Name.ToLower() == "Abc" 的意图是忽略大小写匹配，
            // 仅变换列侧在区分大小写的排序规则下恒不命中；Trim 族不变换值侧会漏配带首尾空格的存储值。
            // 大小写变换使用不变文化（ToLowerInvariant/ToUpperInvariant）：SQL 的 LOWER/UPPER 按排序规则处理、无文化概念，
            // CurrentCulture 变换在特定文化下（如土耳其式 I）会与列侧 SQL 函数结果错配
            var transformed =
                operand.StringMethodName == "ToLower" ? text.ToLowerInvariant()
                : operand.StringMethodName == "ToUpper" ? text.ToUpperInvariant()
                : operand.StringMethodName == "Trim" ? text.Trim()
                : operand.StringMethodName == "TrimStart" ? text.TrimStart()
                : text.TrimEnd();

            var parameter = parameterBuilder.CreateParameter(
                parameterNameAllocator.Allocate(operand.Column.ColumnName),
                operand.Column.ConvertToDb(transformed),
                operand.Column.DbType);
            parameters.Add(parameter);
            sql.Append($"{operand.Sql} {comparisonOperator} {parameter.ParameterName}");
        }

        /// <summary>
        /// 翻译日期部分提取列的比较（如 YEAR([CreatedAt]) = @p）：仅变换列侧，值侧原样参数化。
        /// 年/月/日为整数比较；Date 为零时刻归一比较（与 C# 的 DateTime.Date 语义一致，值侧应传零时刻日期）。
        /// </summary>
        /// <param name="operand">日期部分的列操作数。</param>
        /// <param name="valueExpression">值侧表达式（常量或闭包变量）。</param>
        /// <param name="comparisonOperator">比较运算符。</param>
        private void TranslateDatePartComparison(ColumnOperand operand, Expression valueExpression, string comparisonOperator)
        {
            var value = Evaluate(valueExpression);

            // 值为 null 统一拒绝：C# 中日期部分与 null 的比较（提升比较恒为 false、.Value 访问对 null 抛异常）
            // 不存在应选中 NULL 日期行的合法写法，IS [NOT] NULL 特判会产生与 C# 相反的行集
            if (value == null || value == DBNull.Value)
                throw new InvalidOperationException($"属性 '{operand.Column.PropertyName}' 的日期部分比较值不能为 null（C# 语义下比较结果恒为 false，如需过滤 NULL 日期行请显式使用 x.{operand.Column.PropertyName} != null）。");

            object dbValue;
            DbType? dbType;

            if (operand.Part == DatePart.Date)
            {
                if (value is not DateTime and not DateTimeOffset)
                    throw new InvalidOperationException($"属性 '{operand.Column.PropertyName}' 的日期归一比较值必须是 DateTime/DateTimeOffset，实际为 '{value.GetType()}'。");

                dbValue = operand.Column.ConvertToDb(value);
                dbType = operand.Column.DbType;
            }
            else
            {
                if (value is not int and not long)
                    throw new InvalidOperationException($"日期部分 '{operand.Part}' 的比较值必须是整数，实际为 '{value.GetType()}'。");

                // 整数参数不沿用日期列的 DbType（那会把参数绑定成日期类型），置 null 由提供程序按值推断
                dbValue = value;
                dbType = null;
            }

            var parameter = parameterBuilder.CreateParameter(
                parameterNameAllocator.Allocate(operand.Column.ColumnName),
                dbValue,
                dbType);
            parameters.Add(parameter);
            sql.Append($"{operand.Sql} {comparisonOperator} {parameter.ParameterName}");
        }

        /// <summary>
        /// 获取比较运算符的 SQL 文本。
        /// </summary>
        /// <param name="nodeType">二元运算节点类型。</param>
        /// <returns>SQL 比较运算符。</returns>
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
        /// 翻转比较运算符（操作数交换时使用）：18 &lt; x.Age 等价于 x.Age &gt; 18。
        /// </summary>
        /// <param name="comparisonOperator">原比较运算符。</param>
        /// <returns>翻转后的比较运算符（= 与 &lt;&gt; 两侧对称，保持不变）。</returns>
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
        #endregion

        #region 方法调用翻译（IN/LIKE/判空）
        /// <summary>
        /// 翻译 string.IsNullOrEmpty/IsNullOrWhiteSpace 为 NULL 与空串判断。
        /// <para>已知边缘差异：SQL 的 TRIM 按标准仅去除空格，与 C# IsNullOrWhiteSpace（去除全部空白字符）存在差异。</para>
        /// <para>方言将零长度字符串视为 NULL 时（<see cref="ISqlDialect.TreatsEmptyStringAsNull"/>），
        /// IsNullOrEmpty 退化为纯 NULL 判断；空白串用 TRIM(col) IS NULL 判定。</para>
        /// </summary>
        /// <param name="column">字符串实体列。</param>
        /// <param name="checkWhiteSpace">是否包含空白串判断（IsNullOrWhiteSpace）。</param>
        private void TranslateNullOrEmptyCheck(ColumnMetadata column, bool checkWhiteSpace)
        {
            var quoted = sqlDialect.QuoteIdentifier(column.ColumnName);

            // Oracle：'' 即 NULL，空串条件被 NULL 条件覆盖，IsNullOrEmpty 无需（也无法）单独判断空串
            if (sqlDialect.TreatsEmptyStringAsNull && !checkWhiteSpace)
            {
                sql.Append('(').Append(quoted).Append(" IS NULL)");
                return;
            }

            string emptyCondition;
            if (checkWhiteSpace)
            {
                // Oracle 的 TRIM 古已有之且空串即 NULL，走 TRIM(col) IS NULL；
                // 其余方言用 LTRIM(RTRIM(col))：标准 TRIM 需 SQL Server 2017+（本框架支持 2005+），
                // MySQL/PostgreSQL/SQLite 均内置 LTRIM/RTRIM（与字符串变换比较 GetStringTransformSql 的处理一致）
                emptyCondition = sqlDialect.TreatsEmptyStringAsNull
                    ? "TRIM(" + quoted + ") IS NULL"
                    : "LTRIM(RTRIM(" + quoted + ")) = ''";
            }
            else
            {
                emptyCondition = quoted + " = ''";
            }

            sql.Append('(').Append(quoted).Append(" IS NULL OR ").Append(emptyCondition).Append(')');
        }

        /// <summary>
        /// 翻译集合 Contains 为 IN 子句，集合元素逐个参数化（空集合无法生成合法 IN 子句，显式失败）。
        /// </summary>
        /// <param name="collectionExpression">集合表达式（闭包变量）。</param>
        /// <param name="column">被包含判断的实体列。</param>
        private void TranslateIn(Expression collectionExpression, ColumnMetadata column)
        {
            var collectionValue = Evaluate(collectionExpression);
            if (collectionValue == null)
                throw new InvalidOperationException($"Contains 的调用目标集合为 null：'{collectionExpression}'，请检查闭包变量是否已初始化。");

            if (collectionValue is not IEnumerable collection)
                throw new InvalidOperationException($"Contains 的调用目标必须是集合，实际为 '{collectionExpression}'（求值结果类型 '{collectionValue.GetType()}'）。");

            // 与批量删除的 IN 展开一致，参数名按"列名_序号"展开（清理收口在分配器内）
            var parameterReferences = new List<string>();
            var index = 0;

            foreach (var element in collection)
            {
                if (element == null)
                    throw new InvalidOperationException("Contains 的集合中不能包含 null 元素，null 无法参与 IN 比较。");

                var parameter = parameterBuilder.CreateParameter(
                    parameterNameAllocator.Allocate($"{column.ColumnName}_{index}"),
                    column.ConvertToDb(element),
                    column.DbType);
                parameters.Add(parameter);
                parameterReferences.Add(parameter.ParameterName);
                index++;
            }

            if (parameterReferences.Count == 0)
                throw new InvalidOperationException("Contains 的集合为空，无法生成 IN 子句，请先检查集合是否有元素（或改用其他条件）。");

            // IN 嵌在更大的 WHERE 内无法自动分批，单看本条件就接近方言参数上限时提前给出可定位的失败（组合超限由 DbAccess 执行入口的兜底校验拦截）
            if (parameters.Count > sqlDialect.MaxParameterCount)
                throw new InvalidOperationException($"Contains 的集合元素过多：IN 展开后的参数数量 {parameters.Count} 超出当前方言上限 {sqlDialect.MaxParameterCount}，请缩小集合范围。");

            sql.Append(sqlDialect.QuoteIdentifier(column.ColumnName))
                .Append(" IN (")
                .Append(string.Join(", ", parameterReferences))
                .Append(')');
        }

        /// <summary>
        /// 翻译字符串匹配方法为 LIKE 子句：模式参数化并追加 ESCAPE 子句，通配符（%、_ 及方言附加通配符）与转义符本身被转义，防止用户输入改变匹配范围。
        /// </summary>
        /// <param name="column">字符串实体列。</param>
        /// <param name="argumentExpression">匹配值表达式（常量或闭包变量）。</param>
        /// <param name="pattern">匹配方式。</param>
        private void TranslateLike(ColumnMetadata column, Expression argumentExpression, LikePattern pattern)
        {
            // 转换器列参与 SQL 函数/文本匹配的统一守卫（等值/比较/IN 等值域形态不受限）
            ThrowIfConverterColumn(column, "LIKE 匹配");

            var value = Evaluate(argumentExpression);
            if (value == null || value == DBNull.Value)
                throw new InvalidOperationException($"属性 '{column.PropertyName}' 的 LIKE 匹配值不能为 null。");

            // char 值（net8 的 Contains('A') 重载）归一为单字符字符串，与其余字符串白名单方法行为一致
            var text = value as string ?? (value is char c ? c.ToString() : null);
            if (text == null)
                throw new InvalidOperationException($"属性 '{column.PropertyName}' 的 LIKE 匹配值必须是字符串，实际为 '{value.GetType()}'。");

            // 转义符选用 '!'：反斜杠在 MySQL 默认模式下是字符串字面量的转义符，ESCAPE '\' 会把闭引号转义掉成为未闭合字符串；
            // '!' 在全部受支持方言的字符串字面量中均无特殊含义，也不与 LIKE 通配符冲突。
            // '[' 仅是 SQL Server 方言的 LIKE 通配符，统一转义（'!['）对所有方言都合法，保证跨方言行为一致
            var escaped = text
                .Replace("!", "!!")
                .Replace("%", "!%")
                .Replace("_", "!_")
                .Replace("[", "![");

            switch (pattern)
            {
                case LikePattern.StartsWith:
                    escaped = escaped + "%";
                    break;

                case LikePattern.EndsWith:
                    escaped = "%" + escaped;
                    break;

                default:
                    escaped = "%" + escaped + "%";
                    break;
            }

            var parameter = parameterBuilder.CreateParameter(
                parameterNameAllocator.Allocate(column.ColumnName),
                column.ConvertToDb(escaped),
                column.DbType);
            parameters.Add(parameter);

            sql.Append(sqlDialect.QuoteIdentifier(column.ColumnName))
                .Append(" LIKE ")
                .Append(parameter.ParameterName)
                .Append(" ESCAPE '!'");
        }
        #endregion

        #region 列操作数解析
        /// <summary>
        /// 尝试将表达式解析为实体列：仅接受 lambda 参数上的单级属性访问（可包裹类型转换节点）。
        /// <para>参数根上的成员但不是映射列时抛出异常（导航属性或未映射成员）；
        /// 参数根上的多级成员路径同样抛出异常（嵌套成员无法直接翻译为列）；
        /// JSON 列与并发令牌列不具备查询条件资格，此处统一拦截。</para>
        /// </summary>
        /// <param name="expression">待解析表达式。</param>
        /// <returns>命中的列元数据；表达式与实体列无关（如闭包变量）时返回 null。</returns>
        private ColumnMetadata TryGetColumn(Expression expression)
        {
            // 剥离类型转换节点：枚举比较、可空值类型装箱等会产生 Convert/ConvertChecked 包裹
            var current = expression.StripConversions();
            var member = current as MemberExpression;

            if (member == null)
                return null;

            // 参数根直连成员（x => x.Col）：必须命中映射列，导航属性/未映射成员显式失败
            if (member.Expression == lambdaParameter)
            {
                var column = metadata.GetColumn(member.Member.Name);
                if (column == null)
                    throw new NotSupportedException($"成员 '{member.Member.Name}' 不是实体 '{metadata.EntityType}' 的映射列，WHERE 条件不支持导航属性或未映射成员。");

                // JSON 存储属性参与条件比较的是序列化文本而非结构化内容，语义不明确
                if (column.IsJson)
                    throw new NotSupportedException($"属性 '{column.PropertyName}' 启用了 JSON 存储（[Column(IsJson = true)]），不支持参与 WHERE 条件比较，参与比较的是序列化文本而非结构化内容。");

                // 并发令牌由框架乐观锁专用管理（SET 自动换发、乐观锁条件由框架内部拼接生成），用户查询条件不参与
                if (column.IsConcurrencyToken)
                    throw new NotSupportedException($"属性 '{column.PropertyName}' 标记为并发令牌（[Column(IsConcurrencyToken = true)]），由框架乐观锁专用管理，不支持在查询条件中使用。");

                return column;
            }

            // 参数根的多级成员路径（x => x.Nav.Prop）：导航属性无法翻译为列
            if (IsParameterRootedMember(member))
            {
                // 可空列的 .Value 解包（x.IsApproved.Value）：剥离后按内层列翻译（仅可空列命中）；
                // null 行为已知差异：C# 中 null 列的 .Value 访问抛异常，翻译为 SQL 后 NULL 行静默排除（类 doc 已声明）
                if (member.Member.Name == "Value")
                {
                    var innerColumn = TryGetColumn(member.Expression.StripConversions());
                    if (innerColumn != null && innerColumn.UnderlyingType != null)
                        return innerColumn;
                }

                // 可空列的 HasValue 出现在非常规位置（方法调用参数等，谓词与比较位置已在前置分支处理）：
                // 给出针对性引导而非泛泛的嵌套成员路径拒绝
                if (member.Member.Name == "HasValue")
                    throw new NotSupportedException($"不支持 '{member}'：HasValue 请直接作为谓词使用（如 <c>x.Score.HasValue</c>）、比较布尔值（如 <c>x.Score.HasValue == true</c>）或改写为 <c>x.Score != null</c>。");

                throw new NotSupportedException($"不支持嵌套成员路径 '{member}'，WHERE 条件仅支持实体列的直接比较。");
            }

            // 其余为闭包根成员（x => x.Col == localVar.Field）：与实体列无关，返回 null 交由求值路径参数化
            return null;
        }

        /// <summary>
        /// 判断成员访问链的根是否为 lambda 参数（如 x.CreatedAt.Year）。
        /// </summary>
        /// <param name="member">成员访问表达式。</param>
        /// <returns>根为 lambda 参数时返回 true。</returns>
        private bool IsParameterRootedMember(MemberExpression member)
        {
            var inner = member.Expression;
            while (inner is MemberExpression innerMember)
            {
                inner = innerMember.Expression;
            }

            return inner == lambdaParameter;
        }

        /// <summary>
        /// 解析比较运算的操作数为列操作数：识别直连列、字符串列的字符串变换与日期列的日期部分提取三种形态。
        /// <para>返回 null 表示该操作数与实体列无关（常量/闭包变量），交由求值路径参数化；
        /// 引用实体参数但不属于受支持形态的表达式（嵌套成员、链式方法调用等）显式抛出。</para>
        /// </summary>
        /// <param name="expression">待解析表达式。</param>
        /// <returns>列操作数；与实体列无关时返回 null。</returns>
        private ColumnOperand TryResolveColumnOperand(Expression expression)
        {
            var current = expression.StripConversions();

            // 方法调用：先尝试字符串变换（仅 string 的无参重载、调用目标必须是直连字符串列），
            // 其余引用实体参数的方法调用（带 CultureInfo 的 ToLower、Substring 等）按方法不支持显式失败，
            // 优于落入"操作数必须为实体列"的比较级错误（方法级原因更可定位）
            var call = current as MethodCallExpression;
            if (call != null)
            {
                // 字符串变换候选（仅白名单无参重载）才解析调用目标列；其余方法调用不在此处解析
                var transformCandidate = !call.Method.IsStatic
                    && call.Method.DeclaringType == typeof(string)
                    && IsStringTransformMethod(call.Method.Name)
                    && call.Arguments.Count == 0;
                var receiverColumn = transformCandidate ? TryGetColumn(call.Object) : null;
                if (receiverColumn != null)
                {
                    if (receiverColumn.PropertyType != typeof(string))
                        throw new NotSupportedException($"属性 '{receiverColumn.PropertyName}' 不是字符串类型，不支持字符串变换比较。");

                    // 转换器列参与 SQL 函数/文本匹配的统一守卫（等值/比较/IN 等值域形态不受限）
                    ThrowIfConverterColumn(receiverColumn, "字符串变换比较");

                    return new ColumnOperand
                    {
                        Kind = ColumnOperandKind.StringTransform,
                        Column = receiverColumn,
                        Sql = GetStringTransformSql(call.Method.Name, sqlDialect.QuoteIdentifier(receiverColumn.ColumnName)),
                        StringMethodName = call.Method.Name
                    };
                }

                // 引用实体参数的方法调用按方法不支持显式失败；与实体参数无关的（闭包变量的方法调用）视为值侧可求值表达式
                if (parameterReferenceDetector.Detect(call))
                    throw CreateUnsupportedMethodException(call);

                return null;
            }

            // 日期部分提取：x.CreatedAt.Year / .Month / .Day / .Date（可空列经 .Value 访问），
            // 须在 TryGetColumn 的嵌套成员路径检查之前识别，日期部分本身就是两级成员链
            var member = current as MemberExpression;
            if (member != null && TryGetDatePart(member, out DatePart part, out ColumnMetadata dateColumn))
            {
                return new ColumnOperand
                {
                    Kind = ColumnOperandKind.DatePart,
                    Column = dateColumn,
                    Part = part,
                    Sql = sqlDialect.GetDatePartExpression(sqlDialect.QuoteIdentifier(dateColumn.ColumnName), part)
                };
            }

            // 直连列
            var column = TryGetColumn(expression);
            if (column != null)
            {
                return new ColumnOperand
                {
                    Kind = ColumnOperandKind.Plain,
                    Column = column,
                    Sql = sqlDialect.QuoteIdentifier(column.ColumnName)
                };
            }

            return null;
        }
        #endregion

        #region 列侧形态识别（日期部分/字符串变换）
        /// <summary>
        /// 尝试把成员访问解析为日期列的日期部分提取（x.CreatedAt.Year / .Month / .Day / .Date / .Hour / .Minute / .Second）。
        /// <para>外层成员名必须是 Year/Month/Day/Date/Hour/Minute/Second 之一；内层（剥去类型转换与可空值类型的 .Value 访问后）
        /// 必须是 DateTime/DateTimeOffset 直连列，非日期类型的列显式失败；与实体列无关的成员链返回 false，交由常规路径处理。</para>
        /// </summary>
        /// <param name="member">待解析的成员访问表达式。</param>
        /// <param name="part">解析出的日期部分种类。</param>
        /// <param name="column">日期列元数据。</param>
        /// <returns>命中日期部分提取模式时返回 true。</returns>
        private bool TryGetDatePart(MemberExpression member, out DatePart part, out ColumnMetadata column)
        {
            // 静态成员（如 Settings.Month，Expression == null）不是实体实例的日期部分，交回常规路径按值侧求值
            if (member.Expression == null)
            {
                part = default;    // 未命中时的哑值（调用方不读取）
                column = null;
                return false;
            }

            switch (member.Member.Name)
            {
                case "Year":
                    part = DatePart.Year;
                    break;
                case "Month":
                    part = DatePart.Month;
                    break;
                case "Day":
                    part = DatePart.Day;
                    break;
                case "Date":
                    part = DatePart.Date;
                    break;
                case "Hour":
                    part = DatePart.Hour;
                    break;
                case "Minute":
                    part = DatePart.Minute;
                    break;
                case "Second":
                    part = DatePart.Second;
                    break;
                default:
                    part = DatePart.Year;
                    column = null;
                    return false;
            }

            // 内层剥去类型转换与可空值类型的 .Value 访问（x.CreatedAt.Value.Year），剩下须为直连列；
            // 实体自身的同名属性（如名为 Value 的映射列）不受影响，其 DeclaringType 是实体类型而非 Nullable<>
            var inner = member.Expression.StripConversions();
            var valueMember = inner as MemberExpression;
            if (valueMember != null
                && valueMember.Member.DeclaringType != null
                && valueMember.Member.DeclaringType.IsGenericType
                && valueMember.Member.DeclaringType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                inner = valueMember.Expression.StripConversions();
            }

            column = TryGetColumn(inner);
            if (column == null)
                return false;

            var dateType = column.UnderlyingType ?? column.PropertyType;
            if (dateType != typeof(DateTime) && dateType != typeof(DateTimeOffset))
                throw new NotSupportedException($"成员 '{member.Member.Name}' 的访问目标列 '{column.PropertyName}' 类型为 '{dateType}'，日期部分提取（Year/Month/Day/Date/Hour/Minute/Second）仅支持 DateTime/DateTimeOffset 列。");

            // 转换器列参与 SQL 函数/文本匹配的统一守卫（等值/比较/IN 等值域形态不受限）
            ThrowIfConverterColumn(column, "日期部分提取");
            return true;
        }

        /// <summary>
        /// 判断方法名是否属于受支持的字符串变换（string 的无参实例方法）。
        /// </summary>
        /// <param name="methodName">方法名。</param>
        /// <returns>属于受支持的字符串变换时返回 true。</returns>
        private static bool IsStringTransformMethod(string methodName)
        {
            return methodName == "ToLower" || methodName == "ToUpper"
                || methodName == "Trim" || methodName == "TrimStart" || methodName == "TrimEnd";
        }

        /// <summary>
        /// 生成字符串变换的列侧 SQL 表达式。
        /// </summary>
        /// <param name="methodName">字符串变换方法名。</param>
        /// <param name="quotedColumn">已引用的列名。</param>
        /// <returns>列侧 SQL 表达式。</returns>
        private static string GetStringTransformSql(string methodName, string quotedColumn)
        {
            switch (methodName)
            {
                case "ToLower":
                    return $"LOWER({quotedColumn})";
                case "ToUpper":
                    return $"UPPER({quotedColumn})";
                case "TrimStart":
                    return $"LTRIM({quotedColumn})";
                case "TrimEnd":
                    return $"RTRIM({quotedColumn})";
                default:
                    // Trim 发射 LTRIM(RTRIM(col)) 而非标准 TRIM(col)：后者需 SQL Server 2017+，嵌套形式全方言兼容；
                    // 已知边缘差异：SQL 的 LTRIM/RTRIM 仅去除空格，C# Trim 族去除全部空白字符
                    return $"LTRIM(RTRIM({quotedColumn}))";
            }
        }
        #endregion

        #region 求值与通用辅助
        /// <summary>
        /// 转换器列参与 SQL 函数或文本模式匹配（LOWER/YEAR/LIKE/判空等）时的统一守卫，命中时抛出 NotSupportedException。
        /// <para>等值/比较/IN 等值域形态不受限，参数经 <c>ConvertToDb</c> 与存储值同域比较，语义自洽。</para>
        /// </summary>
        /// <param name="column">目标列。</param>
        /// <param name="feature">受限能力名（用于异常消息）。</param>
        private static void ThrowIfConverterColumn(ColumnMetadata column, string feature)
        {
            // 判据为转换器变换（HasConverterTransform）：EnumAsString 列属性类型非字符串、JSON 列属性类型为复杂引用类型，
            // 均在更早的操作数类型校验处已被拒，到不了此处；转换器会改变数据库存储值（如加密、ticks 数值化），
            // 函数作用于存储文本的结果与 C#（按属性值）语义不一致，显式禁止
            if (column.HasConverterTransform)
                throw new NotSupportedException($"属性 '{column.PropertyName}' 的值经自定义或全局转换器变换后存储，数据库存储值与属性值不同构，不支持{feature}。");
        }

        /// <summary>
        /// 求值可求值子树：不引用 lambda 参数的子树编译求值后由调用方参数化（闭包变量一律参数化、永不内联进 SQL）。
        /// </summary>
        /// <param name="expression">待求值表达式。</param>
        /// <returns>求值结果。</returns>
        private object Evaluate(Expression expression)
        {
            // 求值前置守卫：引用了 x 的子树（如 x.Age > x.Score + 1 的算术右半）脱离实体实例无法编译求值，
            // 显式拦截给出明确错误，优于让 Compile 抛出"变量 x 未定义"的模糊异常
            if (parameterReferenceDetector.Detect(expression))
                throw new NotSupportedException($"WHERE 条件不支持引用实体参数的子表达式 '{expression}'（如算术运算、嵌套成员路径或列间引用），仅支持实体列与常量/闭包变量的直接比较。");

            // 子树确认与实体无关，编译求值（调用方将结果参数化，闭包值永不内联进 SQL）；
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
        /// 构造"方法调用不受支持"的异常。
        /// </summary>
        /// <param name="node">方法调用表达式。</param>
        /// <returns>异常实例。</returns>
        private static NotSupportedException CreateUnsupportedMethodException(MethodCallExpression node)
        {
            return new NotSupportedException(
                $"WHERE 条件不支持方法 '{node.Method.DeclaringType.Name}.{node.Method.Name}' 的调用，" +
                "仅支持集合 Contains、字符串 Contains/StartsWith/EndsWith、string.IsNullOrEmpty/IsNullOrWhiteSpace" +
                "（参数为直连字符串列）与字符串列 ToLower/ToUpper/Trim/TrimStart/TrimEnd 与值的比较。");
        }
        #endregion

        #region 嵌套类型
        /// <summary>
        /// 比较运算的列侧操作数：承载列元数据与列侧 SQL 文本（直连引用列、字符串变换包裹或方言日期部分表达式）。
        /// 仅被本翻译器消费、无独立文件消费方，作为嵌套类型保留（最小暴露面）。
        /// </summary>
        private sealed class ColumnOperand
        {
            /// <summary>操作数形态。</summary>
            public ColumnOperandKind Kind { get; set; }

            /// <summary>底层列元数据（参数命名与值转换复用列配置）。</summary>
            public ColumnMetadata Column { get; set; }

            /// <summary>列侧 SQL 文本。</summary>
            public string Sql { get; set; }

            /// <summary>字符串变换的方法名（ToLower/ToUpper/Trim/TrimStart/TrimEnd），值侧应用相同变换。</summary>
            public string StringMethodName { get; set; }

            /// <summary>日期部分种类。</summary>
            public DatePart Part { get; set; }
        }

        /// <summary>
        /// 列操作数种类：直连列、字符串变换与日期部分提取。
        /// </summary>
        private enum ColumnOperandKind
        {
            /// <summary>直连实体列（x.Age）。</summary>
            Plain = 0,

            /// <summary>字符串列的变换（x.Name.ToLower() / x.Name.Trim() 等）。</summary>
            StringTransform = 1,

            /// <summary>日期列的日期部分提取（x.CreatedAt.Year / .Hour / .Date 等）。</summary>
            DatePart = 2
        }

        /// <summary>字符串匹配方式。</summary>
        private enum LikePattern
        {
            /// <summary>包含：前后各追加一个 %。</summary>
            Contains,

            /// <summary>前缀匹配：末尾追加一个 %。</summary>
            StartsWith,

            /// <summary>后缀匹配：开头追加一个 %。</summary>
            EndsWith
        }
        #endregion
    }
}
