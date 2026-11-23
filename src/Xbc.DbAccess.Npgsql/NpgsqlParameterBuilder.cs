using Npgsql;
using System.Data.Common;

namespace Xbc.DbAccess.Npgsql
{
    /// <summary>
    /// PostgreSQL 数据库参数构建器。
    /// </summary>
    internal sealed class NpgsqlParameterBuilder : DbParameterBuilderBase
    {
        /// <summary>
        /// PostgreSQL 数据库参数构建器实例。
        /// </summary>
        public static readonly NpgsqlParameterBuilder Instance = new NpgsqlParameterBuilder();

        /// <summary>
        /// PostgreSQL 数据库提供程序工厂。
        /// </summary>
        public override DbProviderFactory ProviderFactory => NpgsqlFactory.Instance;

        /// <summary>
        /// 初始化 PostgreSQL 数据库参数构建器。PostgreSQL 参数前缀为 "@"。
        /// </summary>
        private NpgsqlParameterBuilder() : base("@")
        {
        }
    }
}
