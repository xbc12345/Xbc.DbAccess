using System.Text;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.MySql
{
    /// <summary>
    /// MySQL 数据库 SQL 构建器。
    /// </summary>
    internal sealed class MySqlSqlBuilder : SqlBuilderBase
    {
        /// <summary>
        /// MySQL 数据库 SQL 构建器实例。
        /// </summary>
        public static readonly MySqlSqlBuilder Instance = new MySqlSqlBuilder();

        /// <summary>
        /// 获取 SQL 方言。
        /// </summary>
        public override ISqlDialect SqlDialect => MySqlDialect.Instance;

        /// <summary>
        /// 构建 Upsert 语句：ON DUPLICATE KEY UPDATE，键冲突时更新，不存在则插入。
        /// <para>MySQL 的冲突判定覆盖所有唯一键（不只主键），任何唯一键冲突都会触发更新路径；更新路径的受影响行数按 MySQL 语义返回 2。</para>
        /// <para>自增主键为默认值的 Upsert 由调用方直接走插入路径取回（不构造取回上下文），本方言不生成取回子句。</para>
        /// </summary>
        /// <param name="tableName">表名。</param>
        /// <param name="insertColumns">插入列名列表（已引用，含主键；自增列为默认值时排除、带显式值时包含）。</param>
        /// <param name="insertValues">插入值列表（参数占位符，与 insertColumns 平行）。</param>
        /// <param name="conflictKey">冲突键列名（已引用，主键列名）。</param>
        /// <param name="updateColumns">更新列名列表（已引用，由入口统一推导传入）。</param>
        /// <param name="identity">自增列参与方式上下文，本方言不生成取回子句；显式写入形态直接按列清单写入（MySQL 允许显式插入自增列值）。</param>
        /// <returns></returns>
        protected override string BuildUpsertSqlCore(string tableName, List<string> insertColumns, List<string> insertValues, string conflictKey, List<string> updateColumns, InsertIdentityContext identity = null)
        {
            // 更新赋值清单：列 = VALUES(列)，string.Join 单行拼接（与 EntitySet 的 SET 风格一致）
            var assignments = new List<string>(updateColumns.Count);
            foreach (var column in updateColumns)
            {
                assignments.Add($"{column} = VALUES({column})");
            }

            var sqlBuilder = new StringBuilder();
            sqlBuilder.AppendLine($"INSERT INTO {tableName}");
            sqlBuilder.AppendLine($"    ({string.Join(", ", insertColumns)})");
            sqlBuilder.AppendLine("VALUES");
            sqlBuilder.AppendLine($"    ({string.Join(", ", insertValues)})");
            sqlBuilder.AppendLine($"ON DUPLICATE KEY UPDATE {string.Join(", ", assignments)}");
            return sqlBuilder.ToString();
        }

        /// <summary>
        /// 构建窗口查询语句（分页与 Skip/Take 共享的核心）：LIMIT/OFFSET 语法，offset 由入口换算后直传。
        /// </summary>
        /// <param name="querySql">原始查询 SQL 语句（已修剪），不得自带顶层 ORDER BY。</param>
        /// <param name="offset">跳过的行数。</param>
        /// <param name="fetch">取回的行数。</param>
        /// <param name="orderBy">ORDER BY 子句。</param>
        /// <param name="appendRowOrder">是否保证输出行序；LIMIT/OFFSET 形态与该模式无关——
        /// ORDER BY 与 LIMIT/OFFSET 是窗口语义的必需单元（无排序的窗口取任意行无意义），派生表内 ORDER BY 合法。</param>
        /// <param name="serverInfo">服务器信息。</param>
        /// <returns></returns>
        protected override string BuildWindowSqlCore(string querySql,
            int offset,
            int fetch,
            string orderBy,
            bool appendRowOrder,
            DbServerInfo serverInfo = null)
        {
            var subqueryAlias = SqlIdentifierHelper.Generate("q");
            var sqlBuilder = new StringBuilder();
            sqlBuilder.AppendLine($"SELECT * FROM (");
            sqlBuilder.AppendLine(SqlFormatter.IndentLines(querySql));
            sqlBuilder.AppendLine($") AS {subqueryAlias}");
            sqlBuilder.AppendLine($"ORDER BY {orderBy}");
            sqlBuilder.AppendLine($"LIMIT {fetch} OFFSET {offset}");
            return sqlBuilder.ToString();
        }
    }
}
