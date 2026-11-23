namespace Xbc.DbAccess.MySql
{
    /// <summary>
    /// MySQL 数据库保存点 SQL 方言。
    /// </summary>
    internal sealed class MySqlSavepointSqlDialect : ISavepointSqlDialect
    {
        /// <summary>
        /// MySQL 数据库保存点 SQL 方言实例。
        /// </summary>
        public static readonly MySqlSavepointSqlDialect Instance = new MySqlSavepointSqlDialect();

        /// <summary>
        /// 是否支持保存点。
        /// </summary>
        public bool IsSavepointSupported => true;

        /// <summary>
        /// 是否支持释放保存点。
        /// </summary>
        public bool IsReleaseSavepointSupported => true;

        /// <summary>
        /// 
        /// </summary>
        private MySqlSavepointSqlDialect()
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
        /// </summary>
        /// <param name="savepointName"></param>
        /// <returns></returns>
        public string ReleaseSavepoint(string savepointName)
        {
            return $"RELEASE SAVEPOINT {savepointName}";
        }
    }
}
