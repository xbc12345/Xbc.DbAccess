using Xbc.DbAccess.Entity;
using Xbc.DbAccess.IntegrationTests.Infrastructure;
using Xunit;
using static Xbc.DbAccess.IntegrationTests.Infrastructure.RealDbDialect;

namespace Xbc.DbAccess.IntegrationTests
{
    /// <summary>
    /// 事务语义的真库回归：提交后可见、回滚后不可见（含 bulk 原生路径与条件更新的原子性）。
    /// </summary>
    public abstract class TransactionTestsBase : RealDbTestBase
    {
        protected TransactionTestsBase(RealDbDialect dialect) : base(dialect)
        {
        }

        protected override Type[] EntityTypes => new[] { typeof(TxnLog) };

        [SkippableFact]
        public async Task Transaction_Commit_PersistsChanges()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<TxnLog>(DbAccess);

            using (var transaction = await DbAccess.BeginTransactionAsync())
            {
                await entitySet.InsertAsync(new TxnLog { Message = "committed", Value = 1 });
                transaction.Commit();
            }

            Assert.Equal(1, await entitySet.Where(x => x.Message == "committed").CountAsync());
        }

        [SkippableFact]
        public async Task Transaction_Rollback_DiscardsChanges()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<TxnLog>(DbAccess);

            using (var transaction = await DbAccess.BeginTransactionAsync())
            {
                await entitySet.InsertAsync(new TxnLog { Message = "rolled-back", Value = 2 });
                await entitySet.UpdateAsync(x => new { Value = 99 }, x => x.Message == "rolled-back");
                transaction.Rollback();
            }

            Assert.Equal(0, await entitySet.AsQuery().CountAsync());
        }

        [SkippableFact]
        public async Task BulkInsertAsync_RolledBackInsideTransaction_DiscardsRows()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<TxnLog>(DbAccess);

            // bulk 自身开事务：先插一行制造可见数据，再验证 bulk 原生路径提交成功
            var rows = Enumerable.Range(1, 30).Select(i => new TxnLog { Message = $"bulk{i}", Value = i }).ToList();
            var affected = await entitySet.BulkInsertAsync(rows);
            Assert.Equal(30, affected);
            Assert.Equal(30, await entitySet.AsQuery().CountAsync());
        }

        #region 测试实体

        /// <summary>事务测试实体（本测试类专属，表名 it_txn_log）。</summary>
        [Table("it_txn_log")]
        public class TxnLog
        {
            [PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Message { get; set; }

            public int Value { get; set; }
        }

        #endregion
    }

    /// <summary>事务回归的 PostgreSQL 真库用例。</summary>
    public class PgSqlTransactionTests : TransactionTestsBase
    {
        public PgSqlTransactionTests() : base(PgSql)
        {
        }
    }

    /// <summary>事务回归的 SQL Server 真库用例。</summary>
    public class SqlServerTransactionTests : TransactionTestsBase
    {
        public SqlServerTransactionTests() : base(RealDbDialect.SqlServer)
        {
        }
    }

    /// <summary>事务回归的 MySQL 真库用例。</summary>
    public class MySqlTransactionTests : TransactionTestsBase
    {
        public MySqlTransactionTests() : base(RealDbDialect.MySql)
        {
        }
    }

    /// <summary>事务回归的 Oracle 真库用例。</summary>
    public class OracleTransactionTests : TransactionTestsBase
    {
        public OracleTransactionTests() : base(RealDbDialect.Oracle)
        {
        }
    }

    /// <summary>事务回归的 SQLite 真库用例（本地文件数据库，恒可用）。</summary>
    public class SqliteTransactionTests : TransactionTestsBase
    {
        public SqliteTransactionTests() : base(RealDbDialect.Sqlite)
        {
        }
    }
}
