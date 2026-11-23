using Microsoft.Extensions.DependencyInjection;
using Xbc.DbAccess.Entity;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 值转换器 DI 注册扩展，将自定义实体属性与数据库值之间的转换器注册到全局转换器注册表，
    /// 配置期立即生效（与无 DI 场景直接调用 <see cref="DbValueConverterRegistry.Register{T}(IDbValueConverter{T})"/> 等价）。
    /// </summary>
    public static class DbAccessValueConverterServiceCollectionExtensions
    {
        /// <summary>
        /// 注册数据库值转换器到全局转换器注册表（配置期立即生效）。
        /// </summary>
        /// <typeparam name="T">目标属性类型。</typeparam>
        /// <param name="services">服务集合。</param>
        /// <param name="converter">转换器实例。</param>
        /// <returns>服务集合（支持链式配置）。</returns>
        public static IServiceCollection AddDbAccessValueConverter<T>(
            this IServiceCollection services,
            IDbValueConverter<T> converter)
        {
            Guard.ThrowIfNull(services, nameof(services));
            Guard.ThrowIfNull(converter, nameof(converter));

            DbValueConverterRegistry.Register<T>(converter);
            return services;
        }

        /// <summary>
        /// 以转换委托注册数据库值转换器到全局转换器注册表（配置期立即生效）。
        /// </summary>
        /// <typeparam name="T">目标属性类型。</typeparam>
        /// <param name="services">服务集合。</param>
        /// <param name="convertFrom">数据库值转换为实体类型的函数。</param>
        /// <param name="convertTo">实体类型转换为数据库值的函数。</param>
        /// <returns>服务集合（支持链式配置）。</returns>
        public static IServiceCollection AddDbAccessValueConverter<T>(
            this IServiceCollection services,
            Func<object, T> convertFrom,
            Func<T, object> convertTo)
        {
            Guard.ThrowIfNull(services, nameof(services));
            Guard.ThrowIfNull(convertFrom, nameof(convertFrom));
            Guard.ThrowIfNull(convertTo, nameof(convertTo));

            DbValueConverterRegistry.Register<T>(convertFrom, convertTo);
            return services;
        }
    }
}
