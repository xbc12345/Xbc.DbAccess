namespace Xbc.DbAccess
{
    /// <summary>
    /// INSERT/Upsert 语句中自增列的参与方式上下文：默认值时由数据库生成并经输出子句取回；
    /// 显式值时进入列清单，需要显式插入标识列的方言（如 SQL Server 的 IDENTITY_INSERT）据此生成包裹语句。
    /// </summary>
    public sealed class InsertIdentityContext
    {
        private InsertIdentityContext(string identityColumn, string outputParameterName, bool isExplicit)
        {
            IdentityColumn = identityColumn;
            OutputParameterName = outputParameterName;
            IsExplicit = isExplicit;
        }

        /// <summary>
        /// 构建取回上下文：自增列为默认值，由数据库生成并经输出子句（OUTPUT/RETURNING/RETURNING INTO）取回。
        /// </summary>
        /// <param name="identityColumn">标识列名（已引用，如 <c>"Id"</c>）。</param>
        /// <param name="outputParameterName">自增 ID 输出参数名（含前缀，如 <c>:out_Id</c>），仅 RETURNING INTO 方言（Oracle）需要，其余方言可为 null。</param>
        /// <returns></returns>
        public static InsertIdentityContext ForRetrieval(string identityColumn, string outputParameterName = null)
            => new InsertIdentityContext(identityColumn, outputParameterName, isExplicit: false);

        /// <summary>
        /// 构建显式写入上下文：自增列带显式值进入列清单，方言据此生成 IDENTITY_INSERT 类包裹语句。
        /// </summary>
        /// <param name="identityColumn">标识列名（已引用，且在列清单中）。</param>
        /// <returns></returns>
        public static InsertIdentityContext ForExplicit(string identityColumn)
            => new InsertIdentityContext(identityColumn, null, isExplicit: true);

        /// <summary>
        /// 标识列名（已引用）。
        /// </summary>
        public string IdentityColumn { get; }

        /// <summary>
        /// 自增 ID 输出参数名（含前缀），用于 RETURNING INTO 子句的输出占位符；结果集方式（OUTPUT/RETURNING）与显式写入方式不使用。
        /// </summary>
        public string OutputParameterName { get; }

        /// <summary>
        /// 是否带显式值写入（列在清单中）：true 时需要显式插入标识列的方言生成包裹语句；false 时由数据库生成并取回。
        /// </summary>
        public bool IsExplicit { get; }
    }
}
