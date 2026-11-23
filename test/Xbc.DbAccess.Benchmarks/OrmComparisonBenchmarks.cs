using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.Sqlite;

namespace Xbc.DbAccess.Benchmarks
{
    /// <summary>
    /// 流行 ORM 端到端对比基准：内存 SQLite 查 100 行，四方对照 Xbc / Dapper / EF Core / SqlSugar，覆盖窄表（5 列）/ 宽表（14 列）/ 超宽表（32 列）三种形态。
    /// <para>Xbc（框架连接池）与 Dapper（常开连接）走 System.Data.SQLite 栈；
    /// EF Core（DbContext 池 + AsNoTracking）与 SqlSugar（常开连接 + Ado.SqlQuery）走 Microsoft.Data.Sqlite 栈，均为各自官方推荐用法。</para>
    /// <para>两个 SQLite 引擎实例各维护一个共享内存库（根连接常开保活），表结构与 100 行种子数据一致，衡量各 ORM 的查询管道与实体映射开销。</para>
    /// </summary>
    [MemoryDiagnoser]
    public class OrmComparisonBenchmarks
    {
        private const string NarrowSql = "SELECT Id, Name, Price, Stock, CreatedAt FROM NarrowRows";
        private const string WideSql = "SELECT Id, Code, Name, Description, CategoryId, Volume, Price, Discount, Weight, IsActive, CreatedAt, UpdatedAt, RowGuid, Payload FROM WideRows";
        private const string NarrowInsertSql = "INSERT INTO NarrowRows (Id, Name, Price, Stock, CreatedAt) VALUES (@Id, @Name, @Price, @Stock, @CreatedAt)";
        private const string WideInsertSql = "INSERT INTO WideRows (Id, Code, Name, Description, CategoryId, Volume, Price, Discount, Weight, IsActive, CreatedAt, UpdatedAt, RowGuid, Payload) VALUES (@Id, @Code, @Name, @Description, @CategoryId, @Volume, @Price, @Discount, @Weight, @IsActive, @CreatedAt, @UpdatedAt, @RowGuid, @Payload)";
        private const string UltraWideSql = "SELECT Id, Code, Name, Description, CategoryId, Volume, Price, Discount, Weight, IsActive, CreatedAt, UpdatedAt, RowGuid, Payload, Tag, Remark, Sku, BarCode, SortOrder, ViewCount, SalesCount, Rating, Length, Width, Height, IsFeatured, ManufacturedAt, ExpiredAt, BatchNo, WarehouseId, ExternalId, Attachment FROM UltraWideRows";
        private const string UltraWideInsertSql = "INSERT INTO UltraWideRows (Id, Code, Name, Description, CategoryId, Volume, Price, Discount, Weight, IsActive, CreatedAt, UpdatedAt, RowGuid, Payload, Tag, Remark, Sku, BarCode, SortOrder, ViewCount, SalesCount, Rating, Length, Width, Height, IsFeatured, ManufacturedAt, ExpiredAt, BatchNo, WarehouseId, ExternalId, Attachment) VALUES (@Id, @Code, @Name, @Description, @CategoryId, @Volume, @Price, @Discount, @Weight, @IsActive, @CreatedAt, @UpdatedAt, @RowGuid, @Payload, @Tag, @Remark, @Sku, @BarCode, @SortOrder, @ViewCount, @SalesCount, @Rating, @Length, @Width, @Height, @IsFeatured, @ManufacturedAt, @ExpiredAt, @BatchNo, @WarehouseId, @ExternalId, @Attachment)";
        // 库名携带进程 ID：BenchmarkDotNet 每个基准独立进程，避免同名共享内存库跨进程冲突
        private static readonly string SdConnectionString =
            "Data Source=xbc_ormbench_sd_" + System.Diagnostics.Process.GetCurrentProcess().Id + ";Mode=Memory;Cache=Shared";
        private static readonly string MdsConnectionString =
            "DataSource=file:xbc_ormbench_mds_" + System.Diagnostics.Process.GetCurrentProcess().Id + "?mode=memory&cache=shared";
        private static readonly DateTime BaseTime = new DateTime(2026, 1, 1, 8, 0, 0);

