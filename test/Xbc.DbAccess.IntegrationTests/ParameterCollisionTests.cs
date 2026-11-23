using Xbc.DbAccess;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.IntegrationTests.Infrastructure;
using Xunit;
using static Xbc.DbAccess.IntegrationTests.Infrastructure.RealDbDialect;

namespace Xbc.DbAccess.IntegrationTests
{
    /// <summary>
    /// 参数名撞名回归：框架生成的组合参数名（批量插入的"列名_行号"、并发令牌的"列名_Old"、IN 展开的"参数名_索引"）
    /// 与真实列名/用户参数名冲突时，必须经参数名分配器自动让位，保证参数不错位、不重名——
    /// 一旦错位即发生不可逆的数据损坏（值写错列），这是本测试类的核心守护目标。
    /// </summary>
    public abstract class ParameterCollisionTestsBase : RealDbTestBase
    {
        protected ParameterCollisionTestsBase(RealDbDialect dialect) : base(dialect)
        {
        }

        protected override Type[] EntityTypes => new[] { typeof(CollisionRow), typeof(TokenCollisionRow) };

        [SkippableFact]
        public async Task InsertRangeAsync_ColumnNameCollidesWithRowNumberedParameter()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<CollisionRow>(DbAccess);

            // 无自增主键 → InsertRangeAsync 走批量插入（多值 INSERT 参数名为"列名_行号"组合；SQLite 为逐条策略，参数名为列名）：
            // 列 Age 行 0 的组合参数 Age_0 与真实列 Age_0 撞名、列 Name 行 1 的 Name_1 与真实列 Name_1 撞名
            var rows = Enumerable.Range(1, 3).Select(i => new CollisionRow
            {
                Id = i,
                Name = $"N{i}",
                Name_1 = $"N1_{i}",
                Age = 10 + i,
                Age_0 = 100 + i
            }).ToList();
            await entitySet.InsertRangeAsync(rows);

            // 逐行查回验证：撞名列的值必须原样落库（修复前重名参数会错位写入其他行的值）
            foreach (var expected in rows)
            {
                var found = await entitySet.FindAsync(expected.Id);
                Assert.NotNull(found);
                Assert.Equal(expected.Name, found!.Name);
                Assert.Equal(expected.Name_1, found.Name_1);
                Assert.Equal(expected.Age, found.Age);
                Assert.Equal(expected.Age_0, found.Age_0);
            }
        }

        [SkippableFact]
        public async Task UpdateDeleteAsync_TokenOldSuffixCollidesWithRealColumn()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<TokenCollisionRow>(DbAccess);

            // 令牌 WHERE 参数名为 RowVersion_Old，与真实普通列 RowVersion_Old 撞名：
            // Insert → Update → Delete 全链路验证令牌参数与普通列参数互不污染
            var row = new TokenCollisionRow { Name = "A", RowVersion_Old = "column-value-1" };
            await entitySet.InsertAsync(row);

            var loaded = await entitySet.FindAsync(row.Id);
            Assert.NotNull(loaded);
            Assert.Equal("column-value-1", loaded!.RowVersion_Old);
            var tokenBefore = loaded.RowVersion.ToArray();

            loaded.Name = "A2";
            loaded.RowVersion_Old = "column-value-2";
            await entitySet.UpdateAsync(loaded);

            var after = await entitySet.FindAsync(row.Id);
            Assert.NotNull(after);
            Assert.Equal("A2", after!.Name);
            Assert.Equal("column-value-2", after.RowVersion_Old);// 普通列未被令牌旧值参数污染
            Assert.NotEqual(tokenBefore, after.RowVersion);      // 令牌正常轮换

