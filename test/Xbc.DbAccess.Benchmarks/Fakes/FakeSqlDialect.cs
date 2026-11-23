namespace Xbc.DbAccess.Benchmarks.Fakes
{
    /// <summary>
    /// 模拟 SQL 方言，继承方言基类并实现保存点方言接口（诊断基准不触发这些方法，仅满足提供程序工厂契约）。
    /// </summary>
    public sealed class FakeSqlDialect : SqlDialectBase, ISavepointSqlDialect
    {
        /// <summary>
        /// 单例实例。
        /// </summary>
        public static readonly FakeSqlDialect Instance = new FakeSqlDialect();

        private FakeSqlDialect()
        {
        }

        /// <inheritdoc />
        public override bool SupportsSchema => false;

        /// <inheritdoc />
        public override InsertIdentityMode GetInsertIdentityMode(DbServerInfo serverInfo) => InsertIdentityMode.SelectLastInsertId;

        /// <inheritdoc />
        public override bool SupportsOffsetFetch(DbServerInfo serverInfo) => true;

        /// <inheritdoc />
        public override int MaxParameterCount => 999;

        /// <inheritdoc />
        public override int MaxRowsPerInsertValues => int.MaxValue;// 假方言无行数限制

        /// <inheritdoc />
        public bool IsSavepointSupported => true;

        /// <inheritdoc />
        public override string GetDatePartExpression(string quotedColumn, DatePart part) => part.ToString().ToUpperInvariant() + "(" + quotedColumn + ")";

        /// <inheritdoc />
        public bool IsReleaseSavepointSupported => true;

        /// <inheritdoc />
        public override string QuoteIdentifier(string identifier) => identifier;

        /// <inheritdoc />
        public override string QuoteTableName(string schema, string tableName) => tableName;

        /// <inheritdoc />
        public override string SelectLastInsertId(string columnName) => "SELECT LAST_INSERT_ROWID()";

        /// <inheritdoc />
        public string CreateSavepoint(string savepointName) => $"SAVEPOINT {savepointName}";

        /// <inheritdoc />
        public string RollbackToSavepoint(string savepointName) => $"ROLLBACK TO SAVEPOINT {savepointName}";

        /// <inheritdoc />
        public string ReleaseSavepoint(string savepointName) => $"RELEASE SAVEPOINT {savepointName}";
    }
}
