#if NETFRAMEWORK
using System.Data.SqlClient;
#else
using Microsoft.Data.SqlClient;
#endif

namespace Xbc.DbAccess.SqlServer
{
    /// <summary>
    /// SQL Server 数据库访问工厂类。
    /// </summary>
    public sealed class SqlAccessFactory : DbAccessFactoryBase
    {
        /// <summary>
        /// SQL Server 数据库访问提供程序工厂。
        /// </summary>
        public override DbAccessProviderFactory ProviderFactory => SqlAccessProviderFactory.Instance;

        /// <summary>
        /// 初始化 SQL Server 数据库访问工厂。
        /// </summary>
        /// <param name="connectionOptions">连接选项。</param>
        public SqlAccessFactory(DbConnectionOptions connectionOptions) : base(connectionOptions)
        {
        }

        /// <summary>
        /// 创建 SQL Server 数据库访问工厂。
        /// </summary>
        /// <param name="connectionString"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static IDbAccessFactory CreateSqlServerFactory(string connectionString)
        {
            Guard.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));

            var connectionOptions = new DbConnectionOptions() { ConnectionString = connectionString };
            return new SqlAccessFactory(connectionOptions);
        }

        /// <summary>
        /// 创建 SQL Server 数据库访问工厂。
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IDbAccessFactory CreateSqlServerFactory(Action<DbConnectionOptions> configure)
        {
            Guard.ThrowIfNull(configure, nameof(configure));

            var connectionOptions = new DbConnectionOptions();
            configure(connectionOptions);

            return new SqlAccessFactory(connectionOptions);
        }
    }
}
