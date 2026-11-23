namespace Xbc.DbAccess.MySql
{
    /// <summary>
    /// MySQL 数据库访问工厂类。
    /// </summary>
    public sealed class MySqlAccessFactory : DbAccessFactoryBase
    {
        /// <summary>
        /// MySQL 数据库访问提供程序工厂。
        /// </summary>
        public override DbAccessProviderFactory ProviderFactory => MySqlAccessProviderFactory.Instance;

        /// <summary>
        /// 初始化 MySQL 数据库访问工厂。
        /// </summary>
        /// <param name="connectionOptions">连接选项。</param>
        public MySqlAccessFactory(DbConnectionOptions connectionOptions) : base(connectionOptions)
        {
        }

        /// <summary>
        /// 创建 MySQL 数据库访问工厂。
        /// </summary>
        /// <param name="connectionString"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static IDbAccessFactory CreateMySqlFactory(string connectionString)
        {
            Guard.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));

            var connectionOptions = new DbConnectionOptions() { ConnectionString = connectionString, };
            return new MySqlAccessFactory(connectionOptions);
        }

        /// <summary>
        /// 创建 MySQL 数据库访问工厂。
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IDbAccessFactory CreateMySqlFactory(Action<DbConnectionOptions> configure)
        {
            Guard.ThrowIfNull(configure, nameof(configure));

            var connectionOptions = new DbConnectionOptions();
            configure(connectionOptions);

            return new MySqlAccessFactory(connectionOptions);
        }
    }
}
