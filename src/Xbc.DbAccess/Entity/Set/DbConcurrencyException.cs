namespace Xbc.DbAccess.Entity
{
    /// <summary>
    /// 乐观并发冲突异常：实体配置了并发令牌列（<c>[Column(IsConcurrencyToken = true)]</c>）时，
    /// 实体版更新/删除在 WHERE 中附带令牌等值条件做乐观并发校验，影响行数为 0（目标行不存在或令牌已被并发修改）时抛出。
    /// </summary>
    public sealed class DbConcurrencyException : Exception
    {
        /// <summary>
        /// 初始化并发冲突异常。
        /// </summary>
        public DbConcurrencyException()
        {
        }

        /// <summary>
        /// 初始化并发冲突异常并指定异常消息。
        /// </summary>
        /// <param name="message">异常消息。</param>
        public DbConcurrencyException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化并发冲突异常并指定异常消息与内部异常。
        /// </summary>
        /// <param name="message">异常消息。</param>
        /// <param name="innerException">内部异常。</param>
        public DbConcurrencyException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
