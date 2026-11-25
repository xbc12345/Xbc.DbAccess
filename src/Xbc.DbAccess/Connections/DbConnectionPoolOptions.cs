namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库连接池选项。
    /// <para>会话状态重置：连接回池时按方言执行复位命令（SQL Server <c>sp_reset_connection</c>、PostgreSQL <c>DISCARD ALL</c>、
    /// MySQL <c>ROLLBACK</c> + 常用会话变量复位；Oracle/SQLite 无对应机制，残留风险低），可经 <see cref="EnableSessionStateReset"/> 关闭。
    /// MySQL 的临时表（会话级存续）与用户变量不在复位范围，其余方言特有会话状态（如 Oracle 的 ALTER SESSION 调整）同理；
    /// 需要完整会话隔离时请经 <c>IDbAccess.ProviderFactory.DbProviderFactory.CreateConnection()</c> 创建独立的非池化连接。
    /// 挂起事务防护仅覆盖经框架事务包装器开启的事务：经 <c>UnderlyingConnection</c> 直接开启的事务不会阻止连接回池复用。</para>
    /// </summary>
    public sealed class DbConnectionPoolOptions
    {
        /// <summary>
        /// 是否启用连接池，默认值为 false。
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 连接池最小连接数。连接池首次获取连接时懒预热创建指定数量的连接（不阻塞构造），默认值为 5，也是空闲连接清理的下限。
        /// </summary>
        public int MinPoolSize { get; set; } = 5;

        /// <summary>
        /// 连接池最大连接数，默认值为 100。
        /// </summary>
        public int MaxPoolSize { get; set; } = 100;

        /// <summary>
        /// 连接最大空闲时间，超过该时间的连接可能会被清理，默认值为 15 分钟。
        /// </summary>
        public TimeSpan MaxIdleTime { get; set; } = TimeSpan.FromMinutes(15);

        /// <summary>
        /// 连接最大寿命。超过该寿命的连接在回收时销毁换新、激活与定时清理时淘汰，默认值为 30 分钟。
        /// <para>防止 NAT、负载均衡器或数据库侧静默断链产生的僵尸连接流入借用者；一直被借用的长寿命连接也会在归还池时被换新。</para>
        /// <para><see cref="TimeSpan.Zero"/> 表示不限制寿命。超龄连接的清理不受 <see cref="MinPoolSize"/> 下限保护（寿命为安全上限，优先于保温下限）。</para>
        /// </summary>
        public TimeSpan MaxLifetime { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// 是否在连接归还连接池时执行会话状态重置（按方言提供命令，见 <c>ISqlDialect.SessionStateResetCommands</c>），默认开启。
        /// <para>对标 SQL Server 内置连接池的 <c>sp_reset_connection</c>：临时表、SET 选项等会话残留不再透传给下一个借用者。</para>
        /// <para>重置在归还线程同步执行（每条命令一个往返），对归还延迟敏感且确认无会话残留风险的应用可关闭。</para>
        /// </summary>
        public bool EnableSessionStateReset { get; set; } = true;

        /// <summary>
        /// 连接池清理间隔，默认值为 5 分钟。
        /// </summary>
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 连接等待超时时间。在超时时间内没有获取到连接时会抛出异常，默认值为 3 秒。
        /// </summary>
        public TimeSpan ConnectionWaitTimeout { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// 激活连接时跳过健康检查的时间间隔，默认值为 10 秒。
        /// <para>连接已打开且最近使用时间在此间隔内时跳过连通性验证直接复用；间隔越长复用性能越好但断线检测越滞后，<see cref="TimeSpan.Zero"/> 表示每次激活都验证。</para>
        /// </summary>
        public TimeSpan ValidationSkipInterval { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// 激活连接时执行健康检查命令的超时时间（秒），默认值为 3。
        /// <para>健康检查命令按方言生成（SELECT 1，Oracle 为 SELECT 1 FROM DUAL）。</para>
        /// </summary>
        public int ValidationCommandTimeout { get; set; } = 3;

        /// <summary>
        /// 校验数值约束，非法配置抛出 <see cref="ArgumentOutOfRangeException"/>。
        /// <para>由池化管理器构造时调用；不在属性 setter 中校验，因跨字段约束在逐属性赋值时存在中间非法态。</para>
        /// </summary>
        public void Validate()
        {
            if (MinPoolSize < 0)
                throw new ArgumentOutOfRangeException(nameof(MinPoolSize), MinPoolSize, "连接池最小连接数不能为负数。");

            if (MaxPoolSize < 1)
                throw new ArgumentOutOfRangeException(nameof(MaxPoolSize), MaxPoolSize, "连接池最大连接数至少为 1。");

            if (MinPoolSize > MaxPoolSize)
                throw new ArgumentOutOfRangeException(nameof(MinPoolSize), MinPoolSize, $"连接池最小连接数 {MinPoolSize} 不能大于最大连接数 {MaxPoolSize}。");

            if (MaxIdleTime <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(MaxIdleTime), MaxIdleTime, "连接最大空闲时间必须为正。");

            if (MaxLifetime < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(MaxLifetime), MaxLifetime, "连接最大寿命不能为负数（TimeSpan.Zero 表示不限制）。");

            if (CleanupInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(CleanupInterval), CleanupInterval, "连接池清理间隔必须为正。");

            if (ConnectionWaitTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(ConnectionWaitTimeout), ConnectionWaitTimeout, "连接等待超时时间必须为正。");

            if (ValidationSkipInterval < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(ValidationSkipInterval), ValidationSkipInterval, "跳过健康检查的时间间隔不能为负数（TimeSpan.Zero 表示每次激活都验证）。");

            if (ValidationCommandTimeout < 0)
                throw new ArgumentOutOfRangeException(nameof(ValidationCommandTimeout), ValidationCommandTimeout, "健康检查命令超时时间（秒）不能为负数（0 表示不限时）。");
        }
    }
}
