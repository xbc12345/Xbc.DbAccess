namespace Xbc.DbAccess.Benchmarks
{
    /// <summary>
    /// 基准测试实体与参数对象（按模块聚合的 DTO 文件）。
    /// </summary>
    public static class BenchmarkEntities
    {
        /// <summary>
        /// 窄表实体（5 列），典型业务表结构。
        /// </summary>
        public class NarrowEntity
        {
            public int Id { get; set; }

            public string Name { get; set; }

            public decimal Price { get; set; }

            public int Stock { get; set; }

            public DateTime CreatedAt { get; set; }
        }

        /// <summary>
        /// 宽表实体（14 列，混合类型），模拟复杂报表行。
        /// </summary>
        public class WideEntity
        {
            public int Id { get; set; }

            public string Code { get; set; }

            public string Name { get; set; }

            public string Description { get; set; }

            public int CategoryId { get; set; }

            public long Volume { get; set; }

            public decimal Price { get; set; }

            public decimal Discount { get; set; }

            public double Weight { get; set; }

            public bool IsActive { get; set; }

            public DateTime CreatedAt { get; set; }

            public DateTime? UpdatedAt { get; set; }

            public Guid RowGuid { get; set; }

            public byte[] Payload { get; set; }
        }

        /// <summary>
        /// 宽表实体（严格模式），非可空值类型列声明 <see cref="Xbc.DbAccess.Entity.DbNullHandling.ProviderException"/> 跳过 NULL 检查，
        /// 列结构与 <see cref="WideEntity"/> 完全一致。
        /// </summary>
        public class WideStrictEntity
        {
            public int Id { get; set; }

            public string Code { get; set; }

            public string Name { get; set; }

            public string Description { get; set; }

            [Xbc.DbAccess.Entity.Column(DbNullHandling = Xbc.DbAccess.Entity.DbNullHandling.ProviderException)]
            public int CategoryId { get; set; }

            [Xbc.DbAccess.Entity.Column(DbNullHandling = Xbc.DbAccess.Entity.DbNullHandling.ProviderException)]
            public long Volume { get; set; }

            [Xbc.DbAccess.Entity.Column(DbNullHandling = Xbc.DbAccess.Entity.DbNullHandling.ProviderException)]
            public decimal Price { get; set; }

            [Xbc.DbAccess.Entity.Column(DbNullHandling = Xbc.DbAccess.Entity.DbNullHandling.ProviderException)]
            public decimal Discount { get; set; }

            [Xbc.DbAccess.Entity.Column(DbNullHandling = Xbc.DbAccess.Entity.DbNullHandling.ProviderException)]
            public double Weight { get; set; }

            [Xbc.DbAccess.Entity.Column(DbNullHandling = Xbc.DbAccess.Entity.DbNullHandling.ProviderException)]
            public bool IsActive { get; set; }

            [Xbc.DbAccess.Entity.Column(DbNullHandling = Xbc.DbAccess.Entity.DbNullHandling.ProviderException)]
            public DateTime CreatedAt { get; set; }

            public DateTime? UpdatedAt { get; set; }

            [Xbc.DbAccess.Entity.Column(DbNullHandling = Xbc.DbAccess.Entity.DbNullHandling.ProviderException)]
            public Guid RowGuid { get; set; }

            public byte[] Payload { get; set; }
        }

        /// <summary>
        /// 超宽表实体（32 列，混合类型），模拟现代业务系统的宽表行（ERP/报表场景常见 30+ 列）。
        /// <para>列设计延续 <see cref="WideEntity"/> 的类型全覆盖原则，并给每类类型增加重复列，用于测量映射的列数边际成本。</para>
        /// </summary>
        public class UltraWideEntity
        {
            public int Id { get; set; }

            public string Code { get; set; }

            public string Name { get; set; }

            public string Description { get; set; }

            public int CategoryId { get; set; }

            public long Volume { get; set; }

            public decimal Price { get; set; }

            public decimal Discount { get; set; }

            public double Weight { get; set; }

            public bool IsActive { get; set; }

            public DateTime CreatedAt { get; set; }

            public DateTime? UpdatedAt { get; set; }

            public Guid RowGuid { get; set; }

            public byte[] Payload { get; set; }

            public string Tag { get; set; }

            public string Remark { get; set; }

            public string Sku { get; set; }

            public string BarCode { get; set; }

            public int SortOrder { get; set; }

            public int ViewCount { get; set; }

            public long SalesCount { get; set; }

            public decimal Rating { get; set; }

            public double Length { get; set; }

            public double Width { get; set; }

            public double Height { get; set; }

            public bool IsFeatured { get; set; }

            public DateTime ManufacturedAt { get; set; }

            public DateTime? ExpiredAt { get; set; }

            public string BatchNo { get; set; }

            public int WarehouseId { get; set; }

            public Guid ExternalId { get; set; }

            public byte[] Attachment { get; set; }
        }

        /// <summary>
        /// 端到端执行基准实体（SQLite 实表，4 列）。
        /// </summary>
        public class BenchRow
        {
            public int Id { get; set; }

            public string Name { get; set; }

            public double Price { get; set; }

            public DateTime CreatedAt { get; set; }
        }
    }

    /// <summary>
    /// 参数构建基准用的固定参数对象（1/10/50 个属性），预创建避免测量对象分配。
    /// </summary>
    public static class BenchmarkParams
    {
        /// <summary>1 参数对象。</summary>
        public static readonly object Params1 = new { P0 = 1 };

        /// <summary>10 参数对象。</summary>
        public static readonly object Params10 = new
        {
            P0 = 1,
            P1 = "值1",
            P2 = 2.5,
            P3 = 3,
            P4 = "值4",
            P5 = 5.5,
            P6 = 6,
            P7 = "值7",
            P8 = 8.5,
            P9 = 9
        };

        /// <summary>50 参数对象。</summary>
        public static readonly object Params50 = new
        {
            P0 = 1,
            P1 = "值1",
            P2 = 2.5,
            P3 = 3,
            P4 = "值4",
            P5 = 5.5,
            P6 = 6,
            P7 = "值7",
            P8 = 8.5,
            P9 = 9,
            P10 = 10,
            P11 = "值11",
            P12 = 12.5,
            P13 = 13,
            P14 = "值14",
            P15 = 15.5,
            P16 = 16,
            P17 = "值17",
            P18 = 18.5,
            P19 = 19,
            P20 = 20,
            P21 = "值21",
            P22 = 22.5,
            P23 = 23,
            P24 = "值24",
            P25 = 25.5,
            P26 = 26,
            P27 = "值27",
            P28 = 28.5,
            P29 = 29,
            P30 = 30,
            P31 = "值31",
            P32 = 32.5,
            P33 = 33,
            P34 = "值34",
            P35 = 35.5,
            P36 = 36,
            P37 = "值37",
            P38 = 38.5,
            P39 = 39,
            P40 = 40,
            P41 = "值41",
            P42 = 42.5,
            P43 = 43,
            P44 = "值44",
            P45 = 45.5,
            P46 = 46,
            P47 = "值47",
            P48 = 48.5,
            P49 = 49
        };
    }
}
