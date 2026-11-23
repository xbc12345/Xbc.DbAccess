using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess.Benchmarks.Fakes
{
    /// <summary>
    /// 模拟参数构建器，使用 @ 前缀，产出 FakeDbParameter。
    /// </summary>
    public sealed class FakeParameterBuilder : DbParameterBuilderBase
    {
        /// <summary>
        /// 单例实例。
        /// </summary>
        public static readonly FakeParameterBuilder Instance = new FakeParameterBuilder();

        private FakeParameterBuilder() : base("@")
        {
        }

        /// <inheritdoc />
        public override DbProviderFactory ProviderFactory => FakeDbProviderFactory.Instance;
    }
}
