using System.Data;
using System.Threading.Tasks;
using Xbc.DbAccess.UnitTests.Infrastructure;
using Xunit;

namespace Xbc.DbAccess.UnitTests
{
    /// <summary>
    /// DbAccess 分页查询的单元测试，使用 SQLite 内存数据库。
    /// </summary>
    public class DbAccessPaginationTests : SqliteTestBase
    {
        protected override async Task InitializeDatabaseAsync()
        {
            await base.InitializeDatabaseAsync();

            // 确保有足够的测试数据
            const string insertMoreDataSql = @"
                INSERT INTO TestUsers (Name, Age, Email)
                SELECT 'User' || (SELECT COUNT(*) FROM TestUsers) + 1, 
                       20 + (SELECT COUNT(*) FROM TestUsers), 
                       'user' || (SELECT COUNT(*) FROM TestUsers) + 1 || '@example.com'
                FROM TestUsers LIMIT 10;
            ";
            // 插入更多测试数据
            for (int i = 0; i < 3; i++)
            {
                await DbAccess.ExecuteNonQueryAsync(insertMoreDataSql);
            }
        }

        #region ExecuteReaderWithPagination 分页测试

        [Fact]
        public async Task ExecuteReaderWithPagination_分页查询_生成正确SQL()
        {
            // Arrange
            const string querySql = "SELECT * FROM TestUsers";
            const int pageIndex = 2;
            const int pageSize = 3;

            // Act
            using var reader = await DbAccess.ExecuteReaderWithPaginationAsync(querySql, pageIndex, pageSize, "Id");

            // Assert
            Assert.NotNull(reader);
            var count = 0;
            while (reader.Read())
            {
                count++;
            }
            Assert.True(count <= pageSize);
        }

        [Fact]
        public async Task ExecuteReaderWithPagination_第一页_偏移量为零()
        {
            // Arrange
            const string querySql = "SELECT * FROM TestUsers ORDER BY Id";
            const int pageIndex = 1;
            const int pageSize = 5;

            // Act
            using var reader = await DbAccess.ExecuteReaderWithPaginationAsync(querySql, pageIndex, pageSize, "Id");

            // Assert
            Assert.NotNull(reader);
            var count = 0;
            while (reader.Read())
            {
                count++;
            }
            Assert.True(count > 0);
            Assert.True(count <= pageSize);
        }

        [Fact]
        public async Task ExecuteReaderWithPagination_带参数_参数正确传递()
        {
            // Arrange
            const string querySql = "SELECT * FROM TestUsers WHERE Age > @age";
            const int pageIndex = 1;
            const int pageSize = 5;
            var param = CreateParameter("@age", 25);

            // Act
            using var reader = await DbAccess.ExecuteReaderWithPaginationAsync(querySql, pageIndex, pageSize, "Id", param);

            // Assert
            Assert.NotNull(reader);
        }

        [Fact]
        public async Task ExecuteReaderWithPagination_查询为空_返回空读取器()
        {
            // Arrange
            const string querySql = "SELECT * FROM TestUsers WHERE Id = -1";

            // Act
            using var reader = await DbAccess.ExecuteReaderWithPaginationAsync(querySql, 1, 10, "Id");

            // Assert
            Assert.NotNull(reader);
            Assert.False(reader.Read());
        }

