using System.Data.Common;

namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库连接已耗尽的异常。当连接池中所有连接都在使用中且达到最大连接数限制时抛出此异常。
    /// </summary>
    public sealed class DbConnectionExhaustedException : DbException
    {
        /// <summary>
        /// 最大连接数量。
        /// </summary>
        public int MaxSize { get; }

        /// <summary>
        /// 初始化连接耗尽异常。
        /// </summary>
        /// <param name="maxSize">最大连接数量。</param>
        public DbConnectionExhaustedException(int maxSize)
            : base($"数据库连接已耗尽，已达到最大连接数量限制: {maxSize}。")
        {
            MaxSize = maxSize;
        }
    }
}
