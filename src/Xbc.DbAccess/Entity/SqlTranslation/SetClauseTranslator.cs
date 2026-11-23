using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 赋值表达式（<c>Expression&lt;Func&lt;T,TValues&gt;&gt;</c>）到 SET 子句赋值清单的翻译器，条件更新的前置设施。支持的表达式形态与异常类型详见 <see cref="Translate"/>。
    /// </summary>
    internal static class SetClauseTranslator
    {
        /// <summary>
        /// 翻译赋值表达式为 SET 子句赋值清单。
        /// <para>支持对象初始化器（<c>x => new User { Age = 18 }</c>）与匿名类型（<c>x => new { Age = 18 }</c>）两种形式；
        /// 赋值右侧支持一切不引用实体参数的可求值表达式（常量、闭包变量及含嵌套 lambda 的表达式）。</para>
        /// <para>不支持（抛 <see cref="NotSupportedException"/>）：元组形态与带参构造形态（SET 按成员名寻址落列，两者均无成员名可承载寻址语义）、赋值右侧引用实体属性、赋值到主键列/标识列/并发令牌列/导航属性、
        /// 重复赋值同一列、带参构造 + 初始化器混合形态。赋值值的类型正确性由调用方保证，框架仅校验成员名与列映射正确性。</para>
        /// </summary>
        /// <param name="assignment">赋值表达式，如 <c>x => new { Age = 18 }</c>。</param>
        /// <param name="metadata">实体元数据。</param>
        /// <returns>按表达式出现顺序排列的赋值清单，元素为 (目标列, 数据库值) 元组——值已应用转换器链（null 已转 DBNull），参数命名由调用方经共享的参数名分配器完成。</returns>
        public static List<(ColumnMetadata Column, object DbValue)> Translate(LambdaExpression assignment, EntityMetadata metadata)
        {
            Guard.ThrowIfNull(assignment, nameof(assignment));
            Guard.ThrowIfNull(metadata, nameof(metadata));

            if (assignment.Parameters.Count != 1)
                throw new NotSupportedException($"SET 赋值表达式只支持一个参数，实际包含 {assignment.Parameters.Count} 个。");

            // 检测器以实体 lambda 参数为 target：嵌套 lambda 的参数（如 list.Select(y => y.Name) 的 y）
            // 不属于引用实体参数，值侧表达式可正常求值
            var detector = new ParameterReferenceDetector(assignment.Parameters[0]);
            var assignments = new List<(ColumnMetadata Column, object DbValue)>();

            // 映射形态识别与共同守卫（带参构造 + 初始化器混合形态、非简单赋值绑定）由 MemberMappingParser 单点执行；
            // 空形态（空初始化器/零实参构造）产出空配对，由下方赋值计数检查统一报出
            var kind = MemberMappingParser.TryParse(assignment.Body, out var mappings);
            if (kind == MemberMappingKind.None)
                throw new NotSupportedException("SET 表达式必须是对象初始化器（x => new T { ... }）或匿名类型（x => new { ... }），不支持其他表达式形式。");

            // 元组形态显式拒绝：SET 赋值按成员名寻址落列，元组形态（Create 调用）无元素名，位置装配语义不成立
            if (kind == MemberMappingKind.Tuple)
                throw new NotSupportedException("SET 表达式不支持元组形态（x => ValueTuple.Create(...)），请使用对象初始化器或匿名类型。");

            // 带参构造形态显式拒绝：SET 赋值按成员名寻址落列，构造实参不承载「更新哪一列」的语义（位置即映射），寻址落点不成立
            if (kind == MemberMappingKind.Constructor)
                throw new NotSupportedException("SET 表达式不支持带参构造形态（x => new T(...)），请使用对象初始化器或匿名类型。");

            foreach (var (memberName, valueExpression) in mappings)
            {
                var column = ResolveAssignmentColumn(metadata, memberName);

                // 重复赋值检测先于求值：右侧表达式可能含副作用或抛异常，被拒绝的赋值不应触发求值
                for (int i = 0; i < assignments.Count; i++)
                {
                    if (assignments[i].Column == column)
                        throw new NotSupportedException($"SET 表达式对列 '{column.ColumnName}' 重复赋值。");
                }

                var value = EvaluateValue(detector, valueExpression);
                assignments.Add((column, column.ConvertToDb(value)));
            }

            if (assignments.Count == 0)
                throw new NotSupportedException("SET 表达式未包含任何赋值，无法构建 UPDATE 语句。");

            return assignments;
        }

        /// <summary>
        /// 解析 SET 赋值的目标列并校验约束：非映射列与导航属性、主键、标识列、并发令牌列一律拒绝。
        /// </summary>
        /// <param name="metadata">实体元数据。</param>
        /// <param name="memberName">成员名。</param>
        /// <returns>解析出的目标列。</returns>
        private static ColumnMetadata ResolveAssignmentColumn(EntityMetadata metadata, string memberName)
        {
            var column = metadata.GetColumn(memberName);
            if (column == null)
            {
                if (metadata.GetNavigation(memberName) != null)
                    throw new NotSupportedException($"SET 表达式不支持导航属性 '{memberName}'，条件更新仅更新主实体行的列。");

                throw new NotSupportedException($"成员 '{memberName}' 不是实体 '{metadata.EntityType}' 的映射列，无法用于 SET 赋值。");
            }

            // 主键是行的稳定标识，条件更新不允许改写（与实体版更新跳过主键列的契约一致）
            if (column.IsPrimaryKey)
                throw new NotSupportedException($"属性 '{memberName}' 是主键列，主键是行的稳定标识，条件更新不允许改写。");

            if (column.IsIdentity)
                throw new NotSupportedException($"属性 '{memberName}' 是标识列，其值由数据库自动生成，不允许赋值。");

            // 令牌由框架在更新时自动换发，手工赋值会破坏乐观并发校验（与实体版更新跳过令牌列的契约一致）
            if (column.IsConcurrencyToken)
                throw new NotSupportedException($"属性 '{memberName}' 是并发令牌列，由框架在更新时自动换发，不允许赋值。");

            return column;
        }

        /// <summary>
        /// 求值赋值右侧表达式：仅支持不引用实体参数的子树（常量与闭包变量，含嵌套 lambda），引用实体属性（如引用其他列的值）时抛出异常。
        /// </summary>
        /// <param name="detector">实体参数引用检测器。</param>
        /// <param name="expression">赋值右侧表达式。</param>
        /// <returns>求值结果。</returns>
        private static object EvaluateValue(ParameterReferenceDetector detector, Expression expression)
        {
            if (detector.Detect(expression))
                throw new NotSupportedException($"SET 赋值右侧不支持引用实体属性 '{expression}'，仅支持常量、闭包变量、方法调用等可独立求值的表达式。");

            // DynamicInvoke 在 .NET Framework 上会把用户代码抛出的异常包装为 TargetInvocationException，解包还原原始异常
            try
            {
                return Expression.Lambda(expression).Compile().DynamicInvoke();
            }
            catch (TargetInvocationException ex)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;// 不可达，保持方法有返回路径
            }
        }
    }
}
