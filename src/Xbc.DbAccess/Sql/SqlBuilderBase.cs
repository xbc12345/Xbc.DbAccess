using System.Text;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// SQL 构建器基类，提供构建 SQL 语句的默认实现。
    /// </summary>
    public abstract class SqlBuilderBase : ISqlBuilder
    {
        /// <summary>
        /// SQL 方言。
        /// </summary>
        public abstract ISqlDialect SqlDialect { get; }

        /// <summary>
        /// 构建 SELECT 查询语句。
        /// </summary>
        /// <param name="tableName">表名。</param>
        /// <param name="columns">列名列表，若为 null 或空列表则查询所有列。</param>
        /// <param name="whereClause">WHERE 子句。</param>
        /// <param name="orderBy">ORDER BY 子句，直接拼接进 SQL，禁止包含用户输入（防注入）。</param>
        /// <param name="distinct">是否使用 DISTINCT 关键字。</param>
        /// <returns></returns>
        public virtual string BuildSelectSql(string tableName,
            List<string> columns = null,
            string whereClause = null,
            string orderBy = null,
            bool distinct = false)
        {
            Guard.ThrowIfNullOrWhiteSpace(tableName, nameof(tableName));

            if (columns == null || columns.Count == 0)
                columns = new List<string> { "*" };

            var sqlBuilder = new StringBuilder();

            if (!distinct)
                sqlBuilder.AppendLine("SELECT");
            else
                sqlBuilder.AppendLine("SELECT DISTINCT");

            sqlBuilder.AppendLine($"    {string.Join(", ", columns)}");
            sqlBuilder.AppendLine("FROM");
            sqlBuilder.AppendLine($"    {tableName}");

            if (!string.IsNullOrEmpty(whereClause))
            {
                sqlBuilder.AppendLine("WHERE");
                sqlBuilder.AppendLine($"    {whereClause}");
            }

            // 直接拼接进 SQL，调用方须保证不含用户输入（防注入）
            if (!string.IsNullOrEmpty(orderBy))
            {
                sqlBuilder.AppendLine("ORDER BY");
                sqlBuilder.AppendLine($"    {orderBy}");
            }

            return sqlBuilder.ToString();
        }

        /// <summary>
        /// 构建插入语句。基类负责参数校验与 INSERT 主体拼接，自增 ID 输出子句由派生类按位置重写：
        /// 列清单与 VALUES 之间的（SQL Server 的 OUTPUT）重写 <see cref="BuildInsertOutputClauseBeforeValues"/>，
        /// 语句尾部的（RETURNING/RETURNING INTO）重写 <see cref="BuildInsertOutputClauseAfterValues"/>。
        /// </summary>
        /// <param name="tableName">表名。</param>
        /// <param name="columns">列名列表。</param>
        /// <param name="values">值列表。</param>
        /// <param name="identity">自增列参与方式，null 表示无自增列参与；默认值时由数据库生成并取回（基类仅处理取回形态），显式值时进入列清单并生成 IDENTITY_INSERT 类包裹语句。</param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"><paramref name="identity"/> 为取回形态且派生类未重写任何输出子句钩子时抛出。</exception>
        public virtual string BuildInsertSql(string tableName, List<string> columns, List<string> values, InsertIdentityContext identity = null)
        {
            Guard.ThrowIfNullOrWhiteSpace(tableName, nameof(tableName));
            Guard.ThrowIfNullOrEmpty(columns, nameof(columns));
            Guard.ThrowIfNullOrEmpty(values, nameof(values));

            if (values.Count != columns.Count)
                throw new ArgumentException("参数 values 的长度与 columns 的长度不匹配。", nameof(values));

            // 先探询列清单与 VALUES 之间的位置钩子（仅 SQL Server 的 OUTPUT），未命中再走语句尾部钩子（RETURNING 家族）
            string outputClause = null;
            bool placeBeforeValues = false;

            // 显式写入形态无取回子句（值按列清单写入），仅取回形态探询输出子句钩子
            if (identity != null && !identity.IsExplicit)
            {
                outputClause = BuildInsertOutputClauseBeforeValues(identity);
                if (outputClause != null)
                    placeBeforeValues = true;
                else
                    outputClause = BuildInsertOutputClauseAfterValues(identity);
            }

            var sqlBuilder = new StringBuilder();
            sqlBuilder.AppendLine($"INSERT INTO {tableName}");
            sqlBuilder.AppendLine($"    ({string.Join(", ", columns)})");

            if (placeBeforeValues)
                sqlBuilder.AppendLine(outputClause);

            sqlBuilder.AppendLine("VALUES");
            sqlBuilder.AppendLine($"    ({string.Join(", ", values)})");

            if (outputClause != null && !placeBeforeValues)
                sqlBuilder.AppendLine(outputClause);

            return sqlBuilder.ToString();
        }

        /// <summary>
        /// 构建位于列清单与 VALUES 之间的自增 ID 输出子句（SQL Server 的 <c>OUTPUT INSERTED.列名</c>）。
        /// <para>默认返回 null 表示方言的输出子句不在此位置，基类转而调用 <see cref="BuildInsertOutputClauseAfterValues"/>。</para>
        /// </summary>
        /// <param name="identity">自增 ID 取回上下文。</param>
        /// <returns>输出子句 SQL（不含前后空白），不在此位置时返回 null。</returns>
        protected virtual string BuildInsertOutputClauseBeforeValues(InsertIdentityContext identity) => null;

        /// <summary>
        /// 构建位于语句尾部的自增 ID 输出子句（RETURNING/RETURNING INTO），由支持对应取回方式的方言重写
        /// （与 <see cref="ISqlDialect.GetInsertIdentityMode"/> 返回的方式一一对应）。
        /// <para>默认抛出 <see cref="NotSupportedException"/>。</para>
        /// </summary>
        /// <param name="identity">自增 ID 取回上下文。</param>
        /// <returns>输出子句 SQL（不含前后空白）。</returns>
        protected virtual string BuildInsertOutputClauseAfterValues(InsertIdentityContext identity)
        {
            throw new NotSupportedException(
                $"SQL 构建器 '{GetType()}' 未实现 INSERT 语句的自增 ID 输出子句（OUTPUT/RETURNING/RETURNING INTO），" +
                $"请重写 {nameof(BuildInsertOutputClauseBeforeValues)} 或 {nameof(BuildInsertOutputClauseAfterValues)}，" +
                $"或检查方言的 {nameof(ISqlDialect.GetInsertIdentityMode)} 配置。");
        }

        /// <summary>
        /// 构建 Upsert 语句（存在则更新，不存在则插入），校验后交由 <see cref="BuildUpsertSqlCore"/> 生成方言特定 SQL。
        /// </summary>
        /// <param name="tableName">表名。</param>
        /// <param name="insertColumns">插入列名列表（已引用，含主键；自增列默认值时排除，显式值时包含）。</param>
        /// <param name="insertValues">插入值列表（参数占位符，与 insertColumns 平行）。</param>
        /// <param name="conflictKey">冲突键列名（已引用，主键列名）。不在 insertColumns 中时，MERGE 类方言以 NULL 作为源列值以走插入路径。</param>
        /// <param name="identity">自增列参与方式，null 表示无自增列参与；取回形态仅对支持 OUTPUT/RETURNING 的方言构造，无法配合的方言（如 Oracle 的 MERGE）由调用方传 null，显式值时生成 IDENTITY_INSERT 类包裹语句。</param>
        /// <returns></returns>
        public string BuildUpsertSql(string tableName, List<string> insertColumns, List<string> insertValues, string conflictKey, InsertIdentityContext identity = null)
        {
            Guard.ThrowIfNullOrWhiteSpace(tableName, nameof(tableName));
            Guard.ThrowIfNullOrEmpty(insertColumns, nameof(insertColumns));
            Guard.ThrowIfNullOrEmpty(insertValues, nameof(insertValues));
            Guard.ThrowIfNullOrWhiteSpace(conflictKey, nameof(conflictKey));

            if (insertValues.Count != insertColumns.Count)
                throw new ArgumentException("参数 insertValues 的长度与 insertColumns 的长度不匹配。", nameof(insertValues));

            // 更新列 = 插入列减冲突键与显式写入的自增列：统一在入口推导一次，派生类 Core 钩子直接使用（不再各自重复推导）
            var updateColumns = GetUpsertUpdateColumns(insertColumns, conflictKey, identity);
            return BuildUpsertSqlCore(tableName, insertColumns, insertValues, conflictKey, updateColumns, identity);
        }

        /// <summary>
        /// 推导 Upsert 的更新列：插入列减冲突键与显式写入的自增列（SQL Server 的 MERGE 不允许更新标识列，即使 IDENTITY_INSERT 已开启）。
        /// </summary>
        /// <param name="insertColumns">插入列名列表（已引用）。</param>
        /// <param name="conflictKey">冲突键列名（已引用）。</param>
        /// <param name="identity">自增列参与方式，null 或取回形态时无自增列进入排除集。</param>
        /// <returns>更新列名列表（已引用）。</returns>
        /// <exception cref="ArgumentException">除冲突键与显式写入的自增列外没有可更新的列时抛出。</exception>
        private static List<string> GetUpsertUpdateColumns(List<string> insertColumns, string conflictKey, InsertIdentityContext identity)
        {
            // 显式写入的自增列参与插入但不参与更新：SQL Server 的 MERGE 更新标识列会报错（IDENTITY_INSERT 只放开插入）
            var identityColumn = identity != null && identity.IsExplicit ? identity.IdentityColumn : null;

            var updateColumns = new List<string>(insertColumns.Count);
            foreach (var column in insertColumns)
            {
                if (column != conflictKey && column != identityColumn)
                    updateColumns.Add(column);
            }

            if (updateColumns.Count == 0)
                throw new ArgumentException("除冲突键与显式写入的自增列外没有可更新的列，无法构建 Upsert 语句。", nameof(insertColumns));

            return updateColumns;
        }

        /// <summary>
        /// 构建 Upsert 语句的方言特定 SQL（ON CONFLICT/ON DUPLICATE KEY UPDATE/MERGE），由支持 Upsert 的方言重写。
        /// <para>默认抛出 <see cref="NotSupportedException"/>。</para>
        /// </summary>
        /// <param name="tableName">表名。</param>
        /// <param name="insertColumns">插入列名列表（已引用，含主键；自增列默认值时排除，显式值时包含）。</param>
        /// <param name="insertValues">插入值列表（参数占位符，与 insertColumns 平行）。</param>
        /// <param name="conflictKey">冲突键列名（已引用，主键列名）。不在 insertColumns 中时，以 NULL 作为源列值以走插入路径。</param>
        /// <param name="updateColumns">更新列名列表（已引用，由入口统一推导传入）。</param>
        /// <param name="identity">自增列参与方式，null 表示无自增列参与；取回形态仅对支持 OUTPUT/RETURNING 的方言构造，无法配合的方言（如 Oracle 的 MERGE）由调用方传 null，显式值时生成 IDENTITY_INSERT 类包裹语句。</param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException">派生类未重写本钩子时抛出。</exception>
        protected virtual string BuildUpsertSqlCore(string tableName, List<string> insertColumns, List<string> insertValues, string conflictKey, List<string> updateColumns, InsertIdentityContext identity = null)
        {
            throw new NotSupportedException($"SQL 构建器 '{GetType()}' 未实现 Upsert 语句的构建，请重写 {nameof(BuildUpsertSqlCore)}。");
        }

        /// <summary>
        /// 构建窗口查询语句（OFFSET/FETCH 语义的行窗口截取）：校验参数后交由 <see cref="BuildWindowSqlCore"/> 生成方言特定 SQL。
        /// </summary>
        /// <param name="querySql">原始查询 SQL 语句，不得自带顶层 ORDER BY（排序统一由 orderBy 参数传入）。</param>
        /// <param name="skip">窗口起点行号（OFFSET），非负；0 表示从第一行开始（分页第一页的常规值，SQL 语义合法）。</param>
        /// <param name="take">取回的行数（fetch），正数；0 的空窗口无实用语义，单独 OFFSET 在方言间不可移植（API 层已保证 Skip 必配 Take）。</param>
        /// <param name="orderBy">ORDER BY 子句，直接拼接进 SQL，禁止包含用户输入（防注入）。</param>
        /// <param name="appendRowOrder">是否保证输出行序：true 为终结查询，false 供聚合与投影等包裹场景（契约见 <see cref="ISqlBuilder.BuildWindowSql"/>）。</param>
        /// <param name="serverInfo">服务器信息。</param>
        /// <returns></returns>
        public string BuildWindowSql(string querySql,
            int skip,
            int take,
            string orderBy,
            bool appendRowOrder = true,
            DbServerInfo serverInfo = null)
        {
            Guard.ThrowIfNullOrWhiteSpace(querySql, nameof(querySql));
            Guard.ThrowIfNullOrWhiteSpace(orderBy, nameof(orderBy));

            // skip=0 合法（窗口起点，分页第一页的常规值）；Skip(0) 的无操作拒绝在 API 层（SkipCore），两层的 0 语义不同
            if (skip < 0)
                throw new ArgumentOutOfRangeException(nameof(skip), $"参数 '{nameof(skip)}' 不能为负数（实际值：{skip}）。");

            if (take < 1)
                throw new ArgumentOutOfRangeException(nameof(take), take, "窗口行数必须为正数。");

            querySql = SqlFormatter.TrimSql(querySql);
            return BuildWindowSqlCore(querySql, skip, take, orderBy, appendRowOrder, serverInfo);
        }

        /// <summary>
        /// 构建方言特定的窗口查询语句（分页与 Skip/Take 共享的核心）。
        /// 默认按 <see cref="ISqlDialect.SupportsOffsetFetch"/> 选择 OFFSET/FETCH 或 ROW_NUMBER 兼容语法；
        /// 使用 LIMIT/OFFSET 的方言（MySQL/PostgreSQL/SQLite）应重写本方法（其形态与行序模式无关，见 <paramref name="appendRowOrder"/>）。
        /// <paramref name="querySql"/> 不得自带顶层 ORDER BY，否则与本方法的排序冲突。
        /// </summary>
        /// <param name="querySql">原始查询 SQL 语句（已修剪），不得自带顶层 ORDER BY。</param>
        /// <param name="offset">跳过的行数（分页换算或 Skip 直传）。</param>
        /// <param name="fetch">取回的行数（分页为 pageSize，Skip/Take 为查询级 Take）。</param>
        /// <param name="orderBy">ORDER BY 子句，直接拼接进 SQL，禁止包含用户输入（防注入）。</param>
        /// <param name="appendRowOrder">是否保证输出行序：true 时实现须兑现行序契约（如追加尾部 ORDER BY）；false 为包裹场景，
        /// 实现须保证产物可被派生表/EXISTS 安全包裹（如 SQL Server 的 CTE 形态不可包裹，应回退嵌套形态）。</param>
        /// <param name="serverInfo">服务器信息。</param>
        /// <returns></returns>
        protected virtual string BuildWindowSqlCore(string querySql,
            int offset,
            int fetch,
            string orderBy,
            bool appendRowOrder,
            DbServerInfo serverInfo = null)
        {
            if (SqlDialect.SupportsOffsetFetch(serverInfo))
                return BuildOffsetFetchWindowSql(querySql, offset, fetch, orderBy);

            return BuildRowNumberWindowSql(querySql, offset, fetch, orderBy, appendRowOrder);
        }

        /// <summary>
        /// SQL 标准 OFFSET/FETCH 窗口形态（SQL Server 2012+/Oracle 12c+）；Oracle 的派生表别名不允许 AS，直接跟别名。
        /// ORDER BY 为 OFFSET/FETCH 的语法必需成分，在派生表内合法。
        /// </summary>
        private string BuildOffsetFetchWindowSql(string querySql, int offset, int fetch, string orderBy)
        {
            var sqlBuilder = new StringBuilder();
            sqlBuilder.AppendLine("SELECT * FROM (");
            sqlBuilder.AppendLine(SqlFormatter.IndentLines(querySql));
            sqlBuilder.AppendLine($") {SqlIdentifierHelper.Generate("q")}");
            sqlBuilder.AppendLine($"ORDER BY {orderBy}");
            sqlBuilder.AppendLine($"OFFSET {offset} ROWS");
            sqlBuilder.AppendLine($"FETCH NEXT {fetch} ROWS ONLY");
            return sqlBuilder.ToString();
        }

        /// <summary>
        /// ROW_NUMBER 兼容窗口形态（SQL Server 2008/Oracle 11g 及以下，及未重写窗口构建的方言）。
        /// 嵌套派生表结构可被再次包裹；<paramref name="appendRowOrder"/> 为 false 时省略尾部 ORDER BY（包裹场景行序由外层保证）。
        /// </summary>
        private string BuildRowNumberWindowSql(string querySql, int offset, int fetch, string orderBy, bool appendRowOrder)
        {
            long endRow = offset + fetch;// 计算结束行号

            // 别名带随机段防撞：实体列名恰为 q/rn 时固定别名会与派生表列名重复
            var innerAlias = SqlIdentifierHelper.Generate("q");
            var outerAlias = SqlIdentifierHelper.Generate("q");
            var rowNumberAlias = SqlIdentifierHelper.Generate("rn");

            var sqlBuilder = new StringBuilder();
            sqlBuilder.AppendLine("SELECT * FROM (");
            sqlBuilder.AppendLine($"    SELECT {innerAlias}.*, ROW_NUMBER() OVER (ORDER BY {orderBy}) AS {rowNumberAlias}");
            sqlBuilder.AppendLine($"    FROM (");
            sqlBuilder.AppendLine(SqlFormatter.IndentLines(querySql, 2));
            sqlBuilder.AppendLine($"    ) AS {innerAlias}");
            sqlBuilder.AppendLine($") {outerAlias}");
            sqlBuilder.AppendLine($"WHERE {rowNumberAlias} > {offset} AND {rowNumberAlias} <= {endRow}");

            // 过滤后的行集不显式 ORDER BY 则行序无保证（优化器不保证保留 ROW_NUMBER 的计算序），须与 LIMIT/OFFSET 路径一致
            if (appendRowOrder)
                sqlBuilder.AppendLine($"ORDER BY {orderBy}");

            return sqlBuilder.ToString();
        }
    }
}
