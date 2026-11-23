using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库连接访问器接口，提供获取当前连接的能力。
    /// </summary>
    public interface IDbConnectionAccessor
    {
        /// <summary>
        /// 当前数据库连接。
        /// </summary>
        DbConnection CurrentConnection { get; }

        /// <summary>
        /// 是否存在活动的连接。
        /// </summary>
        bool HasActiveConnection { get; }
    }
}
