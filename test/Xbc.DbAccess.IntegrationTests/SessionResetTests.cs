using Xbc.DbAccess;
using Xbc.DbAccess.Common;
using Xbc.DbAccess.IntegrationTests.Infrastructure;
using Xbc.DbAccess.MySql;
using Xbc.DbAccess.Npgsql;
using Xbc.DbAccess.SqlServer;
using Xunit;
using static Xbc.DbAccess.IntegrationTests.Infrastructure.RealDbDialect;

namespace Xbc.DbAccess.IntegrationTests
{
    /// <summary>
    /// 会话状态重置（backlog §10）的真库回归：连接回池时按方言执行复位命令
    /// （SQL Server sp_reset_connection / PostgreSQL DISCARD ALL / MySQL ROLLBACK + 会话变量复位），
    /// 上一借用者留下的会话残留不透传给下一个借用者。
    /// <para>池容量固定为 1（借-还-借必命中同一条连接），保证残留与复位发生在同一连接上可观测；
    /// Oracle 与 SQLite 无对应重置机制（基类默认空序列），不设用例。</para>
    /// </summary>
    public abstract class SessionResetTestsBase : IDisposable
    {
        private readonly IDbAccessFactory factory;
        private readonly IDbAccess dbAccess;

        /// <summary>当前用例的目标方言。</summary>
        protected abstract RealDbDialect Dialect { get; }

        protected SessionResetTestsBase()
        {
            factory = Dialect switch
            {
                RealDbDialect.PgSql => new NpgsqlAccessFactory(CreateOptions(RealDbProbe.PgConnectionString)),
                RealDbDialect.SqlServer => new SqlAccessFactory(CreateOptions(RealDbProbe.MsSqlConnectionString)),
                RealDbDialect.MySql => new MySqlAccessFactory(CreateOptions(RealDbProbe.MySqlConnectionString)),
                _ => throw new ArgumentOutOfRangeException(nameof(Dialect), $"未知的真库方言 '{Dialect}'。")
            };
            dbAccess = factory.Create();
        }

        private static DbConnectionOptions CreateOptions(string connectionString)
        {
            return new DbConnectionOptions
            {
                ConnectionString = connectionString,
                PoolOptions =
                {
                    Enabled = true,
                    MinPoolSize = 0,
                    MaxPoolSize = 1,
                    ConnectionWaitTimeout = TimeSpan.FromSeconds(5)
                }
            };
        }

        protected void SkipIfDialectUnavailable()
        {
            var available = Dialect switch
            {
                RealDbDialect.PgSql => RealDbProbe.PgSqlAvailable,
                RealDbDialect.SqlServer => RealDbProbe.SqlServerAvailable,
                RealDbDialect.MySql => RealDbProbe.MySqlAvailable,
                _ => false
            };
            Skip.If(!available, $"本地无可连接的 {Dialect}，跳过该方言用例。");
        }

        [SkippableFact]
        public async Task SessionState_IsResetBetweenBorrowCycles()
        {
            SkipIfDialectUnavailable();

            switch (Dialect)
            {
                case RealDbDialect.PgSql:
                {
                    // 上一借用者修改会话变量后归还（回池执行 DISCARD ALL），下一借用者看到的是默认值
                    await dbAccess.ExecuteNonQueryAsync("SET datestyle = 'ISO, DMY'");
                    var dateStyle = await dbAccess.ExecuteScalarAsync<string>("SHOW DateStyle");

                    Assert.Equal("ISO, MDY", dateStyle);
                    break;
                }
                case RealDbDialect.MySql:
                {
                    // 上一借用者修改 sql_mode 后归还（回池执行会话变量复位），下一借用者看到的是服务器默认值
                    await dbAccess.ExecuteNonQueryAsync("SET SESSION sql_mode = 'ANSI'");
                    var isDefault = await dbAccess.ExecuteScalarAsync<long>("SELECT @@SESSION.sql_mode = @@GLOBAL.sql_mode");

                    Assert.Equal(1, isDefault);
                    break;
                }
                case RealDbDialect.SqlServer:
                {
                    // 上一借用者创建临时表后归还（回池执行 sp_reset_connection），下一借用者看到的临时表已不存在
                    await dbAccess.ExecuteNonQueryAsync("CREATE TABLE #it_session_reset (Id INT)");
                    var objectId = await dbAccess.ExecuteScalarAsync<int>("SELECT ISNULL(OBJECT_ID('tempdb..#it_session_reset'), -1)");

                    Assert.Equal(-1, objectId);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(Dialect), $"未知的真库方言 '{Dialect}'。");
            }
        }

        public void Dispose()
        {
            dbAccess?.Dispose();
            (factory as IDisposable)?.Dispose();
        }
    }

    /// <summary>会话状态重置的 PostgreSQL 真库用例（DISCARD ALL）。</summary>
    public class PgSqlSessionResetTests : SessionResetTestsBase
    {
        protected override RealDbDialect Dialect => PgSql;
    }

    /// <summary>会话状态重置的 SQL Server 真库用例（sp_reset_connection）。</summary>
    public class SqlServerSessionResetTests : SessionResetTestsBase
    {
        protected override RealDbDialect Dialect => RealDbDialect.SqlServer;
    }

    /// <summary>会话状态重置的 MySQL 真库用例（会话变量复位）。</summary>
    public class MySqlSessionResetTests : SessionResetTestsBase
    {
        protected override RealDbDialect Dialect => RealDbDialect.MySql;
    }
}
