namespace Xbc.DbAccess.Npgsql
{
    /// <summary>
    /// PostgreSQL 方言实现。
    /// <para>标识符引用（双引号形态）、架构支持、OFFSET/FETCH 能力探测、批量插入策略、纯量 SELECT FROM 子句、
    /// AVG 聚合表达式与空串语义等成员与基类默认实现一致，直接继承；
    /// 分页子句为 LIMIT 家族形态（"仅 offset"分支为裸 OFFSET，与 SQLite/MySQL 的占位形态不同），须重写。</para>
    /// </summary>
    internal sealed class NpgsqlDialect : SqlDialectBase
    {
        /// <summary>
        /// 获取 PostgreSQL 方言实例。
        /// </summary>
        public static readonly NpgsqlDialect Instance = new NpgsqlDialect();

        private NpgsqlDialect()
        {
        }

        /// <summary>
        /// 获取 INSERT 语句自增 ID 的取回方式：PostgreSQL 使用 RETURNING 子句（结果集方式），与引擎版本无关。
        /// </summary>
        /// <param name="serverInfo">数据库服务器版本信息（未使用）。</param>
        public override InsertIdentityMode GetInsertIdentityMode(DbServerInfo serverInfo)
            => InsertIdentityMode.OutputClause;

        /// <summary>
        /// 单条 SQL 语句允许的最大参数数量。Npgsql 驱动受 wire 协议 Bind 消息中 Int16 编码限制，实际最大参数数量为 32767。
        /// </summary>
        public override int MaxParameterCount => 32767;

        /// <summary>
        /// 单条多值 INSERT 语句允许的最大行数。PostgreSQL 无行数硬限制。
        /// </summary>
        public override int MaxRowsPerInsertValues => int.MaxValue;

        /// <summary>
        /// 获取查询最后插入的自增 ID 的 SQL 语句。
        /// PostgreSQL 通常使用 RETURNING 子句，不需要额外查询。
        /// 这里提供备用方案（使用 lastval() 函数）。
        /// </summary>
        /// <param name="columnName">自增 ID 列名。</param>
        /// <returns></returns>
        public override string SelectLastInsertId(string columnName)
        {
            return $"SELECT lastval() AS {QuoteIdentifier(columnName)}";
        }

        /// <summary>
        /// 生成日期部分提取表达式：年/月/日/时/分/秒使用标准的 EXTRACT(YEAR FROM col) 等，日期归一使用 CAST(col AS date)。
        /// <para>EXTRACT 在 PostgreSQL 中返回 numeric 类型，与整数参数比较时由引擎隐式转换。</para>
        /// <para>Second 部分的 EXTRACT(SECOND FROM col) 返回含小数秒的 numeric（如 30.5），
        /// 与 C# <see cref="DateTime.Second"/> 的整秒语义分裂（含小数秒的行会命中错误的整秒值），
        /// 包裹 <c>FLOOR(...)</c> 归一为整秒，保持与 SQL Server DATEPART(SECOND)/MySQL SECOND()/SQLite strftime('%S') 的整秒语义一致。</para>
        /// </summary>
        /// <param name="quotedColumn">已引用的列名。</param>
        /// <param name="part">日期部分种类。</param>
        /// <returns>日期部分：年/月/日/时/分使用标准 EXTRACT，秒包裹 FLOOR，日期归一 CAST(col AS date)。</returns>
        public override string GetDatePartExpression(string quotedColumn, DatePart part)
        {
            switch (part)
            {
                case DatePart.Year:
                    return $"EXTRACT(YEAR FROM {quotedColumn})";
                case DatePart.Month:
                    return $"EXTRACT(MONTH FROM {quotedColumn})";
                case DatePart.Day:
                    return $"EXTRACT(DAY FROM {quotedColumn})";
                case DatePart.Hour:
                    return $"EXTRACT(HOUR FROM {quotedColumn})";
                case DatePart.Minute:
                    return $"EXTRACT(MINUTE FROM {quotedColumn})";
                case DatePart.Second:
                    return $"FLOOR(EXTRACT(SECOND FROM {quotedColumn}))";
                case DatePart.Date:
                    return $"CAST({quotedColumn} AS date)";
                default:
                    throw new ArgumentOutOfRangeException(nameof(part));
            }
        }
    }
}
