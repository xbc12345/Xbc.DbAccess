using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using System.Globalization;

namespace Xbc.DbAccess.Sqlite
{
    /// <summary>
    /// SQLite 数据库参数构建器。
    /// </summary>
    internal sealed class SqliteParameterBuilder : DbParameterBuilderBase
    {
        /// <summary>
        /// SQLite 数据库参数构建器实例。
        /// </summary>
        public static readonly SqliteParameterBuilder Instance = new SqliteParameterBuilder();

        /// <summary>
        /// SQLite 数据库提供程序工厂。
        /// </summary>
        public override DbProviderFactory ProviderFactory => SQLiteFactory.Instance;

        /// <summary>
        /// 初始化 SQLite 数据库参数构建器。SQLite 参数前缀为 "@"。
        /// </summary>
        private SqliteParameterBuilder() : base("@")
        {
        }

        /// <summary>
        /// 创建数据库参数，DateTimeOffset 参数值归一为 ISO 文本（SQLite 无原生 DateTimeOffset 支持）。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="value">参数值。</param>
        /// <param name="dbType">参数数据库类型。</param>
        /// <param name="direction">参数方向。</param>
        /// <param name="size">参数长度（字符/字节数），null 时沿用基类的输出参数默认长度策略。</param>
        /// <returns></returns>
        public override DbParameter CreateParameter(string name, object value, DbType? dbType, ParameterDirection direction, int? size = null)
        {
            // DateTimeOffset → ISO 文本归一；显式配置的 DbType.DateTimeOffset 同步剥离——
            // System.Data.SQLite 不支持 DateTimeOffset DbType（设置即抛异常），归一后的文本按 String 绑定；
            // 若直接绑定会落库为区域格式文本（'2026/3/1 0:30:00 +08:00'），strftime 返回 NULL、字典序范围比较错乱
            if (value is DateTimeOffset dto)
            {
                value = dto.ToString("yyyy-MM-dd HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture);
                if (dbType == DbType.DateTimeOffset)
                    dbType = null;
            }

            return base.CreateParameter(name, value, dbType, direction, size);
        }
    }
}
