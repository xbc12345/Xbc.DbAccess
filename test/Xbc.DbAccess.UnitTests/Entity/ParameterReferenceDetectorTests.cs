using System;
using System.Linq.Expressions;
using Xbc.DbAccess.Entity;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// ParameterReferenceDetector 的直接单元测试：Detect 的复位语义（同实例跨多次检测复用）与构造守卫。
    /// </summary>
    public class ParameterReferenceDetectorTests
    {
        [Fact]
        public void Detect_ResetsFoundBetweenCalls()
        {
            var parameter = Expression.Parameter(typeof(string));
            var detector = new ParameterReferenceDetector(parameter);

            // 第一次检测命中目标参数，第二次检测不含目标参数：Detect 复位 Found，结果互不干扰
            Assert.True(detector.Detect(parameter));
            Assert.False(detector.Detect(Expression.Constant("a")));
        }

        [Fact]
        public void Detect_NestedLambdaParameter_DoesNotMatchTarget()
        {
            // 嵌套 lambda 的参数（y）不是目标参数：值侧含嵌套 lambda 的表达式不判为引用实体参数
            var target = Expression.Parameter(typeof(List<string>), "x");
            var nested = Expression.Parameter(typeof(string), "y");
            var detector = new ParameterReferenceDetector(target);

            Assert.False(detector.Detect(nested));
        }

        [Fact]
        public void Constructor_NullTarget_Throws()
        {
            // null 通配（匹配任意参数）会误伤嵌套 lambda 参数，构造期显式拒绝
            Assert.Throws<ArgumentNullException>(() => new ParameterReferenceDetector(null));
        }
    }
}
