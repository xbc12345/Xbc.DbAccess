using System.Data.Common;

#if NETFRAMEWORK
using System.Data.SqlClient;
#else
using Microsoft.Data.SqlClient;
#endif

namespace Xbc.DbAccess.SqlServer
{
    /// <summary>
    /// SQL Server 数据库访问提供程序工厂。
    /// </summary>
    public sealed class SqlAccessProviderFactory : DbAccessProviderFactory
    {
        /// <summary>
        /// SQL Server 数据库访问提供程序工厂实例。
        /// </summary>
        public static readonly SqlAccessProviderFactory Instance = new SqlAccessProviderFactory();

        /// <summary>
        /// 数据库提供程序工厂。
        /// </summary>
        public override DbProviderFactory DbProviderFactory => SqlClientFactory.Instance;

        /// <summary>
        /// 参数构建器。
        /// </summary>
        public override IDbParameterBuilder ParameterBuilder => SqlParameterBuilder.Instance;

        /// <summary>
        /// SQL 构建器。
        /// </summary>
        public override ISqlBuilder SqlBuilder => SqlServerSqlBuilder.Instance;

        /// <summary>
        /// SQL 方言。
        /// </summary>
        public override ISqlDialect SqlDialect => SqlServerDialect.Instance;

        /// <summary>
        /// 保存点 SQL 方言。
        /// </summary>
        public override ISavepointSqlDialect SavepointSqlDialect => SqlSavepointSqlDialect.Instance;

        /// <summary>
        /// 原生 bulk 插入器（基于 <see cref="SqlBulkCopy"/>）。
        /// </summary>
        public override IDbBulkInserter BulkInserter => SqlServerBulkInserter.Instance;

        /// <summary>
        /// 
        /// </summary>
        private SqlAccessProviderFactory()
        {
        }
    }
}