        private System.Data.SQLite.SQLiteConnection sdRootConnection;
        private System.Data.SQLite.SQLiteConnection sdDapperConnection;
        private SqliteConnection mdsRootConnection;
        private SqliteConnection mdsBackupConnection;
        private IDbAccessFactory dbAccessFactory;
        private IDbAccess dbAccess;
        private ServiceProvider efProvider;
        private SqlSugarClient sugarClient;

        /// <summary>
        /// 建库建表并准备种子数据：System.Data.SQLite 库供 Xbc/Dapper，Microsoft.Data.Sqlite 库供 EF Core/SqlSugar。
        /// <para>四方均按各自推荐用法使用常开/池化连接，避免连接建立成本掩盖查询管道差异。</para>
        /// </summary>
        [GlobalSetup]
        public void Setup()
        {
            // System.Data.SQLite 共享内存库：根连接常开保活
            sdRootConnection = new System.Data.SQLite.SQLiteConnection(SdConnectionString);
            sdRootConnection.Open();
            SeedSdDatabase();

            // Dapper：常开连接（Guid TEXT 列注册类型处理器，Dapper 默认不支持 string → Guid）
            SqlMapper.AddTypeHandler(GuidTextHandler.Instance);
            sdDapperConnection = new System.Data.SQLite.SQLiteConnection(SdConnectionString);
            sdDapperConnection.Open();

            // Xbc：启用连接池（框架推荐形态，连接复用与 EF/SqlSugar 常开口径一致）
            dbAccessFactory = new SqliteAccessFactory(new Xbc.DbAccess.DbConnectionOptions
            {
                ConnectionString = SdConnectionString,
                PoolOptions = new Xbc.DbAccess.DbConnectionPoolOptions { Enabled = true }
            });
            dbAccess = dbAccessFactory.Create();

            // Microsoft.Data.Sqlite 共享内存库：根连接 + 备份连接（EF 可能在间隙关闭其连接，备份连接保证内存库存活）
            mdsRootConnection = new SqliteConnection(MdsConnectionString);
            mdsRootConnection.Open();
            SeedMdsDatabase();
            mdsBackupConnection = new SqliteConnection(MdsConnectionString);
            mdsBackupConnection.Open();

            // EF Core：DbContext 池 + 常开连接（官方推荐性能姿势）
            var services = new ServiceCollection();
            services.AddDbContextPool<BenchDbContext>(options => options.UseSqlite(mdsRootConnection));
            efProvider = services.BuildServiceProvider();

            // SqlSugar：常开连接 + 纯 ADO 映射路径
            sugarClient = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = MdsConnectionString,
                DbType = SqlSugar.DbType.Sqlite,
                IsAutoCloseConnection = false
            });
        }

        /// <summary>
        /// 释放各 ORM 资源与保活连接。
        /// </summary>
        [GlobalCleanup]
        public void Cleanup()
        {
            sugarClient.Dispose();
            efProvider.Dispose();
            dbAccess.Dispose();
            dbAccessFactory.Dispose();
            sdDapperConnection.Dispose();
            mdsBackupConnection.Dispose();
            mdsRootConnection.Dispose();
            sdRootConnection.Dispose();
        }

        #region 窄表查询
        [Benchmark(Description = "Xbc 窄表100行")]
        public async Task<List<BenchmarkEntities.NarrowEntity>> Xbc_Narrow_100()
        {
            return await dbAccess.FromSql<BenchmarkEntities.NarrowEntity>(NarrowSql).ToListAsync();
        }

        [Benchmark(Description = "Dapper 窄表100行")]
        public List<BenchmarkEntities.NarrowEntity> Dapper_Narrow_100()
        {
            return sdDapperConnection.Query<BenchmarkEntities.NarrowEntity>(NarrowSql).ToList();
        }

        [Benchmark(Description = "EFCore 窄表100行")]
        public List<BenchmarkEntities.NarrowEntity> EFCore_Narrow_100()
        {
            using (var scope = efProvider.CreateScope())
            using (var context = scope.ServiceProvider.GetRequiredService<BenchDbContext>())
            {
                return context.NarrowRows.AsNoTracking().ToList();
            }
        }

        [Benchmark(Description = "SqlSugar 窄表100行")]
        public List<BenchmarkEntities.NarrowEntity> SqlSugar_Narrow_100()
        {
            return sugarClient.Ado.SqlQuery<BenchmarkEntities.NarrowEntity>(NarrowSql);
        }
        #endregion

        #region 宽表查询
        [Benchmark(Description = "Xbc 宽表100行")]
        public async Task<List<BenchmarkEntities.WideEntity>> Xbc_Wide_100()
        {
            return await dbAccess.FromSql<BenchmarkEntities.WideEntity>(WideSql).ToListAsync();
        }

        [Benchmark(Description = "Dapper 宽表100行")]
        public List<BenchmarkEntities.WideEntity> Dapper_Wide_100()
        {
            return sdDapperConnection.Query<BenchmarkEntities.WideEntity>(WideSql).ToList();
        }

        [Benchmark(Description = "EFCore 宽表100行")]
        public List<BenchmarkEntities.WideEntity> EFCore_Wide_100()
        {
            using (var scope = efProvider.CreateScope())
            using (var context = scope.ServiceProvider.GetRequiredService<BenchDbContext>())
            {
                return context.WideRows.AsNoTracking().ToList();
            }
        }

        [Benchmark(Description = "SqlSugar 宽表100行")]
        public List<BenchmarkEntities.WideEntity> SqlSugar_Wide_100()
        {
            return sugarClient.Ado.SqlQuery<BenchmarkEntities.WideEntity>(WideSql);
        }
        #endregion

        #region 超宽表查询
        [Benchmark(Description = "Xbc 超宽表100行")]
        public async Task<List<BenchmarkEntities.UltraWideEntity>> Xbc_UltraWide_100()
        {
            return await dbAccess.FromSql<BenchmarkEntities.UltraWideEntity>(UltraWideSql).ToListAsync();
        }

        [Benchmark(Description = "Dapper 超宽表100行")]
        public List<BenchmarkEntities.UltraWideEntity> Dapper_UltraWide_100()
        {
            return sdDapperConnection.Query<BenchmarkEntities.UltraWideEntity>(UltraWideSql).ToList();
        }

        [Benchmark(Description = "EFCore 超宽表100行")]
        public List<BenchmarkEntities.UltraWideEntity> EFCore_UltraWide_100()
        {
            using (var scope = efProvider.CreateScope())
            using (var context = scope.ServiceProvider.GetRequiredService<BenchDbContext>())
            {
                return context.UltraWideRows.AsNoTracking().ToList();
            }
        }

        [Benchmark(Description = "SqlSugar 超宽表100行")]
        public List<BenchmarkEntities.UltraWideEntity> SqlSugar_UltraWide_100()
        {
            return sugarClient.Ado.SqlQuery<BenchmarkEntities.UltraWideEntity>(UltraWideSql);
        }
        #endregion

        /// <summary>
        /// System.Data.SQLite 库建表并插入种子数据。
        /// </summary>
        private void SeedSdDatabase()
        {
            using (var command = sdRootConnection.CreateCommand())
            {
                command.CommandText =
                    "DROP TABLE IF EXISTS NarrowRows; DROP TABLE IF EXISTS WideRows; DROP TABLE IF EXISTS UltraWideRows;" +
                    "CREATE TABLE NarrowRows (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Price REAL NOT NULL, Stock INTEGER NOT NULL, CreatedAt TEXT NOT NULL);" +
                    "CREATE TABLE WideRows (Id INTEGER PRIMARY KEY, Code TEXT NOT NULL, Name TEXT NOT NULL, Description TEXT, CategoryId INTEGER NOT NULL, Volume INTEGER NOT NULL, Price REAL NOT NULL, Discount REAL NOT NULL, Weight REAL NOT NULL, IsActive INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT, RowGuid TEXT NOT NULL, Payload BLOB);" +
                    "CREATE TABLE UltraWideRows (Id INTEGER PRIMARY KEY, Code TEXT NOT NULL, Name TEXT NOT NULL, Description TEXT, CategoryId INTEGER NOT NULL, Volume INTEGER NOT NULL, Price REAL NOT NULL, Discount REAL NOT NULL, Weight REAL NOT NULL, IsActive INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT, RowGuid TEXT NOT NULL, Payload BLOB, Tag TEXT, Remark TEXT, Sku TEXT, BarCode TEXT, SortOrder INTEGER NOT NULL, ViewCount INTEGER NOT NULL, SalesCount INTEGER NOT NULL, Rating REAL NOT NULL, Length REAL NOT NULL, Width REAL NOT NULL, Height REAL NOT NULL, IsFeatured INTEGER NOT NULL, ManufacturedAt TEXT NOT NULL, ExpiredAt TEXT, BatchNo TEXT, WarehouseId INTEGER NOT NULL, ExternalId TEXT NOT NULL, Attachment BLOB);";
                command.ExecuteNonQuery();
            }

            using (var narrowCommand = sdRootConnection.CreateCommand())
            using (var wideCommand = sdRootConnection.CreateCommand())
            using (var ultraCommand = sdRootConnection.CreateCommand())
            {
                narrowCommand.CommandText = NarrowInsertSql;
                wideCommand.CommandText = WideInsertSql;
                ultraCommand.CommandText = UltraWideInsertSql;
                var narrowParameters = CreateSdParameters(narrowCommand, NarrowParameterNames);
                var wideParameters = CreateSdParameters(wideCommand, WideParameterNames);
                var ultraParameters = CreateSdParameters(ultraCommand, UltraWideParameterNames);

                for (int i = 1; i <= 100; i++)
                {
                    narrowParameters["@Id"].Value = i;
                    narrowParameters["@Name"].Value = "商品" + i;
                    narrowParameters["@Price"].Value = (double)(i * 1.5m);
                    narrowParameters["@Stock"].Value = i * 3;
                    narrowParameters["@CreatedAt"].Value = BaseTime.AddMinutes(i);
                    narrowCommand.ExecuteNonQuery();

                    wideParameters["@Id"].Value = i;
                    wideParameters["@Code"].Value = "C" + i.ToString("000");
                    wideParameters["@Name"].Value = "宽行" + i;
                    wideParameters["@Description"].Value = "描述" + i;
                    wideParameters["@CategoryId"].Value = i % 10;
                    wideParameters["@Volume"].Value = i * 100L;
                    wideParameters["@Price"].Value = (double)(i * 2.5m);
                    wideParameters["@Discount"].Value = (double)(i * 0.1m);
                    wideParameters["@Weight"].Value = i * 1.25;
                    wideParameters["@IsActive"].Value = i % 2 == 0;
                    wideParameters["@CreatedAt"].Value = BaseTime.AddMinutes(i);
                    wideParameters["@UpdatedAt"].Value = i % 3 == 0 ? (object)DBNull.Value : BaseTime.AddHours(i);
                    wideParameters["@RowGuid"].Value = CreateSeedGuid(i).ToString();
                    wideParameters["@Payload"].Value = CreateSeedPayload(i);
                    wideCommand.ExecuteNonQuery();

                    SetUltraWideRowValues(ultraParameters, i);
                    ultraCommand.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Microsoft.Data.Sqlite 库建表并插入种子数据。
        /// </summary>
        private void SeedMdsDatabase()
        {
            using (var command = mdsRootConnection.CreateCommand())
            {
                command.CommandText =
                    "DROP TABLE IF EXISTS NarrowRows; DROP TABLE IF EXISTS WideRows; DROP TABLE IF EXISTS UltraWideRows;" +
                    "CREATE TABLE NarrowRows (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Price REAL NOT NULL, Stock INTEGER NOT NULL, CreatedAt TEXT NOT NULL);" +
                    "CREATE TABLE WideRows (Id INTEGER PRIMARY KEY, Code TEXT NOT NULL, Name TEXT NOT NULL, Description TEXT, CategoryId INTEGER NOT NULL, Volume INTEGER NOT NULL, Price REAL NOT NULL, Discount REAL NOT NULL, Weight REAL NOT NULL, IsActive INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT, RowGuid TEXT NOT NULL, Payload BLOB);" +
                    "CREATE TABLE UltraWideRows (Id INTEGER PRIMARY KEY, Code TEXT NOT NULL, Name TEXT NOT NULL, Description TEXT, CategoryId INTEGER NOT NULL, Volume INTEGER NOT NULL, Price REAL NOT NULL, Discount REAL NOT NULL, Weight REAL NOT NULL, IsActive INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT, RowGuid TEXT NOT NULL, Payload BLOB, Tag TEXT, Remark TEXT, Sku TEXT, BarCode TEXT, SortOrder INTEGER NOT NULL, ViewCount INTEGER NOT NULL, SalesCount INTEGER NOT NULL, Rating REAL NOT NULL, Length REAL NOT NULL, Width REAL NOT NULL, Height REAL NOT NULL, IsFeatured INTEGER NOT NULL, ManufacturedAt TEXT NOT NULL, ExpiredAt TEXT, BatchNo TEXT, WarehouseId INTEGER NOT NULL, ExternalId TEXT NOT NULL, Attachment BLOB);";
                command.ExecuteNonQuery();
            }

            using (var narrowCommand = mdsRootConnection.CreateCommand())
            using (var wideCommand = mdsRootConnection.CreateCommand())
            using (var ultraCommand = mdsRootConnection.CreateCommand())
            {
                narrowCommand.CommandText = NarrowInsertSql;
                wideCommand.CommandText = WideInsertSql;
                ultraCommand.CommandText = UltraWideInsertSql;
                var narrowParameters = CreateMdsParameters(narrowCommand, NarrowParameterNames);
                var wideParameters = CreateMdsParameters(wideCommand, WideParameterNames);
                var ultraParameters = CreateMdsParameters(ultraCommand, UltraWideParameterNames);

                for (int i = 1; i <= 100; i++)
                {
                    narrowParameters["@Id"].Value = i;
                    narrowParameters["@Name"].Value = "商品" + i;
                    narrowParameters["@Price"].Value = (double)(i * 1.5m);
                    narrowParameters["@Stock"].Value = i * 3;
                    narrowParameters["@CreatedAt"].Value = BaseTime.AddMinutes(i);
                    narrowCommand.ExecuteNonQuery();

                    wideParameters["@Id"].Value = i;
                    wideParameters["@Code"].Value = "C" + i.ToString("000");
                    wideParameters["@Name"].Value = "宽行" + i;
                    wideParameters["@Description"].Value = "描述" + i;
                    wideParameters["@CategoryId"].Value = i % 10;
                    wideParameters["@Volume"].Value = i * 100L;
                    wideParameters["@Price"].Value = (double)(i * 2.5m);
                    wideParameters["@Discount"].Value = (double)(i * 0.1m);
                    wideParameters["@Weight"].Value = i * 1.25;
                    wideParameters["@IsActive"].Value = i % 2 == 0;
                    wideParameters["@CreatedAt"].Value = BaseTime.AddMinutes(i);
                    wideParameters["@UpdatedAt"].Value = i % 3 == 0 ? (object)DBNull.Value : BaseTime.AddHours(i);
                    wideParameters["@RowGuid"].Value = CreateSeedGuid(i).ToString();
                    wideParameters["@Payload"].Value = CreateSeedPayload(i);
                    wideCommand.ExecuteNonQuery();

                    SetUltraWideRowValues(ultraParameters, i);
                    ultraCommand.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 窄表插入语句的参数名（与 NarrowInsertSql 顺序一致）。
        /// </summary>
        private static readonly string[] NarrowParameterNames = { "Id", "Name", "Price", "Stock", "CreatedAt" };

        /// <summary>
        /// 宽表插入语句的参数名（与 WideInsertSql 顺序一致）。
        /// </summary>
        private static readonly string[] WideParameterNames =
        {
            "Id", "Code", "Name", "Description", "CategoryId", "Volume", "Price", "Discount",
            "Weight", "IsActive", "CreatedAt", "UpdatedAt", "RowGuid", "Payload"
        };

        /// <summary>
        /// 超宽表插入语句的参数名（与 UltraWideInsertSql 顺序一致）。
        /// </summary>
        private static readonly string[] UltraWideParameterNames =
        {
            "Id", "Code", "Name", "Description", "CategoryId", "Volume", "Price", "Discount",
            "Weight", "IsActive", "CreatedAt", "UpdatedAt", "RowGuid", "Payload",
            "Tag", "Remark", "Sku", "BarCode", "SortOrder", "ViewCount", "SalesCount", "Rating",
            "Length", "Width", "Height", "IsFeatured", "ManufacturedAt", "ExpiredAt", "BatchNo",
            "WarehouseId", "ExternalId", "Attachment"
        };

        /// <summary>
        /// 为超宽表插入语句设置第 i 行参数值（两库共用，保证种子数据一致）。
        /// </summary>
        private static void SetUltraWideRowValues(Dictionary<string, System.Data.IDataParameter> parameters, int i)
        {
            parameters["@Id"].Value = i;
            parameters["@Code"].Value = "C" + i.ToString("000");
            parameters["@Name"].Value = "超宽行" + i;
            parameters["@Description"].Value = "描述" + i;
            parameters["@CategoryId"].Value = i % 10;
            parameters["@Volume"].Value = i * 100L;
            parameters["@Price"].Value = (double)(i * 2.5m);
            parameters["@Discount"].Value = (double)(i * 0.1m);
            parameters["@Weight"].Value = i * 1.25;
            parameters["@IsActive"].Value = i % 2 == 0;
            parameters["@CreatedAt"].Value = BaseTime.AddMinutes(i);
            parameters["@UpdatedAt"].Value = i % 3 == 0 ? (object)DBNull.Value : BaseTime.AddHours(i);
            parameters["@RowGuid"].Value = CreateSeedGuid(i).ToString();
            parameters["@Payload"].Value = CreateSeedPayload(i);
            parameters["@Tag"].Value = "T" + i.ToString("00");
            parameters["@Remark"].Value = "备注" + i;
            parameters["@Sku"].Value = "SKU" + i.ToString("000000");
            parameters["@BarCode"].Value = "690" + i.ToString("0000000");
            parameters["@SortOrder"].Value = i;
            parameters["@ViewCount"].Value = i * 17;
            parameters["@SalesCount"].Value = i * 25L;
            parameters["@Rating"].Value = (double)(i * 0.1m);
            parameters["@Length"].Value = i * 1.1;
            parameters["@Width"].Value = i * 2.2;
            parameters["@Height"].Value = i * 3.3;
            parameters["@IsFeatured"].Value = i % 5 == 0;
            parameters["@ManufacturedAt"].Value = BaseTime.AddDays(i % 28);
            parameters["@ExpiredAt"].Value = i % 4 == 0 ? (object)DBNull.Value : BaseTime.AddDays(i + 30);
            parameters["@BatchNo"].Value = "B2026" + i.ToString("0000");
            parameters["@WarehouseId"].Value = i % 5;
            parameters["@ExternalId"].Value = CreateSeedGuid(10000 + i).ToString();
            parameters["@Attachment"].Value = CreateSeedPayload(i + 100);
        }

        /// <summary>
        /// 为 System.Data.SQLite 命令创建具名参数映射。
        /// </summary>
        private static Dictionary<string, System.Data.IDataParameter> CreateSdParameters(System.Data.SQLite.SQLiteCommand command, string[] names)
        {
            var map = new Dictionary<string, System.Data.IDataParameter>();
            foreach (var name in names)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@" + name;
                command.Parameters.Add(parameter);
                map["@" + name] = parameter;
            }
            return map;
        }

        /// <summary>
        /// 为 Microsoft.Data.Sqlite 命令创建具名参数映射。
        /// </summary>
        private static Dictionary<string, System.Data.IDataParameter> CreateMdsParameters(SqliteCommand command, string[] names)
        {
            var map = new Dictionary<string, System.Data.IDataParameter>();
            foreach (var name in names)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@" + name;
                command.Parameters.Add(parameter);
                map["@" + name] = parameter;
            }
            return map;
        }

        /// <summary>
        /// 生成确定性种子 Guid（保证两个库数据一致）。
        /// </summary>
        private static Guid CreateSeedGuid(int index)
        {
            return new Guid(index, 0x2026, 0x1010, 0xBC, 0xBC, 0, 0, 0, 0, 0, (byte)index);
        }

        /// <summary>
        /// 生成 32 字节种子负载数据。
        /// </summary>
        private static byte[] CreateSeedPayload(int index)
        {
            var payload = new byte[32];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(index + i);
            }
            return payload;
        }

        /// <summary>
        /// Dapper 的 Guid 文本类型处理器：SQLite 中 Guid 以 TEXT 存储，Dapper 默认不支持 string → Guid 转换。
        /// </summary>
        private class GuidTextHandler : SqlMapper.TypeHandler<Guid>
        {
            /// <summary>
            /// 单例实例。
            /// </summary>
            public static readonly GuidTextHandler Instance = new GuidTextHandler();

            /// <summary>
            /// 读取 TEXT 列并解析为 Guid。
            /// </summary>
            /// <param name="value">列值（Guid 的字符串形式）。</param>
            /// <returns>解析后的 Guid。</returns>
            public override Guid Parse(object value)
            {
                return new Guid((string)value);
            }

            /// <summary>
            /// 将 Guid 写入参数（字符串形式）。
            /// </summary>
            /// <param name="parameter">目标参数。</param>
            /// <param name="value">Guid 值。</param>
            public override void SetValue(System.Data.IDbDataParameter parameter, Guid value)
            {
                parameter.Value = value.ToString();
            }
        }

        /// <summary>
        /// EF Core 基准 DbContext：复用共享内存库连接，decimal 列显式映射为 REAL（SQLite 无原生 DECIMAL）。
        /// </summary>
        private class BenchDbContext : DbContext
        {
            /// <summary>
            /// 初始化 DbContext。
            /// </summary>
            /// <param name="options">上下文选项。</param>
            public BenchDbContext(DbContextOptions<BenchDbContext> options)
                : base(options)
            {
            }

            /// <summary>
            /// 窄表实体集。
            /// </summary>
            public DbSet<BenchmarkEntities.NarrowEntity> NarrowRows { get; set; }

            /// <summary>
            /// 宽表实体集。
            /// </summary>
            public DbSet<BenchmarkEntities.WideEntity> WideRows { get; set; }

            /// <summary>
            /// 超宽表实体集。
            /// </summary>
            public DbSet<BenchmarkEntities.UltraWideEntity> UltraWideRows { get; set; }

            /// <summary>
            /// 模型配置：表名与列类型对齐种子库结构。
            /// </summary>
            /// <param name="modelBuilder">模型构建器。</param>
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<BenchmarkEntities.NarrowEntity>(entity =>
                {
                    entity.ToTable("NarrowRows");
                    entity.Property(x => x.Price).HasColumnType("REAL");
                });

                modelBuilder.Entity<BenchmarkEntities.WideEntity>(entity =>
                {
                    entity.ToTable("WideRows");
                    entity.Property(x => x.Price).HasColumnType("REAL");
                    entity.Property(x => x.Discount).HasColumnType("REAL");
                });

                modelBuilder.Entity<BenchmarkEntities.UltraWideEntity>(entity =>
                {
                    entity.ToTable("UltraWideRows");
                    entity.Property(x => x.Price).HasColumnType("REAL");
                    entity.Property(x => x.Discount).HasColumnType("REAL");
                    entity.Property(x => x.Rating).HasColumnType("REAL");
                });
            }
        }
    }
}
