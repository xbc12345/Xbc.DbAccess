using System.Linq.Expressions;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 检测表达式子树是否引用了目标参数表达式，用于区分可求值的常量/闭包子树与引用实体参数的子树。
    /// </summary>
    internal sealed class ParameterReferenceDetector : ExpressionVisitor
    {
        private readonly ParameterExpression target;

        /// <summary>
        /// 是否检测到目标参数引用。
        /// </summary>
        public bool Found { get; private set; }

        /// <summary>
        /// 初始化参数引用检测器。
        /// </summary>
        /// <param name="target">目标参数表达式（实体 lambda 参数）。</param>
        public ParameterReferenceDetector(ParameterExpression target)
        {
            // null 通配（匹配任意参数）会误伤值侧表达式中合法的嵌套 lambda 参数，显式拒绝
            this.target = target ?? throw new ArgumentNullException(nameof(target));
        }

        /// <inheritdoc/>
        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == target)
                Found = true;

            return node;
        }

        /// <summary>
        /// 检测表达式子树是否引用了目标参数。
        /// </summary>
        /// <param name="expression">待检测表达式。</param>
        /// <returns>引用了目标参数时返回 true。</returns>
        public bool Detect(Expression expression)
        {
            // 复位上一次结果，同一实例可跨多次检测复用
            Found = false;
            Visit(expression);
            return Found;
        }
    }
}
