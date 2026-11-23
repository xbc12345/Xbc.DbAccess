using System;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess.UnitTests.Transactions
{
    /// <summary>
    /// 用于测试的自定义 DbCommand mock 类。
    /// </summary>
    public class MockDbCommand : DbCommand
    {
        public int ExecuteNonQueryCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        public override bool DesignTimeVisible { get; set; }

        protected override DbConnection DbConnection { get; set; }

        protected override System.Data.Common.DbParameterCollection DbParameterCollection => null!;

        protected override DbTransaction DbTransaction { get; set; }

        public int LastExecuteNonQueryResult { get; set; } = 0;

        public Exception ExecuteNonQueryException { get; set; }

        public override void Cancel()
        {
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter()
        {
            return null!;
        }

        public override int ExecuteNonQuery()
        {
            ExecuteNonQueryCallCount++;
            if (ExecuteNonQueryException != null)
            {
                throw ExecuteNonQueryException;
            }
            return LastExecuteNonQueryResult;
        }

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            ExecuteNonQueryCallCount++;
            if (ExecuteNonQueryException != null)
            {
                throw ExecuteNonQueryException;
            }
            return Task.FromResult(LastExecuteNonQueryResult);
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            return null!;
        }

        public override object ExecuteScalar()
        {
            return null!;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCallCount++;
            }
            base.Dispose(disposing);
        }
    }
}
