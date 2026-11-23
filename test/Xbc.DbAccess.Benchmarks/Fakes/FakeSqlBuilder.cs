using System.Collections.Generic;

namespace Xbc.DbAccess.Benchmarks.Fakes
{
    /// <summary>
    /// 模拟 SQL 构建器（诊断基准不触发构建方法，仅满足提供程序工厂契约）。
    /// </summary>
    public sealed class FakeSqlBuilder : ISqlBuilder
    {
        /// <summary>
        /// 单例实例。
        /// </summary>
        public static readonly FakeSqlBuilder Instance = new FakeSqlBuilder();

        private FakeSqlBuilder()
        {
        }

        /// <inheritdoc />
        public ISqlDialect SqlDialect => FakeSqlDialect.Instance;

        /// <inheritdoc />
        public string BuildSelectSql(string tableName,
            List<string> columns = null,
            string whereClause = null,
            string orderBy = null,
            bool distinct = false)
            => "SELECT 1";

        /// <inheritdoc />
        public string BuildInsertSql(string tableName, List<string> columns, List<string> values, InsertIdentityContext identity = null)
            => "INSERT";

        /// <inheritdoc />
        public string BuildUpsertSql(string tableName, List<string> insertColumns, List<string> insertValues, string conflictKey, InsertIdentityContext identity = null)
            => "UPSERT";

        /// <inheritdoc />
        public string BuildWindowSql(string querySql,
            int skip,
            int take,
            string orderBy,
            bool appendRowOrder = true,
            DbServerInfo serverInfo = null)
            => querySql;
    }
}
