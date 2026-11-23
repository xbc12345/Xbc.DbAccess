namespace Xbc.DbAccess.Diagnostics
{
    /// <summary>
    /// DbAccess 诊断选项，经 <c>AddDbAccessDiagnostics</c> 注册配置，在首次解析数据库工厂时应用。
    /// </summary>
    public sealed class DbAccessDiagnosticsOptions
    {
        /// <summary>
        /// 慢查询告警阈值；null（默认）禁用告警。
        /// <para>计时口径与 <c>xbc.dbaccess.sql.duration</c> 指标一致，不含连接获取时间。</para>
        /// </summary>
        public TimeSpan? SlowQueryThreshold { get; set; }
    }
}
