using System;
using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess.UnitTests.Transactions
{
    /// <summary>
    /// 用于测试的自定义 DbTransaction mock 类。
    /// </summary>
    public class MockDbTransaction : DbTransaction
    {
        private readonly DbConnection _connection;
        private readonly IsolationLevel _isolationLevel;

        public MockDbTransaction(DbConnection connection, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _isolationLevel = isolationLevel;
        }

        public override IsolationLevel IsolationLevel => _isolationLevel;

        protected override DbConnection DbConnection => _connection;

        public int CommitCallCount { get; private set; }

        public int RollbackCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public override void Commit()
        {
            CommitCallCount++;
        }

        public override void Rollback()
        {
            RollbackCallCount++;
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
