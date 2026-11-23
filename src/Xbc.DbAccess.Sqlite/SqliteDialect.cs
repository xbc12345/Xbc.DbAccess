namespace Xbc.DbAccess.Sqlite
{
    /// <summary>
    /// SQLite 方言实现。
    /// <para>标识符引用、纯量 SELECT FROM 子句、AVG 聚合表达式、空串语义与 LIMIT 家族的默认取回方式等成员与基类默认实现一致，直接继承；
    /// 表名引用经基类默认实现按 <see cref="SupportsSchema"/>（false）短路为仅表名，同样无需重写。</para>
    /// </summary>
    internal sealed class SqliteDialect : SqlDialectBase
    {
        /// <summary>
        /// 获取 SQLite 方言实例。
        /// </summary>
        public static readonly SqliteDialect Instance = new SqliteDialect();

        private SqliteDialect()
        {
        }

        /// <summary>
        /// 是否支持架构（Schema）。SQLite 不支持传统架构，仅通过 ATTACH DATABASE 模拟。
        /// </summary>
        public override bool SupportsSchema => false;

        /// <summary>
        /// 获取 INSERT 语句自增 ID 的取回方式：SQLite 3.35.0 起使用 RETURNING 子句（结果集方式）。
        /// <para>版本不满足、未知（serverInfo 为 null）或探测失败（MajorVersion 为 0）时，
        /// 保守降级为 INSERT + SELECT last_insert_rowid() 两条语句。</para>
        /// </summary>
        /// <param name="serverInfo">数据库服务器版本信息。</param>
        /// <returns>引擎版本 >= 3.35.0 时返回 <see cref="InsertIdentityMode.OutputClause"/>。</returns>
        public override InsertIdentityMode GetInsertIdentityMode(DbServerInfo serverInfo)
        {
            // 版本未知（null 或未解析，SQLite 现实版本均为 3.x，MajorVersion 为 0 即探测失败）时保守降级
            if (serverInfo == null || serverInfo.MajorVersion == 0)
                return InsertIdentityMode.SelectLastInsertId;

            var supportsReturning = serverInfo.MajorVersion > 3
                || (serverInfo.MajorVersion == 3 && serverInfo.MinorVersion >= 35);

            return supportsReturning
                ? InsertIdentityMode.OutputClause
                : InsertIdentityMode.SelectLastInsertId;
        }

        /// <summary>
        /// 单条 SQL 语句允许的最大参数数量。SQLite 3.32.0 起 SQLITE_MAX_VARIABLE_NUMBER 默认值已提升至 32766，
        /// 本库捆绑的 System.Data.SQLite（>= 1.0.116）自带 SQLite 3.39+——引擎版本随包依赖锁定而非运行环境决定，
        /// 故直接取 32766。仅当自行替换为 3.32 之前的原生引擎时需注意该上限会降回 999。
        /// </summary>
        public override int MaxParameterCount => 32766;

        /// <summary>
        /// 单条多值 INSERT 语句允许的最大行数。SQLite 的多值 VALUES 在引擎内部按复合 SELECT 处理，
        /// 受 SQLITE_MAX_COMPOUND_SELECT 限制（编译默认 500），超出报 "too many terms in compound SELECT"。
        /// </summary>
        public override int MaxRowsPerInsertValues => 500;

        /// <summary>
        /// 批量插入优先使用"事务内逐条插入 + 命令复用"而非多值 INSERT。
        /// </summary>
        public override bool PreferRowByRowBatchInsert
        {
            get
            {
                // SQLite 为进程内嵌入式引擎，无网络往返可摊薄语句成本：多值 INSERT 的语句准备（prepare）开销随行数
                // 线性增长（500 行 VALUES 单次准备约 11ms），命令复用后逐条单次执行仅约 1µs 量级，实测慢一个数量级以上
                return true;
            }
        }

        /// <summary>
        /// 获取查询最后插入的自增 ID 的 SQL 语句。
        /// </summary>
        /// <param name="columnName">自增 ID 列名。</param>
        /// <returns></returns>
        public override string SelectLastInsertId(string columnName)
        {
            return $"SELECT LAST_INSERT_ROWID() AS {QuoteIdentifier(columnName)}";
        }

        /// <summary>
        /// 生成日期部分提取表达式：年/月/日使用 strftime 取对应分量后 CAST 为 INTEGER，日期归一使用 datetime(date(col))。
        /// <para>日期归一须归到零时刻文本而非 date(col) 的纯日期文本（理由见 Date 分支内联注释）。</para>
        /// </summary>
        /// <param name="quotedColumn">已引用的列名。</param>
        /// <param name="part">日期部分种类。</param>
        /// <returns>日期部分提取的 SQL 表达式。</returns>
        public override string GetDatePartExpression(string quotedColumn, DatePart part)
        {
            switch (part)
            {
                case DatePart.Year:
                    return $"CAST(strftime('%Y', {quotedColumn}) AS INTEGER)";
                case DatePart.Month:
                    return $"CAST(strftime('%m', {quotedColumn}) AS INTEGER)";
                case DatePart.Day:
                    return $"CAST(strftime('%d', {quotedColumn}) AS INTEGER)";
                case DatePart.Hour:
                    return $"CAST(strftime('%H', {quotedColumn}) AS INTEGER)";
                case DatePart.Minute:
                    return $"CAST(strftime('%M', {quotedColumn}) AS INTEGER)";
                case DatePart.Second:
                    return $"CAST(strftime('%S', {quotedColumn}) AS INTEGER)";
                case DatePart.Date:
                    // 归到零时刻文本而非 date(col) 的纯日期文本：DateTime 参数绑定为 'YYYY-MM-DD HH:MM:SS' 的 ISO 文本，
                    // datetime(date(col)) 恰好把列侧归一为同形态零时刻文本，使 =/</> 与零时刻参数的比较全部正确
                    return $"datetime(date({quotedColumn}))";
                default:
                    throw new ArgumentOutOfRangeException(nameof(part));
            }
        }
    }
}
