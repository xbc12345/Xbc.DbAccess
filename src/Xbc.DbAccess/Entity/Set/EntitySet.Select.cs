using System.Data.Common;
using System.Linq.Expressions;
using Xbc.DbAccess.Common;

namespace Xbc.DbAccess.Entity
{
    /// <content>
    /// 查询操作及主键查询 SQL 构建（按主键查找与按谓词查询，导航属性经 Where 组合查询侧的 Include/ThenInclude 加载）。
    /// </content>
    internal sealed partial class EntitySet<T>
    {
        #region 按主键查找
        /// <summary>
        /// 异步根据主键查找实体（不加载导航属性）。
        /// <para>需要加载导航属性时使用 <see cref="Where"/> 组合：
        /// <c>set.Where(u => u.Id == key).Include(u => u.Orders).SingleOrDefaultAsync()</c>，
        /// 能力与 FindAsync 一致且支持多级导航（ThenInclude）与更多取数形态。</para>
        /// </summary>
        /// <param name="key">主键值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>找到的实体；不存在时返回 null。</returns>
        public Task<T> FindAsync(object key, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(key, nameof(key));
            ThrowIfNoPrimaryKey();

            var sql = BuildFindByPrimaryKeySql(key, out var parameters);
            return dbAccess.FromSql<T>(sql, parameters).SingleOrDefaultAsync(cancellationToken);
        }
        #endregion

        #region 转 IEntityQuery<T>
        /// <summary>
        /// 按谓词构建实体查询：谓词经 <see cref="WhereClauseTranslator"/> 翻译为 WHERE 片段，生成全列 SELECT，
        /// 返回可继续组合的查询对象（Include 加载导航属性、First/ToList 取数、分页等）。
        /// <para>谓词能力与条件 UPDATE/DELETE 一致，闭包变量一律参数化。</para>
        /// </summary>
        /// <param name="predicate">过滤条件表达式。</param>
        /// <returns>带 WHERE 条件的实体查询对象。</returns>
        public IEntityQuery<T> Where(Expression<Func<T, bool>> predicate)
        {
            Guard.ThrowIfNull(predicate, nameof(predicate));

            var translation = TranslatePredicate(predicate, new ParameterNameAllocator());
            var sql = sqlBuilder.BuildSelectSql(quotedTableName, null, translation.WhereClause);// columns: null 表示查询所有列
            return dbAccess.FromSql<T>(sql, translation.Parameters.ToArray());
        }

        /// <summary>
        /// 将实体集合投影为无条件的全表查询链（视角转换，不产生查询）：返回可继续组合的查询对象。
        /// </summary>
        /// <returns>全表实体查询对象。</returns>
        public IEntityQuery<T> AsQuery()
        {
            // columns 与 whereClause 均为 null：全列、无过滤
            var sql = sqlBuilder.BuildSelectSql(quotedTableName, null, null);
            return dbAccess.FromSql<T>(sql, Array.Empty<DbParameter>());
        }
        #endregion

        #region SELECT 语句
        /// <summary>
        /// 构建根据主键查询实体的 SQL 语句。
        /// </summary>
        /// <param name="pkValue">主键值。</param>
        /// <param name="parameters">参数数组。</param>
        /// <returns></returns>
        private string BuildFindByPrimaryKeySql(object pkValue, out DbParameter[] parameters)
        {
            var pk = entityMetadata.PrimaryKey;
            pkValue = ValidatePrimaryKeyValue(pkValue, pk);

            // 主键参数与其他语句构建统一走分配器分配（内部收口清理与防撞）：
            // 单参数当前无撞名面，但统一调用形式后语句参数演化时防撞契约天然成立，不依赖「单参数」注释约定
            var allocator = new ParameterNameAllocator();
            var pkParameter = parameterBuilder.CreateParameter(allocator.Allocate(pk.ColumnName), pkValue, pk.DbType);
            var pkEqualsClause = $"{sqlDialect.QuoteIdentifier(pk.ColumnName)} = {pkParameter.ParameterName}";
            parameters = new[] { pkParameter };

            // columns: null 表示查询所有列
            var sql = sqlBuilder.BuildSelectSql(quotedTableName, null, pkEqualsClause);
            return sql;
        }

        /// <summary>
        /// 校验主键值类型与主键属性类型兼容：类型匹配直接返回；数值主键接受可转换值（如传 long 而主键为 int）；否则抛 <see cref="ArgumentException"/>。
        /// </summary>
        /// <param name="pkValue">调用方传入的主键值，不能为 null。</param>
        /// <param name="pk">主键列元数据。</param>
        /// <returns>与主键属性类型兼容的主键值。</returns>
        private static object ValidatePrimaryKeyValue(object pkValue, ColumnMetadata pk)
        {
            var pkType = pk.PropertyType;
            if (pkType.IsInstanceOfType(pkValue))
                return pkValue;

            // 主键类型限定见 EntityMetadataCache（int/uint/long/ulong/string/Guid），仅数值主键接受可转换值
            Exception convertError = null;
            if (EntityMetadataCache.IsNumericPrimaryKeyType(pkType) && pkValue is IConvertible)
            {
                try
                {
                    return Convert.ChangeType(pkValue, pkType);
                }
                catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is InvalidCastException)
                {
                    // 保留原始异常细节，避免诊断信息丢失根因
                    convertError = ex;
                }
            }

            throw new ArgumentException($"主键值类型 '{pkValue.GetType()}' 与实体 '{typeof(T)}' 的主键类型 '{pkType}' 不兼容。", "key", convertError);
        }
        #endregion
    }
}
