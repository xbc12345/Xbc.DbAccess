using System;
using System.Data.Common;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xbc.DbAccess.Sqlite;

namespace Xbc.DbAccess.UnitTests.Entity.Set
{
    /// <summary>
    /// SQLite 方言变体（测试替身）：SQL 生成、参数构建与 SQLite 完全一致，仅调整批量写入执行策略与批次上限。
    /// <para>标准 SQLite 方言恒为逐条策略（<see cref="ISqlDialect.PreferRowByRowBatchInsert"/> 为 true）且参数上限固定，
    /// 多值 INSERT 策略分支与参数超限分批分支在 SQLite 真库下不可达；本变体以真实 SQLite 引擎执行真库测试的同时
    /// 使这些分支可达（批次上限缩小以小数据集即可覆盖满批/尾批/跨批划分）。</para>
    /// </summary>
    internal sealed class VariantSqliteDialect : ISqlDialect
    {
        private readonly ISqlDialect inner = SqliteDialect.Instance;

        /// <summary>
        /// 初始化方言变体。
        /// </summary>
        /// <param name="maxRowsPerInsertValues">单条多值 INSERT 的行数上限（缩小后小数据集即可触发跨批划分）。</param>
        /// <param name="maxParameterCount">单语句参数上限（null 时沿用 SQLite 真实上限 32766）。</param>
        public VariantSqliteDialect(int maxRowsPerInsertValues, int? maxParameterCount = null)
        {
            MaxRowsPerInsertValues = maxRowsPerInsertValues;
            MaxParameterCount = maxParameterCount ?? inner.MaxParameterCount;
        }

        public string QuoteIdentifier(string identifier) => inner.QuoteIdentifier(identifier);

        public string QuoteTableName(string schema, string tableName) => inner.QuoteTableName(schema, tableName);

        public bool SupportsSchema => inner.SupportsSchema;

        public InsertIdentityMode GetInsertIdentityMode(DbServerInfo serverInfo) => inner.GetInsertIdentityMode(serverInfo);

        public bool SupportsOffsetFetch(DbServerInfo serverInfo) => inner.SupportsOffsetFetch(serverInfo);

        public int MaxParameterCount { get; }

        public int MaxRowsPerInsertValues { get; }

        // 变体核心：关闭逐条策略，使多值 INSERT 路径可达
        public bool PreferRowByRowBatchInsert => false;

        public string ScalarSelectFromClause => inner.ScalarSelectFromClause;

        public string SelectLastInsertId(string columnName) => inner.SelectLastInsertId(columnName);

        public string GetDatePartExpression(string quotedColumn, DatePart part) => inner.GetDatePartExpression(quotedColumn, part);

        public string BuildAverageExpression(string quotedColumn) => inner.BuildAverageExpression(quotedColumn);

        public bool TreatsEmptyStringAsNull => inner.TreatsEmptyStringAsNull;
    }

    /// <summary>
    /// SQLite 提供程序工厂变体：连接提供程序、参数构建器与 SQL 构建器与 SQLite 一致，仅方言替换为变体。
    /// </summary>
    internal sealed class VariantSqliteProviderFactory : DbAccessProviderFactory
    {
        private readonly ISqlDialect dialect;

        public VariantSqliteProviderFactory(ISqlDialect dialect)
        {
            this.dialect = dialect;
        }

        public override DbProviderFactory DbProviderFactory => SQLiteFactory.Instance;

        public override IDbParameterBuilder ParameterBuilder => SqliteParameterBuilder.Instance;

        public override ISqlBuilder SqlBuilder => SqliteSqlBuilder.Instance;

        public override ISqlDialect SqlDialect => dialect;

        public override ISavepointSqlDialect SavepointSqlDialect => SqliteSavepointSqlDialect.Instance;
    }

