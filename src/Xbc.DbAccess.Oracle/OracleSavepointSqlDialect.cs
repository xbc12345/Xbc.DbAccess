namespace Xbc.DbAccess.Oracle
{
    /// <summary>
    /// Oracle 数据库保存点 SQL 方言。
    /// </summary>
    internal sealed class OracleSavepointSqlDialect : ISavepointSqlDialect
    {
        /// <summary>
        /// Oracle 数据库保存点 SQL 方言实例。
        /// </summary>
        public static readonly OracleSavepointSqlDialect Instance = new OracleSavepointSqlDialect();

        /// <summary>
        /// 是否支持保存点。
        /// </summary>
        public bool IsSavepointSupported => true;

        /// <summary>
        /// 是否支持释放保存点。
        /// <para>Oracle 不支持 RELEASE SAVEPOINT 语句（官方 SQL Language Reference 无此语法：
        /// 保存点仅由事务提交/回滚统一清理，执行该语句即报 ORA-00900 无效 SQL），返回 false；
        /// 嵌套事务提交时框架跳过释放，保存点由事务结束统一清理。</para>
        /// </summary>
        public bool IsReleaseSavepointSupported => false;

        /// <summary>
        /// 
        /// </summary>
        private OracleSavepointSqlDialect()
        {
        }

        /// <summary>
        /// 构建保存点 SQL 语句。
        /// </summary>
        /// <param name="savepointName"></param>
        /// <returns></returns>
        public string CreateSavepoint(string savepointName)
        {
            return $"SAVEPOINT {savepointName}";
        }

        /// <summary>
        /// 构建回滚保存点 SQL 语句。
        /// </summary>
        /// <param name="savepointName"></param>
        /// <returns></returns>
        public string RollbackToSavepoint(string savepointName)
        {
            return $"ROLLBACK TO SAVEPOINT {savepointName}";
        }

        /// <summary>
        /// 构建释放保存点 SQL 语句。
        /// <para>Oracle 不支持 RELEASE SAVEPOINT（见 <see cref="IsReleaseSavepointSupported"/>），
        /// <see cref="NestedDbTransaction"/> 对不支持的方言跳过释放、由事务结束统一清理，
        /// 本方法不会在运行路径被调用，保留仅作为接口契约的防御实现（误用时显式失败优于静默）。</para>
        /// </summary>
        /// <param name="savepointName"></param>
        /// <returns></returns>
        public string ReleaseSavepoint(string savepointName)
        {
            // Oracle 不支持 RELEASE SAVEPOINT（执行即 ORA-00900）
            throw new NotSupportedException("Oracle 数据库不支持显式的释放保存点，保存点由事务提交或回滚统一清理。");
        }
    }
}
