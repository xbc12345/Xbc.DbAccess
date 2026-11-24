using Xunit;

namespace Xbc.DbAccess.UnitTests.Connections
{
    /// <summary>
    /// DbConnectionPoolOptions 配置校验测试。
    /// </summary>
    public class DbConnectionPoolOptionsValidationTests
    {
        [Fact]
        public void Validate_WhenMinPoolSizeNegative_ThrowsArgumentOutOfRangeException()
        {
            var options = new DbConnectionPoolOptions { MinPoolSize = -1 };

            var exception = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);

            Assert.Equal("MinPoolSize", exception.ParamName);
        }

        [Fact]
        public void Validate_WhenMaxPoolSizeLessThanOne_ThrowsArgumentOutOfRangeException()
        {
            var options = new DbConnectionPoolOptions { MaxPoolSize = 0 };

            var exception = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);

            Assert.Equal("MaxPoolSize", exception.ParamName);
        }

        [Fact]
        public void Validate_WhenMinPoolSizeGreaterThanMaxPoolSize_ThrowsArgumentOutOfRangeException()
        {
            var options = new DbConnectionPoolOptions { MinPoolSize = 5, MaxPoolSize = 3 };

            var exception = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);

            Assert.Equal("MinPoolSize", exception.ParamName);
        }

        [Fact]
        public void Validate_WhenMaxIdleTimeNotPositive_ThrowsArgumentOutOfRangeException()
        {
            var options = new DbConnectionPoolOptions { MaxIdleTime = TimeSpan.Zero };

            var exception = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);

            Assert.Equal("MaxIdleTime", exception.ParamName);
        }

        [Fact]
        public void Validate_WhenMaxLifetimeNegative_ThrowsArgumentOutOfRangeException()
        {
            var options = new DbConnectionPoolOptions { MaxLifetime = TimeSpan.FromSeconds(-1) };

            var exception = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);

            Assert.Equal("MaxLifetime", exception.ParamName);
        }

        [Fact]
        public void Validate_WhenMaxLifetimeZero_AllowsUnlimitedLifetime()
        {
            var options = new DbConnectionPoolOptions { MaxLifetime = TimeSpan.Zero };

            Exception exception = Record.Exception(options.Validate);

            Assert.Null(exception);
        }

        [Fact]
        public void Validate_WhenCleanupIntervalNotPositive_ThrowsArgumentOutOfRangeException()
        {
            var options = new DbConnectionPoolOptions { CleanupInterval = TimeSpan.Zero };

            var exception = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);

            Assert.Equal("CleanupInterval", exception.ParamName);
        }

        [Fact]
        public void Validate_WhenConnectionWaitTimeoutNotPositive_ThrowsArgumentOutOfRangeException()
        {
            var options = new DbConnectionPoolOptions { ConnectionWaitTimeout = TimeSpan.Zero };

            var exception = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);

            Assert.Equal("ConnectionWaitTimeout", exception.ParamName);
        }

        [Fact]
        public void Validate_WhenValidationSkipIntervalNegative_ThrowsArgumentOutOfRangeException()
        {
            var options = new DbConnectionPoolOptions { ValidationSkipInterval = TimeSpan.FromSeconds(-1) };

            var exception = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);

            Assert.Equal("ValidationSkipInterval", exception.ParamName);
        }

        [Fact]
        public void Validate_WhenValidationCommandTimeoutNegative_ThrowsArgumentOutOfRangeException()
        {
            var options = new DbConnectionPoolOptions { ValidationCommandTimeout = -1 };

            var exception = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);

            Assert.Equal("ValidationCommandTimeout", exception.ParamName);
        }

        [Fact]
        public void Validate_WithDefaultOptions_DoesNotThrow()
        {
            Exception exception = Record.Exception(() => new DbConnectionPoolOptions().Validate());

            Assert.Null(exception);
        }
    }
}
