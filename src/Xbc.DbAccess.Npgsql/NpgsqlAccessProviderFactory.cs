using Npgsql;
using System.Data.Common;

namespace Xbc.DbAccess.Npgsql
{
    /// <summary>
    /// PostgreSQL 数据库访问提供程序工厂。
    /// </summary>
    public sealed class NpgsqlAccessProviderFactory : DbAccessProviderFactory
    {
        /// <summary>
        /// PostgreSQL 数据库访问提供程序工厂实例。
        /// </summary>
        public static readonly NpgsqlAccessProviderFactory Instance = new NpgsqlAccessProviderFactory();

        /// <summary>
        /// 数据库提供程序工厂。
        /// </summary>
        public override DbProviderFactory DbProviderFactory => NpgsqlFactory.Instance;

        /// <summary>
        /// 参数构建器。
        /// </summary>
        public override IDbParameterBuilder ParameterBuilder => NpgsqlParameterBuilder.Instance;

        /// <summary>
        /// SQL 构建器。
        /// </summary>
        public override ISqlBuilder SqlBuilder => NpgsqlSqlBuilder.Instance;

        /// <summary>
        /// SQL 方言。
        /// </summary>
        public override ISqlDialect SqlDialect => NpgsqlDialect.Instance;

        /// <summary>
        /// 保存点 SQL 方言。
        /// </summary>
        public override ISavepointSqlDialect SavepointSqlDialect => NpgsqlSavepointSqlDialect.Instance;

        /// <summary>
        /// 原生 bulk 插入器（基于 COPY BINARY）。
        /// </summary>
        public override IDbBulkInserter BulkInserter => NpgsqlBulkInserter.Instance;

        /// <summary>
        /// 
        /// </summary>
        private NpgsqlAccessProviderFactory()
        {
        }
    }
}
