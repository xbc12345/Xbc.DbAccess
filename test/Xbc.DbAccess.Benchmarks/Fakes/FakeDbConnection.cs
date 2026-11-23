using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess.Benchmarks.Fakes
{
    /// <summary>
    /// 模拟数据库连接：Open/Close 无副作用，CreateCommand 返回 FakeDbCommand。
    /// </summary>
    public sealed class FakeDbConnection : DbConnection
    {
        private ConnectionState state = ConnectionState.Closed;

        /// <inheritdoc />
        public override string ConnectionString { get; set; } = string.Empty;

        /// <inheritdoc />
        public override string Database => "fake";

        /// <inheritdoc />
        public override string DataSource => "fake";

        /// <inheritdoc />
        public override string ServerVersion => "1.0.0-fake";

        /// <inheritdoc />
        public override ConnectionState State => state;

        /// <inheritdoc />
        public override void ChangeDatabase(string databaseName)
        {
        }

        /// <inheritdoc />
        public override void Open() => state = ConnectionState.Open;

        /// <inheritdoc />
        public override void Close() => state = ConnectionState.Closed;

        /// <inheritdoc />
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => throw new System.NotSupportedException();

        /// <inheritdoc />
        protected override DbCommand CreateDbCommand() => new FakeDbCommand { Connection = this };
    }
}
