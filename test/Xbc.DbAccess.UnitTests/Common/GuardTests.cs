using Xunit;

namespace Xbc.DbAccess.UnitTests.Common
{
    /// <summary>
    /// Guard 参数守卫的单元测试： ThrowIfNullOrEmpty 的 null / 空串 / 合法值分支。
    /// </summary>
    public class GuardTests
    {
        [Fact]
        public void ThrowIfNullOrEmpty_Null_ThrowsArgumentNullException()
        {
            var exception = Assert.Throws<ArgumentNullException>(() =>
                Guard.ThrowIfNullOrEmpty(null, "value"));

            Assert.Equal("value", exception.ParamName);
        }

        [Fact]
        public void ThrowIfNullOrEmpty_EmptyString_ThrowsArgumentException()
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                Guard.ThrowIfNullOrEmpty(string.Empty, "value"));

            Assert.Equal("value", exception.ParamName);
        }

        [Fact]
        public void ThrowIfNullOrEmpty_ValidValue_DoesNotThrow()
        {
            Guard.ThrowIfNullOrEmpty("sql", "value");
        }
    }
}
