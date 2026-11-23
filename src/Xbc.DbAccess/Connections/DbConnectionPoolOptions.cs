namespace Xbc.DbAccess
{
    /// <summary>
    /// 数据库连接池选项。
    /// <para>已知限制：连接回池复用时仅重置当前数据库，临时表、SET 选项、CONTEXT_INFO、会话级锁等会话状态会残留给下一个借用者；
    /// 需要会话级状态时请经 <c>IDbAccess.ProviderFactory.DbProviderFactory.CreateConnection()</c> 创建独立的非池化连接。</para>
    /// </summary>
    public sealed class DbConnectionPoolOptions
    {
        /// <summary>
        /// 是否启用连接池，默认值为 false。
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 连接池最小连接数。连接池启动时会预创建指定数量的连接，默认值为 5。
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
