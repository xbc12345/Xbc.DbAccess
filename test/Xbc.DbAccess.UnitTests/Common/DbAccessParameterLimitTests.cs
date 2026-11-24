using System.Data;
using System.Data.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Common
{
    /// <summary>
    /// DbAccess 执行入口的参数上限兜底校验单元测试。
    /// <para>以真实 Oracle 方言（单语句参数上限 1000）+ 模拟工厂验证：超限语句在发往数据库之前
    /// 以明确异常失败，且先于连接解析（无任何连接建立）。</para>
    /// </summary>
    public class DbAccessParameterLimitTests
    {
        [Fact]
        public async Task ExecuteNonQueryAsync_WithTooManyParameters_Throws()
        {
            var dbAccess = new Entity.OracleFakeDbAccessFactory().Create();
            var parameters = CreateParameters(dbAccess.ProviderFactory.ParameterBuilder, 1001);// Oracle 方言上限 1000

            // 校验先于连接解析执行：无连接建立即失败，也不触碰模拟命令的静态记录（不影响并行测试）
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => dbAccess.ExecuteNonQueryAsync("UPDATE T SET A = :p0", parameters));

            Assert.Contains("超出当前方言上限", exception.Message);
            Assert.Contains("1001", exception.Message);
        }

        [Fact]
        public async Task ExecuteReaderAsync_WithTooManyParameters_Throws()
        {
            var dbAccess = new Entity.OracleFakeDbAccessFactory().Create();
            var parameters = CreateParameters(dbAccess.ProviderFactory.ParameterBuilder, 1001);

            // Reader 入口（Where/Keyset 查询路径）同样在连接解析前失败
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => dbAccess.ExecuteReaderAsync("SELECT * FROM T WHERE A = :p0", parameters));

            Assert.Contains("超出当前方言上限", exception.Message);
        }

        // null 参数短路分支无需专门测试：全部不传参数的既有测试（千余个）都在走该路径

        /// <summary>
        /// 经参数构建器批量创建参数（名称唯一即可，值不重要）。
        /// </summary>
        private static DbParameter[] CreateParameters(IDbParameterBuilder parameterBuilder, int count)
        {
            var parameters = new DbParameter[count];
            for (int i = 0; i < count; i++)
                parameters[i] = parameterBuilder.CreateParameter("p" + i.ToString(), i, DbType.Int32);

            return parameters;
        }
    }
}
