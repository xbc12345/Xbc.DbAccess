using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using Xunit;
using Xbc.DbAccess.UnitTests.Transactions;

namespace Xbc.DbAccess.UnitTests.Connections
{
    /// <summary>
    /// DbDataReaderWrapper 透传包装的单元测试：成员转发、生命周期管理（读取器/命令/连接释放）、
    /// Close 幂等与构造守卫（基于真实 SQLite 读取器）。
    /// </summary>
    public class DbDataReaderWrapperTests
    {
        #region 测试辅助

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
            x'414243' AS BlobVal,
            'abcdef' AS TextVal,
            NULL AS NullVal";

        private static SQLiteConnection CreateOpenConnection()
        {
            var connection = new SQLiteConnection("Data Source=:memory:");
            connection.Open();
            return connection;
        }

        private static (DbDataReaderWrapper Wrapper, SQLiteConnection Connection, SQLiteCommand Command) CreateWrapper(bool manageConnection = false, bool readFirst = true)
        {
            var connection = CreateOpenConnection();
            var command = connection.CreateCommand();
            command.CommandText = SelectSql;
            var reader = command.ExecuteReader();
            if (readFirst)
                reader.Read();
            return (new DbDataReaderWrapper(reader, connection, command, manageConnection), connection, command);
        }

        #endregion

        [Fact]
        public void Constructor_Guards_ThrowForNullArguments()
        {
            using var connection = CreateOpenConnection();
            var command = connection.CreateCommand();
            command.CommandText = SelectSql;
            using var reader = command.ExecuteReader();

            Assert.Throws<ArgumentNullException>(() => new DbDataReaderWrapper(null, connection, manageConnection: false));
            Assert.Throws<ArgumentNullException>(() => new DbDataReaderWrapper(reader, null, manageConnection: false));
        }

        [Fact]
        public void MetadataProperties_PassThrough()
        {
            var (wrapper, connection, command) = CreateWrapper();

            Assert.NotEqual(Guid.Empty, wrapper.ReaderId);
            Assert.Equal(0, wrapper.Depth);
            Assert.Equal(13, wrapper.FieldCount);
            Assert.Equal(13, wrapper.VisibleFieldCount);
            Assert.True(wrapper.HasRows);
            Assert.False(wrapper.IsClosed);
            Assert.Equal(-1, wrapper.RecordsAffected);

            Assert.Equal("StrVal", wrapper.GetName(0));
            Assert.Equal(0, wrapper.GetOrdinal("StrVal"));
            Assert.Equal(typeof(string), wrapper.GetFieldType(0));
            _ = wrapper.GetDataTypeName(0);// SQLite 返回值提供程序相关（可能为 null），仅验证透传不抛
            Assert.NotNull(wrapper.GetSchemaTable());

            Assert.Equal("Tom", wrapper["StrVal"]);
            Assert.Equal("Tom", wrapper[0]);

            connection.Dispose();
            command.Dispose();
        }

        [Fact]
        public void TypedGetters_PassThrough()
        {
            var (wrapper, connection, command) = CreateWrapper();

            Assert.True(wrapper.GetBoolean(1));
            Assert.Equal((byte)2, wrapper.GetByte(2));
            Assert.Equal((short)300, wrapper.GetInt16(3));
            Assert.Equal(4000, wrapper.GetInt32(4));
            Assert.Equal(500000L, wrapper.GetInt64(5));
            Assert.Equal(1.5f, wrapper.GetFloat(6));
            Assert.Equal(2.5d, wrapper.GetDouble(7));
            Assert.Equal(9.99m, wrapper.GetDecimal(8));
            Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), wrapper.GetDateTime(9));
            Assert.Equal("Tom", wrapper.GetString(0));
            Assert.Equal(4000L, wrapper.GetValue(4));

            var values = new object[13];
            Assert.Equal(13, wrapper.GetValues(values));
            Assert.Equal("Tom", values[0]);

            Assert.False(wrapper.IsDBNull(0));
            Assert.True(wrapper.IsDBNull(12));

