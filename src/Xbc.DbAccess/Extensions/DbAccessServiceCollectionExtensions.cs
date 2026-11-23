using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xbc.DbAccess.Diagnostics;

namespace Xbc.DbAccess
{
    /// <summary>
    /// DbAccess 通用 DI 注册扩展，封装默认注册与命名注册的标准结构，供各方言提供程序包的扩展方法转调。
    /// <para>委托重载负责参数校验并调用工厂创建委托，方言扩展方法只需一行转调。</para>
    /// </summary>
    public static class DbAccessServiceCollectionExtensions
    {
        /// <summary>
        /// 注册 DbAccess 服务作为默认数据库。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="factory">数据库访问工厂，由方言提供程序包创建。</param>
        /// <returns></returns>
        public static IServiceCollection AddDbAccess(
            this IServiceCollection services,
            IDbAccessFactory factory)
        {
            Guard.ThrowIfNull(services, nameof(services));
            Guard.ThrowIfNull(factory, nameof(factory));

            // 首次解析工厂时从容器自动应用诊断配置（日志接入与慢查询阈值，幂等，显式配置优先）
            services.TryAddSingleton(sp =>
            {
                DbAccessDiagnostics.ApplyFromContainer(sp);
                return factory;
            });

            services.TryAddScoped(sp => sp.GetRequiredService<IDbAccessFactory>().Create());
            return services;
        }

        /// <summary>
        /// 注册 DbAccess 服务作为默认数据库。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="configure">连接选项配置委托。</param>
        /// <param name="factoryCreator">工厂创建委托（如 MySqlAccessFactory.CreateMySqlFactory）。</param>
        /// <returns></returns>
        public static IServiceCollection AddDbAccess(
            this IServiceCollection services,
            Action<DbConnectionOptions> configure,
            Func<Action<DbConnectionOptions>, IDbAccessFactory> factoryCreator)
        {
            Guard.ThrowIfNull(services, nameof(services));
            Guard.ThrowIfNull(configure, nameof(configure));
            Guard.ThrowIfNull(factoryCreator, nameof(factoryCreator));

            return services.AddDbAccess(factoryCreator(configure));
        }

        /// <summary>
        /// 注册 DbAccess 服务作为默认数据库。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="connectionString">连接字符串。</param>
        /// <param name="factoryCreator">工厂创建委托（如 MySqlAccessFactory.CreateMySqlFactory）。</param>
        /// <returns></returns>
        public static IServiceCollection AddDbAccess(
            this IServiceCollection services,
            string connectionString,
            Func<Action<DbConnectionOptions>, IDbAccessFactory> factoryCreator)
        {
            Guard.ThrowIfNull(services, nameof(services));
            Guard.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));
            Guard.ThrowIfNull(factoryCreator, nameof(factoryCreator));

            return services.AddDbAccess(options => options.ConnectionString = connectionString, factoryCreator);
        }

        /// <summary>
        /// 注册命名 DbAccess 服务，用于多数据库场景：将工厂注册为命名项，
        /// 由 <see cref="IDbAccessFactoryProvider"/> 按名称解析。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="name">数据库工厂名称（唯一标识）。</param>
        /// <param name="factory">数据库访问工厂，由方言提供程序包创建。</param>
        /// <returns></returns>
        public static IServiceCollection AddDbAccess(
            this IServiceCollection services,
            string name,
            IDbAccessFactory factory)
        {
            Guard.ThrowIfNull(services, nameof(services));
            Guard.ThrowIfNullOrWhiteSpace(name, nameof(name));
            Guard.ThrowIfNull(factory, nameof(factory));

            services.TryAddSingleton<IDbAccessFactoryProvider>(sp =>
            {
                // 首次解析工厂提供器时从容器自动应用诊断配置（日志接入与慢查询阈值，幂等，显式配置优先）
                DbAccessDiagnostics.ApplyFromContainer(sp);
                return new DbAccessFactoryProvider(sp.GetServices<NamedDbAccessFactoryRegistration>());
            });

            services.AddSingleton(new NamedDbAccessFactoryRegistration(name, factory));
            return services;
        }

        /// <summary>
        /// 注册命名 DbAccess 服务，用于多数据库场景：将工厂注册为命名项，
        /// 由 <see cref="IDbAccessFactoryProvider"/> 按名称解析。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="name">数据库工厂名称（唯一标识）。</param>
        /// <param name="configure">连接选项配置委托。</param>
        /// <param name="factoryCreator">工厂创建委托（如 MySqlAccessFactory.CreateMySqlFactory）。</param>
        /// <returns></returns>
        public static IServiceCollection AddDbAccess(
            this IServiceCollection services,
            string name,
            Action<DbConnectionOptions> configure,
            Func<Action<DbConnectionOptions>, IDbAccessFactory> factoryCreator)
        {
            Guard.ThrowIfNull(services, nameof(services));
            Guard.ThrowIfNullOrWhiteSpace(name, nameof(name));
            Guard.ThrowIfNull(configure, nameof(configure));
            Guard.ThrowIfNull(factoryCreator, nameof(factoryCreator));

            return services.AddDbAccess(name, factoryCreator(configure));
        }

        /// <summary>
        /// 注册命名 DbAccess 服务，用于多数据库场景：将工厂注册为命名项，
        /// 由 <see cref="IDbAccessFactoryProvider"/> 按名称解析。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="name">数据库工厂名称（唯一标识）。</param>
        /// <param name="connectionString">连接字符串。</param>
        /// <param name="factoryCreator">工厂创建委托（如 MySqlAccessFactory.CreateMySqlFactory）。</param>
        /// <returns></returns>
        public static IServiceCollection AddDbAccess(
            this IServiceCollection services,
            string name,
            string connectionString,
            Func<Action<DbConnectionOptions>, IDbAccessFactory> factoryCreator)
        {
            Guard.ThrowIfNull(services, nameof(services));
            Guard.ThrowIfNullOrWhiteSpace(name, nameof(name));
            Guard.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));
            Guard.ThrowIfNull(factoryCreator, nameof(factoryCreator));

            return services.AddDbAccess(name, options => options.ConnectionString = connectionString, factoryCreator);
        }

        /// <summary>
        /// 配置 DbAccess 诊断选项（如慢查询告警阈值），与任一 <c>AddDbAccess</c> 重载搭配使用：
        /// 选项在首次解析数据库工厂（或命名工厂提供器）时自动应用；
        /// 容器中的 <see cref="Microsoft.Extensions.Logging.ILoggerFactory"/> 亦在此时自动接入日志路由（若从未显式配置过）。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="configure">诊断选项配置委托。</param>
        /// <returns></returns>
        public static IServiceCollection AddDbAccessDiagnostics(
            this IServiceCollection services,
            Action<DbAccessDiagnosticsOptions> configure)
        {
            Guard.ThrowIfNull(services, nameof(services));
            Guard.ThrowIfNull(configure, nameof(configure));

            services.Configure(configure);
            return services;
        }
    }
}