namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库连接作用域，在作用域内的多个数据库操作共享同一个连接。
    /// </summary>
    public interface IDbConnectionScope : IDisposable
    {
        /// <summary>
        /// 作用域唯一标识。
        /// </summary>
        Guid ScopeId { get; }

        /// <summary>
        /// 是否在作用域释放时自动释放连接。
        /// </summary>
        bool ManageConnection { get; }
    }
}
