using Xbc.DbAccess.Common;
using Xbc.DbAccess.MySql;
using Xbc.DbAccess.Npgsql;
using Xbc.DbAccess.Oracle;
using Xbc.DbAccess.SqlServer;
using Xbc.DbAccess.Sqlite;
using Xunit;

namespace Xbc.DbAccess.IntegrationTests.Infrastructure
{
    /// <summary>
    /// 真库集成测试基类：按方言参数创建数据库访问实例，每个测试方法前后对子类声明的实体表执行
    /// DROP IF EXISTS + CREATE（同一测试类内串行执行，方法间互不残留数据；不同测试类并行，各自使用专属实体类型与表名）。
    /// <para>连接串由 <see cref="RealDbProbe"/> 提供（环境变量可覆盖）。xunit 2.x 不支持 Theory 数据注入构造函数，
    /// 故按"抽象用例基类 + 每方言一个具体子类"组织：用例写在抽象基类（[Fact] 无参数），子类构造函数传入方言。</para>
    /// </summary>
    public abstract class RealDbTestBase : IAsyncLifetime, IDisposable
    {
        private bool _disposed;
        private readonly string? _sqliteDbFilePath;

        /// <summary>当前用例的目标方言。</summary>
        protected RealDbDialect Dialect { get; }

        /// <summary>数据库访问实例（每个测试方法一个，禁用连接池避免残留连接锁表）。</summary>
        protected IDbAccess DbAccess { get; }

        /// <summary>数据库访问工厂。</summary>
        protected IDbAccessFactory Factory { get; }

        protected RealDbTestBase(RealDbDialect dialect)
        {
            Dialect = dialect;

            // SQLite 为本地文件数据库：每个测试实例使用独立的临时文件（测试类并行执行，文件互不冲突）
            string connectionString;
            if (dialect == RealDbDialect.Sqlite)
            {
                _sqliteDbFilePath = Path.Combine(Path.GetTempPath(), $"XbcIt_{Guid.NewGuid():N}.db");
                connectionString = $"Data Source={_sqliteDbFilePath}";
            }
            else
            {
                connectionString = dialect switch
                {
                    RealDbDialect.PgSql => RealDbProbe.PgConnectionString,
                    RealDbDialect.SqlServer => RealDbProbe.MsSqlConnectionString,
                    RealDbDialect.MySql => RealDbProbe.MySqlConnectionString,
                    RealDbDialect.Oracle => RealDbProbe.OracleConnectionString,
                    _ => throw new ArgumentOutOfRangeException(nameof(dialect), $"未知的真库方言 '{dialect}'。")
                };
            }
            var connectionOptions = new DbConnectionOptions
            {
                ConnectionString = connectionString,
                PoolOptions = { Enabled = false }
            };

            Factory = dialect switch
            {
                RealDbDialect.PgSql => new NpgsqlAccessFactory(connectionOptions),
                RealDbDialect.SqlServer => new SqlAccessFactory(connectionOptions),
                RealDbDialect.MySql => new MySqlAccessFactory(connectionOptions),
                RealDbDialect.Oracle => new OracleAccessFactory(connectionOptions),
                RealDbDialect.Sqlite => new SqliteAccessFactory(connectionOptions),
                _ => throw new ArgumentOutOfRangeException(nameof(dialect), $"未知的真库方言 '{dialect}'。")
            };
            DbAccess = Factory.Create();
        }

        /// <summary>
        /// 本测试类使用的实体类型集合（每测试类使用专属实体类型，表名互不冲突）。
        /// </summary>
        protected abstract Type[] EntityTypes { get; }

        /// <summary>
        /// 引用裸 SQL 中的标识符：PostgreSQL/Oracle/SQLite 双引号（列名区分大小写）、SQL Server 方括号、MySQL 反引号。
        /// </summary>
        /// <param name="identifier">表名或列名。</param>
        /// <returns>带方言引用符的标识符。</returns>
        protected string Qi(string identifier)
        {
            return Dialect switch
            {
                RealDbDialect.MySql => "`" + identifier + "`",
                RealDbDialect.SqlServer => "[" + identifier + "]",
                _ => "\"" + identifier + "\""
            };
        }

        /// <summary>
        /// 当前方言不可连接时跳过用例（每个测试方法体首行调用，配合 [SkippableFact]）：
        /// 本地缺失某个数据库环境时该方言用例自动跳过而非失败，提供环境（或环境变量指定连接串）后即自动生效。
        /// </summary>
        protected void SkipIfDialectUnavailable()
        {
            var available = Dialect switch
            {
                RealDbDialect.PgSql => RealDbProbe.PgSqlAvailable,
                RealDbDialect.SqlServer => RealDbProbe.SqlServerAvailable,
                RealDbDialect.MySql => RealDbProbe.MySqlAvailable,
                RealDbDialect.Oracle => RealDbProbe.OracleAvailable,
                RealDbDialect.Sqlite => RealDbProbe.SqliteAvailable,
                _ => false
            };
            Skip.If(!available, $"本地无可连接的 {Dialect}，跳过该方言用例；可用环境变量 XBC_IT_PGSQL / XBC_IT_MSSQL / XBC_IT_MYSQL / XBC_IT_ORACLE 指定连接串。");
        }

        public async Task InitializeAsync()
        {
            // 守卫必须先于建表 DDL：方言不可用时跳过用例（xunit 生命周期在测试方法体之前执行，此处未跳过会在建表时因连接失败而报失败）
            SkipIfDialectUnavailable();

            foreach (var entityType in EntityTypes)
            {
                var (dropSql, createSql) = DdlGenerator.Generate(Dialect, entityType);
                await DbAccess.ExecuteNonQueryAsync(dropSql);
                await DbAccess.ExecuteNonQueryAsync(createSql);
            }
        }

        public async Task DisposeAsync()
        {
            foreach (var entityType in EntityTypes)
            {
                var (dropSql, _) = DdlGenerator.Generate(Dialect, entityType);
                try
                {
                    await DbAccess.ExecuteNonQueryAsync(dropSql);
                }
                catch
                {
                    // 清理失败不影响测试结果（残留表会在下次运行时被 DROP IF EXISTS 兜底）
                }
            }
        }

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

                    // SQLite 临时文件随实例清理（删除失败不影响测试结果，残留文件位于系统临时目录）
                    if (_sqliteDbFilePath != null)
                    {
                        try
                        {
                            if (File.Exists(_sqliteDbFilePath))
                                File.Delete(_sqliteDbFilePath);
                        }
                        catch
                        {
                        }
                    }
                }
                _disposed = true;
            }
        }
    }
}
