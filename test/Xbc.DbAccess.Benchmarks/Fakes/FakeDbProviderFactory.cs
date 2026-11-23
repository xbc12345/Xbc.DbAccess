using System.Data.Common;

namespace Xbc.DbAccess.Benchmarks.Fakes
{
    /// <summary>
    /// 模拟 ADO.NET 提供程序工厂，产出 Fake 连接/命令/参数。
    /// </summary>
    public sealed class FakeDbProviderFactory : DbProviderFactory
    {
        /// <summary>
        /// 单例实例。
        /// </summary>
        public static readonly FakeDbProviderFactory Instance = new FakeDbProviderFactory();

        private FakeDbProviderFactory()
        {
        }

        /// <inheritdoc />
        public override DbConnection CreateConnection() => new FakeDbConnection();

        /// <inheritdoc />
        public override DbCommand CreateCommand() => new FakeDbCommand();

        /// <inheritdoc />
        public override DbParameter CreateParameter() => new FakeDbParameter();
    }
}
