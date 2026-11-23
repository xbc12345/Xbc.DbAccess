using System.Data;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Extensions
{
    /// <summary>
    /// DataRowExtensions 按列名取值的单元测试：各类型转换、DBNull → null 语义、格式化解析与守卫异常。
    /// </summary>
    public class DataRowExtensionsTests
    {
        #region 测试数据

        private static DataTable BuildTable()
        {
            var table = new DataTable();
            table.Columns.Add("StrVal", typeof(string));
            table.Columns.Add("BoolVal", typeof(bool));
            table.Columns.Add("ByteVal", typeof(byte));
            table.Columns.Add("Int16Val", typeof(short));
            table.Columns.Add("Int32Val", typeof(int));
            table.Columns.Add("Int64Val", typeof(long));
            table.Columns.Add("FloatVal", typeof(float));
            table.Columns.Add("DoubleVal", typeof(double));
            table.Columns.Add("DecimalVal", typeof(decimal));
            table.Columns.Add("GuidVal", typeof(Guid));
            table.Columns.Add("DateVal", typeof(DateTime));
            table.Columns.Add("DateOffsetVal", typeof(DateTimeOffset));
            table.Columns.Add("TimeSpanVal", typeof(TimeSpan));

            table.Rows.Add(
                "Tom", true, (byte)2, (short)300, 4000, 500000L, 1.5f, 2.5d, 9.99m,
                new Guid("0123456789ABCDEF0123456789ABCDEF"),
                new DateTime(2026, 1, 2, 3, 4, 5),
                new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(8)),
                new TimeSpan(1, 2, 3));
            table.Rows.Add(table.NewRow());// 全列 DBNull

            return table;
        }

        #endregion

        [Fact]
        public void GetString_ReturnsValue_AndNullForDbNull()
        {
            var row = BuildTable().Rows[0];
            Assert.Equal("Tom", row.GetString("StrVal"));

            var nullRow = BuildTable().Rows[1];
            Assert.Null(nullRow.GetString("StrVal"));
        }

        [Fact]
        public void GetBoolean_ReturnsValue_AndNullForDbNull()
        {
            var row = BuildTable().Rows[0];
            Assert.True(row.GetBoolean("BoolVal"));

            var nullRow = BuildTable().Rows[1];
            Assert.Null(nullRow.GetBoolean("BoolVal"));
        }

        [Fact]
        public void GetByte_ReturnsValue_AndNullForDbNull()
        {
            var row = BuildTable().Rows[0];
            Assert.Equal((byte)2, row.GetByte("ByteVal"));

            var nullRow = BuildTable().Rows[1];
            Assert.Null(nullRow.GetByte("ByteVal"));
        }

        [Fact]
        public void GetInt16_ReturnsValue_AndNullForDbNull()
        {
            var row = BuildTable().Rows[0];
            Assert.Equal((short)300, row.GetInt16("Int16Val"));

            var nullRow = BuildTable().Rows[1];
            Assert.Null(nullRow.GetInt16("Int16Val"));
        }

        [Fact]
        public void GetInt32_ReturnsValue_AndNullForDbNull()
        {
            var row = BuildTable().Rows[0];
            Assert.Equal(4000, row.GetInt32("Int32Val"));

            var nullRow = BuildTable().Rows[1];
            Assert.Null(nullRow.GetInt32("Int32Val"));
        }

        [Fact]
        public void GetInt64_ReturnsValue_AndNullForDbNull()
        {
            var row = BuildTable().Rows[0];
            Assert.Equal(500000L, row.GetInt64("Int64Val"));

            var nullRow = BuildTable().Rows[1];
            Assert.Null(nullRow.GetInt64("Int64Val"));
        }

        [Fact]
        public void GetFloat_ReturnsValue_AndNullForDbNull()
        {
            var row = BuildTable().Rows[0];
            Assert.Equal(1.5f, row.GetFloat("FloatVal"));

            var nullRow = BuildTable().Rows[1];
            Assert.Null(nullRow.GetFloat("FloatVal"));
        }

        [Fact]
        public void GetDouble_ReturnsValue_AndNullForDbNull()
        {
            var row = BuildTable().Rows[0];
            Assert.Equal(2.5d, row.GetDouble("DoubleVal"));

            var nullRow = BuildTable().Rows[1];
            Assert.Null(nullRow.GetDouble("DoubleVal"));
        }

        [Fact]
        public void GetDecimal_ReturnsValue_AndNullForDbNull()
        {
            var row = BuildTable().Rows[0];
            Assert.Equal(9.99m, row.GetDecimal("DecimalVal"));

            var nullRow = BuildTable().Rows[1];
            Assert.Null(nullRow.GetDecimal("DecimalVal"));
        }

        [Fact]
        public void GetGuid_GuidInstancePassthrough_StringParse_AndNullForDbNull()
        {
            var table = BuildTable();
            var guid = new Guid("0123456789ABCDEF0123456789ABCDEF");

            // Guid 实例直接返回（不绕字符串解析）与字符串列解析两条路径
            Assert.Equal(guid, table.Rows[0].GetGuid("GuidVal"));

            var stringTable = new DataTable();
            stringTable.Columns.Add("GuidVal", typeof(string));
            stringTable.Rows.Add("01234567-89ab-cdef-0123-456789abcdef");
            Assert.Equal(guid, stringTable.Rows[0].GetGuid("GuidVal"));

            Assert.Null(BuildTable().Rows[1].GetGuid("GuidVal"));
        }

        [Fact]
        public void GetDateTime_Parse_Format_AndNullForDbNull()
        {
            var row = BuildTable().Rows[0];
            Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), row.GetDateTime("DateVal"));

            // 格式化路径：以字符串值按 ParseExact 解析
            var formatTable = new DataTable();
            formatTable.Columns.Add("DateVal", typeof(string));
            formatTable.Rows.Add("2026/01/02 03:04:05");
            Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), formatTable.Rows[0].GetDateTime("DateVal", "yyyy/MM/dd HH:mm:ss"));

            Assert.Null(BuildTable().Rows[1].GetDateTime("DateVal"));
        }

        [Fact]
        public void GetDateTimeOffset_Parse_Format_AndNullForDbNull()
        {
            var row = BuildTable().Rows[0];
            Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(8)), row.GetDateTimeOffset("DateOffsetVal"));

            var formatTable = new DataTable();
            formatTable.Columns.Add("DateOffsetVal", typeof(string));
            formatTable.Rows.Add("2026-01-02T03:04:05+08:00");
            Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(8)), formatTable.Rows[0].GetDateTimeOffset("DateOffsetVal", "yyyy-MM-dd'T'HH:mm:sszzz"));

            Assert.Null(BuildTable().Rows[1].GetDateTimeOffset("DateOffsetVal"));
        }

        [Fact]
        public void GetTimeSpan_Parse_Format_AndNullForDbNull()
        {
            var row = BuildTable().Rows[0];
            Assert.Equal(new TimeSpan(1, 2, 3), row.GetTimeSpan("TimeSpanVal"));

            var formatTable = new DataTable();
            formatTable.Columns.Add("TimeSpanVal", typeof(string));
            formatTable.Rows.Add("01:02:03");
            Assert.Equal(new TimeSpan(1, 2, 3), formatTable.Rows[0].GetTimeSpan("TimeSpanVal", @"hh\:mm\:ss"));

            Assert.Null(BuildTable().Rows[1].GetTimeSpan("TimeSpanVal"));
        }

        [Fact]
        public void GetFieldValue_NonNullable_ReturnsConvertedValue()
        {
            var row = BuildTable().Rows[0];

            // 值转换统一走 DbValueConvert：int 列读为 string、int 列读为 long
            Assert.Equal("Tom", row.GetFieldValue<string>("StrVal"));
            Assert.Equal(4000, row.GetFieldValue<int>("Int32Val"));
            Assert.Equal(500000L, row.GetFieldValue<long>("Int64Val"));
        }

        [Fact]
        public void GetFieldValue_Nullable_DbNull_ReturnsDefault()
        {
            var nullRow = BuildTable().Rows[1];

            // Nullable 类型 + DBNull 返回 default
            Assert.Null(nullRow.GetFieldValue<int?>("Int32Val"));
        }

        [Fact]
        public void GetFieldValue_NonNullable_DbNull_ThrowsInvalidCastException()
        {
            var nullRow = BuildTable().Rows[1];

            // 非可空类型（含 string 引用类型）+ DBNull 抛 InvalidCastException
            var ex = Assert.Throws<InvalidCastException>(() => nullRow.GetFieldValue<int>("Int32Val"));
            Assert.Contains("Int32Val", ex.Message);
            Assert.Throws<InvalidCastException>(() => nullRow.GetFieldValue<string>("StrVal"));
        }

        [Fact]
        public void Guards_ThrowForNullArguments()
        {
            var row = BuildTable().Rows[0];

            Assert.Throws<ArgumentNullException>(() => DataRowExtensions.GetString(null, "StrVal"));
            Assert.Throws<ArgumentNullException>(() => row.GetString(null));
            Assert.Throws<ArgumentException>(() => row.GetString("  "));
            Assert.Throws<ArgumentNullException>(() => row.GetFieldValue<string>(null));
            Assert.Throws<ArgumentNullException>(() => row.GetDateTime(null, "yyyy-MM-dd"));
            Assert.Throws<ArgumentNullException>(() => row.GetDateTimeOffset(null));
            Assert.Throws<ArgumentNullException>(() => row.GetTimeSpan(null));
            Assert.Throws<ArgumentNullException>(() => row.GetBoolean(null));
            Assert.Throws<ArgumentNullException>(() => row.GetByte(null));
            Assert.Throws<ArgumentNullException>(() => row.GetInt16(null));
            Assert.Throws<ArgumentNullException>(() => row.GetInt32(null));
            Assert.Throws<ArgumentNullException>(() => row.GetInt64(null));
            Assert.Throws<ArgumentNullException>(() => row.GetFloat(null));
            Assert.Throws<ArgumentNullException>(() => row.GetDouble(null));
            Assert.Throws<ArgumentNullException>(() => row.GetDecimal(null));
            Assert.Throws<ArgumentNullException>(() => row.GetGuid(null));
        }
    }
}
