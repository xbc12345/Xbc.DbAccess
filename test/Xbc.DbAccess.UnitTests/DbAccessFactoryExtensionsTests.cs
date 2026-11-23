using System;
using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using Xbc.DbAccess.Sqlite;
using Xunit;

namespace Xbc.DbAccess.UnitTests
{
    /// <summary>
    /// <see cref="DbAccessFactoryExtensions.CreateNativeConnection(IDbAccessFactory)"/> 的单元测试
    /// （使用真实 SQLite 提供程序，连接串指向内存数据库且不打开，不产生任何文件与连接）。
    /// </summary>
    public class DbAccessFactoryExtensionsTests
    {
        [Fact]
        public void CreateNativeConnection_返回原生连接_连接字符串同源且未打开()
        {
            // Arrange
            using var factory = SqliteAccessFactory.CreateSqliteFactory("Data Source=:memory:");

            // Act
            using DbConnection connection = factory.CreateNativeConnection();

            // Assert
            Assert.IsType<SQLiteConnection>(connection);
            Assert.Equal("Data Source=:memory:", connection.ConnectionString);
            Assert.Equal(ConnectionState.Closed, connection.State);
        }

        [Fact]
        public void CreateNativeConnection_工厂为null_抛出ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => DbAccessFactoryExtensions.CreateNativeConnection(null));
        }
    }
}
