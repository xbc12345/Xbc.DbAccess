namespace Xbc.DbAccess.Sqlite
{
    /// <summary>
    /// SQLite 数据库访问工厂类。
    /// </summary>
    public sealed class SqliteAccessFactory : DbAccessFactoryBase
    {
        /// <summary>
        /// SQLite 数据库访问提供程序工厂。
        /// </summary>
        public override DbAccessProviderFactory ProviderFactory => SqliteAccessProviderFactory.Instance;

        /// <summary>
        /// 初始化 SQLite 数据库访问工厂。
        /// </summary>
        /// <param name="connectionOptions">连接选项。</param>
        public SqliteAccessFactory(DbConnectionOptions connectionOptions) : base(connectionOptions)
        {
        }

        /// <summary>
        /// 创建 SQLite 数据库访问工厂。
        /// </summary>
        /// <param name="connectionString"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static IDbAccessFactory CreateSqliteFactory(string connectionString)
        {
            Guard.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));

            var connectionOptions = new DbConnectionOptions { ConnectionString = connectionString };
            return new SqliteAccessFactory(connectionOptions);
        }

        /// <summary>
        /// 创建 SQLite 数据库访问工厂。
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IDbAccessFactory CreateSqliteFactory(Action<DbConnectionOptions> configure)
        {
            Guard.ThrowIfNull(configure, nameof(configure));

            var connectionOptions = new DbConnectionOptions();
            configure(connectionOptions);

            return new SqliteAccessFactory(connectionOptions);
        }
    }
}
