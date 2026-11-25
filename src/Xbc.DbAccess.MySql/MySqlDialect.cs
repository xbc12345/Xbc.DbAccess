using System.Collections.Generic;

namespace Xbc.DbAccess.MySql
{
    /// <summary>
    /// MySQL 方言实现。
    /// <para>架构支持、OFFSET/FETCH 能力探测、批量插入策略、纯量 SELECT FROM 子句、AVG 聚合表达式与空串语义等成员与基类默认实现一致，直接继承；
    /// 标识符引用为反引号形态，分页子句为 LIMIT 家族形态，两者与基类默认不同，须重写。</para>
    /// </summary>
    internal sealed class MySqlDialect : SqlDialectBase
    {
        /// <summary>
        /// 获取 MySQL 方言实例。
        /// </summary>
        public static readonly MySqlDialect Instance = new MySqlDialect();

        private MySqlDialect()
        {
        }

        /// <summary>
        /// 会话状态重置命令序列：MySQL 无 <c>sp_reset_connection</c> 的原生等价机制，回池时执行
        /// <c>ROLLBACK</c>（驱动隐式事务兜底，框架挂起事务防护外的纵深防御）与常用会话变量复位
        /// （<c>sql_mode</c>/<c>time_zone</c>，一条多赋值语句单往返完成）。
        /// <para>临时表（会话级存续，无法枚举清理，连接销毁时自动释放）与用户变量（@v）不在复位范围。</para>
        /// </summary>
        public override IReadOnlyList<string> SessionStateResetCommands { get; } = new[]
        {
            "ROLLBACK",
            "SET SESSION sql_mode = DEFAULT, SESSION time_zone = DEFAULT"
        };

        /// <summary>
        /// 引用标识符（表名、列名等）。标识符中的反引号会被双写转义，避免提前闭合。
        /// </summary>
        /// <param name="identifier">要引用的标识符。</param>
        /// <returns>引用后的标识符。</returns>
        public override string QuoteIdentifier(string identifier)
        {
            return $"`{identifier.Replace("`", "``")}`";
        }

        /// <summary>
        /// 获取 INSERT 语句自增 ID 的取回方式：MySQL 不支持 OUTPUT/RETURNING 子句，
        /// 自增 ID 由 INSERT + SELECT LAST_INSERT_ID() 两条语句取回。
        /// </summary>
        /// <param name="serverInfo">数据库服务器版本信息（未使用）。</param>
        public override InsertIdentityMode GetInsertIdentityMode(DbServerInfo serverInfo)
            => InsertIdentityMode.SelectLastInsertId;

        /// <summary>
        /// 单条 SQL 语句允许的最大参数数量。协议每个预编译语句最多支持 65535 个占位符参数。
        /// </summary>
        public override int MaxParameterCount => 65535;

        /// <summary>
        /// 单条多值 INSERT 语句允许的最大行数。MySQL 无行数硬限制。
        /// </summary>
        public override int MaxRowsPerInsertValues => int.MaxValue;

        /// <summary>
        /// 获取查询最后插入的自增 ID 的 SQL 语句。
        /// </summary>
        /// <param name="columnName">自增 ID 列名。</param>
        /// <returns></returns>
        public override string SelectLastInsertId(string columnName)
        {
            return $"SELECT LAST_INSERT_ID() AS {QuoteIdentifier(columnName)}";
        }

        /// <summary>
        /// 生成日期部分提取表达式：年/月/日/时/分/秒使用 YEAR/MONTH/DAY/HOUR/MINUTE/SECOND 函数，日期归一使用 DATE(col) 函数。
        /// </summary>
        /// <param name="quotedColumn">已引用的列名。</param>
        /// <param name="part">日期部分种类。</param>
        /// <returns>日期部分提取的 SQL 表达式。</returns>
        public override string GetDatePartExpression(string quotedColumn, DatePart part)
        {
            switch (part)
            {
                case DatePart.Year:
                    return $"YEAR({quotedColumn})";
                case DatePart.Month:
                    return $"MONTH({quotedColumn})";
                case DatePart.Day:
                    return $"DAY({quotedColumn})";
                case DatePart.Hour:
                    return $"HOUR({quotedColumn})";
                case DatePart.Minute:
                    return $"MINUTE({quotedColumn})";
                case DatePart.Second:
                    return $"SECOND({quotedColumn})";
                case DatePart.Date:
                    return $"DATE({quotedColumn})";
                default:
                    throw new ArgumentOutOfRangeException(nameof(part));
            }
        }
    }
}
