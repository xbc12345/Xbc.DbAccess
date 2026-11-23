namespace Xbc.DbAccess
{
    /// <summary>
    /// INSERT 语句自增 ID 的取回方式，由方言按引擎能力（结合版本）判定，见 <see cref="ISqlDialect.GetInsertIdentityMode"/>。
    /// </summary>
    public enum InsertIdentityMode
    {
        /// <summary>OUTPUT/RETURNING 子句返回结果集（SQL Server/PostgreSQL/SQLite），插入后以 ExecuteScalar 读取。</summary>
        OutputClause,

        /// <summary>RETURNING INTO 输出参数回传（Oracle），自增 ID 由提供程序在执行后写回输出参数，从参数值读取。</summary>
        ReturningInto,

        /// <summary>INSERT + SELECT LAST INSERT ID 两条语句（MySQL 等），需在同一连接的事务中执行。</summary>
        SelectLastInsertId
    }
}
