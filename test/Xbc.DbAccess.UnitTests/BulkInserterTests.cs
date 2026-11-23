using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xbc.DbAccess.MySql;
using Xbc.DbAccess.Npgsql;
using Xbc.DbAccess.Oracle;
using Xbc.DbAccess.Sqlite;
using Xbc.DbAccess.SqlServer;
using Xunit;

namespace Xbc.DbAccess.UnitTests
{
    /// <summary>
    /// 原生 bulk 插入器的单元测试：各方言工厂接线（BulkInserter 返回各自原生实现，SQLite 返回 null）、
    /// SQL 形状与数据表构建（Npgsql 的 COPY 语句、Oracle 的数组绑定 INSERT 语句、SqlServer 的 DataTable 列类型与行值）、连接类型校验。
    /// </summary>
    public class BulkInserterTests
    {
        #region 工厂接线

        [Fact]
        public void ProviderFactories_ExposeNativeInserters()
        {
            Assert.IsType<SqlServerBulkInserter>(SqlAccessProviderFactory.Instance.BulkInserter);
            Assert.IsType<MySqlBulkInserter>(MySqlAccessProviderFactory.Instance.BulkInserter);
            Assert.IsType<NpgsqlBulkInserter>(NpgsqlAccessProviderFactory.Instance.BulkInserter);
            Assert.IsType<OracleBulkInserter>(OracleAccessProviderFactory.Instance.BulkInserter);
        }

        [Fact]
        public void SqliteProviderFactory_HasNoNativeInserter()
        {
            // SQLite 无原生批量写入 API：不覆写 BulkInserter，实体层回退批量 INSERT（方言逐条策略：事务内逐条 + 命令复用）
            Assert.Null(SqliteAccessProviderFactory.Instance.BulkInserter);
        }

        #endregion

        #region SQL 形状与数据表构建

        [Fact]
        public void Npgsql_BuildCopySql_QuotesSchemaTableAndColumns()
        {
            var columns = new List<BulkInsertColumn>
            {
                new BulkInsertColumn("Name", typeof(string)),
                new BulkInsertColumn("Age", typeof(int))
            };

            var copySql = NpgsqlBulkInserter.BuildCopySql("myschema", "Users", columns);

            Assert.Equal("COPY \"myschema\".\"Users\"" + Environment.NewLine
                 + "    (\"Name\", \"Age\")" + Environment.NewLine
                 + "FROM STDIN (FORMAT BINARY)" + Environment.NewLine, copySql);
        }

        [Fact]
        public void Npgsql_BuildCopySql_WithoutSchema_QuotesTableOnly()
        {
            var columns = new List<BulkInsertColumn> { new BulkInsertColumn("Name", typeof(string)) };

            var copySql = NpgsqlBulkInserter.BuildCopySql(null, "Users", columns);

            Assert.Equal("COPY \"Users\"" + Environment.NewLine
                 + "    (\"Name\")" + Environment.NewLine
                 + "FROM STDIN (FORMAT BINARY)" + Environment.NewLine, copySql);
        }

        [Fact]
        public void Oracle_BuildInsertSql_GeneratesArrayBindPlaceholders()
        {
            var columns = new List<BulkInsertColumn>
            {
                new BulkInsertColumn("Name", typeof(string)),
                new BulkInsertColumn("Age", typeof(int))
            };

            var insertSql = OracleBulkInserter.BuildInsertSql("myschema", "Users", columns);

            Assert.Equal("INSERT INTO \"myschema\".\"Users\"" + Environment.NewLine
                 + "    (\"Name\", \"Age\")" + Environment.NewLine
                 + "VALUES" + Environment.NewLine
                 + "    (:p0, :p1)" + Environment.NewLine, insertSql);
        }

        [Fact]
        public void Oracle_BuildInsertSql_WithoutSchema_QuotesTableOnly()
        {
            var columns = new List<BulkInsertColumn> { new BulkInsertColumn("Name", typeof(string)) };

            var insertSql = OracleBulkInserter.BuildInsertSql(null, "Users", columns);

            Assert.Equal("INSERT INTO \"Users\"" + Environment.NewLine
                 + "    (\"Name\")" + Environment.NewLine
                 + "VALUES" + Environment.NewLine
                 + "    (:p0)" + Environment.NewLine, insertSql);
        }

