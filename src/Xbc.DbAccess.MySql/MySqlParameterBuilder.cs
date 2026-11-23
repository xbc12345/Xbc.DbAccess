using MySqlConnector;
using System.Data.Common;

namespace Xbc.DbAccess.MySql
{
    /// <summary>
    /// MySQL 数据库参数构建器。
    /// </summary>
    internal sealed class MySqlParameterBuilder : DbParameterBuilderBase
    {
        /// <summary>
        /// MySQL 数据库参数构建器实例。
        /// </summary>
        public static readonly MySqlParameterBuilder Instance = new MySqlParameterBuilder();

        /// <summary>
        /// MySQL 数据库提供程序工厂。
        /// </summary>
        public override DbProviderFactory ProviderFactory => MySqlConnectorFactory.Instance;

        /// <summary>
        /// 初始化 MySQL 数据库参数构建器。MySQL 参数前缀为 "@"。
        /// </summary>
        private MySqlParameterBuilder() : base("@")
        {
        }
    }
}
