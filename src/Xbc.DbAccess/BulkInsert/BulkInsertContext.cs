using System.Data;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 原生 bulk 插入上下文，描述目标表、列集与行数据。
    /// <para>行数据延迟枚举，但缓冲语义随方言不同：仅 PostgreSQL 的 COPY 逐行流式写入，SQL Server 与 MySQL 经
    /// <see cref="BuildDataTable"/> 全量物化到内存，Oracle 物化为按列的值数组；回退路径为分批或逐条流式执行。</para>
    /// <para>行值已转换为数据库值（null 统一为 <see cref="DBNull"/>），数组长度与 <see cref="Columns"/> 一一对应。</para>
    /// </summary>
    public sealed class BulkInsertContext
    {
        /// <summary>
        /// 目标表架构名，null 或空时表示无架构（由方言决定是否拼接）。
        /// </summary>
        public string Schema { get; }

        /// <summary>
        /// 目标表名（原始未引用形式）。
        /// </summary>
        public string TableName { get; }

        /// <summary>
        /// 目标列集（原始未引用形式，与每行数组的元素一一对应）。
        /// </summary>
        public IReadOnlyList<BulkInsertColumn> Columns { get; }

        /// <summary>
        /// 行数据（每行一个数组，数组元素与 <see cref="Columns"/> 一一对应；值为数据库值，null 统一为 <see cref="DBNull"/>）。
        /// </summary>
        public IEnumerable<object[]> Rows { get; }

        /// <summary>
        /// 初始化 bulk 插入上下文。
        /// </summary>
        /// <param name="schema">目标表架构名，null 或空时表示无架构。</param>
        /// <param name="tableName">目标表名。</param>
        /// <param name="columns">目标列集。</param>
        /// <param name="rows">行数据。</param>
        public BulkInsertContext(string schema, string tableName, IReadOnlyList<BulkInsertColumn> columns, IEnumerable<object[]> rows)
        {
            Guard.ThrowIfNullOrWhiteSpace(tableName, nameof(tableName));
            Guard.ThrowIfNullOrEmpty(columns, nameof(columns));
            Guard.ThrowIfNull(rows, nameof(rows));

            Schema = schema;
            TableName = tableName;
            Columns = columns;
            Rows = rows;
        }

        /// <summary>
        /// 将列集与行数据构建为 <see cref="DataTable"/>（列类型取列描述的归一 CLR 类型），供以 DataTable 为数据源的原生 bulk API 使用。行数据此时物化。
        /// </summary>
        /// <returns>数据表（列与行数据已物化）。</returns>
        /// <exception cref="ArgumentException">行数据的数组长度与目标列数不一致。</exception>
        public DataTable BuildDataTable()
        {
            var table = new DataTable();
            foreach (var column in Columns)
            {
                table.Columns.Add(column.ColumnName, column.ClrType);
            }

            int rowIndex = 0;
            foreach (var row in Rows)
            {
                // 长度契约在单次枚举循环内校验，避免 DataTable 静默补 DBNull 落库
                if (row.Length != table.Columns.Count)
                    throw new ArgumentException($"bulk 插入的第 {rowIndex} 行数据包含 {row.Length} 个值，与目标列数 {Columns.Count} 不一致。", nameof(Rows));

                table.Rows.Add(row);
                rowIndex++;
            }

            return table;
        }
    }
}
