using System.Linq.Expressions;

namespace Xbc.DbAccess.Common
{
    /// <summary>
    /// 表达式参数替换访问器，将表达式中的源参数替换为目标参数。
    /// </summary>
    internal sealed class ParameterReplaceVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression source;
        private readonly ParameterExpression target;

        /// <summary>
        /// 初始化参数替换访问器。
        /// </summary>
        /// <param name="source">源参数。</param>
        /// <param name="target">目标参数。</param>
        public ParameterReplaceVisitor(ParameterExpression source, ParameterExpression target)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.target = target ?? throw new ArgumentNullException(nameof(target));
        }

        /// <summary>
        /// 访问参数表达式，若匹配源参数则替换为目标参数。
        /// </summary>
        /// <param name="node">参数表达式。</param>
        /// <returns></returns>
        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == source)
                return target;

            return base.VisitParameter(node);
        }
    }
}
