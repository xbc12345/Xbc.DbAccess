namespace Xbc.DbAccess
{
    /// <summary>
    /// SQL 构建器接口，定义构建 SQL 语句的方法。
    /// </summary>
    public interface ISqlBuilder
    {
        /// <summary>
        /// SQL 方言。
        /// </summary>
        ISqlDialect SqlDialect { get; }

        /// <summary>
        /// 构建 SELECT 查询语句。
        /// </summary>
        /// <param name="tableName">表名。</param>
        /// <param name="columns">列名列表，若为 null 或空列表则查询所有列。</param>
        /// <param name="whereClause">WHERE 子句。</param>
        /// <param name="orderBy">ORDER BY 子句，直接拼接进 SQL，禁止包含用户输入（防注入）。</param>
        /// <param name="distinct">是否使用 DISTINCT 关键字。</param>
        /// <returns></returns>
        string BuildSelectSql(string tableName,
            List<string> columns = null,
            string whereClause = null,
            string orderBy = null,
            bool distinct = false);

        /// <summary>
        /// 构建插入语句。
        /// </summary>
        /// <param name="tableName">表名。</param>
        /// <param name="columns">列名列表。</param>
        /// <param name="values">值列表。</param>
        /// <param name="identity">自增列参与方式，null 表示无自增列参与；默认值时由数据库生成并取回，显式值时进入列清单并生成 IDENTITY_INSERT 类包裹语句。</param>
        /// <returns></returns>
        string BuildInsertSql(string tableName, List<string> columns, List<string> values, InsertIdentityContext identity = null);

        /// <summary>
        /// 构建 Upsert 语句（存在则更新，不存在则插入）。
        /// </summary>
        /// <param name="tableName">表名。</param>
        /// <param name="insertColumns">插入列名列表（已引用，含主键；自增列默认值时排除，显式值时包含）。</param>
        /// <param name="insertValues">插入值列表（参数占位符，与 insertColumns 平行）。</param>
        /// <param name="conflictKey">冲突键列名（已引用，主键列名）。不在 insertColumns 中时，MERGE 类方言以 NULL 作为源列值以走插入路径。</param>
        /// <param name="identity">自增列参与方式，null 表示无自增列参与；取回形态仅对支持 OUTPUT/RETURNING 的方言构造，无法配合的方言（如 Oracle 的 MERGE）由调用方传 null，显式值时生成 IDENTITY_INSERT 类包裹语句。</param>
        /// <returns></returns>
        string BuildUpsertSql(string tableName, List<string> insertColumns, List<string> insertValues, string conflictKey, InsertIdentityContext identity = null);

        /// <summary>
        /// 构建窗口查询语句（OFFSET/FETCH 语义的行窗口截取）：分页调用方自行换算页码为 offset（skip = (pageIndex - 1) * pageSize）。
        /// <para>行序契约由 <paramref name="appendRowOrder"/> 表达：true 为终结查询，输出行序等于 orderBy 序；
        /// false 供聚合与投影等包裹场景，窗口行集相同但不保证输出行序（由包裹方的外层排序保证），
        /// 实现保证派生表/EXISTS 子查询内无裸 ORDER BY（SQL Server 语法约束）。</para>
        /// </summary>
        /// <param name="querySql">原始查询 SQL 语句，不得自带顶层 ORDER BY（排序统一由 orderBy 参数传入；首尾空白与尾部分号由实现修剪）。</param>
        /// <param name="skip">跳过的行数（offset），非负；0 表示从第一行开始。</param>
        /// <param name="take">取回的行数（fetch），正数；单独 OFFSET（无取行数）在方言间不可移植，API 层已保证 Skip 必配 Take。</param>
        /// <param name="orderBy">ORDER BY 子句，直接拼接进 SQL，禁止包含用户输入（防注入）。</param>
        /// <param name="appendRowOrder">是否保证输出行序：true（默认，终结查询）兑现行序契约；false（包裹场景）不保证输出行序。</param>
        /// <param name="serverInfo">服务器信息。</param>
        /// <returns></returns>
        string BuildWindowSql(string querySql,
            int skip,
            int take,
            string orderBy,
            bool appendRowOrder = true,
            DbServerInfo serverInfo = null);
    }
}