            connection.Dispose();
            command.Dispose();
        }

        [Fact]
        public void StreamAndCharGetters_PassThrough()
        {
            var (wrapper, connection, command) = CreateWrapper();

            var buffer = new byte[3];
            Assert.Equal(3, wrapper.GetBytes(10, 0, buffer, 0, 3));
            Assert.Equal((byte)'A', buffer[0]);

            var chars = new char[6];
            Assert.Equal(6, wrapper.GetChars(11, 0, chars, 0, 6));
            Assert.Equal('a', chars[0]);
            // GetChar 直接返回 string 的提供程序（SQLite）不支持，字符读取走 GetChars

            using (var stream = wrapper.GetStream(10))
            {
                Assert.Equal(3, stream.Length);
            }

            using (var textReader = wrapper.GetTextReader(11))
            {
                Assert.Equal('a', textReader.Read());
            }

            connection.Dispose();
            command.Dispose();
        }

        [Fact]
        public async Task AsyncAndEnumeration_PassThrough()
        {
            // GetFieldValue 系需要当前行：使用已推进到首行的读取器
            var (wrapper, connection, command) = CreateWrapper();

            Assert.Equal("Tom", wrapper.GetFieldValue<string>(0));
            Assert.Equal("Tom", await wrapper.GetFieldValueAsync<string>(0));
            Assert.False(await wrapper.IsDBNullAsync(0));

            // 首行已消费：ReadAsync 推进到尽头，NextResultAsync 无后续结果集
            Assert.False(await wrapper.ReadAsync(CancellationToken.None));
            Assert.False(await wrapper.NextResultAsync(CancellationToken.None));

            connection.Dispose();
            command.Dispose();
        }

        [Fact]
        public void Enumeration_PassesThrough()
        {
            // 枚举器内部自行推进行位置：使用未消费行位置的读取器
            var (wrapper, connection, command) = CreateWrapper(readFirst: false);

            var enumerated = new List<object>();
            foreach (var row in wrapper)
                enumerated.Add(row);
            Assert.NotEmpty(enumerated);

            // 枚举后行集已读尽
            Assert.False(wrapper.Read());
            Assert.False(wrapper.NextResult());

            connection.Dispose();
            command.Dispose();
        }

        [Fact]
        public void ProviderSpecificGetters_PassThrough()
        {
            var (wrapper, connection, command) = CreateWrapper();

            Assert.Equal(typeof(long), wrapper.GetProviderSpecificFieldType(4));
            Assert.Equal(4000L, wrapper.GetProviderSpecificValue(4));

            var values = new object[13];
            Assert.Equal(13, wrapper.GetProviderSpecificValues(values));

            connection.Dispose();
            command.Dispose();
        }

        [Fact]
        public void Close_IsIdempotent()
        {
            var (wrapper, connection, command) = CreateWrapper();

            wrapper.Close();
            wrapper.Close();

            Assert.True(wrapper.IsClosed);

            connection.Dispose();
            command.Dispose();
        }

        [Fact]
        public void Dispose_WithCommand_ClearsParameters_AndKeepsConnectionOpen()
        {
            var connection = CreateOpenConnection();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT @Id AS Id";
            command.Parameters.AddWithValue("@Id", 1);
            var reader = command.ExecuteReader();
            reader.Read();

            var wrapper = new DbDataReaderWrapper(reader, connection, command, manageConnection: false);
            wrapper.Dispose();

            // 释放链路：关闭读取器、解除参数归属并释放命令；连接不由读取器管理
            // 命令已释放，后续访问按 ADO.NET 契约抛 ObjectDisposedException
            Assert.Throws<ObjectDisposedException>(() => _ = command.Parameters.Count);
            Assert.Equal(ConnectionState.Open, connection.State);

            connection.Dispose();
        }

        [Fact]
        public void Dispose_ManageConnection_DisposesConnection()
        {
            var (wrapper, connection, _) = CreateWrapper(manageConnection: true);

            wrapper.Dispose();

            // 连接已随读取器释放（释放后属性访问抛 ObjectDisposedException）
            Assert.Throws<ObjectDisposedException>(() => connection.State);

            wrapper.Dispose();// 幂等
        }

        #region stub 读取器（异常路径与稀有透传成员）

        /// <summary>
        /// 关闭时抛异常的读取器：验证释放链路对关闭失败的容错（记日志后继续清理，不中断连接/命令释放）。
        /// </summary>
        private sealed class CloseThrowsReader : DbDataReader
        {
            internal static readonly Guid StubGuid = new Guid("12345678-1234-1234-1234-123456789abc");

            public int DisposeCount { get; private set; }

            /// <summary>关闭失败开关：默认正常关闭，仅在关闭失败场景的用例中打开。</summary>
            public bool ThrowOnClose { get; set; }

            private bool closed;

            public override int Depth => 0;
            public override int FieldCount => 2;
            public override bool HasRows => true;
            // 关闭调用后（即使抛异常）驱动侧状态已流转为已关闭——与真实驱动行为一致
            public override bool IsClosed => closed;
            public override int RecordsAffected => -1;
            public override object this[int ordinal] => throw new NotSupportedException();
            public override object this[string name] => throw new NotSupportedException();

            public override bool Read() => false;
            public override bool NextResult() => false;

            public override void Close()
            {
                closed = true;
                if (ThrowOnClose)
                    throw new InvalidOperationException("模拟关闭失败");
            }

            protected override void Dispose(bool disposing)
            {
                // 不调用 base.Dispose：基类实现会触发 Close()，由 wrapper 决定是否调用 Close
                closed = true;
                DisposeCount++;
            }

            public override char GetChar(int ordinal) => 'A';
            public override Guid GetGuid(int ordinal) => StubGuid;
            // 注意：DbDataReader.GetColumnSchema() 非 virtual——基类实现经 GetSchemaTable()(null) 得出空集合，
            // 转传测试（GetColumnSchema_PassThrough）断言空集合同样锁定该路径

            public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
            public override byte GetByte(int ordinal) => throw new NotSupportedException();
            public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length) => throw new NotSupportedException();
            public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length) => throw new NotSupportedException();
            public override string GetDataTypeName(int ordinal) => "object";
            public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
            public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
            public override double GetDouble(int ordinal) => throw new NotSupportedException();
            public override float GetFloat(int ordinal) => throw new NotSupportedException();
            public override short GetInt16(int ordinal) => throw new NotSupportedException();
            public override int GetInt32(int ordinal) => throw new NotSupportedException();
            public override long GetInt64(int ordinal) => throw new NotSupportedException();
            public override string GetName(int ordinal) => ordinal == 0 ? "A" : "B";
            public override int GetOrdinal(string name) => 0;
            public override string GetString(int ordinal) => throw new NotSupportedException();
            public override object GetValue(int ordinal) => throw new NotSupportedException();
            public override int GetValues(object[] values) => 0;
            public override bool IsDBNull(int ordinal) => false;
            public override Type GetFieldType(int ordinal) => typeof(object);
            public override Type GetProviderSpecificFieldType(int ordinal) => typeof(object);
            public override object GetProviderSpecificValue(int ordinal) => throw new NotSupportedException();
            public override int GetProviderSpecificValues(object[] values) => 0;
            public override DataTable GetSchemaTable() => null;
            public override IEnumerator GetEnumerator() => ((IEnumerable)Array.Empty<object>()).GetEnumerator();
        }

        [Fact]
        public void TypedCharGuidGetters_PassThrough()
        {
            // GetChar / GetGuid 为透传成员：SQLite 读取器对部分形态不支持，用 stub 读取器锁定透传行为
            using var connection = new MockDbConnection();
            using var wrapper = new DbDataReaderWrapper(new CloseThrowsReader(), connection, manageConnection: false);

            Assert.Equal('A', wrapper.GetChar(0));
            Assert.Equal(CloseThrowsReader.StubGuid, wrapper.GetGuid(1));
        }

        [Fact]
        public void GetColumnSchema_PassThrough()
        {
            using var connection = new MockDbConnection();
            using var wrapper = new DbDataReaderWrapper(new CloseThrowsReader(), connection, manageConnection: false);

            // IDbColumnSchemaGenerator 实现：透传内部读取器的列架构（stub 返回空集合）
            var columns = wrapper.GetColumnSchema();

            Assert.NotNull(columns);
            Assert.Empty(columns);
        }

        [Fact]
        public void Dispose_CloseThrows_LogsAndContinuesDisposal()
        {
            var reader = new CloseThrowsReader { ThrowOnClose = true };
            var connection = new MockDbConnection();
            var wrapper = new DbDataReaderWrapper(reader, connection, manageConnection: true);

            // 关闭失败不外抛：记日志后继续释放内部读取器与连接（连接泄漏比关闭失败更严重）
            wrapper.Dispose();

            Assert.Equal(1, reader.DisposeCount);
            Assert.Equal(1, connection.DisposeCallCount);

            // 幂等：重复 Dispose 不再重复释放内部资源
            wrapper.Dispose();
            Assert.Equal(1, reader.DisposeCount);
            Assert.Equal(1, connection.DisposeCallCount);
        }

        #endregion
    }
}
