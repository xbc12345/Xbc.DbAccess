using BenchmarkDotNet.Attributes;
using Dapper;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.Sqlite;

namespace Xbc.DbAccess.Benchmarks
{
    /// <summary>
    /// 端到端执行基准：真实 SQLite（本地文件，WAL 模式），三方对照 Xbc / Dapper / 原生 ADO.NET。
    /// <para>所有对照均按"每次操作新建并打开连接"（无连接池），条件一致，衡量各库的执行管道与映射开销：</para>
    /// <para>Xbc 走 Query&lt;T&gt;.ToListAsync / ExecuteNonQueryAsync(匿名参数)；Dapper 走 Query/Execute；原生为手写 ADO.NET。</para>
    /// </summary>
    [MemoryDiagnoser]
    public class ExecutionBenchmarks
    {
        private const string SelectSql = "SELECT Id, Name, Price, CreatedAt FROM BenchRows";
        private const string InsertSql = "INSERT INTO BenchRows (Name, Price, CreatedAt) VALUES (@Name, @Price, @CreatedAt)";
        private static readonly DateTime InsertTime = new DateTime(2026, 1, 1, 8, 0, 0);

        private string dbPath;
        private string connectionString;
        private IDbAccessFactory dbAccessFactory;
        private IDbAccess dbAccess;

        [GlobalSetup]
        public void Setup()
        {
            dbPath = Path.Combine(Path.GetTempPath(), "xbc_dbaccess_benchmarks.db");
            TryDeleteFile(dbPath);
            TryDeleteFile(dbPath + "-wal");
            TryDeleteFile(dbPath + "-shm");

            // Synchronous=Normal 经连接串对每个连接生效（三组对照一致）；WAL 模式持久化在库文件中
            connectionString = "Data Source=" + dbPath + ";Synchronous=Normal;";
            dbAccessFactory = SqliteAccessFactory.CreateSqliteFactory(connectionString);
            dbAccess = dbAccessFactory.Create();

            using (var connection = new System.Data.SQLite.SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var pragmaCommand = connection.CreateCommand())
                {
                    pragmaCommand.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE BenchRows (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Price REAL NOT NULL, CreatedAt TEXT NOT NULL);";
                    pragmaCommand.ExecuteNonQuery();
                }

                using (var transaction = connection.BeginTransaction())
                using (var seedCommand = connection.CreateCommand())
                {
                    seedCommand.Transaction = transaction;
                    seedCommand.CommandText = InsertSql;
                    var pName = seedCommand.CreateParameter();
                    pName.ParameterName = "@Name";
                    var pPrice = seedCommand.CreateParameter();
                    pPrice.ParameterName = "@Price";
                    var pCreated = seedCommand.CreateParameter();
                    pCreated.ParameterName = "@CreatedAt";
                    seedCommand.Parameters.Add(pName);
                    seedCommand.Parameters.Add(pPrice);
                    seedCommand.Parameters.Add(pCreated);

                    for (int i = 0; i < 100; i++)
                    {
                        pName.Value = "种子" + i;
                        pPrice.Value = i * 0.5;
                        pCreated.Value = InsertTime.AddMinutes(i);
                        seedCommand.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
            }
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            dbAccess.Dispose();
            dbAccessFactory.Dispose();
            TryDeleteFile(dbPath);
            TryDeleteFile(dbPath + "-wal");
            TryDeleteFile(dbPath + "-shm");
        }

        #region SELECT 100 行
        [Benchmark(Description = "Xbc 查询100行")]
        public async Task<List<BenchmarkEntities.BenchRow>> Xbc_Select_100()
        {
            return await dbAccess.FromSql<BenchmarkEntities.BenchRow>(SelectSql).ToListAsync();
        }

        [Benchmark(Description = "Dapper 查询100行")]
        public List<BenchmarkEntities.BenchRow> Dapper_Select_100()
        {
            using (var connection = new System.Data.SQLite.SQLiteConnection(connectionString))
            {
                connection.Open();
                return connection.Query<BenchmarkEntities.BenchRow>(SelectSql).ToList();
            }
        }

        [Benchmark(Description = "原生 查询100行")]
        public List<BenchmarkEntities.BenchRow> Raw_Select_100()
        {
            using (var connection = new System.Data.SQLite.SQLiteConnection(connectionString))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = SelectSql;
                connection.Open();

                using (var reader = command.ExecuteReader())
                {
                    var list = new List<BenchmarkEntities.BenchRow>();
                    while (reader.Read())
                    {
                        list.Add(new BenchmarkEntities.BenchRow
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            Price = reader.GetDouble(2),
                            CreatedAt = reader.GetDateTime(3)
                        });
                    }

                    return list;
                }
            }
        }
        #endregion

        #region INSERT 单行（参数化）
        [Benchmark(Description = "Xbc 插入单行")]
        public Task<int> Xbc_Insert_1()
        {
            return dbAccess.ExecuteNonQueryAsync(InsertSql, new { Name = "基准行", Price = 1.5, CreatedAt = InsertTime });
        }

        [Benchmark(Description = "Dapper 插入单行")]
        public int Dapper_Insert_1()
        {
            using (var connection = new System.Data.SQLite.SQLiteConnection(connectionString))
            {
                connection.Open();
                return connection.Execute(InsertSql, new { Name = "基准行", Price = 1.5, CreatedAt = InsertTime });
            }
        }

        [Benchmark(Description = "原生 插入单行")]
        public int Raw_Insert_1()
        {
            using (var connection = new System.Data.SQLite.SQLiteConnection(connectionString))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = InsertSql;
                var pName = command.CreateParameter();
                pName.ParameterName = "@Name";
                pName.Value = "基准行";
                var pPrice = command.CreateParameter();
                pPrice.ParameterName = "@Price";
                pPrice.Value = 1.5;
                var pCreated = command.CreateParameter();
                pCreated.ParameterName = "@CreatedAt";
                pCreated.Value = InsertTime;
                command.Parameters.Add(pName);
                command.Parameters.Add(pPrice);
                command.Parameters.Add(pCreated);

                connection.Open();
                return command.ExecuteNonQuery();
            }
        }
        #endregion

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
                // 数据库文件被占用时忽略（下次运行会重建）
            }
        }
    }
}
