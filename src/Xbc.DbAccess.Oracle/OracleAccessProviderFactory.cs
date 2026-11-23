using Oracle.ManagedDataAccess.Client;
using System.Data.Common;

namespace Xbc.DbAccess.Oracle
{
    /// <summary>
    /// Oracle 数据库访问提供程序工厂。
    /// </summary>
    public sealed class OracleAccessProviderFactory : DbAccessProviderFactory
    {
        /// <summary>
        /// Oracle 数据库访问提供程序工厂实例。
        /// </summary>
        public static readonly OracleAccessProviderFactory Instance = new OracleAccessProviderFactory();

        /// <summary>
        /// 类型初始化时将 ODP.NET 全局默认设为按名称绑定参数。
        /// <para>ODP.NET 默认按占位符出现顺序绑定（<c>BindByName</c> 为 false），与本框架的命名参数语义不符。
        /// 此处全局兜底覆盖进程内所有 OracleCommand（含使用方手工创建的命令），
        /// 与 <see cref="ConfigureCommand"/> 的实例级配置互为双保险。</para>
        /// </summary>
        static OracleAccessProviderFactory()
        {
            OracleConfiguration.BindByName = true;
        }

        /// <summary>
        /// 数据库提供程序工厂。
        /// </summary>
        public override DbProviderFactory DbProviderFactory => OracleClientFactory.Instance;

        /// <summary>
        /// 参数构建器。
        /// </summary>
        public override IDbParameterBuilder ParameterBuilder => OracleParameterBuilder.Instance;

        /// <summary>
        /// SQL 构建器。
        /// </summary>
        public override ISqlBuilder SqlBuilder => OracleSqlBuilder.Instance;

        /// <summary>
        /// SQL 方言。
        /// </summary>
        public override ISqlDialect SqlDialect => OracleDialect.Instance;

        /// <summary>
        /// 保存点 SQL 方言。
        /// </summary>
        public override ISavepointSqlDialect SavepointSqlDialect => OracleSavepointSqlDialect.Instance;

        /// <summary>
        /// 原生 bulk 插入器（基于 ODP.NET 的数组绑定 ArrayBind）。
        /// </summary>
        public override IDbBulkInserter BulkInserter => OracleBulkInserter.Instance;

        /// <summary>
        /// 对命令应用 Oracle 提供程序配置：启用按名称绑定参数。
        /// <para>ODP.NET 默认按占位符出现顺序绑定（<c>BindByName</c> 为 false），与本框架的命名参数语义不符：
        /// 参数集合顺序与 SQL 占位符顺序不一致时会静默错绑值（如把 A 列的值写入 B 列），
        /// 故必须显式开启按名称绑定。</para>
        /// </summary>
        /// <param name="command">数据库命令。</param>
        public override void ConfigureCommand(DbCommand command)
        {
            if (command is OracleCommand oracleCommand)
                oracleCommand.BindByName = true;
        }

        /// <summary>
        /// 
        /// </summary>
        private OracleAccessProviderFactory()
        {
        }
    }
}
