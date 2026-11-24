using Xbc.DbAccess.Sqlite;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 实体测试基类，提供统一的数据库测试环境。
    /// </summary>
    public abstract class EntityTestBase : IAsyncLifetime, IDisposable
    {
        private readonly string _dbFilePath;
        private bool _disposed;

        /// <summary>
        /// 数据库访问实例。
        /// </summary>
        protected IDbAccess DbAccess { get; }

        /// <summary>
        /// 数据库访问工厂。
        /// </summary>
        protected IDbAccessFactory Factory { get; }

        protected EntityTestBase()
        {
            _dbFilePath = Path.Combine(Path.GetTempPath(), $"XbcTest_{GetType().Name}_{Guid.NewGuid()}.db");

            var connectionOptions = new DbConnectionOptions
            {
                ConnectionString = $"Data Source={_dbFilePath}",
                PoolOptions = { Enabled = false }
            };

            Factory = new SqliteAccessFactory(connectionOptions);
            DbAccess = Factory.Create();
        }

        /// <summary>
        /// xunit 异步初始化，在构造函数之后执行数据库架构初始化。
        /// </summary>
        public Task InitializeAsync()
        {
            return InitializeDatabaseAsync();
        }

        /// <summary>
        /// xunit 异步清理（资源释放由 <see cref="Dispose"/> 同步完成）。
        /// </summary>
        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// 初始化数据库架构，子类应重写此方法。
        /// </summary>
        protected abstract Task InitializeDatabaseAsync();

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    DbAccess?.Dispose();
                    TryDeleteDbFile();
                }
                _disposed = true;
            }
        }

        private void TryDeleteDbFile()
        {
            try
            {
                if (File.Exists(_dbFilePath))
                    File.Delete(_dbFilePath);
            }
            catch { }
        }
    }
}
