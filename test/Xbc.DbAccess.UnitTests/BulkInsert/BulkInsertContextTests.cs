using System.Collections.Generic;
using Xunit;

namespace Xbc.DbAccess.UnitTests.BulkInsert
{
    /// <summary>
    /// BulkInsertContext 行数据物化（BuildDataTable）的行长度契约测试：
    /// 供以 DataTable 为数据源的原生 bulk API 使用，长度不一致时显式失败并携带行号。
    /// </summary>
    public class BulkInsertContextTests
    {
        [Fact]
        public void BuildDataTable_RowLengthMismatch_ThrowsWithRowIndex()
        {
            var columns = new List<BulkInsertColumn>
            {
                new BulkInsertColumn("Id", typeof(int)),
                new BulkInsertColumn("Name", typeof(string)),
            };
            var rows = new List<object[]>
            {
                new object[] { 1, "a" },
                new object[] { 2 },// 第 1 行（0 基序号）只有 1 个值
            };
            var context = new BulkInsertContext(null, "AnyTable", columns, rows);

            // 长度契约在单次枚举循环内校验，避免 DataTable 静默补 DBNull 落库；异常消息携带行号
            var exception = Assert.Throws<ArgumentException>(() => context.BuildDataTable());

            Assert.Contains("第 1 行", exception.Message);
        }
    }
}
