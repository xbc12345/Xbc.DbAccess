using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity.Set
{
    /// <summary>
    /// 主键查找的键值校验分支（EntitySet.Select.cs 的 ValidatePrimaryKeyValue）的 SQLite 真库补测：
    /// 数值主键接受可转换键值（类型转换后命中行）、可转换但转换失败的键值携带根因异常、不兼容类型直接拒绝。
    /// </summary>
    public class EntitySetFindKeyTests : EntityTestBase
    {
        [XbcDbEntity.Table("FindKeyUsers")]
        public class FindUser
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
        }

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE FindKeyUsers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                );
                INSERT INTO FindKeyUsers (Id, Name) VALUES (1, 'Alice');
                INSERT INTO FindKeyUsers (Id, Name) VALUES (2, 'Bob');
                INSERT INTO FindKeyUsers (Id, Name) VALUES (5, 'Eve');
            ");
        }

        [Fact]
        public async Task Find_ConvertibleNumericKey_ReturnsEntity()
        {
            var set = DbAccess.Set<FindUser>();

            // 主键为 int，传入可转换的 long：转换后按主键命中行
            var entity = await set.FindAsync(5L);

            Assert.NotNull(entity);
            Assert.Equal(5, entity.Id);
            Assert.Equal("Eve", entity.Name);
        }

        [Fact]
        public async Task Find_UnconvertibleStringKey_ThrowsArgumentExceptionWithRootCause()
        {
            var set = DbAccess.Set<FindUser>();

            // 可转换类型但转换失败（"abc" → int 抛 FormatException）：ArgumentException 携带根因异常
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => set.FindAsync("abc"));

            Assert.IsType<FormatException>(exception.InnerException);
        }

        [Fact]
        public async Task Find_IncompatibleKeyType_ThrowsArgumentException()
        {
            var set = DbAccess.Set<FindUser>();

            // 非可转换类型（Guid）：不接受任何隐式转换，直接拒绝
            await Assert.ThrowsAsync<ArgumentException>(() => set.FindAsync(Guid.NewGuid()));
        }
    }
}
