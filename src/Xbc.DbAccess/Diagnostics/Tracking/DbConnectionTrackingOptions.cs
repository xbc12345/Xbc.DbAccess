namespace Xbc.DbAccess.Diagnostics
{
    /// <summary>
    /// 数据库连接追踪选项。
    /// </summary>
    public sealed class DbConnectionTrackingOptions
    {
        /// <summary>
        /// 是否启用连接追踪，默认值为 false。
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 是否记录堆栈信息（有性能开销），默认值为 false。
        /// </summary>
        public bool RecordStackTrace { get; set; }
    }
}
