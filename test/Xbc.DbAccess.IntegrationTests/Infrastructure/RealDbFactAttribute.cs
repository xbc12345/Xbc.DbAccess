using Xunit;

namespace Xbc.DbAccess.IntegrationTests.Infrastructure
{
    /// <summary>
    /// 仅 SQL Server 的真库测试 Fact 特性（存储过程等 SQL Server 专属能力）：SQL Server 不可连接时跳过。
    /// </summary>
    public sealed class SqlServerOnlyFactAttribute : FactAttribute
    {
        public SqlServerOnlyFactAttribute()
        {
            if (!RealDbProbe.SqlServerAvailable)
                Skip = "本地无可用 SQL Server，跳过存储过程测试；可用环境变量 XBC_IT_MSSQL 指定连接串。";
        }
    }
}