        [Fact]
        public async Task ExecuteReaderWithPagination_查询SQL为空_抛出ArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await DbAccess.ExecuteReaderWithPaginationAsync(null!, 1, 10, "Id"));
        }

        [Fact]
        public async Task ExecuteReaderWithPagination_分页参数乘积溢出_抛出ArgumentOutOfRangeException()
        {
            // (pageIndex - 1) * pageSize 以 long 计算防 int 溢出：跳过行数超过 int.MaxValue 时提前抛出
            var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await DbAccess.ExecuteReaderWithPaginationAsync("SELECT * FROM TestUsers", int.MaxValue, 2, "Id"));
            Assert.Equal("pageIndex", ex.ParamName);

            // DataSet 重载共用同一窗口构建逻辑
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await DbAccess.ExecuteDataSetWithPaginationAsync("SELECT * FROM TestUsers", int.MaxValue, 2, "Id"));
        }

        #endregion

        #region ExecuteReaderWithPaginationAsync 异步分页测试

        [Fact]
        public async Task ExecuteReaderWithPaginationAsync_分页查询_生成正确SQL()
        {
            // Arrange
            const string querySql = "SELECT * FROM TestUsers";

            // Act
            using var reader = await DbAccess.ExecuteReaderWithPaginationAsync(querySql, 2, 5, "Id DESC");

            // Assert
            Assert.NotNull(reader);
        }

        [Fact]
        public async Task ExecuteReaderWithPaginationAsync_取消操作_抛出OperationCanceledException()
        {
            // Arrange
            const string querySql = "SELECT * FROM TestUsers";
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            // TaskCanceledException 继承自 OperationCanceledException，所以使用 ThrowsAny
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await DbAccess.ExecuteReaderWithPaginationAsync(querySql, 1, 10, "Id", null, cts.Token));
        }

        #endregion

        #region ExecuteDataSetWithPagination 分页测试

        [Fact]
        public async Task ExecuteDataSetWithPagination_分页查询_返回数据集()
        {
            // Arrange
            const string querySql = "SELECT * FROM TestUsers ORDER BY Id";

            // Act
            var result = await DbAccess.ExecuteDataSetWithPaginationAsync(querySql, 1, 5, "Id");

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Tables);
            Assert.True(result.Tables[0].Rows.Count <= 5);
        }

        [Fact]
        public async Task ExecuteDataSetWithPagination_分页空结果_返回空数据集()
        {
            // Arrange
            const string querySql = "SELECT * FROM TestUsers WHERE Id = -1";

            // Act
            var result = await DbAccess.ExecuteDataSetWithPaginationAsync(querySql, 1, 10, "Id");

            // Assert
            Assert.NotNull(result);
            // 即使查询没有结果，也会返回一个包含空表的 DataSet
            Assert.Single(result.Tables);
            Assert.Empty(result.Tables[0].Rows);
        }

        #endregion

        #region ExecuteDataSetWithPaginationAsync 异步分页测试

        [Fact]
        public async Task ExecuteDataSetWithPaginationAsync_分页查询_返回数据集()
        {
            // Arrange
            const string querySql = "SELECT * FROM TestUsers ORDER BY Id";

            // Act
            var result = await DbAccess.ExecuteDataSetWithPaginationAsync(querySql, 1, 5, "Id");

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Tables);
        }

        [Fact]
        public async Task ExecuteDataSetWithPaginationAsync_取消操作_抛出OperationCanceledException()
        {
            // Arrange
            const string querySql = "SELECT * FROM TestUsers";
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            // TaskCanceledException 继承自 OperationCanceledException，所以使用 ThrowsAny
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await DbAccess.ExecuteDataSetWithPaginationAsync(querySql, 1, 10, "Id", null, cts.Token));
        }

        #endregion

        #region 分页查询并映射结果测试

        [Fact]
        public async Task ExecuteReaderWithPagination_查询并映射_返回映射结果()
        {
            // Arrange
            const string querySql = "SELECT Id, Name FROM TestUsers ORDER BY Id";
            var results = new List<object>();

            // Act
            using (var reader = await DbAccess.ExecuteReaderWithPaginationAsync(querySql, 1, 5, "Id"))
            {
                while (reader.Read())
                {
                    results.Add(new
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1)
                    });
                }
            }

            // Assert
            Assert.NotEmpty(results);
            Assert.True(results.Count <= 5);
        }

        [Fact]
        public async Task ExecuteReaderWithPagination_空结果_返回空列表()
        {
            // Arrange
            const string querySql = "SELECT * FROM TestUsers WHERE Id = -1";
            var results = new List<object>();

            // Act
            using (var reader = await DbAccess.ExecuteReaderWithPaginationAsync(querySql, 1, 10, "Id"))
            {
                while (reader.Read())
                {
                    results.Add(new { Id = reader.GetInt32(0) });
                }
            }

            // Assert
            Assert.Empty(results);
        }

        [Fact]
        public async Task ExecuteReaderWithPaginationAsync_查询并映射_返回映射结果()
        {
            // Arrange
            const string querySql = "SELECT Id, Name FROM TestUsers ORDER BY Id";
            var results = new List<object>();

            // Act
            using (var reader = await DbAccess.ExecuteReaderWithPaginationAsync(querySql, 1, 5, "Id"))
            {
                while (reader.Read())
                {
                    results.Add(new
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1)
                    });
                }
            }

            // Assert
            Assert.NotEmpty(results);
            Assert.True(results.Count <= 5);
        }

        #endregion
    }
}
