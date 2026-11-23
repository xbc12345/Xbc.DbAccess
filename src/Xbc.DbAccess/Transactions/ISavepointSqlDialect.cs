namespace Xbc.DbAccess
{
    /// <summary>
    /// 保存点 SQL 方言接口，为不同数据库生成保存点的创建/回滚/释放语句。
    /// <para>嵌套子事务通过保存点实现：开启时创建、回滚时回退到保存点、提交时释放保存点。</para>
    /// </summary>
    public interface ISavepointSqlDialect
    {
        /// <summary>
        /// 是否支持保存点。
        /// </summary>
        bool IsSavepointSupported { get; }

        /// <summary>
        /// 是否支持手动释放保存点。部分数据库会在事务提交时自动释放保存点，无需手动释放。
        /// </summary>
        bool IsReleaseSavepointSupported { get; }

        /// <summary>
        /// 构建创建保存点的 SQL 语句。
        /// </summary>
        /// <param name="savepointName">保存点名称。</param>
        /// <returns></returns>
        string CreateSavepoint(string savepointName);

        /// <summary>
        /// 构建回滚到保存点的 SQL 语句。
        /// </summary>
        /// <param name="savepointName">保存点名称。</param>
        /// <returns></returns>
        string RollbackToSavepoint(string savepointName);

        /// <summary>
        /// 构建释放保存点的 SQL 语句。
        /// </summary>
        /// <param name="savepointName">保存点名称。</param>
        /// <returns></returns>
        string ReleaseSavepoint(string savepointName);
    }
}
