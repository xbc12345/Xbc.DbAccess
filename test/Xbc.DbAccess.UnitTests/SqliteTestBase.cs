using System.Data;
using System.Data.Common;
using Xbc.DbAccess.Sqlite;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Infrastructure
{
    /// <summary>
    /// SQLite 单元测试基类，提供真实的 SQLite 内存数据库测试环境。
    /// </summary>
    public abstract class SqliteTestBase : IAsyncLifetime, IDisposable
    {
        private readonly DbConnectionOptions _connectionOptions;
        private readonly IDbAccessFactory _factory;
        private readonly IDbAccess _dbAccess;
        private readonly string _dbFilePath;
        private bool _disposed;

        /// <summary>
        /// 连接选项。
        /// </summary>
        protected DbConnectionOptions ConnectionOptions => _connectionOptions;

        /// <summary>
        /// 数据库访问工厂。
        /// </summary>
        protected IDbAccessFactory Factory => _factory;

        /// <summary>
        /// 测试用的 DbAccess 实例。
        /// </summary>
        protected IDbAccess DbAccess => _dbAccess;

        /// <summary>
        /// 提供程序工厂。
        /// </summary>
        protected DbAccessProviderFactory ProviderFactory => _factory.ProviderFactory;

        protected SqliteTestBase()
        {
            // 使用临时文件数据库，确保多个连接可以访问同一个数据库
            _dbFilePath = Path.Combine(Path.GetTempPath(), $"XbcTest_{Guid.NewGuid()}.db");

            _connectionOptions = new DbConnectionOptions
            {
                ConnectionString = $"Data Source={_dbFilePath}",
                PoolOptions = { Enabled = false }
            };

            _factory = new SqliteAccessFactory(_connectionOptions);
            _dbAccess = _factory.Create();
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
        /// 初始化数据库架构，子类可以重写此方法。
        /// </summary>
        protected virtual Task InitializeDatabaseAsync()
        {
            // 默认创建测试表
            const string createTableSql = @"
                CREATE TABLE IF NOT EXISTS TestUsers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Age INTEGER,
                    Email TEXT,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS TestOrders (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    ProductName TEXT NOT NULL,
                    Amount REAL,
                    OrderDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (UserId) REFERENCES TestUsers(Id)
                );

                -- 插入测试数据
                INSERT INTO TestUsers (Name, Age, Email) VALUES 
                    ('Alice', 25, 'alice@example.com'),
                    ('Bob', 30, 'bob@example.com'),
                    ('Charlie', 35, 'charlie@example.com');

                INSERT INTO TestOrders (UserId, ProductName, Amount) VALUES 
                    (1, 'Product A', 100.50),
                    (1, 'Product B', 200.00),
                    (2, 'Product C', 150.75);
            ";

            return _dbAccess.ExecuteNonQueryAsync(createTableSql);
        }

        /// <summary>
        /// 创建参数。自动处理参数前缀，避免重复添加。
        /// </summary>
        protected DbParameter CreateParameter(string name, object value, DbType? dbType = null)
        {
            // 移除已有的前缀，避免重复添加
            var prefix = ProviderFactory.ParameterBuilder.ParameterPrefix;
            if (name.StartsWith(prefix))
            {
                name = name.Substring(prefix.Length);
            }
            return ProviderFactory.ParameterBuilder.CreateParameter(name, value, dbType);
        }

        /// <summary>
        /// 清理测试数据。
        /// </summary>
        protected Task ClearTestDataAsync()
        {
            return _dbAccess.ExecuteNonQueryAsync("DELETE FROM TestOrders; DELETE FROM TestUsers;");
        }

        public virtual void Dispose()
        {
            if (!_disposed)
            {
                _dbAccess?.Dispose();
                if (_factory is IDisposable disposableFactory)
                {
                    disposableFactory.Dispose();
                }
                _disposed = true;

                // 删除临时数据库文件
                try
                {
                    if (File.Exists(_dbFilePath))
                    {
                        File.Delete(_dbFilePath);
                    }
                }
                catch
                {
                    // 忽略删除失败
                }
            }
        }
    }
}
