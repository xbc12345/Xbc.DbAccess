using Xunit;

namespace Xbc.DbAccess.UnitTests.Extensions
{
    /// <summary>
    /// DbDataReaderExtensions 按列名取值的单元测试：各类型转换、DBNull → null 语义、GetFieldValue 泛型分支与守卫异常。
    /// </summary>
    public class DbDataReaderExtensionsTests : Infrastructure.SqliteTestBase
    {
        private const string SelectSql = @"SELECT
            'Tom' AS StrVal,
            1 AS BoolVal,
            2 AS ByteVal,
            300 AS Int16Val,
            4000 AS Int32Val,
            500000 AS Int64Val,
            1.5 AS FloatVal,
            2.5 AS DoubleVal,
            9.99 AS DecimalVal,
            '2026-01-02 03:04:05' AS DateVal,
            x'0123456789ABCDEF0123456789ABCDEF' AS GuidVal,
            NULL AS NullVal";

        private async Task<System.Data.Common.DbDataReader> ReadAsync()
        {
            var reader = await DbAccess.ExecuteReaderAsync(SelectSql);
            await reader.ReadAsync();
            return reader;
        }

        [Fact]
        public async Task IsDBNull_ChecksColumnBy()
        {
            using var reader = await ReadAsync();

            Assert.False(reader.IsDBNull("StrVal"));
            Assert.True(reader.IsDBNull("NullVal"));
        }

        [Fact]
        public async Task GetString_ReturnsValue_AndNullForDbNull()
        {
            using var reader = await ReadAsync();

            Assert.Equal("Tom", reader.GetString("StrVal"));
            Assert.Null(reader.GetString("NullVal"));
        }

        [Fact]
        public async Task GetBoolean_ReturnsValue_AndNullForDbNull()
        {
            using var reader = await ReadAsync();

            Assert.True(reader.GetBoolean("BoolVal"));
            Assert.Null(reader.GetBoolean("NullVal"));
        }

        [Fact]
        public async Task GetByte_ReturnsValue_AndNullForDbNull()
        {
            using var reader = await ReadAsync();

            Assert.Equal((byte)2, reader.GetByte("ByteVal"));
            Assert.Null(reader.GetByte("NullVal"));
        }

        [Fact]
        public async Task GetInt16_ReturnsValue_AndNullForDbNull()
        {
            using var reader = await ReadAsync();

            Assert.Equal((short)300, reader.GetInt16("Int16Val"));
            Assert.Null(reader.GetInt16("NullVal"));
        }

        [Fact]
        public async Task GetInt32_ReturnsValue_AndNullForDbNull()
        {
            using var reader = await ReadAsync();

            Assert.Equal(4000, reader.GetInt32("Int32Val"));
            Assert.Null(reader.GetInt32("NullVal"));
        }

        [Fact]
        public async Task GetInt64_ReturnsValue_AndNullForDbNull()
        {
            using var reader = await ReadAsync();

            Assert.Equal(500000L, reader.GetInt64("Int64Val"));
            Assert.Null(reader.GetInt64("NullVal"));
        }

        [Fact]
        public async Task GetFloat_ReturnsValue_AndNullForDbNull()
        {
            using var reader = await ReadAsync();

            Assert.Equal(1.5f, reader.GetFloat("FloatVal"));
            Assert.Null(reader.GetFloat("NullVal"));
        }

        [Fact]
        public async Task GetDouble_ReturnsValue_AndNullForDbNull()
        {
            using var reader = await ReadAsync();

            Assert.Equal(2.5d, reader.GetDouble("DoubleVal"));
            Assert.Null(reader.GetDouble("NullVal"));
        }

        [Fact]
        public async Task GetDecimal_ReturnsValue_AndNullForDbNull()
        {
            using var reader = await ReadAsync();

            Assert.Equal(9.99m, reader.GetDecimal("DecimalVal"));
            Assert.Null(reader.GetDecimal("NullVal"));
        }

        [Fact]
        public async Task GetGuid_ReturnsNullForDbNull()
        {
            using var reader = await ReadAsync();

            Assert.Null(reader.GetGuid("NullVal"));
        }

        [Fact]
        public async Task GetDateTime_ReturnsValue_AndNullForDbNull()
        {
            using var reader = await ReadAsync();

            Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), reader.GetDateTime("DateVal"));
            Assert.Null(reader.GetDateTime("NullVal"));
        }

        [Fact]
        public async Task GetFieldValue_NonNullable_ReturnsValue()
        {
            using var reader = await ReadAsync();

            // SQLite 数值统一返回 Int64：按实际域断言（窄化转换不在 GetFieldValue 语义内）
            Assert.Equal(4000L, reader.GetFieldValue<long>("Int32Val"));
            Assert.Equal("Tom", reader.GetFieldValue<string>("StrVal"));
        }

        [Fact]
        public async Task GetFieldValue_NullableOrReference_DbNull_ReturnsDefault()
        {
            using var reader = await ReadAsync();

            Assert.Null(reader.GetFieldValue<int?>("NullVal"));
            Assert.Null(reader.GetFieldValue<string>("NullVal"));
        }

        [Fact]
        public async Task GetFieldValue_NonNullable_DbNull_ThrowsInvalidCastException()
        {
            using var reader = await ReadAsync();

            var ex = Assert.Throws<InvalidCastException>(() => reader.GetFieldValue<int>("NullVal"));
            Assert.Contains("NullVal", ex.Message);
        }

        [Fact]
        public async Task GetValue_ReturnsRawValue()
        {
            using var reader = await ReadAsync();

            Assert.Equal(4000L, reader.GetValue("Int32Val"));
            Assert.Equal(DBNull.Value, reader.GetValue("NullVal"));
        }

        [Fact]
        public async Task Guards_ThrowForNullArguments()
        {
            using var reader = await ReadAsync();

            Assert.Throws<ArgumentNullException>(() => DbDataReaderExtensions.GetString(null, "StrVal"));
            Assert.Throws<ArgumentNullException>(() => reader.GetString(null));
            Assert.Throws<ArgumentException>(() => reader.GetString("  "));
            Assert.Throws<ArgumentNullException>(() => reader.IsDBNull(null));
            Assert.Throws<ArgumentNullException>(() => reader.GetBoolean(null));
            Assert.Throws<ArgumentNullException>(() => reader.GetByte(null));
            Assert.Throws<ArgumentNullException>(() => reader.GetInt16(null));
            Assert.Throws<ArgumentNullException>(() => reader.GetInt32(null));
            Assert.Throws<ArgumentNullException>(() => reader.GetInt64(null));
            Assert.Throws<ArgumentNullException>(() => reader.GetFloat(null));
            Assert.Throws<ArgumentNullException>(() => reader.GetDouble(null));
            Assert.Throws<ArgumentNullException>(() => reader.GetDecimal(null));
            Assert.Throws<ArgumentNullException>(() => reader.GetGuid(null));
            Assert.Throws<ArgumentNullException>(() => reader.GetDateTime(null));
            Assert.Throws<ArgumentNullException>(() => reader.GetFieldValue<string>(null));
            Assert.Throws<ArgumentNullException>(() => reader.GetValue(null));
        }
    }
}