        [Fact]
        public void SqlServer_BuildDataTable_UsesColumnClrTypesAndRows()
        {
            var columns = new List<BulkInsertColumn>
            {
                new BulkInsertColumn("Id", typeof(int)),
                new BulkInsertColumn("Name", typeof(string))
            };
            var rows = new List<object[]>
            {
                new object[] { 1, "Alice" },
                new object[] { 2, DBNull.Value }
            };
            var context = new BulkInsertContext(null, "Users", columns, rows);

            var dataTable = context.BuildDataTable();

            Assert.Equal(2, dataTable.Columns.Count);
            Assert.Equal(typeof(int), dataTable.Columns["Id"].DataType);
            Assert.Equal(typeof(string), dataTable.Columns["Name"].DataType);
            Assert.Equal(2, dataTable.Rows.Count);
            Assert.Equal(1, dataTable.Rows[0]["Id"]);
            Assert.Equal("Alice", dataTable.Rows[0]["Name"]);
            Assert.Equal(DBNull.Value, dataTable.Rows[1]["Name"]);
        }

        [Fact]
        public void MySql_BuildDataTable_UsesColumnClrTypesAndRows()
        {
            var columns = new List<BulkInsertColumn>
            {
                new BulkInsertColumn("Age", typeof(int))
            };
            var rows = new List<object[]> { new object[] { 20 } };
            var context = new BulkInsertContext(null, "Users", columns, rows);

            var dataTable = context.BuildDataTable();

            Assert.Single(dataTable.Columns);
            Assert.Equal(typeof(int), dataTable.Columns["Age"].DataType);
            Assert.Equal(20, dataTable.Rows[0]["Age"]);
        }

        #endregion

        #region 连接类型校验

        [Fact]
        public async Task SqlServerInserter_WrongConnectionType_Throws()
        {
            using (var connection = new SQLiteConnection("Data Source=:memory:"))
            {
                var context = CreateMinimalContext();

                await Assert.ThrowsAsync<ArgumentException>(
                    () => SqlServerBulkInserter.Instance.InsertAsync(connection, null, context));
            }
        }

        [Fact]
        public async Task MySqlInserter_WrongConnectionType_Throws()
        {
            using (var connection = new SQLiteConnection("Data Source=:memory:"))
            {
                var context = CreateMinimalContext();

                await Assert.ThrowsAsync<ArgumentException>(
                    () => MySqlBulkInserter.Instance.InsertAsync(connection, null, context));
            }
        }

        [Fact]
        public async Task NpgsqlInserter_WrongConnectionType_Throws()
        {
            using (var connection = new SQLiteConnection("Data Source=:memory:"))
            {
                var context = CreateMinimalContext();

                await Assert.ThrowsAsync<ArgumentException>(
                    () => NpgsqlBulkInserter.Instance.InsertAsync(connection, null, context));
            }
        }

        [Fact]
        public async Task OracleInserter_WrongConnectionType_Throws()
        {
            using (var connection = new SQLiteConnection("Data Source=:memory:"))
            {
                var context = CreateMinimalContext();

                await Assert.ThrowsAsync<ArgumentException>(
                    () => OracleBulkInserter.Instance.InsertAsync(connection, null, context));
            }
        }

        [Fact]
        public async Task Inserters_NullArguments_Throw()
        {
            var context = CreateMinimalContext();

            await Assert.ThrowsAsync<ArgumentNullException>(() => SqlServerBulkInserter.Instance.InsertAsync(null, null, context));
            await Assert.ThrowsAsync<ArgumentNullException>(() => NpgsqlBulkInserter.Instance.InsertAsync(null, null, context));
            await Assert.ThrowsAsync<ArgumentNullException>(() => OracleBulkInserter.Instance.InsertAsync(null, null, context));
            await Assert.ThrowsAsync<ArgumentNullException>(() => MySqlBulkInserter.Instance.InsertAsync(null, null, context));
        }

        #endregion

        #region 上下文与选项参数校验

        [Fact]
        public void BulkInsertColumn_EmptyName_Throws()
        {
            Assert.Throws<ArgumentException>(() => new BulkInsertColumn(" ", typeof(int)));
            Assert.Throws<ArgumentNullException>(() => new BulkInsertColumn("Id", null));
        }

        [Fact]
        public void BulkInsertContext_EmptyColumns_Throws()
        {
            var rows = new List<object[]>();

            Assert.Throws<ArgumentException>(() => new BulkInsertContext(null, "Users", new List<BulkInsertColumn>(), rows));
            Assert.Throws<ArgumentNullException>(() => new BulkInsertContext(null, "Users", new List<BulkInsertColumn> { new BulkInsertColumn("Id", typeof(int)) }, null));
        }

        #endregion

        /// <summary>
        /// 创建最小 bulk 插入上下文（单列单行）。
        /// </summary>
        private static BulkInsertContext CreateMinimalContext()
        {
            var columns = new List<BulkInsertColumn> { new BulkInsertColumn("Id", typeof(int)) };
            var rows = new List<object[]> { new object[] { 1 } };
            return new BulkInsertContext(null, "Users", columns, rows);
        }
    }
}
