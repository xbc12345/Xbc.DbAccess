namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库连接统计提供程序接口，由连接管理器实现以提供连接统计信息。
    /// </summary>
    public interface IDbConnectionStatisticsProvider
    {
        /// <summary>
        /// 获取数据库连接统计信息。
        /// </summary>
        /// <returns></returns>
        IDbConnectionStatistics GetStatistics();
    }
}
