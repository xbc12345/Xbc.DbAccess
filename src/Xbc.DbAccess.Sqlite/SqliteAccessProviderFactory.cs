using System.Data.Common;
using System.Data.SQLite;

namespace Xbc.DbAccess.Sqlite
{
    /// <summary>
    /// SQLite 数据库访问提供程序工厂。
    /// </summary>
    public sealed class SqliteAccessProviderFactory : DbAccessProviderFactory
    {
        /// <summary>
        /// SQLite 数据库访问提供程序工厂实例。
        /// </summary>
        public static readonly SqliteAccessProviderFactory Instance = new SqliteAccessProviderFactory();

        /// <summary>
        /// 数据库提供程序工厂。
        /// </summary>
        public override DbProviderFactory DbProviderFactory => SQLiteFactory.Instance;

        /// <summary>
        /// 参数构建器。
        /// </summary>
        public override IDbParameterBuilder ParameterBuilder => SqliteParameterBuilder.Instance;

        /// <summary>
        /// SQL 构建器。
        /// </summary>
        public override ISqlBuilder SqlBuilder => SqliteSqlBuilder.Instance;

        /// <summary>
        /// SQL 方言。
        /// </summary>
        public override ISqlDialect SqlDialect => SqliteDialect.Instance;

        /// <summary>
        /// 保存点 SQL 方言。
        /// </summary>
        public override ISavepointSqlDialect SavepointSqlDialect => SqliteSavepointSqlDialect.Instance;

        /// <summary>
        /// 
        /// </summary>
        private SqliteAccessProviderFactory()
        {
        }
    }
}
