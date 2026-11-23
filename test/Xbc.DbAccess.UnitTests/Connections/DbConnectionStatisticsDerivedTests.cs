using Xunit;

namespace Xbc.DbAccess.UnitTests.Connections
{
    /// <summary>
    /// 连接统计信息派生指标测试：覆盖率计算公式及其零值除法防护。
    /// </summary>
    public class DbConnectionStatisticsDerivedTests
    {
        #region PoolingDbConnectionStatistics

        [Fact]
        public void PoolingStatistics_WhenZeroRequests_DerivedRatesAreZero()
        {
            var statistics = new PoolingDbConnectionStatistics();

            Assert.Equal(0, statistics.CreateFailureRate, 6);
            Assert.Equal(0, statistics.HitRate, 6);
            Assert.Equal(0, statistics.QueueRate, 6);
            Assert.Equal(0, statistics.PoolUsageRate, 6);
            Assert.Equal(0, statistics.PoolCapacityRate, 6);
            Assert.Equal(0, statistics.AliveConnections);
        }

        [Fact]
        public void PoolingStatistics_ComputesAccumulatorRates()
        {
            var statistics = new PoolingDbConnectionStatistics
            {
                TotalRequests = 10,
                TotalCreateFailed = 2,
                TotalHits = 4,
                TotalQueued = 5,
            };

            Assert.Equal(20, statistics.CreateFailureRate, 6);
            Assert.Equal(40, statistics.HitRate, 6);
            Assert.Equal(50, statistics.QueueRate, 6);
        }

        [Fact]
        public void PoolingStatistics_AliveConnections_SumsActiveAndIdle()
        {
            var statistics = new PoolingDbConnectionStatistics
            {
                ActiveConnections = 3,
                IdleConnections = 2,
            };

            Assert.Equal(5, statistics.AliveConnections);
        }

        [Fact]
        public void PoolingStatistics_ComputesPoolRates()
        {
            var statistics = new PoolingDbConnectionStatistics
            {
                ActiveConnections = 3,
                IdleConnections = 2,
                MaxPoolSize = 10,
            };

            // 使用率仅含活动连接，容量利用率含空闲连接
            Assert.Equal(30, statistics.PoolUsageRate, 6);
            Assert.Equal(50, statistics.PoolCapacityRate, 6);
        }

        [Fact]
        public void PoolingStatistics_WhenMaxPoolSizeZero_PoolRatesAreZero()
        {
            var statistics = new PoolingDbConnectionStatistics
            {
                ActiveConnections = 3,
                IdleConnections = 2,
                MaxPoolSize = 0,
            };

            Assert.Equal(0, statistics.PoolUsageRate, 6);
            Assert.Equal(0, statistics.PoolCapacityRate, 6);
        }

        #endregion

        #region DefaultDbConnectionStatistics

        [Fact]
        public void DefaultStatistics_WhenZeroRequests_CreateFailureRateIsZero()
        {
            var statistics = new DefaultDbConnectionStatistics();

            Assert.Equal(0, statistics.CreateFailureRate, 6);
        }

        [Fact]
        public void DefaultStatistics_ComputesCreateFailureRate()
        {
            var statistics = new DefaultDbConnectionStatistics
            {
                TotalRequests = 10,
                TotalCreateFailed = 2,
            };

            Assert.Equal(20, statistics.CreateFailureRate, 6);
        }

        [Fact]
        public void DefaultStatistics_AliveConnections_MatchesActiveConnections()
        {
            var statistics = new DefaultDbConnectionStatistics { ActiveConnections = 4 };

            Assert.Equal(4, statistics.AliveConnections);
            Assert.Equal(0, statistics.TotalCancelled);
        }

        #endregion
    }
}
