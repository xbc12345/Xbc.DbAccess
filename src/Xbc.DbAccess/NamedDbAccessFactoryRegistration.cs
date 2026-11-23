namespace Xbc.DbAccess
{
    /// <summary>
    /// 命名数据库工厂注册项，用于 DI 容器收集所有命名工厂。
    /// <para>各提供者包的 <c>AddXxxDbAccess(name, configure)</c> 扩展方法将工厂封装为本类型注册到 DI，<see cref="DbAccessFactoryProvider"/> 构造时收集并构建索引。</para>
    /// </summary>
    public sealed class NamedDbAccessFactoryRegistration
    {
        /// <summary>
        /// 数据库工厂名称。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 数据库工厂实例。
        /// </summary>
        public IDbAccessFactory Factory { get; }

        /// <summary>
        /// 创建命名数据库工厂注册项。
        /// </summary>
        /// <param name="name">数据库工厂名称。</param>
        /// <param name="factory">数据库工厂实例。</param>
        public NamedDbAccessFactoryRegistration(string name, IDbAccessFactory factory)
        {
            Guard.ThrowIfNullOrWhiteSpace(name, nameof(name));

            Name = name;
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }
    }
}
