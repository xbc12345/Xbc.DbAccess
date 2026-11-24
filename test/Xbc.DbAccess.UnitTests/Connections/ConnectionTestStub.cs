using Moq;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Connections
{
    /// <summary>
    /// 测试用数据库连接 stub：状态、打开/关闭/切换数据库行为与异常均可控，用于驱动连接池与激活逻辑的各分支。
    /// </summary>
    public class StubDbConnection : DbConnection
    {
        private ConnectionState state = ConnectionState.Closed;

        /// <summary>
        /// 当前数据库名称，初始为 StubDb；ChangeDatabase 成功后随之变更。
        /// </summary>
        public string CurrentDatabase { get; set; } = "StubDb";

        /// <summary>
        /// Open/OpenAsync 抛出的异常，null 表示打开成功。
        /// </summary>
        public Exception OpenException { get; set; }

        /// <summary>
        /// ChangeDatabase 抛出的异常，null 表示切换成功。
        /// </summary>
        public Exception ChangeDatabaseException { get; set; }

        /// <summary>
        /// Close 抛出的异常，null 表示关闭成功。
        /// </summary>
        public Exception CloseException { get; set; }

        /// <summary>
        /// Dispose 抛出的异常，null 表示释放成功。
        /// </summary>
        public Exception DisposeException { get; set; }

        /// <summary>
        /// Dispose 执行的延迟时长，用于拓宽释放窗口（竞态窗口测试）。
        /// </summary>
        public TimeSpan DisposeDelay { get; set; }

        public int OpenCallCount { get; private set; }

        public int CloseCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public string LastChangeDatabaseName { get; private set; }

        /// <summary>
        /// CreateCommand 返回的固定命令 stub，测试可预先配置其行为（如健康检查结果）。
        /// </summary>
        public StubDbCommand CommandStub { get; } = new StubDbCommand();

        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => CurrentDatabase;

        public override string DataSource => "StubDataSource";

        public override string ServerVersion => "1.0";

        public override ConnectionState State => state;

        /// <summary>
        /// 直接设置连接状态（用于模拟 Broken 等提供程序内部状态）。
        /// </summary>
        public void SetState(ConnectionState newState)
        {
            state = newState;
        }

        public override void Open()
        {
            OpenCallCount++;
            if (OpenException != null)
                throw OpenException;

            state = ConnectionState.Open;
        }

        public override void Close()
        {
            CloseCallCount++;
            if (CloseException != null)
                throw CloseException;

            state = ConnectionState.Closed;
        }

        public override void ChangeDatabase(string databaseName)
        {
            if (ChangeDatabaseException != null)
                throw ChangeDatabaseException;

            LastChangeDatabaseName = databaseName;
            CurrentDatabase = databaseName;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            return new StubDbTransaction(this, isolationLevel);
        }

        protected override DbCommand CreateDbCommand()
        {
            return CommandStub;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCallCount++;

                if (DisposeDelay > TimeSpan.Zero)
                    Thread.Sleep(DisposeDelay);

                if (DisposeException != null)
                    throw DisposeException;

                state = ConnectionState.Closed;
                base.Dispose(disposing);
            }
        }
    }

    /// <summary>
    /// 测试用数据库命令 stub：ExecuteScalar 结果与异常可控，用于驱动连接健康检查分支。
    /// </summary>
    public class StubDbCommand : DbCommand
    {
        /// <summary>
        /// ExecuteScalar 返回值，默认 1（模拟健康检查 SELECT 1 成功）。
        /// </summary>
        public object ScalarResult { get; set; } = 1;

        /// <summary>
        /// ExecuteScalar 抛出的异常，null 表示执行成功。
        /// </summary>
        public Exception ExecuteScalarException { get; set; }

        public int ScalarCallCount { get; private set; }

        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        public override bool DesignTimeVisible { get; set; }

        protected override DbConnection DbConnection { get; set; }

        // 注意：全限定 System.Data.Common 的参数集合类型——Xbc.DbAccess 命名空间下存在同名类型（框架自有 DbParameterCollection），
        // 非限定名会被父命名空间解析优先抢占导致 override 签名不匹配
        protected override System.Data.Common.DbParameterCollection DbParameterCollection => null!;

        protected override DbTransaction DbTransaction { get; set; }

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

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            return null!;
        }

        public override int ExecuteNonQuery()
        {
            return 0;
        }

        public override object ExecuteScalar()
        {
            ScalarCallCount++;
            if (ExecuteScalarException != null)
                throw ExecuteScalarException;

            return ScalarResult;
        }
    }

    /// <summary>
    /// 测试用数据库事务 stub：不做任何真实事务操作，仅用于挂起事务判定分支。
    /// </summary>
    public class StubDbTransaction : DbTransaction
    {
        public StubDbTransaction(DbConnection connection, IsolationLevel isolationLevel)
        {
            DbConnection = connection;
            IsolationLevel = isolationLevel;
        }

        protected override DbConnection DbConnection { get; }

        public override IsolationLevel IsolationLevel { get; }

        public override void Commit()
        {
        }

        public override void Rollback()
        {
        }
    }

    /// <summary>
    /// 连接测试共享辅助方法。
    /// </summary>
    public static class ConnectionTestHelper
    {
        /// <summary>
        /// 创建返回全新 StubDbConnection 的提供程序工厂 mock（每次调用产生独立实例）。
        /// </summary>
        public static Mock<DbProviderFactory> CreateStubFactory()
        {
            var factory = new Mock<DbProviderFactory>();
            factory.Setup(static f => f.CreateConnection()).Returns(() => new StubDbConnection());
            return factory;
        }

        /// <summary>
        /// 创建启用统计的连接池选项；清理间隔调大以避免定时器自动触发清理干扰测试断言。
        /// </summary>
        public static DbConnectionOptions CreatePoolOptions(
            int minPoolSize = 0,
            int maxPoolSize = 10,
            TimeSpan? maxIdleTime = null,
            bool enableStatistics = true)
        {
            return new DbConnectionOptions
            {
                ConnectionString = "Server=test;Database=test;",
                EnableStatistics = enableStatistics,
                PoolOptions = new DbConnectionPoolOptions
                {
                    MinPoolSize = minPoolSize,
                    MaxPoolSize = maxPoolSize,
                    ConnectionWaitTimeout = TimeSpan.FromSeconds(5),
                    CleanupInterval = TimeSpan.FromMinutes(10),
                    MaxIdleTime = maxIdleTime ?? TimeSpan.FromMinutes(15)
                }
            };
        }

        /// <summary>
        /// 等待管理器进入释放中状态（IsDisposing），为 Dispose 与归还/销毁事件的竞态窗口测试建立确定性前提。
        /// </summary>
        public static void WaitForDisposing(DisposableBase manager)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!manager.IsDisposing && stopwatch.ElapsedMilliseconds < 5000)
                Thread.Sleep(5);

            Assert.True(manager.IsDisposing, "管理器未进入释放中状态，竞态窗口测试前提不成立。");
        }
    }
}
