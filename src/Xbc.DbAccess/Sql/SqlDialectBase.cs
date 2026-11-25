namespace Xbc.DbAccess
{
    /// <summary>
    /// SQL 方言抽象基类，收口五方言对 <see cref="ISqlDialect"/> 的公共实现（接口因多目标 net462 无法提供默认方法实现）。
    /// <para>一致的成员直接继承，多数一致的按默认实现继承、差异方言重写（如 <see cref="QuoteIdentifier"/> 的定界符），
    /// 互不相同的声明为抽象成员（如 <see cref="GetDatePartExpression"/>）。新增接口钩子时优先在本类提供默认实现。
    /// 公共契约仍由接口定义，外部直接实现接口不受影响。</para>
    /// </summary>
    public abstract class SqlDialectBase : ISqlDialect
    {
        /// <inheritdoc />
        public abstract int MaxParameterCount { get; }

        /// <inheritdoc />
        /// <remarks>默认返回 null（无 FROM 子句限制）；要求所有 SELECT 显式指定表来源的方言（Oracle 23c 之前）重写返回 " FROM DUAL"。</remarks>
        public virtual string ScalarSelectFromClause => null;

        /// <inheritdoc />
        /// <remarks>
        /// 默认返回 null（该方言无会话残留风险或无原生等价机制，如 Oracle 的临时表为全局临时表对象、事务隔离为事务级设置，
        /// SQLite 无会话级可变状态）；会话状态可变的方言（SQL Server/PostgreSQL/MySQL）重写返回各自的复位命令。
        /// </remarks>
        public virtual string SessionStateResetCommand => null;

        /// <inheritdoc />
        /// <remarks>默认为双引号定界（SQLite/PostgreSQL/Oracle）：<c>"{identifier}"</c>，内部双引号双写转义；反引号（MySQL）或方括号（SQL Server）方言重写。</remarks>
        public virtual string QuoteIdentifier(string identifier)
        {
            return $"\"{identifier.Replace("\"", "\"\"")}\"";
        }

        /// <inheritdoc />
        /// <remarks>默认按接口契约处理；不支持架构的方言（如 SQLite）无需重写。</remarks>
        public virtual string QuoteTableName(string schema, string tableName)
        {
            return string.IsNullOrEmpty(schema) || !SupportsSchema
                ? QuoteIdentifier(tableName)
                : $"{QuoteIdentifier(schema)}.{QuoteIdentifier(tableName)}";
        }

        /// <inheritdoc />
        /// <remarks>默认返回 true（多数关系数据库支持架构）；不支持架构的方言（如 SQLite）重写返回 false。</remarks>
        public virtual bool SupportsSchema => true;

        /// <inheritdoc />
        public abstract InsertIdentityMode GetInsertIdentityMode(DbServerInfo serverInfo);

        /// <inheritdoc />
        /// <remarks>默认返回 false（LIMIT/OFFSET 家族：MySQL/PostgreSQL/SQLite）；需版本判定的 OFFSET/FETCH 方言（SQL Server 2012+/Oracle 12c+）重写。</remarks>
        public virtual bool SupportsOffsetFetch(DbServerInfo serverInfo) => false;

        /// <inheritdoc />
        public abstract int MaxRowsPerInsertValues { get; }

        /// <inheritdoc />
        /// <remarks>默认返回 false（网络型数据库）；进程内嵌入式引擎（如 SQLite）重写返回 true。</remarks>
        public virtual bool PreferRowByRowBatchInsert => false;

        /// <inheritdoc />
        public abstract string SelectLastInsertId(string columnName);

        /// <inheritdoc />
        public abstract string GetDatePartExpression(string quotedColumn, DatePart part);

        /// <inheritdoc />
        /// <remarks>默认 <c>AVG({quotedColumn})</c>；SQL Server 的 AVG 继承参数整数类型，重写为 CAST 包裹形式。</remarks>
        public virtual string BuildAverageExpression(string quotedColumn)
        {
            return $"AVG({quotedColumn})";
        }

        /// <inheritdoc />
        /// <remarks>默认返回 false（空串与 NULL 是不同的值）；零长度字符串即 NULL 的方言（Oracle）重写返回 true。</remarks>
        public virtual bool TreatsEmptyStringAsNull => false;
    }
}
