using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Extensions
{
    /// <summary>
    /// DbConnectionExtensions.CreateCommand 的单元测试：连接与 SQL 的守卫校验、默认 Text 命令构建。
    /// </summary>
    public class DbConnectionExtensionsTests
    {
        private static DbConnection CreateConnection()
        {
            return new SQLiteConnection("Data Source=:memory:");
        }

        [Fact]
        public void CreateCommand_NullConnection_ThrowsArgumentNullException()
        {
            DbConnection connection = null;

            var ex = Assert.Throws<ArgumentNullException>(() =>
                DbConnectionExtensions.CreateCommand(connection, "SELECT 1"));

            Assert.Equal("connection", ex.ParamName);
        }

        [Fact]
        public void CreateCommand_NullSql_ThrowsArgumentNullException()
        {
            using var connection = CreateConnection();

            var ex = Assert.Throws<ArgumentNullException>(() => connection.CreateCommand(null));

            Assert.Equal("sql", ex.ParamName);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void CreateCommand_WhitespaceSql_ThrowsArgumentException(string sql)
        {
            using var connection = CreateConnection();

            var ex = Assert.Throws<ArgumentException>(() => connection.CreateCommand(sql));

            Assert.Equal("sql", ex.ParamName);
        }

        [Fact]
        public void CreateCommand_DefaultOverload_BuildsTextCommand()
        {
            using var connection = CreateConnection();

            using var command = connection.CreateCommand("SELECT 1");

            Assert.Equal("SELECT 1", command.CommandText);
            Assert.Equal(CommandType.Text, command.CommandType);
        }

        [Fact]
        public void CreateCommand_WithParameters_AndTransaction_BindsAll()
        {
            using var connection = CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var parameter = connection.CreateCommand("SELECT 1").CreateParameter();
            parameter.ParameterName = "@Id";
            parameter.Value = 1;

            using var command = connection.CreateCommand(
                "SELECT @Id",
                CommandType.Text,
                transaction,
                parameter);

            Assert.Equal("SELECT @Id", command.CommandText);
            Assert.Equal(CommandType.Text, command.CommandType);
            Assert.Same(parameter, command.Parameters["@Id"]);
            Assert.Same(transaction, command.Transaction);
        }
    }
}
