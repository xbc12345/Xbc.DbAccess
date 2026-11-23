namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库访问工厂提供器，用于多数据库场景下按名称解析 <see cref="IDbAccessFactory"/>。
    /// </summary>
    public interface IDbAccessFactoryProvider
    {
        /// <summary>
        /// 获取所有已注册的数据库工厂名称。
        /// </summary>
        IEnumerable<string> Names { get; }

        /// <summary>
        /// 注册命名数据库工厂。
        /// </summary>
        /// <param name="name">数据库工厂名称。</param>
        /// <param name="factory">数据库工厂实例。</param>
        void Register(string name, IDbAccessFactory factory);

        /// <summary>
        /// 按名称获取数据库工厂。
        /// </summary>
        /// <param name="name">数据库工厂名称。</param>
        /// <returns>数据库工厂实例。</returns>
        IDbAccessFactory GetFactory(string name);

        /// <summary>
        /// 按名称创建数据库访问实例。
        /// </summary>
        /// <param name="name">数据库工厂名称。</param>
        /// <returns>数据库访问实例。</returns>
        IDbAccess Create(string name);
    }
}
