using System.Collections;
using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess.Benchmarks.Fakes
{
    /// <summary>
    /// 预生成数据的 DbDataReader 模拟实现。
    /// <para>数据行在构造前生成并传入，Read() 仅移动游标，用于隔离数据库 I/O、纯测映射性能。</para>
    /// <para>Xbc 映射器（表达式树）与 Dapper（Parse）共用同一读取器，保证对照公平。</para>
    /// </summary>
    public sealed class FakeDbDataReader : DbDataReader
    {
        private readonly string[] names;
        private readonly Type[] types;
        private readonly object[][] rows;
        private int cursor = -1;

        /// <summary>
        /// 初始化模拟读取器。
        /// </summary>
        /// <param name="names">列名数组。</param>
        /// <param name="types">列类型数组（与列名一一对应）。</param>
        /// <param name="rows">预生成的数据行（每行为与列数等长的对象数组）。</param>
        public FakeDbDataReader(string[] names, Type[] types, object[][] rows)
        {
            this.names = names;
            this.types = types;
            this.rows = rows;
        }

        /// <summary>
        /// 创建空结果集的读取器（无列、无行）。
        /// </summary>
        /// <returns></returns>
        public static FakeDbDataReader Empty()
        {
            return new FakeDbDataReader(Array.Empty<string>(), Array.Empty<Type>(), Array.Empty<object[]>());
        }

        /// <inheritdoc />
        public override int Depth => 0;

        /// <inheritdoc />
        public override int FieldCount => names.Length;

        /// <inheritdoc />
        public override bool HasRows => rows.Length > 0;

        /// <inheritdoc />
        public override bool IsClosed => false;

        /// <inheritdoc />
        public override int RecordsAffected => -1;

        /// <inheritdoc />
        public override object this[int ordinal] => rows[cursor][ordinal];

        /// <inheritdoc />
        public override object this[string name] => rows[cursor][GetOrdinal(name)];

        /// <inheritdoc />
        public override bool Read()
        {
            cursor++;
            return cursor < rows.Length;
        }

        /// <inheritdoc />
        public override bool NextResult() => false;

        /// <inheritdoc />
        public override void Close()
        {
        }

        /// <inheritdoc />
        public override string GetName(int ordinal) => names[ordinal];

        /// <inheritdoc />
        public override string GetDataTypeName(int ordinal) => types[ordinal].Name;

        /// <inheritdoc />
        public override Type GetFieldType(int ordinal) => types[ordinal];

        /// <inheritdoc />
        public override int GetOrdinal(string name)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            throw new IndexOutOfRangeException($"列 '{name}' 不存在。");
        }

        /// <inheritdoc />
        public override bool IsDBNull(int ordinal) => rows[cursor][ordinal] is DBNull;

        /// <inheritdoc />
        public override object GetValue(int ordinal) => rows[cursor][ordinal];

        /// <inheritdoc />
        public override int GetValues(object[] values)
        {
            var row = rows[cursor];
            var count = Math.Min(values.Length, row.Length);
            for (int i = 0; i < count; i++)
                values[i] = row[i];
            return count;
        }

        /// <inheritdoc />
        public override bool GetBoolean(int ordinal) => (bool)rows[cursor][ordinal];

        /// <inheritdoc />
        public override byte GetByte(int ordinal) => (byte)rows[cursor][ordinal];

        /// <inheritdoc />
        public override char GetChar(int ordinal) => (char)rows[cursor][ordinal];

        /// <inheritdoc />
        public override DateTime GetDateTime(int ordinal) => (DateTime)rows[cursor][ordinal];

        /// <inheritdoc />
        public override decimal GetDecimal(int ordinal) => (decimal)rows[cursor][ordinal];

        /// <inheritdoc />
        public override double GetDouble(int ordinal) => (double)rows[cursor][ordinal];

        /// <inheritdoc />
        public override float GetFloat(int ordinal) => (float)rows[cursor][ordinal];

        /// <inheritdoc />
        public override Guid GetGuid(int ordinal) => (Guid)rows[cursor][ordinal];

        /// <inheritdoc />
        public override short GetInt16(int ordinal) => (short)rows[cursor][ordinal];

        /// <inheritdoc />
        public override int GetInt32(int ordinal) => (int)rows[cursor][ordinal];

        /// <inheritdoc />
        public override long GetInt64(int ordinal) => (long)rows[cursor][ordinal];

        /// <inheritdoc />
        public override string GetString(int ordinal) => (string)rows[cursor][ordinal];

        /// <inheritdoc />
        public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public override IEnumerator GetEnumerator() => throw new NotSupportedException();

        /// <inheritdoc />
        public override DataTable GetSchemaTable() => null;
    }
}
