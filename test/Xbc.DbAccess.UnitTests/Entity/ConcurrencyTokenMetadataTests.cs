using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 并发令牌列（[Column(IsConcurrencyToken = true)]）的元数据构建测试：
    /// 识别、类型约束（byte[]）、与标识列互斥、唯一性约束。
    /// </summary>
    public class ConcurrencyTokenMetadataTests
    {
        #region 测试实体类

        /// <summary>
        /// 正确配置的令牌实体。
        /// </summary>
        [XbcDbEntity.Table("TokenUsers")]
        public class TokenUserEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;

            [XbcDbEntity.Column(IsConcurrencyToken = true)]
            public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        }

        /// <summary>
        /// 令牌列配置了列名映射。
        /// </summary>
        [XbcDbEntity.Table("TokenNamedUsers")]
        public class TokenNamedEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            [XbcDbEntity.Column("Version", IsConcurrencyToken = true)]
            public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        }

        /// <summary>
        /// 未配置令牌的普通实体。
        /// </summary>
        [XbcDbEntity.Table("PlainUsers")]
        public class PlainUserEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
        }

        /// <summary>
        /// 令牌列类型非 byte[]：构建元数据时应显式失败。
        /// </summary>
        [XbcDbEntity.Table("BadTokenUsers")]
        public class BadTokenTypeEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            [XbcDbEntity.Column(IsConcurrencyToken = true)]
            public int Version { get; set; }
        }

        /// <summary>
        /// 令牌列与标识列同时声明：语义矛盾（标识列由数据库生成、不进写路径列清单），应显式失败。
        /// </summary>
        [XbcDbEntity.Table("MixedTokenUsers")]
        public class MixedTokenEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            [XbcDbEntity.Column(IsConcurrencyToken = true, IsIdentity = true)]
            public byte[] Version { get; set; } = Array.Empty<byte>();
        }

        /// <summary>
        /// 多个令牌列：每表至多一个，应显式失败。
        /// </summary>
        [XbcDbEntity.Table("DuplicateTokenUsers")]
        public class DuplicateTokenEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            [XbcDbEntity.Column(IsConcurrencyToken = true)]
            public byte[] RowVersion { get; set; } = Array.Empty<byte>();

            [XbcDbEntity.Column(IsConcurrencyToken = true)]
            public byte[] RowVersion2 { get; set; } = Array.Empty<byte>();
        }

        /// <summary>
        /// 令牌列同时为主键：令牌在每次更新成功后换发新值，作为主键会破坏主键稳定性，应显式失败。
        /// </summary>
        [XbcDbEntity.Table("PkTokenUsers")]
        public class PkTokenEntity
        {
            [XbcDbEntity.PrimaryKey]
            [XbcDbEntity.Column(IsConcurrencyToken = true)]
            public byte[] Id { get; set; } = Array.Empty<byte>();
        }

        /// <summary>
        /// 令牌列配置类型转换器：令牌的生成与等值校验直接操作 Guid 字节（不经转换器），应显式失败。
        /// </summary>
        [XbcDbEntity.Table("TokenConverterUsers")]
        public class TokenConverterEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            [XbcDbEntity.Column(IsConcurrencyToken = true, ConverterType = typeof(TokenDummyConverter))]
            public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        }

        /// <summary>
        /// 令牌列配置 EnumAsString：令牌以原生字节读写，枚举字符串存储格式不生效，应显式失败。
        /// </summary>
        [XbcDbEntity.Table("TokenEnumStringUsers")]
        public class TokenEnumStringEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            [XbcDbEntity.Column(IsConcurrencyToken = true, EnumAsString = true)]
            public TokenStatus Status { get; set; }
        }

        /// <summary>
        /// 令牌列测试用枚举。
        /// </summary>
        public enum TokenStatus
        {
            Active,
            Inactive
        }

        /// <summary>
        /// 测试用占位转换器（互斥校验先于转换器解析执行，类型仅需非 null）。
        /// </summary>
        public class TokenDummyConverter
        {
        }

        #endregion

        [Fact]
        public void GetMetadata_配置令牌列_应识别为并发令牌()
        {
            var metadata = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(TokenUserEntity));

            Assert.NotNull(metadata.ConcurrencyTokenColumn);
            Assert.True(metadata.ConcurrencyTokenColumn.IsConcurrencyToken);
            Assert.Equal("RowVersion", metadata.ConcurrencyTokenColumn.ColumnName);
            Assert.False(metadata.ConcurrencyTokenColumn.IsPrimaryKey);
            Assert.False(metadata.ConcurrencyTokenColumn.IsIdentity);
        }

        [Fact]
        public void GetMetadata_令牌列名映射_应使用映射后的列名()
        {
            var metadata = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(TokenNamedEntity));

            Assert.NotNull(metadata.ConcurrencyTokenColumn);
            Assert.Equal("Version", metadata.ConcurrencyTokenColumn.ColumnName);
        }

        [Fact]
        public void GetMetadata_未配置令牌列_应为null()
        {
            var metadata = XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(PlainUserEntity));

            Assert.Null(metadata.ConcurrencyTokenColumn);
        }

        [Fact]
        public void GetMetadata_令牌列类型非byte数组_应抛出InvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(BadTokenTypeEntity)));
        }

        [Fact]
        public void GetMetadata_令牌列与标识列同时声明_应抛出InvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(MixedTokenEntity)));
        }

        [Fact]
        public void GetMetadata_多个令牌列_应抛出InvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(DuplicateTokenEntity)));
        }

        [Fact]
        public void GetMetadata_令牌列配置ConverterType_应抛出InvalidOperationException()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(TokenConverterEntity)));

            Assert.Contains("同时声明了 IsConcurrencyToken、ConverterType", ex.Message);
            Assert.Contains("列级开关至多其一", ex.Message);
        }

        [Fact]
        public void GetMetadata_令牌列配置EnumAsString_应抛出InvalidOperationException()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => XbcDbEntity.EntityMetadataCache.GetMetadata(typeof(TokenEnumStringEntity)));

            Assert.Contains("同时声明了 IsConcurrencyToken、EnumAsString", ex.Message);
            Assert.Contains("列级开关至多其一", ex.Message);
        }
    }
}
