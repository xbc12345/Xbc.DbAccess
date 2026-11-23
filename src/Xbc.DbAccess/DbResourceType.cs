namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库资源类型。
    /// </summary>
    internal enum DbResourceType
    {
        /// <summary>
        /// 连接。
        /// </summary>
        Connection,

        /// <summary>
        /// 连接作用域。
        /// </summary>
        ConnectionScope,

        /// <summary>
        /// 数据库命令。
        /// </summary>
        Command,

        /// <summary>
        /// 数据读取器。
        /// </summary>
        DataReader,

        /// <summary>
        /// 事务。
        /// </summary>
        Transaction,

        /// <summary>
        /// 数据适配器。
        /// </summary>
        DataAdapter,
    }
}
