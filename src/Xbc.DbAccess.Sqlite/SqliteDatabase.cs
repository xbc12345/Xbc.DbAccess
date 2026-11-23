using System.Data.SQLite;
using Xbc.DbAccess.Diagnostics;

namespace Xbc.DbAccess.Sqlite
{
    /// <summary>
    /// SQLite 数据库文件管理。
    /// </summary>
    public static class SqliteDatabase
    {
        /// <summary>
        /// 新建数据库文件。若文件已存在则先删除；数据库文件的编码固定为 UTF-8，
        /// 创建后立即打开连接并执行初始化脚本（如建表语句）。
        /// </summary>
        /// <param name="dbPath">数据库文件路径。</param>
        /// <param name="sql">初始化脚本。</param>
        /// <exception cref="ArgumentException"></exception>
        public static async Task CreateDataBaseAsync(string dbPath, string sql)
        {
            Guard.ThrowIfNullOrWhiteSpace(dbPath, nameof(dbPath));
            Guard.ThrowIfNullOrWhiteSpace(sql, nameof(sql));

            if (File.Exists(dbPath))
                File.Delete(dbPath);

            using (var connection = new SQLiteConnection($"Data Source={dbPath}"))
            {
                await connection.OpenAsync().ConfigureAwait(false);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
