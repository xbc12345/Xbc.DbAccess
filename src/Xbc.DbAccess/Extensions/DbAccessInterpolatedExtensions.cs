using System.Data;
using System.Data.Common;
using System.Text;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 为 <see cref="IDbAccess"/> 提供 FormattableString 插值参数化执行扩展（EF FromSqlInterpolated 同款语义）：
    /// 插值孔在编译期即与 SQL 骨架结构分离（编译器产出 Format 骨架 + 值数组，不拼最终文本），
    /// 孔一律转为数据库参数、值永不进入 SQL 文本——注入面被编译器产品形态封死而非靠校验。
    /// <para>与 string 版裸 SQL 入口的信任等级不同（string = 可信文本透传，本类 = 结构分离参数化），
    /// 故以独立方法名区分而非同名重载：C# 规范规定插值字符串优先绑定 string 重载（对 string 的转换优于
    /// 对 FormattableString），同名重载既无法捕获现有调用点，也会让两个信任等级混在一个方法名下。</para>
    /// <para>方法只接受 FormattableString 参数（string 无隐式转换）：调用点必须内联 $"" 插值，
    /// 先物化字符串再传入会编译失败——最常见的误用形态被编译期封死。</para>
    /// <para>语义约束：插值孔只能是值（标量/集合/null）；孔位于字符串字面量内时显式拒绝
    /// （参数引用写入字面量不会绑定值，如 '%@fs_0%' 只是普通文本），应把含通配符的整个模式作为孔值；
    /// 孔也不能承载列名/表名等标识符语义（参数无法表达标识符，运行期 SQL 语法错误暴露）。
    /// 集合孔经参数转换管道自动展开为 IN 参数列表（与字典参数形态同机制）。</para>
    /// </summary>
    public static class DbAccessInterpolatedExtensions
    {
        /// <summary>插值参数基名：孔编号以此为前缀（fs_0、fs_1…），经方言 ParameterBuilder 补齐前缀后写入 SQL 骨架。</summary>
        private const string InterpolatedParameterBaseName = "fs";

        #region ExecuteXxx
        /// <summary>
        /// 异步执行插值参数化的非查询 SQL，返回受影响的行数。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="interpolatedSql">插值 SQL（调用点内联 $"" 书写，插值孔转为数据库参数）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>受影响的行数。</returns>
        public static Task<int> ExecuteNonQueryInterpolatedAsync(this IDbAccess dbAccess, FormattableString interpolatedSql, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            Guard.ThrowIfNull(interpolatedSql, nameof(interpolatedSql));

            var (sql, parameters) = BuildInterpolated(dbAccess, interpolatedSql);
            return dbAccess.ExecuteNonQueryAsync(sql, parameters, cancellationToken);
        }

        /// <summary>
        /// 异步执行插值参数化的查询 SQL，返回结果集中第一行第一列的值。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="interpolatedSql">插值 SQL（调用点内联 $"" 书写，插值孔转为数据库参数）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>第一行第一列的值。</returns>
        public static Task<object> ExecuteScalarInterpolatedAsync(this IDbAccess dbAccess, FormattableString interpolatedSql, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            Guard.ThrowIfNull(interpolatedSql, nameof(interpolatedSql));

            var (sql, parameters) = BuildInterpolated(dbAccess, interpolatedSql);
            return dbAccess.ExecuteScalarAsync(sql, parameters, cancellationToken);
        }

        /// <summary>
        /// 异步执行插值参数化的查询 SQL，返回结果集中第一行第一列的值并转换为指定类型。
        /// </summary>
        /// <typeparam name="T">返回值类型。</typeparam>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="interpolatedSql">插值 SQL（调用点内联 $"" 书写，插值孔转为数据库参数）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>转换后的第一行第一列的值。</returns>
        public static async Task<T> ExecuteScalarInterpolatedAsync<T>(this IDbAccess dbAccess, FormattableString interpolatedSql, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            Guard.ThrowIfNull(interpolatedSql, nameof(interpolatedSql));

            var (sql, parameters) = BuildInterpolated(dbAccess, interpolatedSql);
            return await dbAccess.ExecuteScalarAsync<T>(sql, parameters, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 异步执行插值参数化的查询 SQL，返回 <see cref="DbDataReader"/> 实例。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="interpolatedSql">插值 SQL（调用点内联 $"" 书写，插值孔转为数据库参数）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>数据读取器。</returns>
        public static Task<DbDataReader> ExecuteReaderInterpolatedAsync(this IDbAccess dbAccess, FormattableString interpolatedSql, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            Guard.ThrowIfNull(interpolatedSql, nameof(interpolatedSql));

            var (sql, parameters) = BuildInterpolated(dbAccess, interpolatedSql);
            return dbAccess.ExecuteReaderAsync(sql, parameters, cancellationToken);
        }

        /// <summary>
        /// 异步执行插值参数化的查询 SQL，返回 <see cref="DataSet"/> 实例。
        /// </summary>
        /// <param name="dbAccess">数据库访问接口。</param>
        /// <param name="interpolatedSql">插值 SQL（调用点内联 $"" 书写，插值孔转为数据库参数）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>数据集。</returns>
        public static Task<DataSet> ExecuteDataSetInterpolatedAsync(this IDbAccess dbAccess, FormattableString interpolatedSql, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            Guard.ThrowIfNull(interpolatedSql, nameof(interpolatedSql));

            var (sql, parameters) = BuildInterpolated(dbAccess, interpolatedSql);
            return dbAccess.ExecuteDataSetAsync(sql, parameters, cancellationToken);
        }
        #endregion

        #region 骨架解析与参数构建
        /// <summary>
        /// 解析插值 SQL 骨架：Format 中的 <c>{N}</c> 孔替换为方言前缀参数名（fs_N），孔值编入参数字典，
        /// 返回的字典经 <see cref="DbParameterConversionExtensions.ConvertToDbParameters"/> 现有管道转换
        /// （名称清洗、方言前缀、集合展开为 IN 参数列表均复用既有机制）。
        /// <para>Format 是 FormattableString 的转义骨架：源码字面花括号在骨架中只会以 <c>{{</c> 与 <c>}}</c>
        /// 的转义形式出现——即骨架中未转义的 <c>{N}</c> 无歧义地就是孔（含 JSON 字面量的
        /// <c>{{"a":1}}</c> 不会被误判），花括号解析无需 SQL 词法参与；但 SQL 引号仍是源码原文，
        /// 孔是否落在字面量内须经引用状态机判定（字面量内的孔显式拒绝）。</para>
        /// </summary>
        /// <param name="dbAccess">数据库访问接口（取方言参数构建器的前缀约定）。</param>
        /// <param name="interpolatedSql">插值 SQL。</param>
        /// <returns>替换后的 SQL 文本与参数字典（无孔时字典为 null）。</returns>
        /// <exception cref="ArgumentException">孔编号超出值数组（骨架与值不一致）或骨架非法。</exception>
        /// <exception cref="NotSupportedException">孔位于 SQL 字符串字面量内。</exception>
        internal static (string Sql, Dictionary<string, object> Parameters) BuildInterpolated(IDbAccess dbAccess, FormattableString interpolatedSql)
        {
            Guard.ThrowIfNull(dbAccess, nameof(dbAccess));
            Guard.ThrowIfNull(interpolatedSql, nameof(interpolatedSql));

            var format = interpolatedSql.Format;
            var args = interpolatedSql.GetArguments();

            // 无孔形态：ToString 即最终文本——string.Format 语义下 {{ 与 }} 转义仍会还原（0 参时无值可格式化，
            // 文化无关），转义语义委托 BCL 不维护手写扫描器；代价仅一次字符串分配，对 SQL 执行量级可忽略
            if (args.Length == 0)
                return (interpolatedSql.ToString(), null);

            var parameterBuilder = dbAccess.ProviderFactory.ParameterBuilder;
            var sb = new StringBuilder(format.Length + args.Length * 8);
            var parameters = new Dictionary<string, object>(args.Length);
            char quote = '\0';// 当前所在引用类型：'\0'=引用外；'\''/"'/`'=对应引用内

            var i = 0;
            while (i < format.Length)
            {
                var c = format[i];

                // {{ 与 }} 是 FormattableString 骨架级转义（编译器对源码字面花括号的编码），与 SQL 引用无关——
                // 字面量内同样必须反转义（如 JSON 字面量 '{{"a":1}}' 应还原为 {"a":1}），故先于引用状态处理
                if (c == '{' && i + 1 < format.Length && format[i + 1] == '{')
                {
                    sb.Append('{');
                    i += 2;
                    continue;
                }

                if (c == '}' && i + 1 < format.Length && format[i + 1] == '}')
                {
                    sb.Append('}');
                    i += 2;
                    continue;
                }

                if (c == '{')
                {
                    // 孔必须位于引用外：参数引用写入字面量不会绑定值（'%@fs_0%' 是普通文本而非参数），
                    // 静默错数据不可接受，显式拒绝并引导把含通配符的整个模式作为孔值
                    if (quote != '\0')
                        throw new NotSupportedException($"插值孔位于 SQL 字符串字面量内，参数引用无法在字面量内生效。请把整个字面量（含 % 等通配符）作为插值值，如 \"... LIKE " + "{pattern}" + "\"（通配符并入值）。");

                    // {N} / {N,align} / {N:format} → 参数占位符（对齐与格式说明符无参数化语义，只跳过不参与文本）
                    var scan = i + 1;
                    var numberStart = scan;

                    while (scan < format.Length && char.IsDigit(format[scan]))
                    {
                        scan++;
                    }

                    if (scan == numberStart)
                    {
                        // 非数字的裸 {（编译器不会产出，手搭骨架宽松处理）：按字面拷贝
                        sb.Append('{');
                        i++;
                        continue;
                    }

                    var holeNumber = int.Parse(format.Substring(numberStart, scan - numberStart));
                    if (holeNumber >= args.Length)
                        throw new ArgumentException($"插值孔编号 {holeNumber} 超出值数组长度（{args.Length}），FormattableString 骨架与值数组不一致。", nameof(interpolatedSql));

                    var baseName = $"{InterpolatedParameterBaseName}_{holeNumber}";
                    sb.Append(parameterBuilder.GetParameterName(baseName));
                    parameters[baseName] = args[holeNumber];

                    // 跳过对齐/格式说明符段至闭括号（说明符内不含花括号，取首个 } 即闭）
                    while (scan < format.Length && format[scan] != '}')
                    {
                        scan++;
                    }

                    if (scan >= format.Length)
                        throw new ArgumentException("插值孔缺少闭花括号，FormattableString 骨架非法。", nameof(interpolatedSql));

                    i = scan + 1;
                    continue;
                }

                // 引用状态推进（ConsumeQuoteChar 契约：引用打开后任意字符均返回 ≥1 并原样消费，
                // 引用内的普通字符到此即被拷贝；同引号双写转义一并消费，引用状态由调用方跨循环持有）
                var consumed = SqlFormatter.ConsumeQuoteChar(format, i, c, ref quote);
                if (consumed > 0)
                {
                    sb.Append(format, i, consumed);
                    i += consumed;
                    continue;
                }

                sb.Append(c);
                i++;
            }

            return (sb.ToString(), parameters);
        }
        #endregion
    }
}
