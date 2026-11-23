using System;
using System.Data;
using System.Data.Common;

namespace Xbc.DbAccess.UnitTests.Transactions
{
    /// <summary>
    /// 用于测试的自定义 DbConnection mock 类。
    /// </summary>
    public class MockDbConnection : DbConnection
    {
        private readonly MockDbTransaction _transaction;
        private readonly MockDbCommand _command;
        private ConnectionState _state = ConnectionState.Closed;

        public MockDbConnection()
        {
            _transaction = new MockDbTransaction(this);
            _command = new MockDbCommand();
        }

        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "TestDatabase";

        public override string DataSource => "TestDataSource";

        public override string ServerVersion => "1.0";

        public override ConnectionState State => _state;

        public MockDbTransaction MockTransaction => _transaction;

        public MockDbCommand MockCommand => _command;

        public int BeginTransactionCallCount { get; private set; }

        public int CreateCommandCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public IsolationLevel? LastIsolationLevel { get; private set; }

        /// <summary>
        /// 用于模拟 BeginTransaction 抛出异常。
        /// </summary>
        public Exception BeginTransactionException { get; set; }

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
            _state = ConnectionState.Closed;
        }

        public override void Open()
        {
            _state = ConnectionState.Open;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            if (BeginTransactionException != null)
            {
                throw BeginTransactionException;
            }
            BeginTransactionCallCount++;
            LastIsolationLevel = isolationLevel;
            return _transaction;
        }

        protected override DbCommand CreateDbCommand()
        {
            CreateCommandCallCount++;
            return _command;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCallCount++;
                _transaction.Dispose();
                _command.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
