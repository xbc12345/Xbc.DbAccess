using MySqlConnector;
using System.Data.Common;

namespace Xbc.DbAccess.MySql
{
    /// <summary>
    /// MySQL 数据库访问提供程序工厂。
    /// </summary>
    public sealed class MySqlAccessProviderFactory : DbAccessProviderFactory
    {
        /// <summary>
        /// MySQL 数据库访问提供程序工厂实例。
        /// </summary>
        public static readonly MySqlAccessProviderFactory Instance = new MySqlAccessProviderFactory();

        /// <summary>
        /// 数据库提供程序工厂。
        /// </summary>
        public override DbProviderFactory DbProviderFactory => MySqlConnectorFactory.Instance;

        /// <summary>
        /// 参数构建器。
        /// </summary>
        public override IDbParameterBuilder ParameterBuilder => MySqlParameterBuilder.Instance;

        /// <summary>
        /// SQL 构建器。
        /// </summary>
        public override ISqlBuilder SqlBuilder => MySqlSqlBuilder.Instance;

        /// <summary>
        /// SQL 方言。
        /// </summary>
        public override ISqlDialect SqlDialect => MySqlDialect.Instance;

        /// <summary>
        /// 保存点 SQL 方言。
        /// </summary>
        public override ISavepointSqlDialect SavepointSqlDialect => MySqlSavepointSqlDialect.Instance;

        /// <summary>
        /// 原生 bulk 插入器（基于 MySqlConnector 的 <see cref="MySqlBulkCopy"/>）。
        /// </summary>
        public override IDbBulkInserter BulkInserter => MySqlBulkInserter.Instance;

        /// <summary>
        /// 
        /// </summary>
        private MySqlAccessProviderFactory()
        {
        }
    }
}
