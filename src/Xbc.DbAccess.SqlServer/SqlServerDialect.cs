using System.Collections.Generic;

namespace Xbc.DbAccess.SqlServer
{
    /// <summary>
    /// SQL Server 方言实现。
    /// <para>架构支持、批量插入策略、纯量 SELECT FROM 子句、空串语义等成员与基类默认实现一致，直接继承；
    /// 分页子句为 OFFSET/FETCH 形态，与基类默认实现（SQL 标准语法）逐字符一致，同样无需重写。</para>
    /// </summary>
    internal sealed class SqlServerDialect : SqlDialectBase
    {
        /// <summary>
        /// 获取 SQL Server 方言实例。
        /// </summary>
        public static readonly SqlServerDialect Instance = new SqlServerDialect();

        private SqlServerDialect()
        {
        }

        /// <summary>
        /// 会话状态重置命令序列：回池时执行 <c>sp_reset_connection</c>，一次性复位临时表、SET 选项、残留事务、
        /// CONTEXT_INFO 等会话状态（对标 SQL Server 内置连接池的复用复位行为）。
        /// </summary>
        public override IReadOnlyList<string> SessionStateResetCommands { get; } = new[] { "EXEC sp_reset_connection" };

        /// <summary>
        /// 引用标识符（表名、列名等）。标识符中的 <c>]</c> 会被转义为 <c>]]</c>，避免提前闭合。
        /// </summary>
        /// <param name="identifier">要引用的标识符。</param>
        /// <returns>引用后的标识符。</returns>
        public override string QuoteIdentifier(string identifier)
        {
            return $"[{identifier.Replace("]", "]]")}]";
        }

        /// <summary>
        /// 获取 INSERT 语句自增 ID 的取回方式：SQL Server 2005（版本 9.0）起使用 OUTPUT 子句（结果集方式），
        /// 更低的已知版本降级为 INSERT + SELECT SCOPE_IDENTITY() 两条语句。
        /// <para>版本未知时选择 OUTPUT：该路径在不支持的老版本上产生显式语法错误（立即可见）；
        /// 若保守降级则会走 SelectLastInsertId 备用路径，该路径在本框架的两条命令模式下
        /// 会静默返回 NULL（SCOPE_IDENTITY 按批隔离），危害更大。</para>
        /// </summary>
        /// <param name="serverInfo">数据库服务器版本信息。</param>
        /// <returns>引擎版本 >= 9.0 或版本未知时返回 <see cref="InsertIdentityMode.OutputClause"/>。</returns>
        public override InsertIdentityMode GetInsertIdentityMode(DbServerInfo serverInfo)
        {
            // 版本未知时选择 OUTPUT，理由见 doc 说明：两个路径的失败模式不同，应指向显式失败的一方
            if (serverInfo == null || serverInfo.MajorVersion == 0)
                return InsertIdentityMode.OutputClause;

            // 从 SQL Server 2005 开始支持 OUTPUT 子句（版本 9.0）
            return serverInfo.MajorVersion >= 9
                ? InsertIdentityMode.OutputClause
                : InsertIdentityMode.SelectLastInsertId;
        }

        /// <summary>
        /// 是否支持 SQL 标准的 OFFSET/FETCH 分页语法。
        /// <para>SQL Server 2012（版本 11.0）起支持；版本未知（null 或未解析）时保守返回 false，
        /// 由 SQL 构建器基类的分页模板降级为 ROW_NUMBER 兼容语法。</para>
        /// </summary>
        /// <param name="serverInfo">数据库服务器版本信息。</param>
        /// <returns>引擎版本 >= 11.0 时返回 true。</returns>
        public override bool SupportsOffsetFetch(DbServerInfo serverInfo)
        {
            // 版本未知（null 或未解析）时保守降级
            if (serverInfo == null || serverInfo.MajorVersion == 0)
                return false;

            // 从 SQL Server 2012 开始支持 OFFSET 和 FETCH NEXT（版本 11.0）
            return serverInfo.MajorVersion >= 11;
        }

        /// <summary>
        /// 单条 SQL 语句允许的最大参数数量。引擎上限为 2100（RPC 请求的总参数量，含内部参数），
        /// sp_executesql/sp_prepexec 等内部机制会占用少量参数位，不预留余量时恰好打满 2100 会报 too many parameters。
        /// </summary>
        public override int MaxParameterCount => 2090;

        /// <summary>
        /// 单条多值 INSERT 语句允许的最大行数。表值构造器的硬性限制为 1000 行，超出将报错。
        /// </summary>
        public override int MaxRowsPerInsertValues => 1000;

        /// <summary>
        /// 获取查询最后插入的自增 ID 的 SQL 语句。
        /// <para>SCOPE_IDENTITY() 按批处理（batch）隔离：INSERT 与本语句须在同一命令文本内执行才能取到值，
        /// 拆分为两条独立命令会返回 NULL。本框架中 SQL Server 2005 及以上（含版本未知）走 OUTPUT 子句路径
        /// （见 <see cref="GetInsertIdentityMode"/>），仅已知低于 9.0 的版本会走到此备用路径，
        /// 启用前须确保与 INSERT 拼接为同一批。</para>
        /// </summary>
        /// <param name="columnName">自增 ID 列名。</param>
        /// <returns></returns>
        public override string SelectLastInsertId(string columnName)
        {
            return $"SELECT SCOPE_IDENTITY() AS {QuoteIdentifier(columnName)}";
        }

        /// <summary>
        /// 生成日期部分提取表达式：年/月/日使用 YEAR/MONTH/DAY 函数，时/分/秒使用 DATEPART（SQL Server 无独立的 HOUR 等函数），
        /// 日期归一使用 CAST(col AS DATE)（SQL Server 2008 起支持 DATE 类型）。
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
                    return $"DATEPART(HOUR, {quotedColumn})";
                case DatePart.Minute:
                    return $"DATEPART(MINUTE, {quotedColumn})";
                case DatePart.Second:
                    return $"DATEPART(SECOND, {quotedColumn})";
                case DatePart.Date:
                    return $"CAST({quotedColumn} AS DATE)";
                default:
                    throw new ArgumentOutOfRangeException(nameof(part));
            }
        }

        /// <summary>
        /// 生成数值列的 AVG 聚合表达式。
        /// <para>SQL Server 的 AVG 继承参数的整数类型（引擎行为：AVG(int) 返回 int，1 与 2 的均值为 1 而非 1.5），
        /// 无条件 CAST 为 DECIMAL(38,6) 恢复精确均值语义：decimal 列升位 DECIMAL(38,6) 无损（默认精度低于 38），
        /// 与其余方言（AVG 恒返回精确数值）的结果语义对齐。</para>
        /// </summary>
        /// <param name="quotedColumn">已引用的列名。</param>
        /// <returns>AVG 聚合表达式。</returns>
        public override string BuildAverageExpression(string quotedColumn)
        {
            return $"AVG(CAST({quotedColumn} AS DECIMAL(38,6)))";
        }
    }
}
