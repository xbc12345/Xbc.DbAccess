using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 查询链标量聚合（AsQuery/Where + COUNT/SUM/AVG/MAX/MIN 与 Any/All 的 EXISTS 布尔化）的 SQLite 真库测试：
    /// 验证聚合值、空表/无匹配的可空语义、窗口叠加与选择器校验异常。
    /// </summary>
    public class EntitySetAggregateTests : EntityTestBase
    {
        #region 测试实体

        [XbcDbEntity.Table("AggregateUsers")]
        public class UserEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
            public int Amount { get; set; }
            public int? Score { get; set; }
            public bool IsActive { get; set; }

            [XbcDbEntity.NotMapped]
            public string Note { get; set; }

            [XbcDbEntity.NotMapped]
            public int Level { get; set; }
        }

        [XbcDbEntity.Table("AggregateEmpty")]
        public class EmptyEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public int Amount { get; set; }
        }

        /// <summary>
        /// 带自定义转换器的数值列实体：属性 decimal、存储文本（模拟加密等不同构存储）——聚合守卫的测试载体。
        /// </summary>
        [XbcDbEntity.Table("AggregateTransform")]
        public class TransformEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Column(ConverterType = typeof(AmountTextConverter))]
            public decimal Amount { get; set; }
        }

        /// <summary>
        /// Amount 列的模拟转换器：decimal 属性以文本形式存储。
        /// </summary>
        private sealed class AmountTextConverter : Xbc.DbAccess.Entity.IDbValueConverter<decimal>
        {
            public decimal ConvertFrom(object dbValue) => dbValue == null || dbValue == DBNull.Value ? 0m : decimal.Parse(dbValue.ToString(), System.Globalization.CultureInfo.InvariantCulture);
            public object ConvertTo(decimal value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 带枚举列的实体：SUM/AVG 枚举显式拒绝与 MAX/MIN 放行的测试载体（数值存储与名称字符串存储各一列）。
        /// </summary>
        [XbcDbEntity.Table("AggregateEnum")]
        public class EnumEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public AggregateStatus Status { get; set; }

            [XbcDbEntity.Column(EnumAsString = true)]
            public AggregateStatus StatusName { get; set; }
        }

        /// <summary>
        /// 聚合测试枚举：名称字母序 Active &lt; Inactive &lt; Pending，与定义序（Pending 最小）刻意相反，验证聚合序为存储值序。
        /// </summary>
        public enum AggregateStatus
        {
            Pending = 1,
            Active = 2,
            Inactive = 3
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE AggregateUsers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Amount INTEGER NOT NULL,
                    Score INTEGER NULL,
                    IsActive INTEGER NOT NULL
                );
                INSERT INTO AggregateUsers (Name, Amount, Score, IsActive) VALUES ('Alice', 100, 90, 1);
                INSERT INTO AggregateUsers (Name, Amount, Score, IsActive) VALUES ('Bob', 200, NULL, 0);
                INSERT INTO AggregateUsers (Name, Amount, Score, IsActive) VALUES ('Carol', 300, 60, 1);

                CREATE TABLE AggregateEmpty (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Amount INTEGER NOT NULL
                );

                CREATE TABLE AggregateTransform (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Amount TEXT NOT NULL
                );

                CREATE TABLE AggregateEnum (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Status INTEGER NOT NULL,
                    StatusName TEXT NOT NULL
                );
                INSERT INTO AggregateEnum (Status, StatusName) VALUES (1, 'Pending');
                INSERT INTO AggregateEnum (Status, StatusName) VALUES (2, 'Active');
                INSERT INTO AggregateEnum (Status, StatusName) VALUES (3, 'Inactive');
            ");
        }

        #region Count

        [Fact]
        public async Task Count_WithoutPredicate_ReturnsTotalRows()
        {
            var count = await DbAccess.Set<UserEntity>().AsQuery().CountAsync();

            Assert.Equal(3, count);
        }

        [Fact]
        public async Task Count_WithWhere_ReturnsMatchingRows()
        {
            var count = await DbAccess.Set<UserEntity>().Where(u => u.IsActive && u.Amount >= 200).CountAsync();

            Assert.Equal(1, count);
        }

        [Fact]
        public async Task Count_EmptyTable_ReturnsZero()
        {
            var count = await DbAccess.Set<EmptyEntity>().AsQuery().CountAsync();

            Assert.Equal(0, count);
        }

        [Fact]
        public async Task Count_NoMatch_ReturnsZero()
        {
            var count = await DbAccess.Set<UserEntity>().Where(u => u.Amount > 10000).CountAsync();

            Assert.Equal(0, count);
        }

        [Fact]
        public async Task Count_WithOrderBy_NoWindow_IgnoresOrdering()
        {
            // 排序对计数无意义：无窗口时链式 Count 忽略排序、统计 WHERE 命中行数（Alice/Carol 两行 active）
            var count = await DbAccess.Set<UserEntity>().Where(u => u.IsActive).OrderBy(u => u.Amount).CountAsync();

            Assert.Equal(2, count);
        }

        [Fact]
        public async Task Count_WithWindow_CountsWindowRows()
        {
            // 窗口语义：COUNT 包裹在窗口子查询外，统计窗口内行数（1）而非 WHERE 命中总数（3）
            var count = await DbAccess.Set<UserEntity>().Where(u => u.Amount > 0).OrderByDescending(u => u.Amount).Take(1).CountAsync();

            Assert.Equal(1, count);
        }

        #endregion

        #region 链式聚合（Where/OrderBy/窗口 叠加）
        // 聚合只存在于查询链：条件经 Where、全表经 AsQuery，本区域验证链式形态的
        // 条件过滤、排序忽略与窗口聚合语义

        [Fact]
        public async Task ChainAggregate_OrderBy_NoWindow_IgnoresOrdering()
        {
            // 排序对聚合值无影响：无窗口时忽略排序（active 行 Alice 100 + Carol 300 = 400）
            var sum = await DbAccess.Set<UserEntity>().Where(u => u.IsActive).OrderBy(u => u.Amount).SumAsync(u => u.Amount);

            Assert.Equal(400m, sum);
        }

        [Fact]
        public async Task ChainAggregate_WithWindow_AggregatesWindowRows()
        {
            // 窗口语义：聚合包裹在窗口子查询外，对窗口内行（Amount 降序前 2 行：300/200）求值
            var query = DbAccess.Set<UserEntity>().Where(u => u.Amount > 0).OrderByDescending(u => u.Amount).Take(2);

            Assert.Equal(500m, await query.SumAsync(u => u.Amount));
            Assert.Equal(250m, await query.AverageAsync(u => u.Amount));
            Assert.Equal(300, await query.MaxAsync(u => u.Amount));
            Assert.Equal(200, await query.MinAsync(u => u.Amount));
        }

        [Fact]
        public async Task ChainAggregate_WithWindow_SkipTake_AggregatesWindowRows()
        {
            // Skip(1).Take(1)：窗口仅含中间行 Bob（Amount 200），SUM/MAX 对窗口内行求值
            var query = DbAccess.Set<UserEntity>().Where(u => u.Amount > 0).OrderBy(u => u.Amount).Skip(1).Take(1);

            Assert.Equal(200m, await query.SumAsync(u => u.Amount));
            Assert.Equal(200, await query.MaxAsync(u => u.Amount));
        }

        [Fact]
        public async Task ChainAggregate_NullableColumn_UsesNullableOverload()
        {
            // int? 列经链式 MAX/MIN：NULL 行不参与比较（Score: 90/NULL/60）
            var max = await DbAccess.Set<UserEntity>().Where(u => u.IsActive).MaxAsync(u => u.Score);
            var min = await DbAccess.Set<UserEntity>().Where(u => u.Amount > 0).MinAsync(u => u.Score);

            Assert.Equal(90, max);
            Assert.Equal(60, min);
        }

        [Fact]
        public async Task ChainAggregate_NonNumericColumn_DelegatesToDatabase()
        {
            // 链式路径同样不预检数值类型：SQLite 对文本列 SUM 返回 0.0（各数据库语义不一，以数据库为准）
            var sum = await DbAccess.Set<UserEntity>().Where(u => u.IsActive).SumAsync(u => u.Name);

            Assert.Equal(0m, sum);
        }

        [Fact]
        public async Task ChainAggregate_TransformColumn_Throws()
        {
            // 存储变换列的聚合对象是存储值而非属性值（派生表引用存储列），链式路径与 Set 层同语义显式拒绝
            await Assert.ThrowsAsync<NotSupportedException>(
                () => DbAccess.Set<TransformEntity>().Where(x => x.Id > 0).MaxAsync(x => x.Amount));
        }

        #endregion

        #region Sum / Average

        [Fact]
        public async Task Sum_NumericColumn_ReturnsTotal()
        {
            var sum = await DbAccess.Set<UserEntity>().AsQuery().SumAsync(u => u.Amount);

            Assert.Equal(600m, sum);
        }

        [Fact]
        public async Task Sum_NullableColumn_IgnoresNullRows()
        {
            var sum = await DbAccess.Set<UserEntity>().AsQuery().SumAsync(u => u.Score);

            Assert.Equal(150m, sum);
        }

        [Fact]
        public async Task Sum_WithWhere_SumsMatchingRowsOnly()
        {
            var sum = await DbAccess.Set<UserEntity>().Where(u => u.Name != "Bob").SumAsync(u => u.Amount);

            Assert.Equal(400m, sum);
        }

        [Fact]
        public async Task Sum_NoMatch_ReturnsNull()
        {
            var sum = await DbAccess.Set<UserEntity>().Where(u => u.Amount > 10000).SumAsync(u => u.Amount);

            Assert.Null(sum);
        }

        [Fact]
        public async Task Sum_EmptyTable_ReturnsNull()
        {
            var sum = await DbAccess.Set<EmptyEntity>().AsQuery().SumAsync(u => u.Amount);

            Assert.Null(sum);
        }

        [Fact]
        public async Task Sum_NonNumericColumn_DelegatesToDatabase()
        {
            // 「有没有意义」由用户决定，框架不预检数值类型：SQLite 对文本列 SUM 返回 0.0（各数据库语义不一，以数据库为准）
            var sum = await DbAccess.Set<UserEntity>().AsQuery().SumAsync(u => u.Name);

            Assert.Equal(0m, sum);
        }

        [Fact]
        public async Task Sum_UnmappedSelector_Throws()
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => DbAccess.Set<UserEntity>().AsQuery().SumAsync(u => u.Note));
        }

        [Fact]
        public async Task Sum_DeepPathSelector_Throws()
        {
            // 多级路径属解析失败（EntityMetadata.ResolveColumn），抛 ArgumentException
            await Assert.ThrowsAsync<ArgumentException>(() => DbAccess.Set<UserEntity>().AsQuery().SumAsync(u => u.Name.Length));
        }

        [Fact]
        public async Task Sum_ConstantSelector_Throws()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => DbAccess.Set<UserEntity>().AsQuery().SumAsync(u => 1));
        }

        [Fact]
        public async Task Sum_TransformColumn_Throws()
        {
            // 属性 decimal、存储文本：SUM 对文本做方言相关的隐式数值转换会产出乱值，守卫显式拒绝（与 keyset 排序键同哲学）
            await Assert.ThrowsAsync<NotSupportedException>(
                () => DbAccess.Set<TransformEntity>().AsQuery().SumAsync(x => x.Amount));
        }

        [Fact]
        public async Task Sum_EnumColumn_SumsStoredNumericValues()
        {
            // 枚举列的 SUM 放行（「有没有意义」由用户决定）：按存储数值聚合（1+2+3），与排序端枚举放行同哲学
            var sum = await DbAccess.Set<EnumEntity>().AsQuery().SumAsync(x => x.Status);

            Assert.Equal(6m, sum);
        }

        [Fact]
        public async Task Max_EnumColumn_ReturnsNumericMax()
        {
            // 数值枚举的 MAX 有明确业务意义（如最高优先级）：按定义序数值聚合，与排序端同语义同待遇
            var max = await DbAccess.Set<EnumEntity>().AsQuery().MaxAsync(x => x.Status);

            Assert.Equal(AggregateStatus.Inactive, max);
        }

        [Fact]
        public async Task Max_EnumAsStringColumn_ReturnsLexicographicMax()
        {
            // 名称枚举的 MAX 按名称字母序聚合（Active < Inactive < Pending，与定义序相反），
            // 结果可观察，与排序键放行枚举同哲学（OrderByDesc(StatusName).First().StatusName ≡ MAX）
            var max = await DbAccess.Set<EnumEntity>().AsQuery().MaxAsync(x => x.StatusName);

            Assert.Equal(AggregateStatus.Pending, max);
        }

        [Fact]
        public async Task Max_TransformColumn_Throws()
        {
            // MAX 按存储值序取最值（文本字典序而非数值序），结果静默错误，守卫显式拒绝
            await Assert.ThrowsAsync<NotSupportedException>(
                () => DbAccess.Set<TransformEntity>().AsQuery().MaxAsync(x => x.Amount));
        }

        [Fact]
        public async Task Average_NumericColumn_ReturnsMean()
        {
            // SQLite 对整数列的 AVG 返回浮点，验证经 decimal 归一后数值正确
            var average = await DbAccess.Set<UserEntity>().AsQuery().AverageAsync(u => u.Amount);

            Assert.Equal(200m, average);
        }

        [Fact]
        public async Task Average_NullableColumn_IgnoresNullRows()
        {
            var average = await DbAccess.Set<UserEntity>().AsQuery().AverageAsync(u => u.Score);

            Assert.Equal(75m, average);
        }

        [Fact]
        public async Task Average_NoMatch_ReturnsNull()
        {
            var average = await DbAccess.Set<UserEntity>().Where(u => u.Amount > 10000).AverageAsync(u => u.Amount);

            Assert.Null(average);
        }

        [Fact]
        public async Task Average_NonNumericColumn_DelegatesToDatabase()
        {
            // 不预检数值类型：SQLite 对文本列 AVG 返回 0.0（各数据库语义不一，以数据库为准）
            var average = await DbAccess.Set<UserEntity>().AsQuery().AverageAsync(u => u.Name);

            Assert.Equal(0m, average);
        }

        #endregion

        #region Max / Min

        [Fact]
        public async Task Max_NumericColumn_ReturnsMaxValue()
        {
            // SQLite 的 MAX 对 int 列返回 long，验证经 ChangeType 归一为 TResult=int
            var max = await DbAccess.Set<UserEntity>().AsQuery().MaxAsync(u => u.Amount);

            Assert.Equal(300, max);
        }

        [Fact]
        public async Task Min_NumericColumn_ReturnsMinValue()
        {
            var min = await DbAccess.Set<UserEntity>().AsQuery().MinAsync(u => u.Amount);

            Assert.Equal(100, min);
        }

        [Fact]
        public async Task Max_NullableColumn_IgnoresNullRows()
        {
            var max = await DbAccess.Set<UserEntity>().AsQuery().MaxAsync(u => u.Score);

            Assert.Equal(90, max);
        }

        [Fact]
        public async Task Min_NullableColumn_IgnoresNullRows()
        {
            var min = await DbAccess.Set<UserEntity>().AsQuery().MinAsync(u => u.Score);

            Assert.Equal(60, min);
        }

        [Fact]
        public async Task Max_WithWhere_ReturnsMaxOfMatchingRows()
        {
            var max = await DbAccess.Set<UserEntity>().Where(u => u.Name != "Carol").MaxAsync(u => u.Amount);

            Assert.Equal(200, max);
        }

        [Fact]
        public async Task Max_NoMatch_ReturnsNull()
        {
            var max = await DbAccess.Set<UserEntity>().Where(u => u.Amount > 10000).MaxAsync(u => u.Amount);

            Assert.Null(max);
        }

        [Fact]
        public async Task Min_EmptyTable_ReturnsNull()
        {
            var min = await DbAccess.Set<EmptyEntity>().AsQuery().MinAsync(u => u.Amount);

            Assert.Null(min);
        }

        [Fact]
        public async Task Max_UnmappedSelector_Throws()
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => DbAccess.Set<UserEntity>().AsQuery().MaxAsync(u => u.Level));
        }

        [Fact]
        public async Task Max_DeepPathSelector_Throws()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => DbAccess.Set<UserEntity>().AsQuery().MaxAsync(u => u.Name.Length));
        }

        #endregion

        #region Any / All

        [Fact]
        public async Task Any_WithoutPredicate_ReturnsTrueWhenTableHasRows()
        {
            var any = await DbAccess.Set<UserEntity>().AsQuery().AnyAsync();

            Assert.True(any);
        }

        [Fact]
        public async Task Any_WithoutPredicate_ReturnsFalseOnEmptyTable()
        {
            var any = await DbAccess.Set<EmptyEntity>().AsQuery().AnyAsync();

            Assert.False(any);
        }

        [Fact]
        public async Task Any_WithPredicate_ReturnsTrueWhenMatchExists()
        {
            var any = await DbAccess.Set<UserEntity>().Where(u => u.Name == "Bob" && !u.IsActive).AnyAsync();

            Assert.True(any);
        }

        [Fact]
        public async Task Any_WithPredicate_ReturnsFalseWhenNoMatch()
        {
            var any = await DbAccess.Set<UserEntity>().Where(u => u.Amount > 10000).AnyAsync();

            Assert.False(any);
        }

        [Fact]
        public async Task All_AllRowsMatch_ReturnsTrue()
        {
            var all = await DbAccess.Set<UserEntity>().AsQuery().AllAsync(u => u.Amount > 0);

            Assert.True(all);
        }

        [Fact]
        public async Task All_SomeRowsDoNotMatch_ReturnsFalse()
        {
            var all = await DbAccess.Set<UserEntity>().AsQuery().AllAsync(u => u.IsActive);

            Assert.False(all);
        }

        [Fact]
        public async Task All_NoRowsMatch_ReturnsFalse()
        {
            var all = await DbAccess.Set<UserEntity>().AsQuery().AllAsync(u => u.Amount > 10000);

            Assert.False(all);
        }

        [Fact]
        public async Task All_EmptyTable_ReturnsTrue()
        {
            var all = await DbAccess.Set<EmptyEntity>().AsQuery().AllAsync(u => u.Amount > 0);

            Assert.True(all);
        }

        [Fact]
        public async Task All_WithNullCheckPredicate_UsesIsNotNullSemantics()
        {
            // Bob 的 Score 为 NULL：Score != null 经翻译为 IS NOT NULL，Bob 不满足 → All 为 false
            var all = await DbAccess.Set<UserEntity>().AsQuery().AllAsync(u => u.Score != null);

            Assert.False(all);
        }

        [Fact]
        public async Task All_NullPredicate_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => DbAccess.Set<UserEntity>().AsQuery().AllAsync(null!));
        }

        [Fact]
        public async Task All_WithPredicate_ParameterAvoidsChainParameterNames()
        {
            // 链 WHERE 已占用首个生成参数名（=100）：All 谓词参数自动避让（=250）；
            // 若参数串扰则谓词误用 100 比较，Bob(200)/Carol(300) 均通过而误判为 true
            var all = await DbAccess.Set<UserEntity>().Where(u => u.Amount > 100).AllAsync(u => u.Amount > 250);

            Assert.False(all);
        }

        #endregion
    }
}
