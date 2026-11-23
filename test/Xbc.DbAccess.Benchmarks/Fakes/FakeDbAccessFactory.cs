using Xbc.DbAccess.Diagnostics;

namespace Xbc.DbAccess.Benchmarks.Fakes
{
    /// <summary>
    /// 模拟数据库访问工厂，创建挂在 Fake 管道上的 IDbAccess 实例。
    /// </summary>
    public sealed class FakeDbAccessFactory : DbAccessFactoryBase
    {
        /// <summary>
        /// 初始化模拟工厂（连接串仅为占位，Fake 管道不建立任何连接）。
        /// </summary>
        public FakeDbAccessFactory()
            : base(new DbConnectionOptions { ConnectionString = "Data Source=fake" })
        {
        }

        /// <inheritdoc />
        public override DbAccessProviderFactory ProviderFactory => FakeProviderFactory.Instance;
    }
}
