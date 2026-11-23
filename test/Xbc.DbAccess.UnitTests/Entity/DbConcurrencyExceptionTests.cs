using Xbc.DbAccess.Entity;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// DbConcurrencyException 构造重载的单元测试。
    /// </summary>
    public class DbConcurrencyExceptionTests
    {
        [Fact]
        public void Constructors_CarryMessageAndInnerException()
        {
            Assert.NotNull(new DbConcurrencyException());

            var inner = new InvalidOperationException("inner");
            var ex = new DbConcurrencyException("乐观并发冲突", inner);

            Assert.Equal("乐观并发冲突", ex.Message);
            Assert.Same(inner, ex.InnerException);
        }
    }
}
