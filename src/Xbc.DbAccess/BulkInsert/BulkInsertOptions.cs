namespace Xbc.DbAccess
{
    /// <summary>
    /// 原生 bulk 插入选项，承载跨方言通用的批量写入参数；各方言实现按自身能力映射，不支持的选项静默忽略。
    /// </summary>
    public sealed class BulkInsertOptions
    {
        /// <summary>
        /// 每批写入的行数。null 时使用方言默认值（通常不分批）。
        /// 仅 SQL Server（SqlBulkCopy.BatchSize）映射为原生批次大小；MySQL/PostgreSQL/Oracle 均不支持分批，忽略此选项。
        /// </summary>
        public int? BatchSize { get; set; }

        /// <summary>
        /// 命令超时时间（秒）。null 时使用方言默认值。
        /// SQL Server/MySQL/Oracle 映射为原生超时；PostgreSQL 的 COPY 无对应超时控制，忽略此选项。
        /// </summary>
        public int? CommandTimeout { get; set; }
    }
}
