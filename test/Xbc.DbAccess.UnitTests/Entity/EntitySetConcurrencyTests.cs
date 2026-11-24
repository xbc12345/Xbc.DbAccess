using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 乐观并发令牌（[Column(IsConcurrencyToken = true)]，byte[]）的 SQLite 真库集成测试：
    /// 插入生成初始令牌、更新换发新令牌、令牌过期/行不存在的冲突异常、批量路径与无令牌实体的行为不变。
    /// </summary>
    public class EntitySetConcurrencyTests : EntityTestBase
    {
        #region 测试实体

        [XbcDbEntity.Table("ConcurrencyUsers")]
        public class UserEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
            public int Age { get; set; }

            [XbcDbEntity.Column(IsConcurrencyToken = true)]
            public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        }

        /// <summary>
        /// 无自增主键的令牌实体：覆盖批量插入的方言逐条路径（SQLite）。
        /// </summary>
        [XbcDbEntity.Table("ConcurrencyExplicitPk")]
        public class ExplicitPkEntity
        {
            [XbcDbEntity.PrimaryKey]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;

            [XbcDbEntity.Column(IsConcurrencyToken = true)]
            public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        }

        /// <summary>
        /// 未配置令牌的普通实体：验证既有行为不受影响。
        /// </summary>
        [XbcDbEntity.Table("ConcurrencyPlainUsers")]
        public class PlainUserEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE ConcurrencyUsers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Age INTEGER NOT NULL,
                    RowVersion BLOB NOT NULL
                );

                CREATE TABLE ConcurrencyExplicitPk (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    RowVersion BLOB NOT NULL
                );

                CREATE TABLE ConcurrencyPlainUsers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                );
            ");
        }

        #region 插入：生成初始令牌

        [Fact]
        public async Task Insert_GeneratesInitialToken()
        {
            var entity = new UserEntity { Name = "Alice", Age = 20 };

            await DbAccess.Set<UserEntity>().InsertAsync(entity);

            Assert.NotNull(entity.RowVersion);
            Assert.Equal(16, entity.RowVersion.Length);// Guid 字节

            var row = await LoadUserAsync(entity.Id);
            Assert.Equal(entity.RowVersion, row.RowVersion);// 落库值与实体一致
        }

        [Fact]
        public async Task InsertRange_NoIdentityPath_GeneratesDistinctTokens()
        {
            var entities = Enumerable.Range(1, 3)
                .Select(i => new ExplicitPkEntity { Id = i, Name = $"User{i}" })
                .ToList();

            await DbAccess.Set<ExplicitPkEntity>().InsertRangeAsync(entities);

            var rows = await DbAccess.FromSql<ExplicitPkEntity>("SELECT * FROM ConcurrencyExplicitPk ORDER BY Id").ToListAsync();
            Assert.Equal(3, rows.Count);
            Assert.All(rows, row =>
            {
                Assert.NotNull(row.RowVersion);
                Assert.Equal(16, row.RowVersion.Length);
            });
            Assert.Equal(3, rows.Select(row => Convert.ToBase64String(row.RowVersion)).Distinct().Count());// 令牌互不相同
        }

        /// <summary>
        /// SQLite 方言启用逐条策略（<see cref="ISqlDialect.PreferRowByRowBatchInsert"/>）：无自增主键的大集合
        /// 批量插入走"事务内逐条 + 命令复用"路径（行数超过原多值单批上限 500），全量落库、值正确且令牌逐行互不相同
        /// （回归守护：策略切换不影响数据正确性与乐观并发令牌生成）。
        /// </summary>
        [Fact]
        public async Task InsertRange_LargeSet_RowByRowStrategy_InsertsAllWithinTransaction()
        {
            var entities = Enumerable.Range(1, 1200)
                .Select(i => new ExplicitPkEntity { Id = i, Name = $"User{i}" })
                .ToList();

            var affectedRows = await DbAccess.Set<ExplicitPkEntity>().InsertRangeAsync(entities);

            Assert.Equal(1200, affectedRows);
            Assert.Equal(1200L, await DbAccess.ExecuteScalarAsync("SELECT COUNT(*) FROM ConcurrencyExplicitPk"));

            var rows = await DbAccess.FromSql<ExplicitPkEntity>("SELECT * FROM ConcurrencyExplicitPk ORDER BY Id").ToListAsync();
            Assert.Equal(1200, rows.Count);
            Assert.All(rows, row => Assert.Equal($"User{row.Id}", row.Name));// 逐行值正确（参数不错位）
            Assert.Equal(1200, rows.Select(row => Convert.ToBase64String(row.RowVersion)).Distinct().Count());// 令牌逐行互不相同
        }

        [Fact]
        public async Task Upsert_GeneratesInitialToken()
        {
            var entity = new UserEntity { Name = "Bob", Age = 30 };

            await DbAccess.Set<UserEntity>().UpsertAsync(entity);

            var row = await LoadUserAsync(entity.Id);
            Assert.NotNull(row.RowVersion);
            Assert.Equal(16, row.RowVersion.Length);
        }

        [Fact]
        public async Task Upsert_RenewsTokenOnUpdatePath()
        {
            // 插入建立初始令牌
            var entity = new UserEntity { Name = "Alice", Age = 20 };
            await DbAccess.Set<UserEntity>().InsertAsync(entity);
            var oldToken = entity.RowVersion;

            // 模拟并发读取的过时快照实体（持有插入时的旧令牌）
            var staleEntity = new UserEntity { Id = entity.Id, Name = "Alice", Age = 20, RowVersion = oldToken };

            // Upsert 同主键 → 走更新路径 → 换发新令牌（不换发会让过时快照的后续更新静默覆盖本次写入）
            entity.Name = "Alice2";
            await DbAccess.Set<UserEntity>().UpsertAsync(entity);

            // 实体与数据库的令牌均已换发为新值
            Assert.NotEqual(oldToken, entity.RowVersion);
            var row = await LoadUserAsync(entity.Id);
            Assert.Equal(entity.RowVersion, row.RowVersion);

            // 过时快照（旧令牌）的后续更新检出冲突
            staleEntity.Age = 99;
            await Assert.ThrowsAsync<DbConcurrencyException>(
                () => DbAccess.Set<UserEntity>().UpdateAsync(staleEntity));
        }

        #endregion

        #region 更新：换发新令牌与冲突检测

        [Fact]
        public async Task Update_RenewsTokenOnSuccess()
        {
            var entity = new UserEntity { Name = "Alice", Age = 20 };
            await DbAccess.Set<UserEntity>().InsertAsync(entity);
            var oldToken = entity.RowVersion;

            entity.Name = "Alice2";
            var affectedRows = await DbAccess.Set<UserEntity>().UpdateAsync(entity);

            Assert.Equal(1, affectedRows);
            Assert.NotEqual(oldToken, entity.RowVersion);// 实体已回填新令牌

            var row = await LoadUserAsync(entity.Id);
            Assert.Equal("Alice2", row.Name);
            Assert.Equal(entity.RowVersion, row.RowVersion);// 落库值与新令牌一致
            Assert.NotEqual(oldToken, row.RowVersion);// 旧令牌已作废
        }

        [Fact]
        public async Task Update_StaleToken_ThrowsDbConcurrencyException()
        {
            var entity = new UserEntity { Name = "Alice", Age = 20 };
            await DbAccess.Set<UserEntity>().InsertAsync(entity);

            // 两份加载副本：第一份更新后换发令牌，第二份持有过期令牌
            var first = await LoadUserAsync(entity.Id);
            var second = await LoadUserAsync(entity.Id);
            first.Name = "Alice2";
            await DbAccess.Set<UserEntity>().UpdateAsync(first);

            second.Name = "Conflict";
            await Assert.ThrowsAsync<DbConcurrencyException>(
                () => DbAccess.Set<UserEntity>().UpdateAsync(second));

            // 落库值仍为第一份的新令牌与内容
            var row = await LoadUserAsync(entity.Id);
            Assert.Equal("Alice2", row.Name);
            Assert.Equal(first.RowVersion, row.RowVersion);
        }

        [Fact]
        public async Task Update_MissingRow_ThrowsDbConcurrencyException()
        {
            var entity = new UserEntity { Name = "Alice", Age = 20 };
            await DbAccess.Set<UserEntity>().InsertAsync(entity);

            // 加载后改主键指向不存在的行：令牌有效但行不存在，同样按并发冲突语义抛出
            var ghost = await LoadUserAsync(entity.Id);
            ghost.Id = entity.Id + 1000;

            await Assert.ThrowsAsync<DbConcurrencyException>(
                () => DbAccess.Set<UserEntity>().UpdateAsync(ghost));
        }

        [Fact]
        public async Task Update_EmptyToken_ThrowsInvalidOperationException()
        {
            var entity = new UserEntity { Name = "Alice", Age = 20 };
            await DbAccess.Set<UserEntity>().InsertAsync(entity);

            // 手工构造的实体令牌为空：无法构建等值条件，显式失败优于误报并发冲突
            var manual = new UserEntity { Id = entity.Id, Name = "Manual", Age = 20 };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => DbAccess.Set<UserEntity>().UpdateAsync(manual));
        }

        [Fact]
        public async Task UpdateRange_ConflictRollsBackAll()
        {
            var e1 = new UserEntity { Name = "A1", Age = 1 };
            var e2 = new UserEntity { Name = "A2", Age = 2 };
            var set = DbAccess.Set<UserEntity>();
            await set.InsertAsync(e1);
            await set.InsertAsync(e2);

            // e1 的过期副本先加载，再由另一副本并发修改（换发令牌），e2 保持新鲜
            var e1Stale = await LoadUserAsync(e1.Id);
            var e1Other = await LoadUserAsync(e1.Id);
            await set.UpdateAsync(e1Other);

            e1Stale.Name = "B1";
            var e2Fresh = await LoadUserAsync(e2.Id);
            e2Fresh.Name = "B2";

            // 逐条更新：e2 成功在前、e1 冲突在后，外层事务整体回滚
            await Assert.ThrowsAsync<DbConcurrencyException>(
                () => set.UpdateRangeAsync(new[] { e2Fresh, e1Stale }));

            var row2 = await LoadUserAsync(e2.Id);
            Assert.Equal("A2", row2.Name);// e2 的更新已回滚
        }

        [Fact]
        public async Task UpdateRange_DuplicateInstanceConflict_RestoresOriginalToken()
        {
            var e1 = new UserEntity { Name = "D1", Age = 1 };
            var e2 = new UserEntity { Name = "D2", Age = 2 };
            var set = DbAccess.Set<UserEntity>();
            await set.InsertAsync(e1);
            await set.InsertAsync(e2);
            var originalToken = e1.RowVersion;

            // e2 的过期副本构造冲突源（另一副本并发修改换发令牌），e1 保持新鲜
            var e2Stale = await LoadUserAsync(e2.Id);
            var e2Other = await LoadUserAsync(e2.Id);
            await set.UpdateAsync(e2Other);

            // 同一实例 e1 重复出现两次 + 冲突实体收尾：倒序回放使首次记录的原始令牌最后落地，
            // 终态与回滚后的数据库一致（正序回放终态落在中间态令牌上，后续更新会误报冲突）
            e1.Name = "D1-Updated";
            await Assert.ThrowsAsync<DbConcurrencyException>(
                () => set.UpdateRangeAsync(new[] { e1, e1, e2Stale }));

            Assert.Equal(originalToken, e1.RowVersion);// e1 恢复为插入时的原始令牌
            var row = await LoadUserAsync(e1.Id);
            Assert.Equal(originalToken, row.RowVersion);// 数据库回滚后同为原始令牌
            Assert.Equal("D1", row.Name);// e1 的两次更新均已回滚

            // e1 后续更新不误报冲突（倒序回放的正确性验证）
            e1.Age = 42;
            await set.UpdateAsync(e1);
        }

        #endregion

        #region 删除：令牌校验

        [Fact]
        public async Task Delete_WithFreshToken_Succeeds()
        {
            var entity = new UserEntity { Name = "Alice", Age = 20 };
            await DbAccess.Set<UserEntity>().InsertAsync(entity);

            var fresh = await LoadUserAsync(entity.Id);
            var affectedRows = await DbAccess.Set<UserEntity>().DeleteAsync(fresh);

            Assert.Equal(1, affectedRows);
            Assert.Empty(await DbAccess.FromSql<UserEntity>("SELECT * FROM ConcurrencyUsers").ToListAsync());
        }

        [Fact]
        public async Task Delete_StaleToken_ThrowsDbConcurrencyException()
        {
            var entity = new UserEntity { Name = "Alice", Age = 20 };
            await DbAccess.Set<UserEntity>().InsertAsync(entity);

            var first = await LoadUserAsync(entity.Id);
            var second = await LoadUserAsync(entity.Id);
            await DbAccess.Set<UserEntity>().DeleteAsync(first);

            await Assert.ThrowsAsync<DbConcurrencyException>(
                () => DbAccess.Set<UserEntity>().DeleteAsync(second));

            // 行已被第一份删除，落库无残留
            Assert.Empty(await DbAccess.FromSql<UserEntity>("SELECT * FROM ConcurrencyUsers").ToListAsync());
        }

        [Fact]
        public async Task DeleteRange_TokenEntities_DeletesRowByRow()
        {
            var set = DbAccess.Set<UserEntity>();
            var e1 = new UserEntity { Name = "A1", Age = 1 };
            var e2 = new UserEntity { Name = "A2", Age = 2 };
            await set.InsertAsync(e1);
            await set.InsertAsync(e2);

            var fresh1 = await LoadUserAsync(e1.Id);
            var fresh2 = await LoadUserAsync(e2.Id);

            var affectedRows = await set.DeleteRangeAsync(new[] { fresh1, fresh2 });

            Assert.Equal(2, affectedRows);
            Assert.Empty(await DbAccess.FromSql<UserEntity>("SELECT * FROM ConcurrencyUsers").ToListAsync());
        }

        #endregion

        #region 无令牌实体：行为不变

        [Fact]
        public async Task Update_NoToken_MissingRow_ReturnsZero()
        {
            var entity = new PlainUserEntity { Name = "Alice" };
            await DbAccess.Set<PlainUserEntity>().InsertAsync(entity);

            // 未配置令牌：行不存在仍静默返回 0，不抛并发冲突
            var ghost = new PlainUserEntity { Id = entity.Id + 1000, Name = "Ghost" };
            var affectedRows = await DbAccess.Set<PlainUserEntity>().UpdateAsync(ghost);

            Assert.Equal(0, affectedRows);
        }

        #endregion

        #region 辅助

        private async Task<UserEntity> LoadUserAsync(int id)
        {
            var rows = await DbAccess.FromSql<UserEntity>("SELECT * FROM ConcurrencyUsers WHERE Id = @id", new { id }).ToListAsync();
            return Assert.Single(rows);
        }

        #endregion
    }
}
