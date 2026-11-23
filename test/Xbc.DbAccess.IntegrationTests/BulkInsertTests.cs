using Xbc.DbAccess.Entity;
using Xbc.DbAccess.IntegrationTests.Infrastructure;
using Xunit;
using static Xbc.DbAccess.IntegrationTests.Infrastructure.RealDbDialect;

namespace Xbc.DbAccess.IntegrationTests
{
    /// <summary>
    /// bulk 插入的真库回归：方言原生批量路径（SQL Server SqlBulkCopy / PostgreSQL COPY）、
    /// 多值 INSERT 分批与逐条回退（SQLite）三条执行路径在真实库上语义一致（行数、NULL 值、守卫异常）。
    /// </summary>
    public abstract class BulkInsertTestsBase : RealDbTestBase
    {
        protected BulkInsertTestsBase(RealDbDialect dialect) : base(dialect)
        {
        }

        protected override Type[] EntityTypes => new[] { typeof(BulkOrder) };

        private static BulkInsertContext BuildContext(IEnumerable<object[]> rows)
        {
            return new BulkInsertContext(
                null,
                "it_bulk_order",
                new[]
                {
                    new BulkInsertColumn("Name", typeof(string)),
                    new BulkInsertColumn("Amount", typeof(decimal))
                },
                rows);
        }

        private static IEnumerable<object[]> BuildRows(int count)
        {
            for (var i = 0; i < count; i++)
                yield return new object[] { $"user_{i}", 10m + i };
        }

        [SkippableFact]
        public async Task BulkInsert_InsertsRows_AndReturnsAffectedCount()
        {
            SkipIfDialectUnavailable();

            var inserted = await DbAccess.BulkInsertAsync(BuildContext(BuildRows(3)));

            Assert.Equal(3, inserted);
            Assert.Equal(3, await DbAccess.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Qi("it_bulk_order")}"));
        }

        [SkippableFact]
        public async Task BulkInsert_LargeBatch_InsertsAllRows()
        {
            SkipIfDialectUnavailable();

            // 大行数触发方言批量边界（native 批量 / 多值 INSERT 自动分批 / 逐条回退）
            const int rowCount = 300;
            var inserted = await DbAccess.BulkInsertAsync(BuildContext(BuildRows(rowCount)));

            Assert.Equal(rowCount, inserted);
            Assert.Equal(rowCount, await DbAccess.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Qi("it_bulk_order")}"));
        }

        [SkippableFact]
        public async Task BulkInsert_NullValue_WritesDatabaseNull()
        {
            SkipIfDialectUnavailable();

            var inserted = await DbAccess.BulkInsertAsync(BuildContext(new[]
            {
                new object[] { "with_null", DBNull.Value }
            }));

            Assert.Equal(1, inserted);
            Assert.Equal(1, await DbAccess.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {Qi("it_bulk_order")} WHERE {Qi("Amount")} IS NULL"));
        }

        [SkippableFact]
        public async Task BulkInsert_EmptyRows_ReturnsZero()
        {
            SkipIfDialectUnavailable();

            var inserted = await DbAccess.BulkInsertAsync(BuildContext(Array.Empty<object[]>()));

            Assert.Equal(0, inserted);
            Assert.Equal(0, await DbAccess.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Qi("it_bulk_order")}"));
        }

        [SkippableFact]
        public async Task BulkInsert_ColumnCountMismatch_ThrowsArgumentException()
        {
            SkipIfDialectUnavailable();

            // 行数组长度与列集不一致
            var context = new BulkInsertContext(
                null,
                "it_bulk_order",
                new[]
                {
                    new BulkInsertColumn("Name", typeof(string)),
                    new BulkInsertColumn("Amount", typeof(decimal))
                },
                new object[][] { new object[] { "only_name" } });

            await Assert.ThrowsAnyAsync<ArgumentException>(() => DbAccess.BulkInsertAsync(context));
        }

        [SkippableFact]
        public async Task BulkInsert_InvalidOptions_ThrowsArgumentException()
        {
            SkipIfDialectUnavailable();

            var context = BuildContext(BuildRows(1));

            await Assert.ThrowsAsync<ArgumentException>(() => DbAccess.BulkInsertAsync(
                context, new BulkInsertOptions { BatchSize = 0 }));
            await Assert.ThrowsAsync<ArgumentException>(() => DbAccess.BulkInsertAsync(
                context, new BulkInsertOptions { CommandTimeout = -1 }));
        }

        [SkippableFact]
        public async Task BulkInsert_NullArguments_ThrowsArgumentNullException()
        {
            SkipIfDialectUnavailable();

            await Assert.ThrowsAsync<ArgumentNullException>(() => DbAccess.BulkInsertAsync(null));
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                DbAccessBulkInsertExtensions.BulkInsertAsync(null, BuildContext(BuildRows(1))));
        }

        #region 测试实体

        /// <summary>bulk 插入测试实体（本测试类专属，表名 it_bulk_order）。</summary>
        [Table("it_bulk_order")]
        public class BulkOrder
        {
            [PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; }

            // 可空：DDL 生成可空列，支持 NULL 值写入用例
            public decimal? Amount { get; set; }
        }

        #endregion
    }

    /// <summary>bulk 插入回归的 PostgreSQL 真库用例（COPY 原生路径）。</summary>
    public class PgSqlBulkInsertTests : BulkInsertTestsBase
    {
        public PgSqlBulkInsertTests() : base(PgSql)
        {
        }
    }

    /// <summary>bulk 插入回归的 SQL Server 真库用例（SqlBulkCopy 原生路径）。</summary>
    public class SqlServerBulkInsertTests : BulkInsertTestsBase
    {
        public SqlServerBulkInsertTests() : base(RealDbDialect.SqlServer)
        {
        }
    }

    /// <summary>bulk 插入回归的 MySQL 真库用例（多值 INSERT 自动分批回退）。</summary>
    public class MySqlBulkInsertTests : BulkInsertTestsBase
    {
        public MySqlBulkInsertTests() : base(RealDbDialect.MySql)
        {
        }
    }

    /// <summary>bulk 插入回归的 Oracle 真库用例。</summary>
    public class OracleBulkInsertTests : BulkInsertTestsBase
    {
        public OracleBulkInsertTests() : base(RealDbDialect.Oracle)
        {
        }
    }

    /// <summary>bulk 插入回归的 SQLite 真库用例（逐条 + 命令复用回退）。</summary>
    public class SqliteBulkInsertTests : BulkInsertTestsBase
    {
        public SqliteBulkInsertTests() : base(RealDbDialect.Sqlite)
        {
        }
    }
}

