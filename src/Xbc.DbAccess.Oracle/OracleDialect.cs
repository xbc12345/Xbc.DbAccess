namespace Xbc.DbAccess.Oracle
{
    /// <summary>
    /// Oracle 方言实现。
    /// <para>标识符引用（双引号形态）、架构支持、批量插入策略与 AVG 聚合表达式等成员与基类默认实现一致，直接继承；
    /// 分页子句为 OFFSET/FETCH 形态，与基类默认实现（SQL 标准语法）逐字符一致，同样无需重写。</para>
    /// </summary>
    internal sealed class OracleDialect : SqlDialectBase
    {
        /// <summary>
        /// 获取 Oracle 方言实例。
        /// </summary>
        public static readonly OracleDialect Instance = new OracleDialect();

        private OracleDialect()
        {
        }

        /// <summary>
        /// 单条 SQL 语句允许的最大参数数量。1000 为 Oracle IN 列表表达式上限（ORA-01795），绑定变量总数的实际上限更高；
        /// 作为通用参数上限取保守值，仅影响批量分批粒度，不影响正确性。
        /// </summary>
        public override int MaxParameterCount => 1000;

        /// <inheritdoc />
        /// <remarks>Oracle 23c 之前不支持无 FROM 的纯量 SELECT，需追加 FROM DUAL。</remarks>
        public override string ScalarSelectFromClause => " FROM DUAL";

        /// <summary>
        /// 获取 INSERT 语句自增 ID 的取回方式：Oracle 使用 RETURNING INTO 输出参数方式。
        /// <para>Oracle 的自增列（12c IDENTITY 或序列填充列）可通过 <c>RETURNING "Id" INTO :out_Id</c> 单语句取回：
        /// 自增 ID 由提供程序在执行后写回输出参数，不产生结果集。RETURNING INTO 适用于所有受支持版本。</para>
        /// </summary>
        /// <param name="serverInfo">数据库服务器版本信息（未使用）。</param>
        public override InsertIdentityMode GetInsertIdentityMode(DbServerInfo serverInfo)
            => InsertIdentityMode.ReturningInto;

        /// <summary>
        /// 是否支持 SQL 标准的 OFFSET/FETCH 分页语法。
        /// <para>Oracle 12c（版本 12.0）起支持；版本未知（null 或未解析）时保守返回 false，
        /// 由 SQL 构建器基类的分页模板降级为 ROW_NUMBER 兼容语法。</para>
        /// </summary>
        /// <param name="serverInfo">数据库服务器版本信息。</param>
        /// <returns>引擎版本 >= 12.0 时返回 true。</returns>
        public override bool SupportsOffsetFetch(DbServerInfo serverInfo)
        {
            // 版本未知（null 或未解析）时保守降级
            if (serverInfo == null || serverInfo.MajorVersion == 0)
                return false;

            // 从 Oracle 12c 开始支持 OFFSET 和 FETCH NEXT（版本 12.0）
            return serverInfo.MajorVersion >= 12;
        }

        /// <summary>
        /// 单条多值 INSERT 语句允许的最大行数。Oracle 23c 之前不支持 <c>VALUES (...), (...)</c> 多值插入语法，
        /// 返回 1 使批量插入退化为逐行的单值 INSERT，保证生成语法合法。
        /// </summary>
        public override int MaxRowsPerInsertValues => 1;

        /// <summary>
        /// 获取查询最后插入的自增 ID 的 SQL 语句。
        /// <para>Oracle 不支持通过通用 SELECT 语句获取自增 ID（需 RETURNING INTO 子句或序列 CURRVAL），
        /// 调用即抛出 <see cref="NotSupportedException"/>，避免生成必然执行失败的占位 SQL。</para>
        /// <para>插入路径已按 <see cref="GetInsertIdentityMode"/> 自动使用 RETURNING INTO 输出参数取回，
        /// 正常不会走到此方法，异常仅作为误用路径的防御。</para>
        /// </summary>
        /// <param name="columnName">自增 ID 列别名。</param>
        /// <exception cref="NotSupportedException">Oracle 不支持通过 SELECT 语句获取自增 ID。</exception>
        public override string SelectLastInsertId(string columnName)
        {
            throw new NotSupportedException("Oracle 提供程序不支持通过 SELECT 语句获取自增 ID：请使用 RETURNING INTO 子句，或在插入前通过序列 NEXTVAL 显式生成主键。");
        }

        /// <summary>
        /// 生成日期部分提取表达式：年/月/日/时/分/秒使用标准的 EXTRACT(YEAR FROM col) 等，日期归一使用 TRUNC(col)（截断到当日零时刻）。
        /// <para>时/分/秒的 EXTRACT 要求来源为 TIMESTAMP 类型，.NET 的 DateTime 在 Oracle 中映射为 DATE 类型，
        /// 须先 CAST(col AS TIMESTAMP) 再提取（ORA-30076：对 DATE 类型的 HOUR 提取非法）。</para>
        /// <para>Second 部分的 EXTRACT(SECOND FROM ...) 返回含小数秒的值（如 30.5），
        /// 与 C# <see cref="DateTime.Second"/> 的整秒语义分裂（含小数秒的行会命中错误的整秒值），
        /// 包裹 <c>TRUNC(...)</c> 归一为整秒（秒域非负，向零截断与取整等价），
        /// 保持与 SQL Server DATEPART(SECOND)/MySQL SECOND()/SQLite strftime('%S') 的整秒语义一致。</para>
        /// </summary>
        /// <param name="quotedColumn">已引用的列名。</param>
        /// <param name="part">日期部分种类。</param>
        /// <returns>日期部分提取的 SQL 表达式。</returns>
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
                    return $"EXTRACT(HOUR FROM CAST({quotedColumn} AS TIMESTAMP))";
                case DatePart.Minute:
                    return $"EXTRACT(MINUTE FROM CAST({quotedColumn} AS TIMESTAMP))";
                case DatePart.Second:
                    return $"TRUNC(EXTRACT(SECOND FROM CAST({quotedColumn} AS TIMESTAMP)))";
                case DatePart.Date:
                    return $"TRUNC({quotedColumn})";
                default:
                    throw new ArgumentOutOfRangeException(nameof(part));
            }
        }

        /// <summary>
        /// Oracle 中零长度字符串即 NULL（无法存储空串），空串判断需退化为纯 NULL 判断。
        /// </summary>
        public override bool TreatsEmptyStringAsNull => true;
    }
}
