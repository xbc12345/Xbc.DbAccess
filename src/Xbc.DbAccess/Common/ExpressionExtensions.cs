using System.Linq.Expressions;

namespace Xbc.DbAccess.Common
{
    /// <summary>
    /// 表达式扩展方法，提供组合和解析功能。
    /// </summary>
    internal static class ExpressionExtensions
    {
        /// <summary>
        /// 剥离表达式外层的类型转换节点（Convert/ConvertChecked/TypeAs）：枚举比较、可空值类型装箱等会产生包裹。
        /// </summary>
        /// <param name="expression">待剥离表达式。</param>
        /// <returns>剥离后的内层表达式。</returns>
        public static Expression StripConversions(this Expression expression)
        {
            Guard.ThrowIfNull(expression, nameof(expression));

            var current = expression;
            while (current.NodeType == ExpressionType.Convert
                || current.NodeType == ExpressionType.ConvertChecked
                || current.NodeType == ExpressionType.TypeAs)
            {
                current = ((UnaryExpression)current).Operand;
            }

            return current;
        }

        /// <summary>
        /// 构建逻辑 And 表达式。
        /// </summary>
        /// <param name="left">左表达式。</param>
        /// <param name="right">右表达式。</param>
        /// <returns></returns>
        public static Expression<Func<T, bool>> AndAlso<T>(this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        {
            Guard.ThrowIfNull(left, nameof(left));
            Guard.ThrowIfNull(right, nameof(right));

            return Combine(left, right, Expression.AndAlso);
        }

        /// <summary>
        /// 构建逻辑 Or 表达式。
        /// </summary>
        /// <param name="left">左表达式。</param>
        /// <param name="right">右表达式。</param>
        /// <returns></returns>
        public static Expression<Func<T, bool>> OrElse<T>(this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        {
            Guard.ThrowIfNull(left, nameof(left));
            Guard.ThrowIfNull(right, nameof(right));

            return Combine(left, right, Expression.OrElse);
        }

        /// <summary>
        /// 构建逻辑 Not 表达式。
        /// </summary>
        /// <param name="predicate">原表达式。</param>
        /// <returns></returns>
        public static Expression<Func<T, bool>> Not<T>(this Expression<Func<T, bool>> predicate)
        {
            Guard.ThrowIfNull(predicate, nameof(predicate));

            var body = Expression.Not(predicate.Body);
            return Expression.Lambda<Func<T, bool>>(body, predicate.Parameters);
        }

        /// <summary>
        /// 使用指定的二元运算合并两个表达式，统一参数引用。
        /// </summary>
        /// <param name="left">左表达式。</param>
        /// <param name="right">右表达式。</param>
        /// <param name="func">二元运算工厂。</param>
        /// <returns></returns>
        private static Expression<Func<T, bool>> Combine<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right, Func<Expression, Expression, BinaryExpression> func)
        {
            // 将右操作数的参数替换为跟左操作数一样的参数
            // 例如：left: x=> x.Id == 1, right: y=> y.Name == "张三", func: Expression.AndAlso
            // 合并后的表达式：x=> x.Id == 1 && x.Name == "张三"
            var parameter = left.Parameters[0];
            var newLeft = left.Body;
            var newRight = new ParameterReplaceVisitor(right.Parameters[0], parameter).Visit(right.Body);
            var binaryExpr = func(newLeft, newRight);
            return Expression.Lambda<Func<T, bool>>(binaryExpr, parameter);
        }
    }
}
