using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// bulk 插入（BulkInsertAsync）的 SQLite 真库集成测试：SQLite 无原生批量写入 API，走回退路径（方言逐条策略：事务内逐条 + 命令复用），
    /// 覆盖行数返回、自增列忽略与不回填、退化场景与参数校验异常、并发令牌生成、导航同步、大集合流式执行与调用方事务嵌套。
    /// </summary>
    public class EntitySetBulkInsertTests : EntityTestBase
    {
        #region 测试实体

        public enum UserLevel
        {
            Normal = 0,
            Vip = 2
        }

        [XbcDbEntity.Table("BulkUsers")]
        public class BulkUserEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;

            public int Age { get; set; }

            public int? Bonus { get; set; }

            public UserLevel Level { get; set; }
        }

        /// <summary>
        /// 显式主键实体：验证可空列（null → DBNull）与枚举列（转底层整数）的落库值。
        /// </summary>
        [XbcDbEntity.Table("BulkManualUsers")]
        public class BulkManualUserEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;

            public int? Bonus { get; set; }

            public UserLevel Level { get; set; }
        }

        /// <summary>
        /// 配置并发令牌的实体：bulk 插入同样生成初始令牌。
        /// </summary>
        [XbcDbEntity.Table("BulkTokenUsers")]
        public class BulkTokenUserEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;

            [XbcDbEntity.Column(IsConcurrencyToken = true)]
            public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        }

        /// <summary>
        /// 全部列均为自增列的退化实体：bulk 插入应显式失败。
        /// </summary>
        [XbcDbEntity.Table("BulkAllIdentity")]
        public class BulkAllIdentityEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }
        }

        /// <summary>
        /// 1:N 导航的显式主键父实体：插入后按值优先级同步子实体外键列。
        /// </summary>
        [XbcDbEntity.Table("BulkParents")]
        public class BulkParentEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;

            [Navigation(OtherKey = "ParentId")]
            public List<BulkChildEntity> Children { get; set; }
        }

        [XbcDbEntity.Table("BulkChildren")]
        public class BulkChildEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public int ParentId { get; set; }
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE BulkUsers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Age INTEGER NOT NULL,
                    Bonus INTEGER NULL,
                    Level INTEGER NOT NULL
                );
                CREATE TABLE BulkManualUsers (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Bonus INTEGER NULL,
                    Level INTEGER NOT NULL
                );
                CREATE TABLE BulkTokenUsers (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    RowVersion BLOB NOT NULL
                );
                CREATE TABLE BulkAllIdentity (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT
                );
                CREATE TABLE BulkParents (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL
                );
                CREATE TABLE BulkChildren (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ParentId INTEGER NOT NULL
                );
            ");
        }

        #region 回退路径与基础行为

        [Fact]
        public void BulkInsert_SqliteHasNoNativeInserter_FallsBackToRowByRowInsert()
        {
            // SQLite 无原生批量写入 API：工厂未覆写 BulkInserter，实体层回退批量 INSERT（方言逐条策略）
            Assert.Null(DbAccess.ProviderFactory.BulkInserter);
        }

        [Fact]
        public async Task BulkInsert_InsertsRowsAndReturnsCount()
        {
            var entities = Enumerable.Range(1, 3)
                .Select(i => new BulkManualUserEntity { Id = i, Name = $"User{i}", Bonus = i * 10, Level = UserLevel.Vip })
                .ToList();

            var affectedRows = await DbAccess.Set<BulkManualUserEntity>().BulkInsertAsync(entities);

            Assert.Equal(3, affectedRows);
            var rows = await DbAccess.FromSql<BulkManualUserEntity>("SELECT * FROM BulkManualUsers ORDER BY Id").ToListAsync();
            Assert.Equal(3, rows.Count);
            Assert.Equal("User2", rows[1].Name);
            Assert.Equal(20, rows[1].Bonus);
            Assert.Equal(UserLevel.Vip, rows[1].Level);
        }

        [Fact]
        public async Task BulkInsert_ConvertsNullAndEnumValues()
        {
            var entities = new List<BulkManualUserEntity>
            {
                new BulkManualUserEntity { Id = 1, Name = "Alice", Bonus = null, Level = UserLevel.Normal },
                new BulkManualUserEntity { Id = 2, Name = "Bob", Bonus = 5, Level = UserLevel.Vip }
            };

            await DbAccess.Set<BulkManualUserEntity>().BulkInsertAsync(entities);

            // null 列落库为 NULL，枚举列落库为底层整数
            Assert.Equal(DBNull.Value, await DbAccess.ExecuteScalarAsync("SELECT Bonus FROM BulkManualUsers WHERE Id = 1"));
            Assert.Equal(5L, await DbAccess.ExecuteScalarAsync("SELECT Bonus FROM BulkManualUsers WHERE Id = 2"));
            Assert.Equal(0L, await DbAccess.ExecuteScalarAsync("SELECT Level FROM BulkManualUsers WHERE Id = 1"));
            Assert.Equal(2L, await DbAccess.ExecuteScalarAsync("SELECT Level FROM BulkManualUsers WHERE Id = 2"));
        }

        [Fact]
        public async Task BulkInsert_IdentityEntity_IgnoresIdentityColumnWithoutBackfill()
        {
            var entities = Enumerable.Range(1, 3)
                .Select(i => new BulkUserEntity { Name = $"User{i}", Age = 20 + i })
                .ToList();

            var affectedRows = await DbAccess.Set<BulkUserEntity>().BulkInsertAsync(entities);

            Assert.Equal(3, affectedRows);
            Assert.All(entities, entity => Assert.Equal(0, entity.Id));// 自增 ID 不回填，实体保持默认值

            var rows = await DbAccess.FromSql<BulkUserEntity>("SELECT * FROM BulkUsers ORDER BY Id").ToListAsync();
            Assert.Equal(new List<int> { 1, 2, 3 }, rows.Select(row => row.Id).ToList());// 自增列由数据库生成
        }

        [Fact]
        public async Task BulkInsert_AllIdentityColumns_Throws()
        {
            var entities = new List<BulkAllIdentityEntity> { new BulkAllIdentityEntity() };

            var exception = await Assert.ThrowsAsync<NotSupportedException>(
                () => DbAccess.Set<BulkAllIdentityEntity>().BulkInsertAsync(entities));

            Assert.Contains("全部为自增列", exception.Message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task BulkInsert_NonPositiveBatchSize_Throws(int batchSize)
        {
            var entities = new List<BulkManualUserEntity> { new BulkManualUserEntity { Id = 1, Name = "Alice" } };

            await Assert.ThrowsAsync<ArgumentException>(
                () => DbAccess.Set<BulkManualUserEntity>().BulkInsertAsync(entities, options: new BulkInsertOptions { BatchSize = batchSize }));
        }

        [Fact]
        public async Task BulkInsert_NegativeCommandTimeout_Throws()
        {
            var entities = new List<BulkManualUserEntity> { new BulkManualUserEntity { Id = 1, Name = "Alice" } };

            await Assert.ThrowsAsync<ArgumentException>(
                () => DbAccess.Set<BulkManualUserEntity>().BulkInsertAsync(entities, options: new BulkInsertOptions { CommandTimeout = -1 }));
        }

        [Fact]
        public async Task BulkInsert_EmptyCollection_Throws()
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => DbAccess.Set<BulkManualUserEntity>().BulkInsertAsync(new List<BulkManualUserEntity>()));
        }

        [Fact]
        public async Task BulkInsert_NullElement_Throws()
        {
            var entities = new List<BulkManualUserEntity> { null };

            await Assert.ThrowsAsync<ArgumentException>(
                () => DbAccess.Set<BulkManualUserEntity>().BulkInsertAsync(entities));
        }

        [Fact]
        public async Task BulkInsert_OptionsIgnoredInFallbackPath()
        {
            // 回退路径静默忽略选项（SQLite 无原生 API），合法值不影响插入结果
            var entities = new List<BulkManualUserEntity> { new BulkManualUserEntity { Id = 1, Name = "Alice" } };

            var affectedRows = await DbAccess.Set<BulkManualUserEntity>().BulkInsertAsync(entities, options: new BulkInsertOptions { BatchSize = 10, CommandTimeout = 30 });

            Assert.Equal(1, affectedRows);
            Assert.Equal(1L, await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM BulkManualUsers"));
        }

        #endregion

        #region 并发令牌与导航同步

        [Fact]
        public async Task BulkInsert_GeneratesInitialConcurrencyTokens()
        {
            var entities = Enumerable.Range(1, 3)
                .Select(i => new BulkTokenUserEntity { Id = i, Name = $"User{i}" })
                .ToList();

            await DbAccess.Set<BulkTokenUserEntity>().BulkInsertAsync(entities);

            var rows = await DbAccess.FromSql<BulkTokenUserEntity>("SELECT * FROM BulkTokenUsers ORDER BY Id").ToListAsync();
            Assert.Equal(3, rows.Count);
            Assert.All(rows, row => Assert.Equal(16, row.RowVersion.Length));
            Assert.Equal(3, rows.Select(row => Convert.ToBase64String(row.RowVersion)).Distinct().Count());// 令牌互不相同
        }

        [Fact]
        public async Task BulkInsert_OneToManyNavigation_SyncsChildForeignKeys()
        {
            var parents = new List<BulkParentEntity>
            {
                new BulkParentEntity
                {
                    Id = 1,
                    Name = "p1",
                    Children = new List<BulkChildEntity> { new BulkChildEntity(), new BulkChildEntity() }
                },
                new BulkParentEntity { Id = 2, Name = "p2" }
            };

            await DbAccess.Set<BulkParentEntity>().BulkInsertAsync(parents);

            // 显式主键已有效：子实体外键列默认值时回填父键值，不递归插入子实体
            Assert.All(parents[0].Children, child => Assert.Equal(1, child.ParentId));
            Assert.Equal(0L, await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM BulkChildren"));// 子实体不入库
        }

        #endregion

        #region 分批与事务

        [Fact]
        public async Task BulkInsert_LargeSet_InsertsAllRowsWithinTransaction()
        {
            // 3 个非自增列（Id/Name/RowVersion）；SQLite 已切换逐条策略（不再按 VALUES 批次分批），1200 行逐条执行
            var entities = Enumerable.Range(1, 1200)
                .Select(i => new BulkTokenUserEntity { Id = i, Name = $"User{i}" })
                .ToList();

            var affectedRows = await DbAccess.Set<BulkTokenUserEntity>().BulkInsertAsync(entities);

            Assert.Equal(1200, affectedRows);
            Assert.Equal(1200L, await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM BulkTokenUsers"));
        }

        [Fact]
        public async Task BulkInsert_WithinCallerTransaction_CommitsAsSavepoint()
        {
            var entities = new List<BulkManualUserEntity> { new BulkManualUserEntity { Id = 1, Name = "Alice" } };

            // 调用方已有活动事务：bulk 插入嵌套为保存点子事务，随调用方提交落库
            using (var transaction = await DbAccess.BeginTransactionAsync())
            {
                await DbAccess.Set<BulkManualUserEntity>().BulkInsertAsync(entities);
                transaction.Commit();
            }

            Assert.Equal(1L, await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM BulkManualUsers"));
        }

        #endregion
    }
}
