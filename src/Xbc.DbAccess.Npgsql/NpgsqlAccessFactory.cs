namespace Xbc.DbAccess.Npgsql
{
    /// <summary>
    /// PostgreSQL 数据库访问工厂类。
    /// </summary>
    public sealed class NpgsqlAccessFactory : DbAccessFactoryBase
    {
        /// <summary>
        /// PostgreSQL 数据库访问提供程序工厂。
        /// </summary>
        public override DbAccessProviderFactory ProviderFactory => NpgsqlAccessProviderFactory.Instance;

        /// <summary>
        /// 初始化 PostgreSQL 数据库访问工厂。
        /// </summary>
        /// <param name="connectionOptions">连接选项。</param>
        public NpgsqlAccessFactory(DbConnectionOptions connectionOptions) : base(connectionOptions)
        {
        }

        /// <summary>
        /// 创建 PostgreSQL 数据库访问工厂。
        /// </summary>
        /// <param name="connectionString"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static IDbAccessFactory CreateNpgsqlFactory(string connectionString)
        {
            Guard.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));

            var connectionOptions = new DbConnectionOptions() { ConnectionString = connectionString, };
            return new NpgsqlAccessFactory(connectionOptions);
        }

        /// <summary>
        /// 创建 PostgreSQL 数据库访问工厂。
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IDbAccessFactory CreateNpgsqlFactory(Action<DbConnectionOptions> configure)
        {
            Guard.ThrowIfNull(configure, nameof(configure));

            var connectionOptions = new DbConnectionOptions();
            configure(connectionOptions);

            return new NpgsqlAccessFactory(connectionOptions);
        }
    }
}
