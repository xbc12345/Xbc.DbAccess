using System.Data;
using System.Data.Common;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.Oracle;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// EntitySet 的 RETURNING INTO 插入路径单元测试（Oracle 自增 ID 回填）。
    /// <para>无 Oracle 服务器环境：以真实 Oracle 方言/SQL 构建器/参数构建器 + 模拟 ADO 管道驱动完整执行路径，
    /// 模拟命令在 ExecuteNonQuery 时向输出参数写回自增 ID（Oracle NUMBER 经 ODP.NET 回传为 decimal），
    /// 验证 SQL 生成、输出参数注册与实体回填；真实数据库行为由使用方在 Oracle 环境验证。</para>
    /// </summary>
    public class EntitySetReturningIntoTests
    {
        private readonly OracleFakeDbAccessFactory factory = new OracleFakeDbAccessFactory();
        private readonly IDbAccess dbAccess;
        private readonly EntitySet<OracleReturningEntity> entitySet;

        public EntitySetReturningIntoTests()
        {
            dbAccess = factory.Create();
            entitySet = new EntitySet<OracleReturningEntity>(dbAccess);
            OracleFakeDbCommand.ExecutedCommands.Clear();
            OracleFakeDbTransaction.BeginCount = 0;
        }

        #region 测试实体

        [XbcDbEntity.Table("OracleReturning")]
        public class OracleReturningEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public long Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        #endregion

        [Fact]
        public async Task InsertAsync_有自增列_应生成ReturningInto子句并回填自增ID()
        {
            var entity = new OracleReturningEntity { Name = "Alice" };

            var affectedRows = await entitySet.InsertAsync(entity);

            Assert.Equal(1, affectedRows);
            Assert.Equal(123, entity.Id);

            var record = Assert.Single(OracleFakeDbCommand.ExecutedCommands);
            Assert.Contains("INSERT INTO \"OracleReturning\"", record.Sql);
            Assert.Contains("RETURNING \"Id\" INTO :out_Id", record.Sql);

            var outputParameter = record.Parameters.Single(p => p.ParameterName == ":out_Id");
            Assert.Equal(ParameterDirection.Output, outputParameter.Direction);

            var nameParameter = record.Parameters.Single(p => p.ParameterName == ":Name");
            Assert.Equal("Alice", nameParameter.Value);

            // RETURNING INTO 为单语句取回，单条插入不应开启事务
            Assert.Equal(0, OracleFakeDbTransaction.BeginCount);
        }

        [Fact]
        public async Task InsertRangeAsync_有自增列_应逐条生成ReturningInto并回填自增ID()
        {
            var entities = new[]
            {
                new OracleReturningEntity { Name = "Alice" },
                new OracleReturningEntity { Name = "Bob" }
            };

            var affectedRows = await entitySet.InsertRangeAsync(entities);

            Assert.Equal(2, affectedRows);
            Assert.All(entities, e => Assert.Equal(123, e.Id));
            Assert.Equal(2, OracleFakeDbCommand.ExecutedCommands.Count);
            Assert.All(OracleFakeDbCommand.ExecutedCommands, r => Assert.Contains("RETURNING \"Id\" INTO :out_Id", r.Sql));

            // 批量路径由外层事务保证原子性
            Assert.Equal(1, OracleFakeDbTransaction.BeginCount);
        }
    }

    #region 模拟管道（真实 Oracle 方言/构建器/参数构建器 + 无数据库的 ADO 模拟）

    /// <summary>
    /// 模拟 Oracle 命令执行记录：SQL 与参数引用快照（参数对象在命令 Dispose 后仍可读取）。
    /// </summary>
    internal sealed class OracleFakeCommandRecord
    {
        /// <summary>执行的 SQL 语句。</summary>
        public string Sql { get; set; }

        /// <summary>参数引用快照。</summary>
        public List<DbParameter> Parameters { get; set; }
    }

    /// <summary>
    /// 模拟 Oracle 数据库命令：记录执行的 SQL 与参数，ExecuteNonQuery 时模拟数据库向输出参数写回自增 ID。
    /// </summary>
    internal sealed class OracleFakeDbCommand : DbCommand
    {
        /// <summary>已执行命令的记录（测试类内串行执行，测试构造时清空）。</summary>
        public static readonly List<OracleFakeCommandRecord> ExecutedCommands = new List<OracleFakeCommandRecord>();

        private readonly OracleFakeDbParameterCollection parameters = new OracleFakeDbParameterCollection();

        /// <inheritdoc />
        public override string CommandText { get; set; } = string.Empty;

        /// <inheritdoc />
        public override int CommandTimeout { get; set; }

        /// <inheritdoc />
        public override CommandType CommandType { get; set; } = CommandType.Text;

        /// <inheritdoc />
        public override UpdateRowSource UpdatedRowSource { get; set; }

        /// <inheritdoc />
        protected override DbConnection DbConnection { get; set; }

        /// <inheritdoc />
        protected override System.Data.Common.DbParameterCollection DbParameterCollection => parameters;

        /// <inheritdoc />
        protected override DbTransaction DbTransaction { get; set; }

        /// <inheritdoc />
        public override bool DesignTimeVisible { get; set; }

        /// <inheritdoc />
        public override void Cancel()
        {
        }

        /// <inheritdoc />
        public override void Prepare()
        {
        }

        /// <inheritdoc />
        protected override DbParameter CreateDbParameter() => new OracleFakeDbParameter();

        /// <inheritdoc />
        public override int ExecuteNonQuery()
        {
            // 记录 SQL 与参数引用快照，供测试断言
            ExecutedCommands.Add(new OracleFakeCommandRecord
            {
                Sql = CommandText,
                Parameters = parameters.Items.ToList()
            });

            // 模拟 Oracle RETURNING INTO：向输出参数写回自增 ID（Oracle NUMBER 经 ODP.NET 回传为 decimal）
            foreach (var parameter in parameters.Items)
            {
                if (parameter.Direction == ParameterDirection.Output)
                    parameter.Value = 123m;
            }

            return 1;
        }

        /// <inheritdoc />
        public override object ExecuteScalar() => throw new NotSupportedException("RETURNING INTO 路径不应以 ExecuteScalar 读取结果集。");

        /// <inheritdoc />
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException("RETURNING INTO 路径不应产生结果集。");
    }

    /// <summary>
    /// 模拟 Oracle 数据库连接：Open/Close 无副作用，CreateCommand 返回模拟命令，事务计数供断言。
    /// </summary>
    internal sealed class OracleFakeDbConnection : DbConnection
    {
        private ConnectionState state = ConnectionState.Closed;

        /// <inheritdoc />
        public override string ConnectionString { get; set; } = string.Empty;

        /// <inheritdoc />
        public override string Database => "oracle-fake";

        /// <inheritdoc />
        public override string DataSource => "oracle-fake";

        /// <inheritdoc />
        public override string ServerVersion => "19.0.0";

        /// <inheritdoc />
        public override ConnectionState State => state;

        /// <inheritdoc />
        public override void ChangeDatabase(string databaseName)
        {
        }

        /// <inheritdoc />
        public override void Open() => state = ConnectionState.Open;

        /// <inheritdoc />
        public override void Close() => state = ConnectionState.Closed;

        /// <inheritdoc />
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new OracleFakeDbTransaction(this);

        /// <inheritdoc />
        protected override DbCommand CreateDbCommand() => new OracleFakeDbCommand();
    }

    /// <summary>
    /// 模拟 Oracle 数据库事务：Commit/Rollback 无副作用，开启次数计数供断言。
    /// </summary>
    internal sealed class OracleFakeDbTransaction : DbTransaction
    {
        private readonly OracleFakeDbConnection connection;

        /// <summary>事务开启次数（测试类内串行执行，测试构造时清零）。</summary>
        public static int BeginCount;

        /// <summary>初始化模拟事务。</summary>
        /// <param name="connection">模拟连接。</param>
        public OracleFakeDbTransaction(OracleFakeDbConnection connection)
        {
            this.connection = connection;
            BeginCount++;
        }

        /// <inheritdoc />
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        /// <inheritdoc />
        protected override DbConnection DbConnection => connection;

        /// <inheritdoc />
        public override void Commit()
        {
        }

        /// <inheritdoc />
        public override void Rollback()
        {
        }
    }

    /// <summary>
    /// 模拟 Oracle 数据库参数。
    /// </summary>
    internal sealed class OracleFakeDbParameter : DbParameter
    {
        /// <inheritdoc />
        public override DbType DbType { get; set; }

        /// <inheritdoc />
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        /// <inheritdoc />
        public override bool IsNullable { get; set; }

        /// <inheritdoc />
        public override string ParameterName { get; set; }

        /// <inheritdoc />
        public override int Size { get; set; }

        /// <inheritdoc />
        public override string SourceColumn { get; set; } = string.Empty;

        /// <inheritdoc />
        public override bool SourceColumnNullMapping { get; set; }

        /// <inheritdoc />
        public override DataRowVersion SourceVersion { get; set; } = DataRowVersion.Current;

        /// <inheritdoc />
        public override object Value { get; set; }

        /// <inheritdoc />
        public override void ResetDbType()
        {
            DbType = DbType.Object;
        }
    }

    /// <summary>
    /// 模拟 Oracle 参数集合，内部以列表承载参数。
    /// </summary>
    internal sealed class OracleFakeDbParameterCollection : System.Data.Common.DbParameterCollection
    {
        private readonly List<DbParameter> items = new List<DbParameter>();

        /// <summary>内部参数列表（供命令记录快照）。</summary>
        public List<DbParameter> Items => items;

        /// <inheritdoc />
        public override int Count => items.Count;

        /// <inheritdoc />
        public override object SyncRoot => ((System.Collections.ICollection)items).SyncRoot;

        /// <inheritdoc />
        public override int Add(object value)
        {
            items.Add((DbParameter)value);
            return items.Count - 1;
        }

        /// <inheritdoc />
        public override void Clear() => items.Clear();

        /// <inheritdoc />
        public override bool Contains(object value) => items.Contains((DbParameter)value);

        /// <inheritdoc />
        public override bool Contains(string value)
        {
            foreach (var item in items)
            {
                if (string.Equals(item.ParameterName, value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)items).CopyTo(array, index);

        /// <inheritdoc />
        public override System.Collections.IEnumerator GetEnumerator() => items.GetEnumerator();

        /// <inheritdoc />
        public override int IndexOf(object value) => items.IndexOf((DbParameter)value);

        /// <inheritdoc />
        public override int IndexOf(string parameterName)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i].ParameterName, parameterName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        /// <inheritdoc />
        public override void Insert(int index, object value) => items.Insert(index, (DbParameter)value);

        /// <inheritdoc />
        public override void Remove(object value) => items.Remove((DbParameter)value);

        /// <inheritdoc />
        public override void RemoveAt(int index) => items.RemoveAt(index);

        /// <inheritdoc />
        public override void RemoveAt(string parameterName) => items.RemoveAt(IndexOf(parameterName));

        /// <inheritdoc />
        protected override DbParameter GetParameter(int index) => items[index];

        /// <inheritdoc />
        protected override DbParameter GetParameter(string parameterName) => items[IndexOf(parameterName)];

        /// <inheritdoc />
        protected override void SetParameter(int index, DbParameter value) => items[index] = value;

        /// <inheritdoc />
        protected override void SetParameter(string parameterName, DbParameter value) => items[IndexOf(parameterName)] = value;

        /// <inheritdoc />
        public override void AddRange(Array values)
        {
            foreach (var value in values)
                Add(value);
        }
    }

    /// <summary>
    /// 模拟 Oracle ADO.NET 提供程序工厂，产出模拟连接/命令/参数。
    /// </summary>
    internal sealed class OracleFakeAdoFactory : DbProviderFactory
    {
        /// <summary>单例实例。</summary>
        public static readonly OracleFakeAdoFactory Instance = new OracleFakeAdoFactory();

        private OracleFakeAdoFactory()
        {
        }

        /// <inheritdoc />
        public override DbConnection CreateConnection() => new OracleFakeDbConnection();

        /// <inheritdoc />
        public override DbCommand CreateCommand() => new OracleFakeDbCommand();

        /// <inheritdoc />
        public override DbParameter CreateParameter() => new OracleFakeDbParameter();
    }

    /// <summary>
    /// Oracle 模拟提供程序工厂：组合真实 Oracle 方言/SQL 构建器/参数构建器与模拟 ADO 管道。
    /// <para>参数构建器使用真实 <see cref="OracleParameterBuilder"/>（":" 前缀、真实 OracleParameter），
    /// 尽量贴近真实 Oracle 行为；连接/命令为模拟实现，不建立任何数据库连接。</para>
    /// </summary>
    internal sealed class OracleFakeProviderFactory : DbAccessProviderFactory
    {
        /// <summary>单例实例。</summary>
        public static readonly OracleFakeProviderFactory Instance = new OracleFakeProviderFactory();

        private OracleFakeProviderFactory()
        {
        }

        /// <inheritdoc />
        public override DbProviderFactory DbProviderFactory => OracleFakeAdoFactory.Instance;

        /// <inheritdoc />
        public override IDbParameterBuilder ParameterBuilder => OracleParameterBuilder.Instance;

        /// <inheritdoc />
        public override ISqlBuilder SqlBuilder => OracleSqlBuilder.Instance;

        /// <inheritdoc />
        public override ISqlDialect SqlDialect => OracleDialect.Instance;

        /// <inheritdoc />
        public override ISavepointSqlDialect SavepointSqlDialect => OracleSavepointSqlDialect.Instance;
    }

    /// <summary>
    /// Oracle 模拟数据库访问工厂（连接串仅为占位，模拟管道不建立任何连接；每次实例化以隔离服务器信息缓存）。
    /// </summary>
    internal sealed class OracleFakeDbAccessFactory : DbAccessFactoryBase
    {
        /// <summary>初始化 Oracle 模拟工厂。</summary>
        public OracleFakeDbAccessFactory()
            : base(new DbConnectionOptions { ConnectionString = "Data Source=oracle-fake" })
        {
        }

        /// <inheritdoc />
        public override DbAccessProviderFactory ProviderFactory => OracleFakeProviderFactory.Instance;
    }

    #endregion
}
