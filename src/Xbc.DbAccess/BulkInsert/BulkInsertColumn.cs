namespace Xbc.DbAccess
{
    /// <summary>
    /// bulk 插入的目标列描述：列名与归一后的 CLR 类型（可空值类型取基础类型、枚举取底层整数类型），
    /// 供需要静态类型信息的实现使用（如构建 DataTable 的列类型）。
    /// </summary>
    public sealed class BulkInsertColumn
    {
        /// <summary>
        /// 列名（原始未引用形式，由各方言实现按自身语法引用）。
        /// </summary>
        public string ColumnName { get; }

        /// <summary>
        /// 列的 CLR 类型（可空值类型与枚举已归一为基础类型）。
        /// </summary>
        public Type ClrType { get; }

        /// <summary>
        /// 初始化 bulk 插入列描述。
        /// </summary>
        /// <param name="columnName">列名。</param>
        /// <param name="clrType">列的 CLR 类型。</param>
        public BulkInsertColumn(string columnName, Type clrType)
        {
            Guard.ThrowIfNullOrWhiteSpace(columnName, nameof(columnName));
            Guard.ThrowIfNull(clrType, nameof(clrType));

            ColumnName = columnName;
            ClrType = clrType;
        }
    }
}
