using System.Collections.Concurrent;

namespace Xbc.DbAccess
{
    /// <summary>
    /// <see cref="IDbAccessFactoryProvider"/> 的默认实现。
    /// </summary>
    public sealed class DbAccessFactoryProvider : IDbAccessFactoryProvider
    {
        private readonly ConcurrentDictionary<string, IDbAccessFactory> factories
            = new ConcurrentDictionary<string, IDbAccessFactory>(StringComparer.Ordinal);

        /// <summary>
        /// 创建数据库工厂提供器并收集所有命名注册项。
        /// </summary>
        /// <param name="registrations">命名数据库工厂注册项集合（通常由 DI 容器注入），不可为 null。</param>
        public DbAccessFactoryProvider(IEnumerable<NamedDbAccessFactoryRegistration> registrations)
        {
            Guard.ThrowIfNull(registrations, nameof(registrations));

            foreach (var registration in registrations)
            {
                if (registration == null)
                    continue;

                if (!factories.TryAdd(registration.Name, registration.Factory))
                    throw new InvalidOperationException($"已存在同名的数据库工厂: {registration.Name}。");
            }
        }

        /// <inheritdoc />
        public IEnumerable<string> Names => factories.Keys;

        /// <inheritdoc />
        public void Register(string name, IDbAccessFactory factory)
        {
            Guard.ThrowIfNullOrWhiteSpace(name, nameof(name));
            Guard.ThrowIfNull(factory, nameof(factory));

            if (!factories.TryAdd(name, factory))
                throw new InvalidOperationException($"已存在同名的数据库工厂: {name}。");
        }

        /// <inheritdoc />
        public IDbAccessFactory GetFactory(string name)
        {
            Guard.ThrowIfNullOrWhiteSpace(name, nameof(name));

            if (factories.TryGetValue(name, out var factory))
            {
                // 已释放的工厂提前抛出明确异常，避免实例延迟到首次执行才抛 ObjectDisposedException
                if (factory is DbAccessFactoryBase factoryBase && (factoryBase.IsDisposing || factoryBase.IsDisposed))
                    throw new InvalidOperationException($"名为 '{name}' 的数据库工厂已释放，无法创建数据库访问实例。");

                return factory;
            }

            throw new InvalidOperationException($"未找到名为 '{name}' 的数据库工厂。");
        }

        /// <inheritdoc />
        public IDbAccess Create(string name) => GetFactory(name).Create();
    }
}
