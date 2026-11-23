namespace Xbc.DbAccess.Oracle
{
    /// <summary>
    /// Oracle 数据库访问工厂类。
    /// </summary>
    public sealed class OracleAccessFactory : DbAccessFactoryBase
    {
        /// <summary>
        /// Oracle 数据库提供程序工厂。
        /// </summary>
        public override DbAccessProviderFactory ProviderFactory => OracleAccessProviderFactory.Instance;

        /// <summary>
        /// 初始化 Oracle 数据库访问工厂。
        /// </summary>
        /// <param name="connectionOptions">连接选项。</param>
        public OracleAccessFactory(DbConnectionOptions connectionOptions) : base(connectionOptions)
        {
        }

        /// <summary>
        /// 创建 Oracle 数据库访问工厂。
        /// </summary>
        /// <param name="connectionString"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static IDbAccessFactory CreateOracleFactory(string connectionString)
        {
            Guard.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));

            var connectionOptions = new DbConnectionOptions() { ConnectionString = connectionString };
            return new OracleAccessFactory(connectionOptions);
        }

        /// <summary>
        /// 创建 Oracle 数据库访问工厂。
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IDbAccessFactory CreateOracleFactory(Action<DbConnectionOptions> configure)
        {
            Guard.ThrowIfNull(configure, nameof(configure));

            var connectionOptions = new DbConnectionOptions();
            configure(connectionOptions);

            return new OracleAccessFactory(connectionOptions);
        }
    }
}
