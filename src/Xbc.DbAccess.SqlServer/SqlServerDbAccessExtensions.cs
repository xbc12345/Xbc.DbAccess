using Microsoft.Extensions.DependencyInjection;

namespace Xbc.DbAccess.SqlServer
{
    /// <summary>
    /// SQL Server DbAccess DI 扩展。
    /// </summary>
    public static class SqlServerDbAccessExtensions
    {
        /// <summary>
        /// 注册 SQL Server DbAccess 服务作为默认数据库。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="configure">连接选项配置委托。</param>
        /// <returns></returns>
        public static IServiceCollection AddSqlServerDbAccess(
            this IServiceCollection services,
            Action<DbConnectionOptions> configure)
        {
            return services.AddDbAccess(configure, SqlAccessFactory.CreateSqlServerFactory);
        }

        /// <summary>
        /// 注册 SQL Server DbAccess 服务作为默认数据库。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="connectionString">连接字符串。</param>
        /// <returns></returns>
        public static IServiceCollection AddSqlServerDbAccess(
            this IServiceCollection services,
            string connectionString)
        {
            return services.AddDbAccess(connectionString, SqlAccessFactory.CreateSqlServerFactory);
        }

        /// <summary>
        /// 注册命名 SQL Server DbAccess 服务，用于多数据库场景。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="name">数据库工厂名称（唯一标识）。</param>
        /// <param name="configure">连接选项配置委托。</param>
        /// <returns></returns>
        public static IServiceCollection AddSqlServerDbAccess(
            this IServiceCollection services,
            string name,
            Action<DbConnectionOptions> configure)
        {
            return services.AddDbAccess(name, configure, SqlAccessFactory.CreateSqlServerFactory);
        }

        /// <summary>
        /// 注册命名 SQL Server DbAccess 服务，用于多数据库场景。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="name">数据库工厂名称（唯一标识）。</param>
        /// <param name="connectionString">连接字符串。</param>
        /// <returns></returns>
        public static IServiceCollection AddSqlServerDbAccess(
            this IServiceCollection services,
            string name,
            string connectionString)
        {
            return services.AddDbAccess(name, connectionString, SqlAccessFactory.CreateSqlServerFactory);
        }
    }
}