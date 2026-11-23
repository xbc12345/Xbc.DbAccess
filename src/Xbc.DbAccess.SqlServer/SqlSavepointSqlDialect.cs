namespace Xbc.DbAccess.SqlServer
{
    /// <summary>
    /// SQL Server 数据库保存点 SQL 方言。
    /// </summary>
    internal sealed class SqlSavepointSqlDialect : ISavepointSqlDialect
    {
        /// <summary>
        /// SQL Server 数据库保存点 SQL 方言实例。
        /// </summary>
        public static readonly SqlSavepointSqlDialect Instance = new SqlSavepointSqlDialect();

        /// <summary>
        /// 是否支持保存点。
        /// </summary>
        public bool IsSavepointSupported => true;

        /// <summary>
        /// 是否支持释放保存点。
        /// <para>SQL Server 不支持 RELEASE SAVEPOINT 语句（官方 Transact-SQL 参考无此语法：
        /// 保存点仅由事务提交/回滚统一清理），返回 false；嵌套事务提交时框架跳过释放，
        /// 保存点由事务结束统一清理。</para>
        /// </summary>
        public bool IsReleaseSavepointSupported => false;

        /// <summary>
        /// 
        /// </summary>
        private SqlSavepointSqlDialect()
        {
        }

        /// <summary>
        /// 构建保存点 SQL 语句。
        /// </summary>
        /// <param name="savepointName"></param>
        /// <returns></returns>
        public string CreateSavepoint(string savepointName)
        {
            return $"SAVE TRANSACTION {savepointName}";
        }

        /// <summary>
        /// 构建回滚保存点 SQL 语句。
        /// </summary>
        /// <param name="savepointName"></param>
        /// <returns></returns>
        public string RollbackToSavepoint(string savepointName)
        {
            return $"ROLLBACK TRANSACTION {savepointName}";
        }

        /// <summary>
        /// 构建释放保存点 SQL 语句。
        /// </summary>
        /// <param name="savepointName"></param>
        /// <returns></returns>
        public string ReleaseSavepoint(string savepointName)
        {
            // SQL Server 不支持 RELEASE SAVEPOINT
            throw new NotSupportedException("SQL Server 数据库不支持显式的释放保存点，保存点由事务提交或回滚统一清理。");
        }
    }
}
