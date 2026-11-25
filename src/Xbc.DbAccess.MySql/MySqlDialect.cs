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
        /// 单条 SQL 语句允许的最大参数数量。协议每个预编译语句最多支持 65535 个占位符参数。
        /// </summary>
        public override int MaxParameterCount => 65535;

        /// <summary>
        /// 会话状态重置命令：MySQL 协议级有 COM_RESET_CONNECTION（5.7+ 引入）但为二进制协议命令、无 SQL 形态，
        /// 以单条多语句批模拟重置（单往返）。<c>ROLLBACK</c> 必须位于批首：残留事务须回滚丢弃
        /// （<c>SET autocommit = 1</c> 会隐式提交活动事务，不能顶替回滚语义），其后为字符集与会话变量复位；
        /// 批内执行顺序与服务端按序执行保证与逐条执行一致。任一语句失败由调用方将连接按失效销毁，与逐条执行的失败语义相同。
        /// <para>多语句批依赖驱动 MULTI_STATEMENTS 能力（MySqlConnector/MySql.Data 默认开启，连接串显式关闭 AllowBatch 时不可用）；
        /// 极老版本代理（如 ProxySQL 1.x）不支持多语句批，此类环境复位将失败并触发连接销毁重建（失败语义与单语句失败一致）。</para>
        /// <para>autocommit 与字符集（SET NAMES）的残留必须复位：autocommit=0 残留会让下一个借用者的
        /// 语句进入未提交事务并被回滚（数据丢失）；字符集残留影响文本编码。
        /// 临时表（会话级存续，无法枚举清理，连接销毁时自动释放）与用户变量（@v）不在复位范围。</para>
        /// </summary>
        public override string SessionStateResetCommand { get; } =
            "ROLLBACK; SET NAMES DEFAULT; SET SESSION autocommit = 1, SESSION sql_mode = DEFAULT, SESSION time_zone = DEFAULT";

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
