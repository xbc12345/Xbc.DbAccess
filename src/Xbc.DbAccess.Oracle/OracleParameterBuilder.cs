using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess.Oracle
{
    /// <summary>
    /// Oracle 数据库参数构建器。
    /// </summary>
    internal sealed class OracleParameterBuilder : DbParameterBuilderBase
    {
        /// <summary>
        /// Oracle 数据库参数构建器实例。
        /// </summary>
        public static readonly OracleParameterBuilder Instance = new OracleParameterBuilder();

        /// <summary>
        /// Oracle 数据库提供程序工厂。
        /// </summary>
        public override DbProviderFactory ProviderFactory => OracleClientFactory.Instance;

        /// <summary>
        /// 初始化 Oracle 数据库参数构建器。Oracle 参数前缀为 ":"。
        /// </summary>
        private OracleParameterBuilder() : base(":")
        {
        }

        /// <summary>
        /// Oracle 布尔值域归一：bool 转换为 decimal 的 1/0（Oracle 无原生布尔类型），单条与 bulk 两条写入路径共用。
        /// </summary>
        /// <param name="value">参数值。</param>
        /// <returns>归一后的值，非 bool 原样返回。</returns>
        internal static object NormalizeBoolean(object value)
        {
            return value is bool b ? (b ? 1m : 0m) : value;
        }

        /// <summary>
        /// 创建数据库参数，bool 参数值归一为 decimal 的 1/0（Oracle 无原生布尔类型）。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="value">参数值。</param>
        /// <param name="dbType">参数数据库类型。</param>
        /// <param name="direction">参数方向。</param>
        /// <param name="size">参数长度（字符/字节数），null 时沿用基类的输出参数默认长度策略。</param>
        /// <returns></returns>
        public override DbParameter CreateParameter(string name, object value, DbType? dbType, ParameterDirection direction, int? size = null)
        {
            // bool → 1/0 归一为 decimal（与 NUMBER 列域一致）；显式配置的 DbType.Boolean 同步剥离——
            // ODP.NET 对 Boolean DbType 映射 PL/SQL BOOLEAN，与 NUMBER 列比较时抛 ORA-00932（读路径按非零转 true 已兼容）
            value = NormalizeBoolean(value);
            if (dbType == DbType.Boolean)
                dbType = null;

            return base.CreateParameter(name, value, dbType, direction, size);
        }
    }
}
