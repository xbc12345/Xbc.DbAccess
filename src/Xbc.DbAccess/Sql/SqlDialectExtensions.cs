namespace Xbc.DbAccess
{
    /// <summary>
    /// <see cref="ISqlDialect"/> 的内部计算扩展：多值 INSERT 与 IN 条件的分批决策。
    /// </summary>
    internal static class SqlDialectExtensions
    {
        /// <summary>
        /// 计算多值插入每批最大行数：参数数量上限除以每行参数数，再与单语句 VALUES 行数上限取较小值，至少为 1。
        /// 每行参数数为 0 时仅受方言行数上限约束。
        /// </summary>
        /// <param name="sqlDialect">SQL 方言。</param>
        /// <param name="parameterCountPerRow">每行参数数（多值插入的列数）。</param>
        /// <returns>每批最大行数。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="parameterCountPerRow"/> 为负数。</exception>
        internal static int GetMaxRowsPerInsertBatch(this ISqlDialect sqlDialect, int parameterCountPerRow)
        {
            Guard.ThrowIfNull(sqlDialect, nameof(sqlDialect));

            if (parameterCountPerRow < 0)
                throw new ArgumentOutOfRangeException(nameof(parameterCountPerRow), parameterCountPerRow, "每行参数数不能为负数。");

            // 无参数列（全部为自增列的退化场景）时不受参数数量限制，仅受方言行数上限约束
            int maxRowsPerBatch = parameterCountPerRow > 0
                ? sqlDialect.MaxParameterCount / parameterCountPerRow
                : int.MaxValue;

            // 单语句 VALUES 行数硬限制（如 SQL Server 1000 行）须一并约束
            if (sqlDialect.MaxRowsPerInsertValues < maxRowsPerBatch)
                maxRowsPerBatch = sqlDialect.MaxRowsPerInsertValues;

            return Math.Max(1, maxRowsPerBatch);
        }

        /// <summary>
        /// 计算 IN 子句（每个键值占一个参数）每批可容纳的最大键数，即参数数量上限，至少为 1。
        /// </summary>
        /// <param name="sqlDialect">SQL 方言。</param>
        /// <returns>每批最大键数。</returns>
        internal static int GetMaxKeysPerInClauseBatch(this ISqlDialect sqlDialect)
        {
            Guard.ThrowIfNull(sqlDialect, nameof(sqlDialect));

            // IN 子句每个键值仅占 1 个参数：分批上限即参数数量上限；钳制到至少 1，方言实现错误时兜底
            return Math.Max(1, sqlDialect.MaxParameterCount);
        }
    }
}
