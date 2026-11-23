namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库服务器信息。
    /// </summary>
    public class DbServerInfo
    {
        /// <summary>
        /// 服务器版本。
        /// </summary>
        public string ServerVersion { get; set; }

        /// <summary>
        /// 数据源名称。
        /// </summary>
        public string DataSource { get; set; }

        /// <summary>
        /// 数据库名称。
        /// </summary>
        public string Database { get; set; }

        /// <summary>
        /// 主版本号。
        /// </summary>
        public int MajorVersion { get; set; }

        /// <summary>
        /// 次版本号。
        /// </summary>
        public int MinorVersion { get; set; }

        /// <summary>
        /// 修订版本号。
        /// </summary>
        public int RevisionVersion { get; set; }
    }
}
