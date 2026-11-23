using Xbc.DbAccess.Entity;
using Xbc.DbAccess.IntegrationTests.Infrastructure;
using Xunit;
using static Xbc.DbAccess.IntegrationTests.Infrastructure.RealDbDialect;

namespace Xbc.DbAccess.IntegrationTests
{
    /// <summary>
    /// 乐观并发令牌的真库回归：令牌自动生成、更新换发、陈旧令牌冲突抛 <see cref="DbConcurrencyException"/>。
    /// </summary>
    public abstract class ConcurrencyTestsBase : RealDbTestBase
    {
        protected ConcurrencyTestsBase(RealDbDialect dialect) : base(dialect)
        {
        }

        protected override Type[] EntityTypes => new[] { typeof(VersionedRow) };

        [SkippableFact]
        public async Task InsertAsync_GeneratesInitialToken()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<VersionedRow>(DbAccess);
            var row = new VersionedRow { Name = "A" };
            await entitySet.InsertAsync(row);

            var found = await entitySet.FindAsync(row.Id);
            Assert.NotNull(found);
            Assert.NotNull(found!.RowVersion);
            Assert.NotEmpty(found.RowVersion);
        }

        [SkippableFact]
        public async Task UpdateAsync_RotatesToken()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<VersionedRow>(DbAccess);
            var row = new VersionedRow { Name = "A" };
            await entitySet.InsertAsync(row);

            var loaded1 = await entitySet.FindAsync(row.Id);
            var tokenBefore = loaded1!.RowVersion.ToArray();

            loaded1.Name = "A2";
            await entitySet.UpdateAsync(loaded1);

            var loaded2 = await entitySet.FindAsync(row.Id);
            Assert.Equal("A2", loaded2!.Name);
            Assert.NotEqual(tokenBefore, loaded2.RowVersion);
        }

        [SkippableFact]
        public async Task UpdateAsync_StaleToken_Throws()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<VersionedRow>(DbAccess);
            var row = new VersionedRow { Name = "A" };
            await entitySet.InsertAsync(row);

            // 两个客户端加载同一行，先后更新：后者持有陈旧令牌，应检测到并发冲突
            var client1 = await entitySet.FindAsync(row.Id);
            var client2 = await entitySet.FindAsync(row.Id);

            client1!.Name = "ByClient1";
            await entitySet.UpdateAsync(client1);

            client2!.Name = "ByClient2";
            await Assert.ThrowsAsync<DbConcurrencyException>(() => entitySet.UpdateAsync(client2));

            var final = await entitySet.FindAsync(row.Id);
            Assert.Equal("ByClient1", final!.Name);
        }

        [SkippableFact]
        public async Task DeleteAsync_StaleToken_Throws()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<VersionedRow>(DbAccess);
            var row = new VersionedRow { Name = "A" };
            await entitySet.InsertAsync(row);

            var client1 = await entitySet.FindAsync(row.Id);
            var client2 = await entitySet.FindAsync(row.Id);

            client1!.Name = "ByClient1";
            await entitySet.UpdateAsync(client1);

            await Assert.ThrowsAsync<DbConcurrencyException>(() => entitySet.DeleteAsync(client2!));

            Assert.Equal(1, await entitySet.AsQuery().CountAsync());
        }

        #region 测试实体

        /// <summary>并发令牌测试实体（本测试类专属，表名 it_versioned_row）。</summary>
        [Table("it_versioned_row")]
        public class VersionedRow
        {
            [PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; }

            [Column(IsConcurrencyToken = true)]
            public byte[] RowVersion { get; set; }
        }

        #endregion
    }

    /// <summary>并发令牌回归的 PostgreSQL 真库用例。</summary>
    public class PgSqlConcurrencyTests : ConcurrencyTestsBase
    {
        public PgSqlConcurrencyTests() : base(PgSql)
        {
        }
    }

    /// <summary>并发令牌回归的 SQL Server 真库用例。</summary>
    public class SqlServerConcurrencyTests : ConcurrencyTestsBase
    {
        public SqlServerConcurrencyTests() : base(RealDbDialect.SqlServer)
        {
        }
    }

    /// <summary>并发令牌回归的 MySQL 真库用例。</summary>
    public class MySqlConcurrencyTests : ConcurrencyTestsBase
    {
        public MySqlConcurrencyTests() : base(RealDbDialect.MySql)
        {
        }
    }

    /// <summary>并发令牌回归的 Oracle 真库用例。</summary>
    public class OracleConcurrencyTests : ConcurrencyTestsBase
    {
        public OracleConcurrencyTests() : base(RealDbDialect.Oracle)
        {
        }
    }

    /// <summary>并发令牌回归的 SQLite 真库用例（本地文件数据库，恒可用）。</summary>
    public class SqliteConcurrencyTests : ConcurrencyTestsBase
    {
        public SqliteConcurrencyTests() : base(RealDbDialect.Sqlite)
        {
        }
    }
}
