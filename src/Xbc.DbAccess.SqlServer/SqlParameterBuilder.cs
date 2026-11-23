using System.Data.Common;

#if NETFRAMEWORK
using System.Data.SqlClient;
#else
using Microsoft.Data.SqlClient;
#endif

namespace Xbc.DbAccess.SqlServer
{
    /// <summary>
    /// SQL Server 数据库参数构建器。
    /// </summary>
    internal sealed class SqlParameterBuilder : DbParameterBuilderBase
    {
        /// <summary>
        /// SQL Server 数据库参数构建器实例。
        /// </summary>
        public static readonly SqlParameterBuilder Instance = new SqlParameterBuilder();

        /// <summary>
        /// SQL Server 数据库提供工厂。
        /// </summary>
        public override DbProviderFactory ProviderFactory => SqlClientFactory.Instance;

        /// <summary>
        /// 初始化 SQL Server 数据库参数构建器。SQL Server 参数前缀为 "@"。
        /// </summary>
        private SqlParameterBuilder() : base("@")
        {
        }
    }
}
