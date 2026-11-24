using Xbc.DbAccess.Common;
using Xbc.DbAccess.Oracle;
using Xbc.DbAccess.SqlServer;

namespace Xbc.DbAccess.IntegrationTests.Infrastructure
{
    /// <summary>
    /// "服务器版本未知"测试工厂：重写服务器信息探测，固定返回 MajorVersion == 0 的空 <see cref="DbServerInfo"/>，
    /// 强制方言构建器走低版本兼容 SQL 路径（SQL Server → ROW_NUMBER 包裹、Oracle → ROWNUM 三层嵌套）。
    /// <para>用于在新版本真库（默认走 OFFSET/FETCH 主路径）上回归验证兼容 SQL 的可执行性与结果正确性；
    /// 各方言真实工厂为 sealed，此处直接派生自 <see cref="DbAccessFactoryBase"/> 并复用静态提供程序工厂实例，
    /// 连接管理行为与真实工厂一致。</para>
    /// </summary>
    internal sealed class LegacyVersionSqlAccessFactory : DbAccessFactoryBase
    {
        /// <summary>初始化"版本未知"的 SQL Server 数据库访问工厂。</summary>
        public LegacyVersionSqlAccessFactory(DbConnectionOptions connectionOptions) : base(connectionOptions)
        {
        }

        /// <summary>SQL Server 提供程序工厂（与真实工厂一致）。</summary>
        public override DbAccessProviderFactory ProviderFactory => SqlAccessProviderFactory.Instance;

        /// <summary>固定返回 MajorVersion == 0（版本未知 → ROW_NUMBER 兼容路径）。</summary>
        protected override Task<DbServerInfo> GetDbServerInfoAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new DbServerInfo());
    }

    /// <summary>
    /// "服务器版本未知"的 Oracle 工厂（版本未知 → ROWNUM 三层嵌套兼容路径），结构同 SQL Server 版。
    /// </summary>
    internal sealed class LegacyVersionOracleAccessFactory : DbAccessFactoryBase
    {
        /// <summary>初始化"版本未知"的 Oracle 数据库访问工厂。</summary>
        public LegacyVersionOracleAccessFactory(DbConnectionOptions connectionOptions) : base(connectionOptions)
        {
        }

        /// <summary>Oracle 提供程序工厂（与真实工厂一致）。</summary>
        public override DbAccessProviderFactory ProviderFactory => OracleAccessProviderFactory.Instance;

        /// <summary>固定返回 MajorVersion == 0（版本未知 → ROWNUM 兼容路径）。</summary>
        protected override Task<DbServerInfo> GetDbServerInfoAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new DbServerInfo());
    }
}
