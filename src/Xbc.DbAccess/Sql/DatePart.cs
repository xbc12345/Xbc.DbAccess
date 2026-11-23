namespace Xbc.DbAccess
{
    /// <summary>
    /// WHERE 条件日期部分提取的种类，对应 <c>DateTime/DateTimeOffset</c> 列的 <c>Year/Month/Day/Date/Hour/Minute/Second</c> 成员访问。
    /// </summary>
    public enum DatePart
    {
        /// <summary>
        /// 年份部分（如 2024），整数比较。
        /// </summary>
        Year = 0,

        /// <summary>
        /// 月份部分（1-12），整数比较。
        /// </summary>
        Month = 1,

        /// <summary>
        /// 日部分（1-31），整数比较。
        /// </summary>
        Day = 2,

        /// <summary>
        /// 日期归一（截断到零时刻），与 .NET 的 <c>DateTime.Date</c> 语义一致。
        /// </summary>
        Date = 3,

        /// <summary>
        /// 小时部分（0-23），整数比较。
        /// </summary>
        Hour = 4,

        /// <summary>
        /// 分钟部分（0-59），整数比较。
        /// </summary>
        Minute = 5,

        /// <summary>
        /// 秒部分（0-59），整数比较。
        /// </summary>
        Second = 6
    }
}
