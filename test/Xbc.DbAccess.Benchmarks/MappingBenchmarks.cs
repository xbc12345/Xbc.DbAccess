using BenchmarkDotNet.Attributes;
using Dapper;
using Xbc.DbAccess.Benchmarks.Fakes;
using Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.Benchmarks
{
    /// <summary>
    /// DbDataReader → 实体 纯映射性能基准。
    /// <para>使用预生成数据的 FakeDbDataReader 隔离数据库 I/O，三方对照：</para>
    /// <para>Xbc（表达式树映射委托 + 缓存）、Dapper（Parse，IL Emit 反序列化器 + 缓存）、手写映射（理论下限）。</para>
    /// <para>说明：Xbc 映射入口为异步（ReadAsync），Dapper/手写为同步，各自代表库的真实使用形态。</para>
    /// <para>表形态设计依据：窄表（5 列）取典型业务行、宽表（14 列）取复杂报表行，两者均刻意覆盖框架全部读取分支
    /// （值类型/引用类型/结构体/可空），超宽表（32 列）取现代业务系统规模（ERP/报表常见 30+ 列）并在每类类型上重复列以测量列数边际成本；
    /// 行规模 x1/x100/x1000 用于分离每查询固定开销与每行边际成本。</para>
    /// </summary>
    [MemoryDiagnoser]
    public class MappingBenchmarks
    {
        private static readonly string[] NarrowNames = { "Id", "Name", "Price", "Stock", "CreatedAt" };
        private static readonly Type[] NarrowTypes = { typeof(int), typeof(string), typeof(decimal), typeof(int), typeof(DateTime) };

        private static readonly string[] WideNames =
        {
            "Id", "Code", "Name", "Description", "CategoryId", "Volume", "Price", "Discount",
            "Weight", "IsActive", "CreatedAt", "UpdatedAt", "RowGuid", "Payload"
        };
        private static readonly Type[] WideTypes =
        {
            typeof(int), typeof(string), typeof(string), typeof(string), typeof(int), typeof(long),
            typeof(decimal), typeof(decimal), typeof(double), typeof(bool), typeof(DateTime),
            typeof(DateTime), typeof(Guid), typeof(byte[])
        };

        private static readonly string[] UltraWideNames =
        {
            "Id", "Code", "Name", "Description", "CategoryId", "Volume", "Price", "Discount",
            "Weight", "IsActive", "CreatedAt", "UpdatedAt", "RowGuid", "Payload",
            "Tag", "Remark", "Sku", "BarCode", "SortOrder", "ViewCount", "SalesCount", "Rating",
            "Length", "Width", "Height", "IsFeatured", "ManufacturedAt", "ExpiredAt", "BatchNo",
            "WarehouseId", "ExternalId", "Attachment"
        };
        private static readonly Type[] UltraWideTypes =
        {
            typeof(int), typeof(string), typeof(string), typeof(string), typeof(int), typeof(long),
            typeof(decimal), typeof(decimal), typeof(double), typeof(bool), typeof(DateTime),
            typeof(DateTime), typeof(Guid), typeof(byte[]),
            typeof(string), typeof(string), typeof(string), typeof(string), typeof(int), typeof(int),
            typeof(long), typeof(decimal), typeof(double), typeof(double), typeof(double), typeof(bool),
            typeof(DateTime), typeof(DateTime), typeof(string), typeof(int), typeof(Guid), typeof(byte[])
        };

        private object[][] narrowRows1;
        private object[][] narrowRows100;
        private object[][] narrowRows1000;
        private object[][] wideRows1;
        private object[][] wideRows100;
        private object[][] wideRows1000;
        private object[][] ultraWideRows1;
        private object[][] ultraWideRows100;
        private object[][] ultraWideRows1000;
        private byte[][] payloadCache;

        /// <summary>
        /// 预生成全部数据集，映射委托缓存在预热迭代中构建（BenchmarkDotNet 丢弃前若干次结果）。
        /// </summary>
        [GlobalSetup]
        public void Setup()
        {
            payloadCache = new byte[8][];
            for (int i = 0; i < payloadCache.Length; i++)
            {
                var payload = new byte[32];
                for (int j = 0; j < payload.Length; j++)
                    payload[j] = (byte)(i * 31 + j);
                payloadCache[i] = payload;
            }

            narrowRows1 = GenerateNarrowRows(1);
            narrowRows100 = GenerateNarrowRows(100);
            narrowRows1000 = GenerateNarrowRows(1000);
            wideRows1 = GenerateWideRows(1);
            wideRows100 = GenerateWideRows(100);
            wideRows1000 = GenerateWideRows(1000);
            ultraWideRows1 = GenerateUltraWideRows(1);
            ultraWideRows100 = GenerateUltraWideRows(100);
            ultraWideRows1000 = GenerateUltraWideRows(1000);
        }

        #region 窄表（5 列）
        [Benchmark(Description = "Xbc 映射 窄表x1")]
        public Task<List<BenchmarkEntities.NarrowEntity>> Xbc_Narrow_x1()
            => EntityMapper<BenchmarkEntities.NarrowEntity>.MapListAsync(CreateNarrowReader(narrowRows1));

        [Benchmark(Description = "Xbc 映射 窄表x100")]
        public Task<List<BenchmarkEntities.NarrowEntity>> Xbc_Narrow_x100()
            => EntityMapper<BenchmarkEntities.NarrowEntity>.MapListAsync(CreateNarrowReader(narrowRows100));

        [Benchmark(Description = "Xbc 映射 窄表x1000")]
        public Task<List<BenchmarkEntities.NarrowEntity>> Xbc_Narrow_x1000()
            => EntityMapper<BenchmarkEntities.NarrowEntity>.MapListAsync(CreateNarrowReader(narrowRows1000));

        [Benchmark(Description = "Dapper 映射 窄表x1")]
        public List<BenchmarkEntities.NarrowEntity> Dapper_Narrow_x1()
            => CreateNarrowReader(narrowRows1).Parse<BenchmarkEntities.NarrowEntity>().ToList();

        [Benchmark(Description = "Dapper 映射 窄表x100")]
        public List<BenchmarkEntities.NarrowEntity> Dapper_Narrow_x100()
            => CreateNarrowReader(narrowRows100).Parse<BenchmarkEntities.NarrowEntity>().ToList();

        [Benchmark(Description = "Dapper 映射 窄表x1000")]
        public List<BenchmarkEntities.NarrowEntity> Dapper_Narrow_x1000()
            => CreateNarrowReader(narrowRows1000).Parse<BenchmarkEntities.NarrowEntity>().ToList();

        [Benchmark(Description = "手写映射 窄表x100")]
        public List<BenchmarkEntities.NarrowEntity> Manual_Narrow_x100()
            => MapNarrowManual(CreateNarrowReader(narrowRows100));

        [Benchmark(Description = "手写映射 窄表x1000")]
        public List<BenchmarkEntities.NarrowEntity> Manual_Narrow_x1000()
            => MapNarrowManual(CreateNarrowReader(narrowRows1000));
        #endregion

        #region 宽表（14 列）
        [Benchmark(Description = "Xbc 映射 宽表x1")]
        public Task<List<BenchmarkEntities.WideEntity>> Xbc_Wide_x1()
            => EntityMapper<BenchmarkEntities.WideEntity>.MapListAsync(CreateWideReader(wideRows1));

        [Benchmark(Description = "Xbc 映射 宽表x100")]
        public Task<List<BenchmarkEntities.WideEntity>> Xbc_Wide_x100()
            => EntityMapper<BenchmarkEntities.WideEntity>.MapListAsync(CreateWideReader(wideRows100));

        [Benchmark(Description = "Xbc 映射 宽表x1000")]
        public Task<List<BenchmarkEntities.WideEntity>> Xbc_Wide_x1000()
            => EntityMapper<BenchmarkEntities.WideEntity>.MapListAsync(CreateWideReader(wideRows1000));

        [Benchmark(Description = "Dapper 映射 宽表x1")]
        public List<BenchmarkEntities.WideEntity> Dapper_Wide_x1()
            => CreateWideReader(wideRows1).Parse<BenchmarkEntities.WideEntity>().ToList();

        [Benchmark(Description = "Dapper 映射 宽表x100")]
        public List<BenchmarkEntities.WideEntity> Dapper_Wide_x100()
            => CreateWideReader(wideRows100).Parse<BenchmarkEntities.WideEntity>().ToList();

        [Benchmark(Description = "Dapper 映射 宽表x1000")]
        public List<BenchmarkEntities.WideEntity> Dapper_Wide_x1000()
            => CreateWideReader(wideRows1000).Parse<BenchmarkEntities.WideEntity>().ToList();

        [Benchmark(Description = "手写映射 宽表x100")]
        public List<BenchmarkEntities.WideEntity> Manual_Wide_x100()
            => MapWideManual(CreateWideReader(wideRows100));

        [Benchmark(Description = "手写映射 宽表x1000")]
        public List<BenchmarkEntities.WideEntity> Manual_Wide_x1000()
            => MapWideManual(CreateWideReader(wideRows1000));

        [Benchmark(Description = "Xbc 映射 宽表严格x100")]
        public Task<List<BenchmarkEntities.WideStrictEntity>> Xbc_WideStrict_x100()
            => EntityMapper<BenchmarkEntities.WideStrictEntity>.MapListAsync(CreateWideReader(wideRows100));

        [Benchmark(Description = "Xbc 映射 宽表严格x1000")]
        public Task<List<BenchmarkEntities.WideStrictEntity>> Xbc_WideStrict_x1000()
            => EntityMapper<BenchmarkEntities.WideStrictEntity>.MapListAsync(CreateWideReader(wideRows1000));
        #endregion

        #region 超宽表（32 列）
        [Benchmark(Description = "Xbc 映射 超宽表x1")]
        public Task<List<BenchmarkEntities.UltraWideEntity>> Xbc_UltraWide_x1()
            => EntityMapper<BenchmarkEntities.UltraWideEntity>.MapListAsync(CreateUltraWideReader(ultraWideRows1));

        [Benchmark(Description = "Xbc 映射 超宽表x100")]
        public Task<List<BenchmarkEntities.UltraWideEntity>> Xbc_UltraWide_x100()
            => EntityMapper<BenchmarkEntities.UltraWideEntity>.MapListAsync(CreateUltraWideReader(ultraWideRows100));

        [Benchmark(Description = "Xbc 映射 超宽表x1000")]
        public Task<List<BenchmarkEntities.UltraWideEntity>> Xbc_UltraWide_x1000()
            => EntityMapper<BenchmarkEntities.UltraWideEntity>.MapListAsync(CreateUltraWideReader(ultraWideRows1000));

        [Benchmark(Description = "Dapper 映射 超宽表x1")]
        public List<BenchmarkEntities.UltraWideEntity> Dapper_UltraWide_x1()
            => CreateUltraWideReader(ultraWideRows1).Parse<BenchmarkEntities.UltraWideEntity>().ToList();

        [Benchmark(Description = "Dapper 映射 超宽表x100")]
        public List<BenchmarkEntities.UltraWideEntity> Dapper_UltraWide_x100()
            => CreateUltraWideReader(ultraWideRows100).Parse<BenchmarkEntities.UltraWideEntity>().ToList();

        [Benchmark(Description = "Dapper 映射 超宽表x1000")]
        public List<BenchmarkEntities.UltraWideEntity> Dapper_UltraWide_x1000()
            => CreateUltraWideReader(ultraWideRows1000).Parse<BenchmarkEntities.UltraWideEntity>().ToList();

        [Benchmark(Description = "手写映射 超宽表x100")]
        public List<BenchmarkEntities.UltraWideEntity> Manual_UltraWide_x100()
            => MapUltraWideManual(CreateUltraWideReader(ultraWideRows100));

        [Benchmark(Description = "手写映射 超宽表x1000")]
        public List<BenchmarkEntities.UltraWideEntity> Manual_UltraWide_x1000()
            => MapUltraWideManual(CreateUltraWideReader(ultraWideRows1000));
        #endregion

        #region 辅助方法
        private FakeDbDataReader CreateNarrowReader(object[][] rows)
            => new FakeDbDataReader(NarrowNames, NarrowTypes, rows);

        private FakeDbDataReader CreateWideReader(object[][] rows)
            => new FakeDbDataReader(WideNames, WideTypes, rows);

        private FakeDbDataReader CreateUltraWideReader(object[][] rows)
            => new FakeDbDataReader(UltraWideNames, UltraWideTypes, rows);

        private static List<BenchmarkEntities.NarrowEntity> MapNarrowManual(FakeDbDataReader reader)
        {
            var list = new List<BenchmarkEntities.NarrowEntity>();
            while (reader.Read())
            {
                list.Add(new BenchmarkEntities.NarrowEntity
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Price = reader.GetDecimal(2),
                    Stock = reader.GetInt32(3),
                    CreatedAt = reader.GetDateTime(4)
                });
            }

            return list;
        }

        private static List<BenchmarkEntities.WideEntity> MapWideManual(FakeDbDataReader reader)
        {
            var list = new List<BenchmarkEntities.WideEntity>();
            while (reader.Read())
            {
                list.Add(new BenchmarkEntities.WideEntity
                {
                    Id = reader.GetInt32(0),
                    Code = reader.GetString(1),
                    Name = reader.GetString(2),
                    Description = reader.GetString(3),
                    CategoryId = reader.GetInt32(4),
                    Volume = reader.GetInt64(5),
                    Price = reader.GetDecimal(6),
                    Discount = reader.GetDecimal(7),
                    Weight = reader.GetDouble(8),
                    IsActive = reader.GetBoolean(9),
                    CreatedAt = reader.GetDateTime(10),
                    UpdatedAt = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                    RowGuid = reader.GetGuid(12),
                    Payload = (byte[])reader.GetValue(13)
                });
            }

            return list;
        }

        private static List<BenchmarkEntities.UltraWideEntity> MapUltraWideManual(FakeDbDataReader reader)
        {
            var list = new List<BenchmarkEntities.UltraWideEntity>();
            while (reader.Read())
            {
                list.Add(new BenchmarkEntities.UltraWideEntity
                {
                    Id = reader.GetInt32(0),
                    Code = reader.GetString(1),
                    Name = reader.GetString(2),
                    Description = reader.GetString(3),
                    CategoryId = reader.GetInt32(4),
                    Volume = reader.GetInt64(5),
                    Price = reader.GetDecimal(6),
                    Discount = reader.GetDecimal(7),
                    Weight = reader.GetDouble(8),
                    IsActive = reader.GetBoolean(9),
                    CreatedAt = reader.GetDateTime(10),
                    UpdatedAt = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                    RowGuid = reader.GetGuid(12),
                    Payload = (byte[])reader.GetValue(13),
                    Tag = reader.GetString(14),
                    Remark = reader.GetString(15),
                    Sku = reader.GetString(16),
                    BarCode = reader.GetString(17),
                    SortOrder = reader.GetInt32(18),
                    ViewCount = reader.GetInt32(19),
                    SalesCount = reader.GetInt64(20),
                    Rating = reader.GetDecimal(21),
                    Length = reader.GetDouble(22),
                    Width = reader.GetDouble(23),
                    Height = reader.GetDouble(24),
                    IsFeatured = reader.GetBoolean(25),
                    ManufacturedAt = reader.GetDateTime(26),
                    ExpiredAt = reader.IsDBNull(27) ? null : reader.GetDateTime(27),
                    BatchNo = reader.GetString(28),
                    WarehouseId = reader.GetInt32(29),
                    ExternalId = reader.GetGuid(30),
                    Attachment = (byte[])reader.GetValue(31)
                });
            }

            return list;
        }

        private static object[][] GenerateNarrowRows(int count)
        {
            var rows = new object[count][];
            for (int i = 0; i < count; i++)
            {
                rows[i] = new object[]
                {
                    i + 1,
                    "名称_" + i,
                    (i + 1) * 1.23m,
                    i % 97,
                    new DateTime(2026, 1, 1, 8, 0, 0).AddMinutes(i)
                };
            }

            return rows;
        }

        private object[][] GenerateWideRows(int count)
        {
            var rows = new object[count][];
            for (int i = 0; i < count; i++)
            {
                rows[i] = new object[]
                {
                    i + 1,
                    "C" + i.ToString("D8"),
                    "名称_" + i,
                    "描述文本_" + i,
                    i % 20,
                    (long)i * 1000,
                    (i + 1) * 0.98m,
                    (i % 5) * 0.1m,
                    i * 0.37,
                    i % 2 == 0,
                    new DateTime(2026, 1, 1, 8, 0, 0).AddHours(i),
                    i % 3 == 0 ? DBNull.Value : new DateTime(2026, 2, 1, 8, 0, 0).AddHours(i),
                    new Guid(i + 1, 0x1234, 0x5678, 0x9A, 0xBC, 0xDE, 0xF0, 0x11, 0x22, 0x33, 0x44),
                    payloadCache[i % payloadCache.Length]
                };
            }

            return rows;
        }

        private object[][] GenerateUltraWideRows(int count)
        {
            var rows = new object[count][];
            for (int i = 0; i < count; i++)
            {
                rows[i] = new object[]
                {
                    i + 1,
                    "C" + i.ToString("D8"),
                    "名称_" + i,
                    "描述文本_" + i,
                    i % 20,
                    (long)i * 1000,
                    (i + 1) * 0.98m,
                    (i % 5) * 0.1m,
                    i * 0.37,
                    i % 2 == 0,
                    new DateTime(2026, 1, 1, 8, 0, 0).AddHours(i),
                    i % 3 == 0 ? DBNull.Value : new DateTime(2026, 2, 1, 8, 0, 0).AddHours(i),
                    new Guid(i + 1, 0x1234, 0x5678, 0x9A, 0xBC, 0xDE, 0xF0, 0x11, 0x22, 0x33, 0x44),
                    payloadCache[i % payloadCache.Length],
                    "标签_" + i % 7,
                    "备注文本_" + i,
                    "SKU" + i.ToString("D10"),
                    "69000000" + i.ToString("D5"),
                    i % 100,
                    i * 13,
                    (long)i * 3,
                    (i % 50) * 0.1m,
                    i * 0.11,
                    i * 0.22,
                    i * 0.33,
                    i % 4 == 0,
                    new DateTime(2026, 3, 1, 8, 0, 0).AddMinutes(i),
                    i % 7 == 0 ? DBNull.Value : new DateTime(2026, 4, 1, 8, 0, 0).AddMinutes(i),
                    "B" + i.ToString("D6"),
                    i % 300,
                    new Guid(i + 2, 0x1111, 0x2222, 0x9A, 0xBC, 0xDE, 0xF0, 0x11, 0x22, 0x33, 0x55),
                    payloadCache[(i + 3) % payloadCache.Length]
                };
            }

            return rows;
        }
        #endregion
    }
}