            await entitySet.DeleteAsync(after);// Delete 的令牌 WHERE 参数同样与该列撞名
            Assert.Equal(0, await entitySet.AsQuery().CountAsync());
        }

        [SkippableFact]
        public async Task InExpansion_ParameterNameCollidesWithUserParameter()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<CollisionRow>(DbAccess);
            var rows = Enumerable.Range(1, 3).Select(i => new CollisionRow
            {
                Id = i,
                Name = $"N{i}",
                Name_1 = $"N1_{i}",
                Age = 10 + i,
                Age_0 = 100 + i
            }).ToList();
            await entitySet.InsertRangeAsync(rows);

            // IN 展开：集合参数 Ids 展开为 Ids_0、Ids_1，与用户参数 Ids_0 撞名；
            // 声明顺序两个方向都要验证——分配器登记了全部属性名，展开名应让位为 Ids_0_2
            var prefix = DbAccess.ProviderFactory.ParameterBuilder.ParameterPrefix;
            var sql = $"SELECT COUNT(*) FROM {Qi("it_param_collision_row")} WHERE {Qi("Id")} IN ({prefix}Ids) AND {Qi("Age")} <> {prefix}Ids_0";

            var count1 = await DbAccess.ExecuteScalarAsync(sql, new { Ids_0 = 999, Ids = new[] { 1, 2 } });
            Assert.Equal(2, Convert.ToInt32(count1));

            var count2 = await DbAccess.ExecuteScalarAsync(sql, new { Ids = new[] { 1, 2 }, Ids_0 = 999 });
            Assert.Equal(2, Convert.ToInt32(count2));
        }

        [SkippableFact]
        public async Task InExpansion_DapperStyleWithoutParentheses()
        {
            SkipIfDialectUnavailable();
            var entitySet = new EntitySet<CollisionRow>(DbAccess);
            var rows = Enumerable.Range(1, 3).Select(i => new CollisionRow
            {
                Id = i,
                Name = $"N{i}",
                Name_1 = $"N1_{i}",
                Age = 10 + i,
                Age_0 = 100 + i
            }).ToList();
            await entitySet.InsertRangeAsync(rows);

            var prefix = DbAccess.ProviderFactory.ParameterBuilder.ParameterPrefix;

            // Dapper 风格：IN @Ids 不带括号，展开时自动补括号（大小写与 NOT IN 均支持）
            var noParen = $"SELECT COUNT(*) FROM {Qi("it_param_collision_row")} WHERE {Qi("Id")} IN {prefix}Ids";
            Assert.Equal(2, Convert.ToInt32(await DbAccess.ExecuteScalarAsync(noParen, new { Ids = new[] { 1, 2 } })));

            var lowerCase = $"SELECT COUNT(*) FROM {Qi("it_param_collision_row")} WHERE {Qi("Id")} in {prefix}Ids";
            Assert.Equal(2, Convert.ToInt32(await DbAccess.ExecuteScalarAsync(lowerCase, new { Ids = new[] { 2, 3 } })));

            var notIn = $"SELECT COUNT(*) FROM {Qi("it_param_collision_row")} WHERE {Qi("Id")} NOT IN {prefix}Ids";
            Assert.Equal(1, Convert.ToInt32(await DbAccess.ExecuteScalarAsync(notIn, new { Ids = new[] { 1, 2 } })));

            // 已带括号写法不产生双括号，行为不变
            var withParen = $"SELECT COUNT(*) FROM {Qi("it_param_collision_row")} WHERE {Qi("Id")} IN ({prefix}Ids)";
            Assert.Equal(2, Convert.ToInt32(await DbAccess.ExecuteScalarAsync(withParen, new { Ids = new[] { 1, 2 } })));
        }

        #region 测试实体

        /// <summary>批量插入撞名测试实体（本测试类专属，表名 it_param_collision_row；无自增主键以触发批量插入路径）。</summary>
        [Table("it_param_collision_row")]
        public class CollisionRow
        {
            [PrimaryKey]
            public int Id { get; set; }

            public string Name { get; set; }

            /// <summary>与列 Name 行 1 的组合参数 Name_1 撞名的真实列。</summary>
            public string Name_1 { get; set; }

            public int Age { get; set; }

            /// <summary>与列 Age 行 0 的组合参数 Age_0 撞名的真实列。</summary>
            public int Age_0 { get; set; }
        }

        /// <summary>并发令牌撞名测试实体（本测试类专属，表名 it_token_collision_row）。</summary>
        [Table("it_token_collision_row")]
        public class TokenCollisionRow
        {
            [PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; }

            /// <summary>与并发令牌 WHERE 参数名 RowVersion_Old 撞名的真实普通列。</summary>
            public string RowVersion_Old { get; set; }

            [Column(IsConcurrencyToken = true)]
            public byte[] RowVersion { get; set; }
        }

        #endregion
    }

    /// <summary>参数撞名回归的 PostgreSQL 真库用例。</summary>
    public class PgSqlParameterCollisionTests : ParameterCollisionTestsBase
    {
        public PgSqlParameterCollisionTests() : base(PgSql)
        {
        }
    }

    /// <summary>参数撞名回归的 SQL Server 真库用例。</summary>
    public class SqlServerParameterCollisionTests : ParameterCollisionTestsBase
    {
        public SqlServerParameterCollisionTests() : base(RealDbDialect.SqlServer)
        {
        }
    }

    /// <summary>参数撞名回归的 MySQL 真库用例。</summary>
    public class MySqlParameterCollisionTests : ParameterCollisionTestsBase
    {
        public MySqlParameterCollisionTests() : base(RealDbDialect.MySql)
        {
        }
    }

    /// <summary>参数撞名回归的 Oracle 真库用例。</summary>
    public class OracleParameterCollisionTests : ParameterCollisionTestsBase
    {
        public OracleParameterCollisionTests() : base(RealDbDialect.Oracle)
        {
        }
    }

    /// <summary>参数撞名回归的 SQLite 真库用例（本地文件数据库，恒可用）。</summary>
    public class SqliteParameterCollisionTests : ParameterCollisionTestsBase
    {
        public SqliteParameterCollisionTests() : base(RealDbDialect.Sqlite)
        {
        }
    }
}