    /// <summary>
    /// 多值 INSERT 策略工厂：真实 SQLite 引擎 + 多值策略变体方言（每批 2 行，小数据集即可覆盖满批/尾批划分）。
    /// </summary>
    internal sealed class MultiValueSqliteFactory : DbAccessFactoryBase
    {
        private static readonly VariantSqliteProviderFactory provider = new(new VariantSqliteDialect(maxRowsPerInsertValues: 2));

        public MultiValueSqliteFactory(DbConnectionOptions connectionOptions) : base(connectionOptions)
        {
        }

        public override DbAccessProviderFactory ProviderFactory => provider;
    }

    /// <summary>
    /// 小参数上限工厂：真实 SQLite 引擎 + 参数上限为 4 的变体方言（小数据集即可覆盖 IN 条件参数超限分批）。
    /// </summary>
    internal sealed class SmallParameterLimitSqliteFactory : DbAccessFactoryBase
    {
        private static readonly VariantSqliteProviderFactory provider = new(new VariantSqliteDialect(maxRowsPerInsertValues: int.MaxValue, maxParameterCount: 4));

        public SmallParameterLimitSqliteFactory(DbConnectionOptions connectionOptions) : base(connectionOptions)
        {
        }

        public override DbAccessProviderFactory ProviderFactory => provider;
    }

    /// <summary>
    /// 旧版本探测工厂：真实 SQLite 引擎，但 <see cref="DbAccessFactoryBase.GetDbServerInfoAsync"/> 强制返回 3.34.0，
    /// 使自增 ID 取回方式降级为 SELECT LAST_INSERT_ROWID() 两条语句模式（当前捆绑引擎 3.39+ 恒走 RETURNING，该路径不可达）。
    /// </summary>
    internal sealed class LegacyVersionSqliteFactory : DbAccessFactoryBase
    {
        private readonly DbServerInfo forcedServerInfo;

        public LegacyVersionSqliteFactory(DbConnectionOptions connectionOptions, DbServerInfo forcedServerInfo) : base(connectionOptions)
        {
            this.forcedServerInfo = forcedServerInfo;
        }

        public override DbAccessProviderFactory ProviderFactory => SqliteAccessProviderFactory.Instance;

        protected override Task<DbServerInfo> GetDbServerInfoAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(forcedServerInfo);
        }
    }

    /// <summary>
    /// 变体 SQLite 真库测试作用域：独立临时库文件 + 指定变体工厂，Dispose 时释放连接并删除库文件。
    /// </summary>
    internal sealed class VariantSqliteScope : IDisposable
    {
        /// <summary>变体种类。</summary>
        public enum Kind
        {
            /// <summary>多值 INSERT 策略（每批 2 行）。</summary>
            MultiValueStrategy,

            /// <summary>参数上限 4（IN 条件分批）。</summary>
            SmallParameterLimit,

            /// <summary>版本探测降级为 3.34.0（SELECT LAST_INSERT_ROWID() 模式）。</summary>
            LegacyIdentityProbe,
        }

        private readonly string dbFilePath;
        private readonly IDbAccessFactory factory;

        public IDbAccess DbAccess { get; }

        public VariantSqliteScope(Kind kind)
        {
            dbFilePath = Path.Combine(Path.GetTempPath(), $"XbcTest_Variant_{Guid.NewGuid():N}.db");

            var options = new DbConnectionOptions
            {
                ConnectionString = $"Data Source={dbFilePath}",
                PoolOptions = { Enabled = false }
            };

            factory = kind switch
            {
                Kind.MultiValueStrategy => new MultiValueSqliteFactory(options),
                Kind.SmallParameterLimit => new SmallParameterLimitSqliteFactory(options),
                _ => new LegacyVersionSqliteFactory(options, new DbServerInfo { ServerVersion = "3.34.0", MajorVersion = 3, MinorVersion = 34 }),
            };

            DbAccess = factory.Create();
        }

        public void Dispose()
        {
            (DbAccess as IDisposable)?.Dispose();
            (factory as IDisposable)?.Dispose();
            try
            {
                File.Delete(dbFilePath);
            }
            catch { }
        }
    }
}
