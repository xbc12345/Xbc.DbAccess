using System.Data.Common;

namespace Xbc.DbAccess.Benchmarks.Fakes
{
    /// <summary>
    /// 模拟 Xbc.DbAccess 提供程序工厂，组合全部模拟组件，构成无数据库的完整执行管道。
    /// </summary>
    public sealed class FakeProviderFactory : DbAccessProviderFactory
    {
        /// <summary>
        /// 单例实例。
        /// </summary>
        public static readonly FakeProviderFactory Instance = new FakeProviderFactory();

        private FakeProviderFactory()
        {
        }

        /// <inheritdoc />
        public override DbProviderFactory DbProviderFactory => FakeDbProviderFactory.Instance;

        /// <inheritdoc />
        public override IDbParameterBuilder ParameterBuilder => FakeParameterBuilder.Instance;

        /// <inheritdoc />
        public override ISqlBuilder SqlBuilder => FakeSqlBuilder.Instance;

        /// <inheritdoc />
        public override ISqlDialect SqlDialect => FakeSqlDialect.Instance;

        /// <inheritdoc />
        public override ISavepointSqlDialect SavepointSqlDialect => FakeSqlDialect.Instance;
    }
}
