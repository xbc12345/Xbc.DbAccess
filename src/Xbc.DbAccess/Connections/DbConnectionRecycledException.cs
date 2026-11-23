using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库连接已被回收的异常。当数据库连接被回收后继续使用该连接时抛出此异常。
    /// </summary>
    public sealed class DbConnectionRecycledException : DbException
    {
        /// <summary>
        /// 连接唯一标识。
        /// </summary>
        public Guid ConnectionId { get; }

        /// <summary>
        /// 初始化连接回收异常。
        /// </summary>
        /// <param name="connectionId">连接唯一标识。</param>
        /// <param name="message">异常消息。</param>
        public DbConnectionRecycledException(Guid connectionId, string message) : base(message)
        {
            ConnectionId = connectionId;
        }
    }
}
