namespace Xbc.DbAccess.IntegrationTests.Infrastructure
{
    /// <summary>
    /// 真库集成测试的方言选择（本地已部署 PostgreSQL、SQL Server、MySQL 与 Oracle 环境）。
    /// </summary>
    public enum RealDbDialect
    {
        /// <summary>PostgreSQL（Npgsql 方言）。</summary>
        PgSql,

        /// <summary>SQL Server（SqlServer 方言）。</summary>
        SqlServer,

        /// <summary>MySQL（MySql 方言）。</summary>
        MySql,

        /// <summary>Oracle（Oracle 方言）。</summary>
        Oracle,

        /// <summary>SQLite（Sqlite 方言，本地文件数据库，恒可用）。</summary>
        Sqlite
    }
}
