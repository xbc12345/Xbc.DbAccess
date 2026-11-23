using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 实体查询共享辅助：终结执行前的窗口状态校验、包裹前基础 SQL 准备与手写 ORDER BY 检测、排序子句生成与方言窗口 SQL 构建，供查询基类、投影执行器与 keyset 分页共用。
    /// </summary>
    internal static class EntityQuerySqlHelpers
    {
        /// <summary>
        /// 准备包裹/扩展用基础 SQL：TrimSql 去尾分号与空白，并检测手写顶层 ORDER BY（命中抛 <see cref="InvalidOperationException"/>，大小写不敏感）。
        /// </summary>
        /// <param name="sql">SQL 语句。</param>
        /// <returns>去空白且确认不含顶层 ORDER BY 的基础 SQL。</returns>
        internal static string PrepareWrappableBaseSql(string sql)
        {
            // 先校验后清理：词边界检测对首尾空白/分号不敏感，对原始 SQL 检测与清理后检测等价
            if (ContainsOrderByClause(sql))
                throw new InvalidOperationException("聚合/投影包裹要求基础 SQL 不含顶层 ORDER BY（派生表限制），请从 SQL 中移除。");

            return SqlFormatter.TrimSql(sql);
        }

        /// <summary>
        /// 检测 SQL 是否包含 ORDER BY 子句（大小写不敏感，词边界匹配，引用感知）。
        /// <para>单引号字符串字面量、双引号与反引号引用内的「ORDER BY」文本不计入检测
        /// （如 <c>WHERE Memo = '请 ORDER BY 我'</c>），避免把字面量内容误判为顶层排序子句而误拒合法 SQL。</para>
        /// </summary>
        /// <param name="sql">SQL 语句。</param>
        /// <returns>包含 ORDER BY 子句时返回 true。</returns>
        internal static bool ContainsOrderByClause(string sql)
        {
            // 算法：单遍扫描维护引用状态（''/""/`` 双写转义），仅在引用外做词边界与 BY 验证——
            // 定位独立成词的 ORDER（词首、词尾均不紧贴标识符字符），ORDER 与 BY 之间的空白（空格/换行）忽略，
            // 紧随的 BY 同样独立成词即判定命中；词边界规则与 \bORDER\s+BY\b 正则等价
            // （REORDER/ORDERED/ORDERBY/ORDER BYE 均不命中）
            var length = sql.Length;
            char quote = '\0';// 当前所在引用类型：'\0'=引用外；'\''/"'/`'=对应引用内
            var i = 0;

            while (i < length)
            {
                var c = sql[i];
                var consumed = SqlFormatter.ConsumeQuoteChar(sql, i, c, ref quote);
                if (consumed > 0)
                {
                    i += consumed;// 引用上下文：不计入关键词检测
                    continue;
                }

                // 引用外：独立成词的 ORDER 候选（词首、词尾均不紧贴标识符字符）
                if ((c == 'O' || c == 'o')
                    && (i == 0 || !SqlIdentifierHelper.IsIdentifierChar(sql[i - 1]))
                    && i + 5 <= length
                    && string.Compare(sql, i, "ORDER", 0, 5, StringComparison.OrdinalIgnoreCase) == 0
                    && (i + 5 == length || !SqlIdentifierHelper.IsIdentifierChar(sql[i + 5])))
                {
                    var j = i + 5;
                    while (j < length && char.IsWhiteSpace(sql[j]))
                    {
                        j++;
                    }

                    if (j + 2 <= length
                        && string.Compare(sql, j, "BY", 0, 2, StringComparison.OrdinalIgnoreCase) == 0
                        && (j + 2 == length || !SqlIdentifierHelper.IsIdentifierChar(sql[j + 2])))
                        return true;
                }

                i++;
            }

            return false;
        }

        /// <summary>
        /// 终结执行前的窗口状态校验（无副作用）：窗口必须伴随排序（无排序时窗口返回不确定行序），Skip 必须配 Take（单独 OFFSET 的合法性因数据库而异）。
        /// <para>返回是否处于窗口态，调用方以返回值决定是否走窗口构建路径——窗口态判断与校验规则的单一来源，实体查询基类与投影执行器共用。</para>
        /// </summary>
        /// <param name="orderings">查询级排序链。</param>
        /// <param name="skip">窗口跳过行数；null 表示未设置。</param>
        /// <param name="take">窗口取行数；null 表示未设置。</param>
        /// <returns>处于窗口态（Skip/Take 任一设置）时返回 true。</returns>
        internal static bool ValidateWindowState(List<(ColumnMetadata Column, bool Descending)> orderings, int? skip, int? take)
        {
            var hasWindowState = skip.HasValue || take.HasValue;
            if (hasWindowState && orderings.Count == 0)
                throw new InvalidOperationException("窗口（Skip/Take）要求稳定行序，但当前查询未设置排序（OrderBy/OrderByDescending）；正常链式调用不可达此状态，终结端防御校验。");

            if (skip.HasValue && !take.HasValue)
                throw new InvalidOperationException("Skip 之后必须调用 Take 设置取行数，单独的 OFFSET 在不同数据库间合法性不一，且无界窗口的语义不完整。");

            return hasWindowState;
        }

        /// <summary>
        /// 构建方言窗口 SQL：获取服务器版本信息后委托方言构建器包裹源查询（OFFSET/FETCH 与排序子句构成窗口，行序追加与否由调用方决定）。
        /// </summary>
        /// <param name="dbAccess">数据库访问对象（提供服务器信息与方言构建器）。</param>
        /// <param name="querySql">被包裹的源查询 SQL。</param>
        /// <param name="skip">跳过的行数（OFFSET），非负；0 表示从第一行开始。</param>
        /// <param name="take">取回的行数（FETCH），正数。</param>
        /// <param name="orderBy">ORDER BY 子句（由调用方经 <see cref="BuildOrderClause"/> 构建）。</param>
        /// <param name="appendRowOrder">是否保证输出行序（契约见 <see cref="ISqlBuilder.BuildWindowSql"/>）。</param>
        /// <returns>包裹后的窗口 SQL。</returns>
        internal static async Task<string> BuildWindowSqlAsync(
            IDbAccess dbAccess,
            string querySql,
            int skip,
            int take,
            string orderBy,
            bool appendRowOrder)
        {
            var serverInfo = await dbAccess.Factory.GetServerInfoAsync().ConfigureAwait(false);
            return dbAccess.ProviderFactory.SqlBuilder.BuildWindowSql(querySql, skip, take, orderBy, appendRowOrder, serverInfo);
        }

        /// <summary>
        /// 构建排序子句：列名经方言引用，ASC/DESC 为固定方向词，逗号连接多键，不接受字符串拼接。
        /// </summary>
        /// <param name="orderings">查询级排序链，不得为空。</param>
        /// <param name="sqlDialect">SQL 方言（标识符引用）。</param>
        /// <param name="flipDescending">是否翻转全部键方向（keyset Before 方向按翻转序取页，取回后内存反转为正向）。</param>
        /// <returns>排序子句（如 <c>"Age" DESC, "Name" ASC</c>）。</returns>
        internal static string BuildOrderClause(List<(ColumnMetadata Column, bool Descending)> orderings, ISqlDialect sqlDialect, bool flipDescending = false)
        {
            // 空链静默返回空串会下游漂移（窗口入口的 orderBy 守卫以费解的消息拦截、纯排序路径拼出裸 "ORDER BY"）；
            // 当前调用点均有前置保证（窗口态经 ValidateWindowState 强制排序、纯排序分支以 Count > 0 进入），此为共享构建器的自防御
            if (orderings == null || orderings.Count == 0)
                throw new ArgumentException("排序链为空，无法构建 ORDER BY 子句。", nameof(orderings));

            var parts = new string[orderings.Count];
            for (int i = 0; i < orderings.Count; i++)
            {
                var descending = orderings[i].Descending != flipDescending;
                parts[i] = sqlDialect.QuoteIdentifier(orderings[i].Column.ColumnName) + (descending ? " DESC" : " ASC");
            }

            return string.Join(", ", parts);
        }
    }
}
