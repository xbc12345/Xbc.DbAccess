using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess.Benchmarks.Fakes
{
    /// <summary>
    /// 模拟数据库命令：ExecuteNonQuery 固定返回 1、ExecuteScalar 返回 0、ExecuteReader 返回空结果集。
    /// <para>无任何 I/O，用于隔离诊断（span/指标）开销与执行管道开销。</para>
    /// </summary>
    public sealed class FakeDbCommand : DbCommand
    {
        private readonly FakeDbParameterCollection parameters = new FakeDbParameterCollection();

        /// <inheritdoc />
        public override string CommandText { get; set; } = string.Empty;

        /// <inheritdoc />
        public override int CommandTimeout { get; set; }

        /// <inheritdoc />
        public override CommandType CommandType { get; set; } = CommandType.Text;

        /// <inheritdoc />
        public override UpdateRowSource UpdatedRowSource { get; set; }

        /// <inheritdoc />
        protected override DbConnection DbConnection { get; set; }

        /// <inheritdoc />
        protected override System.Data.Common.DbParameterCollection DbParameterCollection => parameters;

        /// <inheritdoc />
        protected override DbTransaction DbTransaction { get; set; }

        /// <inheritdoc />
        public override bool DesignTimeVisible { get; set; }

        /// <inheritdoc />
        public override void Cancel()
        {
        }

        /// <inheritdoc />
        public override void Prepare()
        {
        }

        /// <inheritdoc />
        protected override DbParameter CreateDbParameter() => new FakeDbParameter();

        /// <inheritdoc />
        public override int ExecuteNonQuery() => 1;

        /// <inheritdoc />
        public override object ExecuteScalar() => 0L;

        /// <inheritdoc />
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => FakeDbDataReader.Empty();
    }
}
