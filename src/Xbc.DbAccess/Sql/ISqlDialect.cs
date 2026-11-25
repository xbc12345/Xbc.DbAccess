namespace Xbc.DbAccess
{
    /// <summary>
    /// SQL 方言接口，定义数据库特定的 SQL 语法。
    /// </summary>
    public interface ISqlDialect
    {
        /// <summary>
        /// 引用标识符（表名、列名等）。
        /// </summary>
        /// <param name="identifier">要引用的标识符。</param>
        /// <returns></returns>
        string QuoteIdentifier(string identifier);

        /// <summary>
        /// 引用完整表名。架构为空或方言不支持架构（<see cref="SupportsSchema"/> 为 false）时仅引用表名，否则为"架构.表名"格式。
        /// </summary>
        /// <param name="schema">架构名，可为 null 或空。</param>
        /// <param name="tableName">表名。</param>
        /// <returns></returns>
        string QuoteTableName(string schema, string tableName);

        /// <summary>
        /// 是否支持架构（Schema）。
        /// </summary>
        bool SupportsSchema { get; }

        /// <summary>
        /// 获取 INSERT 语句自增 ID 的取回方式（能力探测，不生成 SQL，结果可缓存）。
        /// <para>能力可能与引擎版本相关（如 SQLite 需 3.35.0+ 才支持 RETURNING），按 <paramref name="serverInfo"/> 判定；
        /// 版本未知时各方言按既定策略返回（如保守降级到 <see cref="InsertIdentityMode.SelectLastInsertId"/>）。</para>
        /// </summary>
        /// <param name="serverInfo">数据库服务器版本信息，可能为 null 或版本未解析。</param>
        /// <returns>自增 ID 取回方式。</returns>
        InsertIdentityMode GetInsertIdentityMode(DbServerInfo serverInfo);

        /// <summary>
        /// 是否支持 SQL 标准的 OFFSET/FETCH 分页语法，按引擎版本判定（如 SQL Server 2012+/Oracle 12c+）。
        /// 版本未知时返回 false，降级为 ROW_NUMBER 兼容语法；LIMIT/OFFSET 方言（MySQL/PostgreSQL/SQLite）返回 false 并重写分页构建。
        /// </summary>
        /// <param name="serverInfo">数据库服务器版本信息，可能为 null 或版本未解析。</param>
        /// <returns>支持返回 true。</returns>
        bool SupportsOffsetFetch(DbServerInfo serverInfo);

        /// <summary>
        /// 单条 SQL 语句允许的最大参数数量（SQL Server 2100、MySQL 65535、PostgreSQL 32767、SQLite 999、Oracle 1000），
        /// 批量写入与 IN 条件的批次大小按此上限计算。
        /// </summary>
        int MaxParameterCount { get; }

        /// <summary>
        /// 单条多值 INSERT 语句（<c>INSERT INTO T (...) VALUES (...), (...)</c>）允许的最大行数。
        /// SQL Server 为 1000 行；Oracle 23c 前不支持多值 VALUES，返回 1 退化为单行插入；无行数限制返回 <see cref="int.MaxValue"/>。
        /// </summary>
        int MaxRowsPerInsertValues { get; }

        /// <summary>
        /// 批量插入是否优先使用"事务内逐条插入 + 命令复用"而非多值 INSERT。
        /// 进程内嵌入式引擎（如 SQLite）多值 INSERT 的语句准备开销高于逐条复用执行，返回 true；
        /// 网络型数据库多值 INSERT 可摊薄往返，返回 false。仅影响执行策略与批次划分，不影响 SQL 生成与结果。
        /// </summary>
        bool PreferRowByRowBatchInsert { get; }

        /// <summary>
        /// 无 FROM 子句的纯量 SELECT（如 <c>SELECT 1</c>）需要追加的 FROM 子句。
        /// Oracle 23c 前要求 SELECT 显式指定表来源，返回 " FROM DUAL"；无此限制的方言返回 null。
        /// </summary>
        string ScalarSelectFromClause { get; }

        /// <summary>
        /// 会话状态重置命令序列：连接归还连接池时按序执行，将会话恢复到"干净"状态后再入池复用，
        /// 对标 SQL Server 内置连接池的 <c>sp_reset_connection</c>。
        /// <para>SQL Server 返回 <c>sp_reset_connection</c>（临时表、SET 选项、残留事务等一次性复位）；
        /// PostgreSQL 返回 <c>DISCARD ALL</c>；MySQL 返回 <c>ROLLBACK</c> 与常用会话变量复位（无原生等价机制）；
        /// 无会话残留风险或无等价机制的方言（Oracle/SQLite）返回空序列。</para>
        /// <para>任一命令执行失败，连接按失效处理销毁重建。</para>
        /// </summary>
        System.Collections.Generic.IReadOnlyList<string> SessionStateResetCommands { get; }

        /// <summary>
        /// 获取查询最后插入的自增 ID 的 SQL 语句，用于不支持 OUTPUT/RETURNING 子句的数据库。
        /// </summary>
        /// <param name="columnName">自增 ID 列名。</param>
        /// <returns></returns>
        string SelectLastInsertId(string columnName);

        /// <summary>
        /// 生成日期列的日期部分提取表达式（如 <c>YEAR(col)</c>、<c>EXTRACT(YEAR FROM col)</c>、SQLite 的 <c>strftime</c>）。
        /// <see cref="Xbc.DbAccess.DatePart.Date"/> 为截断到零时刻（<c>CAST(col AS DATE)</c> 等）。
        /// </summary>
        /// <param name="quotedColumn">已引用的列名（经 <see cref="QuoteIdentifier"/> 处理）。</param>
        /// <param name="part">日期部分种类。</param>
        /// <returns>日期部分提取的 SQL 表达式。</returns>
        string GetDatePartExpression(string quotedColumn, DatePart part);

        /// <summary>
        /// 生成数值列的 AVG 聚合表达式，默认 <c>AVG({quotedColumn})</c>。
        /// SQL Server 的 AVG 继承参数整数类型（AVG(int) 返回 int），重写为 <c>AVG(CAST(... AS DECIMAL(38,6)))</c> 以保持各方言结果语义一致。
        /// </summary>
        /// <param name="quotedColumn">已引用的列名（经 <see cref="QuoteIdentifier"/> 处理）。</param>
        /// <returns>AVG 聚合表达式。</returns>
        string BuildAverageExpression(string quotedColumn);

        /// <summary>
        /// 数据库是否把零长度字符串当作 NULL 处理。Oracle 中 <c>''</c> 即 <c>NULL</c>（无法存储空串），返回 true；其余方言返回 false。
        /// </summary>
        bool TreatsEmptyStringAsNull { get; }
    }
}
