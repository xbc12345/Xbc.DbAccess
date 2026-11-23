using System.Linq.Expressions;
using Xbc.DbAccess.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Common
{
    /// <summary>
    /// ExpressionExtensions 的单元测试。
    /// </summary>
    public class ExpressionExtensionsTests
    {
        #region AndAlso 测试

        [Fact]
        public void AndAlso_TwoPredicates_ReturnsCombinedPredicate()
        {
            Expression<Func<TestClass, bool>> left = x => x.Value > 0;
            Expression<Func<TestClass, bool>> right = x => x.Value < 100;

            var combined = left.AndAlso(right);

            Assert.NotNull(combined);

            var func = combined.Compile();
            Assert.True(func(new TestClass { Value = 50 }));
            Assert.False(func(new TestClass { Value = -1 }));
            Assert.False(func(new TestClass { Value = 101 }));
        }

        [Fact]
        public void AndAlso_LeftNull_ThrowsArgumentNullException()
        {
            Expression<Func<TestClass, bool>> right = x => x.Value > 0;

            Assert.Throws<ArgumentNullException>(() =>
                ExpressionExtensions.AndAlso<TestClass>(null, right));
        }

        [Fact]
        public void AndAlso_RightNull_ThrowsArgumentNullException()
        {
            Expression<Func<TestClass, bool>> left = x => x.Value > 0;

            Assert.Throws<ArgumentNullException>(() =>
                left.AndAlso(null));
        }

        [Fact]
        public void AndAlso_MultipleConditions_CombinesCorrectly()
        {
            Expression<Func<TestClass, bool>> condition1 = x => x.Value > 0;
            Expression<Func<TestClass, bool>> condition2 = x => x.Value < 100;
            Expression<Func<TestClass, bool>> condition3 = x => x.Name != null;

            var combined = condition1.AndAlso(condition2).AndAlso(condition3);

            var func = combined.Compile();
            Assert.True(func(new TestClass { Value = 50, Name = "Test" }));
            Assert.False(func(new TestClass { Value = 50, Name = null }));
            Assert.False(func(new TestClass { Value = -1, Name = "Test" }));
        }

        #endregion

        #region OrElse 测试

        [Fact]
        public void OrElse_TwoPredicates_ReturnsCombinedPredicate()
        {
            Expression<Func<TestClass, bool>> left = x => x.Value < 0;
            Expression<Func<TestClass, bool>> right = x => x.Value > 100;

            var combined = left.OrElse(right);

            Assert.NotNull(combined);

            var func = combined.Compile();
            Assert.True(func(new TestClass { Value = -1 }));
            Assert.True(func(new TestClass { Value = 101 }));
            Assert.False(func(new TestClass { Value = 50 }));
        }

        [Fact]
        public void OrElse_LeftNull_ThrowsArgumentNullException()
        {
            Expression<Func<TestClass, bool>> right = x => x.Value > 0;

            Assert.Throws<ArgumentNullException>(() =>
                ExpressionExtensions.OrElse<TestClass>(null, right));
        }

        [Fact]
        public void OrElse_RightNull_ThrowsArgumentNullException()
        {
            Expression<Func<TestClass, bool>> left = x => x.Value > 0;

            Assert.Throws<ArgumentNullException>(() =>
                left.OrElse(null));
        }

        [Fact]
        public void OrElse_MultipleConditions_CombinesCorrectly()
        {
            Expression<Func<TestClass, bool>> condition1 = x => x.Value < 0;
            Expression<Func<TestClass, bool>> condition2 = x => x.Value > 100;
            Expression<Func<TestClass, bool>> condition3 = x => x.Name == null;

            var combined = condition1.OrElse(condition2).OrElse(condition3);

            var func = combined.Compile();
            Assert.True(func(new TestClass { Value = -1, Name = "Test" }));
            Assert.True(func(new TestClass { Value = 101, Name = "Test" }));
            Assert.True(func(new TestClass { Value = 50, Name = null }));
            Assert.False(func(new TestClass { Value = 50, Name = "Test" }));
        }

        #endregion

        #region Not 测试

        [Fact]
        public void Not_Predicate_ReturnsNegatedPredicate()
        {
            Expression<Func<TestClass, bool>> predicate = x => x.Value > 0;

            var negated = predicate.Not();

            Assert.NotNull(negated);

            var func = negated.Compile();
            Assert.False(func(new TestClass { Value = 1 }));
            Assert.True(func(new TestClass { Value = -1 }));
            Assert.True(func(new TestClass { Value = 0 }));
        }

        [Fact]
        public void Not_NullPredicate_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ExpressionExtensions.Not<TestClass>(null));
        }

        [Fact]
        public void Not_DoubleNegation_ReturnsOriginal()
        {
            Expression<Func<TestClass, bool>> predicate = x => x.Value > 0;

            var doubleNegated = predicate.Not().Not();

            var func = doubleNegated.Compile();
            Assert.True(func(new TestClass { Value = 1 }));
            Assert.False(func(new TestClass { Value = -1 }));
        }

        #endregion

        #region 复杂组合测试

        [Fact]
        public void Combined_AndAlsoAndOrElse_WorksCorrectly()
        {
            Expression<Func<TestClass, bool>> condition1 = x => x.Value > 0;
            Expression<Func<TestClass, bool>> condition2 = x => x.Value < 100;
            Expression<Func<TestClass, bool>> condition3 = x => x.Name != null;

            // (Value > 0 AND Value < 100) OR Name != null
            var combined = condition1.AndAlso(condition2).OrElse(condition3);

            var func = combined.Compile();
            Assert.True(func(new TestClass { Value = 50, Name = "Test" }));
            Assert.True(func(new TestClass { Value = 50, Name = null })); // First part true
            Assert.True(func(new TestClass { Value = -1, Name = "Test" })); // Second part true
            Assert.False(func(new TestClass { Value = -1, Name = null })); // Both false
        }

        [Fact]
        public void Combined_NotWithAndAlso_WorksCorrectly()
        {
            Expression<Func<TestClass, bool>> condition1 = x => x.Value > 0;
            Expression<Func<TestClass, bool>> condition2 = x => x.Value < 100;

            // NOT (Value > 0 AND Value < 100)
            var combined = condition1.AndAlso(condition2).Not();

            var func = combined.Compile();
            Assert.False(func(new TestClass { Value = 50 }));
            Assert.True(func(new TestClass { Value = -1 }));
            Assert.True(func(new TestClass { Value = 101 }));
        }

        #endregion

        #region 测试辅助类

        public class TestClass
        {
            public int Value { get; set; }
            public string Name { get; set; }
            public NestedClass Nested { get; set; }

            public int GetValue() => Value;
        }

        public class NestedClass
        {
            public string Name { get; set; }
        }

        #endregion
    }
}
