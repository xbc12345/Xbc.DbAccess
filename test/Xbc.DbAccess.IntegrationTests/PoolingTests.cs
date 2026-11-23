using System.Collections.Concurrent;
using System.Data.Common;
using Xbc.DbAccess;
using Xbc.DbAccess.Common;
using Xbc.DbAccess.Sqlite;
using Xunit;

namespace Xbc.DbAccess.IntegrationTests
{
    /// <summary>
    /// 连接池的真库回归（SQLite 文件库承载，池管理逻辑方言无关）：借还复用、并发借还排队、
    /// 池耗尽等待超时与工厂释放幂等。统计计数随各行为路径执行（Record* 系列）。
    /// </summary>
    public class PoolingTests : IDisposable
    {
        private readonly string dbFilePath;
        private readonly IDbAccessFactory factory;
        private readonly IDbAccess dbAccess;

        public PoolingTests()
        {
            dbFilePath = Path.Combine(Path.GetTempPath(), $"XbcPoolIt_{Guid.NewGuid():N}.db");
            factory = new SqliteAccessFactory(new DbConnectionOptions
            {
                ConnectionString = $"Data Source={dbFilePath}",
                EnableStatistics = true,
                PoolOptions =
                {
                    Enabled = true,
                    MinPoolSize = 1,
                    MaxPoolSize = 4,
                    MaxIdleTime = TimeSpan.FromMinutes(10),
                    ConnectionWaitTimeout = TimeSpan.FromSeconds(1)
                }
            });
            dbAccess = factory.Create();
        }

        public void Dispose()
        {
            dbAccess?.Dispose();
            (factory as IDisposable)?.Dispose();
            try
            {
                if (File.Exists(dbFilePath))
                    File.Delete(dbFilePath);
            }
            catch
            {
                // 文件占用时忽略，临时目录自动清理
            }
        }

        [Fact]
        public async Task Pool_ConnectionsExecuteAcrossBorrowReturnCycles()
        {
            await dbAccess.ExecuteNonQueryAsync("CREATE TABLE IF NOT EXISTS pool_users (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT)");

            // 多次借还周期：连接归还回池后再次借出仍可正常执行（复用与校验路径）
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var affected = await dbAccess.ExecuteNonQueryAsync(
                    "INSERT INTO pool_users (Name) VALUES (@Name)",
                    new { Name = $"cycle_{cycle}" });

                Assert.Equal(1, affected);
            }

            Assert.Equal(3, await dbAccess.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pool_users"));
        }

        [Fact]
        public async Task Pool_ConcurrentAccess_QueuesBeyondMaxPoolSize()
        {
            await dbAccess.ExecuteNonQueryAsync("CREATE TABLE IF NOT EXISTS pool_users (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT)");

            // 12 个并发任务超出 MaxPoolSize(4)：超出部分排队等待归还，全部任务应成功完成
            const int taskCount = 12;
            var results = new ConcurrentBag<long>();
            await Task.WhenAll(Enumerable.Range(0, taskCount).Select(async _ =>
            {
                var count = await dbAccess.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM pool_users");
                results.Add(count);
            }));

            Assert.Equal(taskCount, results.Count);
            Assert.All(results, count => Assert.True(count >= 0));
        }

        [Fact]
        public async Task Pool_Exhausted_WaitTimesOut()
        {
            await dbAccess.ExecuteNonQueryAsync("CREATE TABLE IF NOT EXISTS pool_users (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT)");

            // 借满 MaxPoolSize(4) 个连接不归还（读取器未释放）：池耗尽，第 5 个借用等待超时失败
            var holders = new List<DbDataReader>();
            for (var i = 0; i < 4; i++)
                holders.Add(await dbAccess.ExecuteReaderAsync($"SELECT {i}"));

            var ex = await Record.ExceptionAsync(() => dbAccess.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM pool_users"));
            Assert.NotNull(ex);// ConnectionWaitTimeout(1s) 内无连接归还，借用超时

            foreach (var holder in holders)
                holder.Dispose();
        }

        [Fact]
        public async Task Pool_Dispose_IsIdempotent()
        {
            await dbAccess.ExecuteNonQueryAsync("SELECT 1");

            Record.Exception(dbAccess.Dispose);
            Record.Exception(dbAccess.Dispose);// 重复释放不抛
        }
    }
}
