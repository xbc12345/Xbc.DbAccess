using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace Xbc.DbAccess.IntegrationTests.Infrastructure
{
    /// <summary>
    /// 真库连接探测与连接串管理：连接串支持环境变量覆盖（XBC_IT_PGSQL / XBC_IT_MSSQL / XBC_IT_MYSQL / XBC_IT_ORACLE），
    /// 默认值指向本机默认端口的常用配置；可用性探测进程内只做一次（静态缓存）。
    /// </summary>
    public static class RealDbProbe
    {
        /// <summary>PostgreSQL 连接串（环境变量 XBC_IT_PGSQL 可覆盖）。</summary>
        public static readonly string PgConnectionString =
            Environment.GetEnvironmentVariable("XBC_IT_PGSQL")
            ?? "Host=127.0.0.1;Port=5432;Username=postgres;Password=123456;Database=postgres;Pooling=false;Timeout=5";

        /// <summary>SQL Server 连接串（环境变量 XBC_IT_MSSQL 可覆盖，默认 Windows 集成认证）。</summary>
        public static readonly string MsSqlConnectionString =
            Environment.GetEnvironmentVariable("XBC_IT_MSSQL")
            ?? "Server=127.0.0.1,1433;Database=master;Integrated Security=True;TrustServerCertificate=True;Pooling=False;Connect Timeout=5";

        /// <summary>MySQL 连接串（环境变量 XBC_IT_MYSQL 可覆盖）。</summary>
        public static readonly string MySqlConnectionString =
            Environment.GetEnvironmentVariable("XBC_IT_MYSQL")
            ?? "Server=127.0.0.1;Port=3306;User ID=root;Password=123456;Database=mysql;Pooling=False;Connection Timeout=5";

        /// <summary>Oracle 连接串（环境变量 XBC_IT_ORACLE 可覆盖，EZCONNECT 格式）。</summary>
        public static readonly string OracleConnectionString =
            Environment.GetEnvironmentVariable("XBC_IT_ORACLE")
            ?? "User Id=system;Password=123456;Data Source=127.0.0.1:1521/XE;Pooling=False;Connection Timeout=5";

        private static readonly Lazy<bool> pgSqlAvailableLazy = new(TryConnectPgSql);
        private static readonly Lazy<bool> sqlServerAvailableLazy = new(TryConnectSqlServer);
        private static readonly Lazy<bool> mySqlAvailableLazy = new(TryConnectMySql);
        private static readonly Lazy<bool> oracleAvailableLazy = new(TryConnectOracle);

        /// <summary>PostgreSQL 是否可连接。</summary>
        public static bool PgSqlAvailable => pgSqlAvailableLazy.Value;

        /// <summary>SQL Server 是否可连接。</summary>
        public static bool SqlServerAvailable => sqlServerAvailableLazy.Value;

        /// <summary>MySQL 是否可连接。</summary>
        public static bool MySqlAvailable => mySqlAvailableLazy.Value;

        /// <summary>Oracle 是否可连接。</summary>
        public static bool OracleAvailable => oracleAvailableLazy.Value;

        /// <summary>SQLite 是否可用（本地文件数据库，创建文件即用，恒为 true）。</summary>
        public static bool SqliteAvailable => true;

        private static bool TryConnectPgSql()
        {
            try
            {
                using var connection = new NpgsqlConnection(PgConnectionString);
                connection.Open();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryConnectSqlServer()
        {
            try
            {
                using var connection = new Microsoft.Data.SqlClient.SqlConnection(MsSqlConnectionString);
                connection.Open();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryConnectMySql()
        {
            try
            {
                using var connection = new MySqlConnection(MySqlConnectionString);
                connection.Open();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryConnectOracle()
        {
            try
            {
                using var connection = new OracleConnection(OracleConnectionString);
                connection.Open();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
