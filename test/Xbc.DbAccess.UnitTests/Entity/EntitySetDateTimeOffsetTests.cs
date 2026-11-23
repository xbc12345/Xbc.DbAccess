using System;
using System.Linq;
using System.Threading.Tasks;
using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// DateTimeOffset 列的 SQLite 真库集成测试：参数绑定须归一为 ISO 文本（yyyy-MM-dd HH:mm:ss.fffffffzzz），
    /// 覆盖日期部分谓词命中（strftime 依赖 ISO 文本，非 ISO 文本时恒空）、范围比较的字典序与时间序一致
    ///（跨月份数据验证斜杠区域格式的字典序错乱已修复）与读写往返精度（7 位小数秒 + 时区偏移）。
    /// </summary>
    public class EntitySetDateTimeOffsetTests : EntityTestBase
    {
        #region 测试实体

        [XbcDbEntity.Table("DtoRows")]
        public class DtoRow
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public DateTimeOffset OccurredAt { get; set; }

            public DateTimeOffset? NotifiedAt { get; set; }
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            // SQLite 无原生 DateTimeOffset 类型，列以 TEXT 存储（框架读路径按文本解析为 DateTimeOffset）
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE DtoRows (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    OccurredAt TEXT NOT NULL,
                    NotifiedAt TEXT
                );
            ");
        }

        /// <summary>
        /// 日期部分谓词（Year）应命中正确的行：strftime('%Y', col) 要求列值为 ISO 文本，
        /// 参数归一缺失时提供程序写入区域格式文本（'2026/3/1 ...'），strftime 返回 NULL、谓词恒空。
        /// <para>已知边界（SQLite 方言固有语义，非归一问题）：日期函数对带时区偏移的文本先转 UTC 再提取部分，
        /// 跨年/跨日边界的偏移可能使部分值与 .NET 本地墙上时间偏移一天/一年；本用例的数据（+08:00 偏移）
        /// 转 UTC 后年份不变，断言不受影响。</para>
        /// </summary>
        [Fact]
        public async Task Where_YearPredicate_ShouldMatchByYear()
        {
            await DbAccess.Set<DtoRow>().InsertAsync(new DtoRow { OccurredAt = new DateTimeOffset(2026, 3, 1, 0, 30, 0, TimeSpan.FromHours(8)) });
            await DbAccess.Set<DtoRow>().InsertAsync(new DtoRow { OccurredAt = new DateTimeOffset(2025, 12, 31, 23, 59, 59, TimeSpan.FromHours(8)) });

            var rows = await DbAccess.Set<DtoRow>()
                .Where(r => r.OccurredAt.Year == 2026)
                .ToListAsync();

            var row = Assert.Single(rows);
            Assert.Equal(2026, row.OccurredAt.Year);
        }

        /// <summary>
        /// 范围比较（&gt;）应按时间序而非字典序命中：跨月份数据（2026-03-01 与 2026-12-01）在
        /// 斜杠区域格式文本下字典序与时间序错乱（'2026/12/1' &lt; '2026/3/1'），ISO 文本下二者一致。
        /// </summary>
        [Fact]
        public async Task Where_RangeComparison_ShouldFollowChronologicalOrder()
        {
            var march = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.FromHours(8));
            var december = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.FromHours(8));

            await DbAccess.Set<DtoRow>().InsertAsync(new DtoRow { OccurredAt = march });
            await DbAccess.Set<DtoRow>().InsertAsync(new DtoRow { OccurredAt = december });

            // 阈值经闭包变量参数化（走 DateTimeOffset 参数绑定路径，非 SQL 字面量）
            var threshold = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(8));
            var rows = await DbAccess.Set<DtoRow>()
                .Where(r => r.OccurredAt > threshold)
                .ToListAsync();

            var row = Assert.Single(rows);
            Assert.Equal(december, row.OccurredAt);
        }

        /// <summary>
        /// 读写往返应保留完整精度（7 位小数秒）与时区偏移：ISO 文本 yyyy-MM-dd HH:mm:ss.fffffffzzz 与
        /// 读路径的不变文化解析互为可逆变换。
        /// </summary>
        [Fact]
        public async Task InsertAndReadBack_ShouldRoundTripWithFullPrecision()
        {
            var occurredAt = new DateTimeOffset(2026, 3, 1, 0, 30, 0, TimeSpan.FromHours(8)).AddTicks(1234567);
            var notifiedAt = new DateTimeOffset(2026, 4, 15, 12, 0, 0, TimeSpan.FromHours(8)).AddTicks(7654321);

            var entity = new DtoRow { OccurredAt = occurredAt, NotifiedAt = notifiedAt };
            await DbAccess.Set<DtoRow>().InsertAsync(entity);

            var row = await DbAccess.Set<DtoRow>()
                .Where(r => r.Id == entity.Id)
                .ToListAsync();

            var loaded = Assert.Single(row);
            Assert.Equal(occurredAt, loaded.OccurredAt);
            Assert.Equal(notifiedAt, loaded.NotifiedAt);
        }

        /// <summary>
        /// 可空 DateTimeOffset 列的 NULL 写入与读回应保持 null 语义（NotifiedAt 未赋值落库为 NULL、读回为 null）。
        /// </summary>
        [Fact]
        public async Task InsertAndReadBack_NullableColumn_ShouldPreserveNull()
        {
            var entity = new DtoRow { OccurredAt = new DateTimeOffset(2026, 3, 1, 0, 30, 0, TimeSpan.FromHours(8)), NotifiedAt = null };
            await DbAccess.Set<DtoRow>().InsertAsync(entity);

            var rows = await DbAccess.Set<DtoRow>()
                .Where(r => r.Id == entity.Id)
                .ToListAsync();

            var loaded = Assert.Single(rows);
            Assert.Null(loaded.NotifiedAt);
        }
    }
}
