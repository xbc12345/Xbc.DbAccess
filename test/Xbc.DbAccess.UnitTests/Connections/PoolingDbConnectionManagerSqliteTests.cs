using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using Moq;
using Xbc.DbAccess.Diagnostics;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Connections
{
    /// <summary>
    /// PoolingDbConnectionManager 的 SQLite 真实连接测试：验证挂起事务的连接归还时销毁而非回收复用。
    /// </summary>
    public class PoolingDbConnectionManagerSqliteTests : IDisposable
    {
        private readonly string _dbFilePath;
        private readonly Mock<IDbConnectionTracker> _tracker;
        private readonly PoolingDbConnectionManager _manager;

        public PoolingDbConnectionManagerSqliteTests()
        {
            _dbFilePath = Path.Combine(Path.GetTempPath(), $"XbcPoolTest_{Guid.NewGuid():N}.db");
            _tracker = new Mock<IDbConnectionTracker>();
            _manager = CreateManager();
        }

        public void Dispose()
        {
            _manager?.Dispose();

            try
            {
                if (File.Exists(_dbFilePath))
                    File.Delete(_dbFilePath);
            }
            catch
            {
                // SQLite 连接释放存在延迟时忽略删除失败
            }

            GC.SuppressFinalize(this);
        }

        private PoolingDbConnectionManager CreateManager()
        {
            var options = new DbConnectionOptions
            {
                ConnectionString = $"Data Source={_dbFilePath}",
                EnableStatistics = true,
                PoolOptions = new DbConnectionPoolOptions
                {
                    Enabled = true,
                    MinPoolSize = 0,
                    MaxPoolSize = 1,
                    ConnectionWaitTimeout = TimeSpan.FromSeconds(5),
                    CleanupInterval = TimeSpan.FromMinutes(10)
                }
            };

            return new PoolingDbConnectionManager(options, SQLiteFactory.Instance, _tracker.Object);
        }

        [Fact]
        public async Task GetOrCreateAsync_WithPendingTransaction_DestroysInsteadOfRecycling()
        {
            var connection = (PoolingDbConnection)await _manager.GetOrCreateAsync();
            Assert.Equal(ConnectionState.Open, connection.State);

            // 挂起未完成事务的连接回池会让新借用者的命令跑进旧事务，归还时应直接销毁
            _ = connection.BeginTransaction();

            _manager.Release(connection);

            Assert.True(connection.IsDisposed);
            var statistics = (PoolingDbConnectionStatistics)_manager.GetStatistics();
            Assert.Equal(1, statistics.TotalRequests);
            Assert.Equal(1, statistics.TotalCreated);
            Assert.Equal(0, statistics.TotalRecycled);// 未回收入池
            Assert.Equal(0, statistics.TotalReleased);

            // 销毁时已归还许可证（MaxPoolSize=1）：后续请求仍可正常获取连接
            var second = (PoolingDbConnection)await _manager.GetOrCreateAsync();

            Assert.NotSame(connection, second);
            Assert.False(second.IsDisposed);
            Assert.Equal(ConnectionState.Open, second.State);
        }
    }
}
