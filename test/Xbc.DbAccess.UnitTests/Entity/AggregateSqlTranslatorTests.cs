using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using Xbc.DbAccess.Common;
using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// AggregateSqlTranslator 的纯翻译单元测试（不依赖数据库）：键选择器三形态解析与资格守卫、Enumerable 白名单聚合匹配、
    /// 分组投影项/排序目标解析、HAVING 谓词翻译（参数化、null 特判、逻辑组合）与拒绝清单（设计文档 §5）。
    /// <para>lambda 的 TKey 以命名替身类型承载（匿名键的类型由编译器生成无法显式命名，成员引用路径在 SQLite 端到端测试覆盖）。</para>
    /// </summary>
    public class AggregateSqlTranslatorTests
    {
        #region 测试实体

        [XbcDbEntity.Table("TranslatorGroupRows")]
        public class GroupRow
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public long CustomerId { get; set; }
            public string Region { get; set; }
            public int Status { get; set; }
            public decimal Amount { get; set; }
            public int Qty { get; set; }
            public bool IsActive { get; set; }
            public decimal? Balance { get; set; }
            public int? Score { get; set; }

            // 枚举名称字符串列：EnumAsString 语义确定，键/聚合合法（资格守卫放行）
            [XbcDbEntity.Column(EnumAsString = true)]
            public RowLevel Level { get; set; }

            // 带自定义转换器的列：存储值与属性值不同构，键/聚合拒绝
            [XbcDbEntity.Column(ConverterType = typeof(GroupTokenConverter))]
            public string Token { get; set; }

            // JSON 存储列：参与分组/聚合的是序列化文本，拒绝
            [XbcDbEntity.Column(IsJson = true)]
            public GroupPayload Extra { get; set; }

            // 并发令牌列：框架乐观锁专用管理，键/聚合拒绝（令牌仅支持 byte[]）
            [XbcDbEntity.Column(IsConcurrencyToken = true)]
            public byte[] Version { get; set; }

            [XbcDbEntity.NotMapped]
            public string Note { get; set; }

            public int OtherId { get; set; }

            // 导航属性：键/聚合成员解析必须拒绝导航路径
            [XbcDbEntity.Navigation(ThisKey = "OtherId", OtherKey = "Id")]
            public GroupOther Other { get; set; }
        }

        [XbcDbEntity.Table("TranslatorGroupOthers")]
        public class GroupOther
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; }
        }

        public enum RowLevel
        {
            Normal = 0,
            Locked = 1
        }

        public sealed class GroupPayload
        {
            public string Tag { get; set; }
        }

        /// <summary>复合键的命名替身类型（成员名与匿名键一致；匿名类型由编译器生成无法显式命名）。</summary>
        public sealed class CompositeKeyRow
        {
            public long CustomerId { get; set; }

            public string Region { get; set; }

            public int Status { get; set; }
        }

        /// <summary>命名复合键的初始化器形态载体（无参构造 + 可写属性）。</summary>
        public sealed class GroupKeyDto
        {
            public long CustomerId { get; set; }

            public string Region { get; set; }
        }

        /// <summary>命名投影结果类型载体（成员名 = 绑定落点）。</summary>
        public sealed class GroupStatDto
        {
            public long CustomerId { get; set; }

            public int Cnt { get; set; }

            public decimal Total { get; set; }
        }

        /// <summary>复合键整体出列 + 计数的命名投影载体（多列内复合 g.Key 拒绝的编译可达形态）。</summary>
        public sealed class KeyWithCntDto
        {
            public GroupKeyDto Key { get; set; }

            public int Cnt { get; set; }
        }

        private sealed class GroupTokenConverter : Xbc.DbAccess.Entity.IDbValueConverter<string>
        {
            public string ConvertFrom(object dbValue) => dbValue == null ? null! : dbValue.ToString()!.Substring(4);
            public object ConvertTo(string value) => value == null ? null! : "enc:" + value;
        }

        #endregion

        private static readonly EntityMetadata Metadata = EntityMetadataCache<GroupRow>.Metadata;

        /// <summary>纯翻译用最简方言：标识符直通不引用、AVG 直出 AVG(col)，其余成员翻译器用不到。</summary>
        private sealed class NeutralGroupDialect : SqlDialectBase
        {
            public override bool SupportsSchema => false;
            public override int MaxParameterCount => 999;
            public override int MaxRowsPerInsertValues => int.MaxValue;
            public override bool TreatsEmptyStringAsNull => false;

            public override string QuoteIdentifier(string identifier) => identifier;
            public override string QuoteTableName(string schema, string tableName) => tableName;
            public override InsertIdentityMode GetInsertIdentityMode(DbServerInfo serverInfo) => InsertIdentityMode.SelectLastInsertId;
            public override string SelectLastInsertId(string columnName) => throw new NotSupportedException();
            public override string GetDatePartExpression(string quotedColumn, DatePart part) => throw new NotSupportedException();
            public override string BuildAverageExpression(string quotedColumn) => "AVG(" + quotedColumn + ")";
        }

        /// <summary>纯翻译用参数构建器：@ 前缀 + 最小 DbParameter 桩（仅承载 ParameterName/Value/DbType 供断言）。</summary>
        private sealed class PrefixOnlyParameterBuilder : IDbParameterBuilder
        {
            public string ParameterPrefix => "@";

            public string GetParameterName(string name) => "@" + name;

            public DbParameter CreateParameter(string name, object value, DbType? dbType = null)
                => CreateParameter(name, value, dbType, ParameterDirection.Input);

            public DbParameter CreateParameter(string name, object value, DbType? dbType, ParameterDirection direction, int? size = null)
            {
                var parameter = new StubDbParameter
                {
                    ParameterName = GetParameterName(name),
                    Value = value ?? DBNull.Value
                };

                if (dbType.HasValue)
                    parameter.DbType = dbType.Value;

                return parameter;
            }
        }

        /// <summary>断言用途的最小 DbParameter 桩。</summary>
        private sealed class StubDbParameter : DbParameter
        {
            public override DbType DbType { get; set; } = DbType.Object;
            public override ParameterDirection Direction { get; set; }
            public override bool IsNullable { get; set; }
            public override string ParameterName { get; set; }
            public override int Size { get; set; }
            public override string SourceColumn { get; set; }
            public override bool SourceColumnNullMapping { get; set; }
            public override DataRowVersion SourceVersion { get; set; }
            public override object Value { get; set; }

            public override void ResetDbType()
            {
                DbType = DbType.Object;
            }
        }

        private static readonly ISqlDialect Dialect = new NeutralGroupDialect();
        private static readonly IDbParameterBuilder ParameterBuilder = new PrefixOnlyParameterBuilder();

        private static List<GroupedKeyColumn> ParseKey<TKey>(Expression<Func<GroupRow, TKey>> selector)
        {
            return AggregateSqlTranslator.ParseKeySelector(selector, Metadata);
        }

        private static (GroupedOutputColumn[] Columns, System.Reflection.MemberInfo[] Members, System.Reflection.ConstructorInfo Constructor, ProjectionPlan.Shape Shape) ParseSelect(
            LambdaExpression selector,
            List<GroupedKeyColumn> keyColumns)
        {
            return AggregateSqlTranslator.ParseSelectSelector(selector, keyColumns, selector.ReturnType, Metadata, Dialect);
        }

        private static List<GroupedOrderingTarget> ParseOrdering(
            LambdaExpression keySelector,
            List<GroupedKeyColumn> keyColumns,
            bool descending = false)
        {
            return AggregateSqlTranslator.ParseOrderingSelector(keySelector, keyColumns, Metadata, Dialect, descending);
        }

        private static AggregateSqlTranslator.HavingTranslation TranslateHaving(
            LambdaExpression predicate,
            List<GroupedKeyColumn> keyColumns,
            ParameterNameAllocator allocator = null)
        {
            return AggregateSqlTranslator.TranslateHaving(
                predicate, Metadata, keyColumns, Dialect, ParameterBuilder, allocator ?? new ParameterNameAllocator());
        }

        private static AggregateFragment TranslateAgg(LambdaExpression aggregateSelector)
        {
            var body = aggregateSelector.Body.StripConversions();
            return AggregateSqlTranslator.TranslateAggregateCall((MethodCallExpression)body, aggregateSelector.Parameters[0], Metadata, Dialect);
        }

        #region 键选择器解析

        [Fact]
        public void ParseKey_SingleMember_ProducesOneColumnWithoutMemberName()
        {
            var keys = ParseKey((Expression<Func<GroupRow, long>>)(x => x.CustomerId));

            var key = Assert.Single(keys);
            Assert.Equal("CustomerId", key.Column.ColumnName);
            Assert.Null(key.MemberName);
        }

        [Fact]
        public void ParseKey_AnonymousType_ProducesColumnsWithMemberNames()
        {
            var keys = ParseKey(x => new { x.CustomerId, x.Region });

            Assert.Equal(2, keys.Count);
            Assert.Equal("CustomerId", keys[0].MemberName);
            Assert.Equal("Region", keys[1].MemberName);
        }

        [Fact]
        public void ParseKey_NamedInitializer_MapsBindingTargets()
        {
            var keys = ParseKey(x => new GroupKeyDto { CustomerId = x.CustomerId, Region = x.Region });

            Assert.Equal(2, keys.Count);
            Assert.Equal("CustomerId", keys[0].MemberName);
            Assert.Equal("CustomerId", keys[0].Column.ColumnName);
            Assert.Equal("Region", keys[1].MemberName);
        }

        [Fact]
        public void ParseKey_ComputedExpression_Throws()
        {
            // 键只能是实体列：计算表达式（防注入立场）在列解析层拒绝
            Assert.Throws<ArgumentException>(() => ParseKey(x => x.Amount * 2));
        }

        [Fact]
        public void ParseKey_ConstantKey_Throws()
        {
            Assert.Throws<ArgumentException>(() => ParseKey((Expression<Func<GroupRow, int>>)(x => 5)));
        }

        [Fact]
        public void ParseKey_NestedMemberPath_Throws()
        {
            Assert.Throws<ArgumentException>(() => ParseKey(x => x.Token.Length));
        }

        [Fact]
        public void ParseKey_NavigationPath_Throws()
        {
            // 导航路径不是列元数据：拒绝并引导连接列以列形态声明进视图实体（设计文档 §3.4）
            Assert.Throws<NotSupportedException>(() => ParseKey(x => x.Other.Name));
        }

        [Fact]
        public void ParseKey_NotMappedMember_Throws()
        {
            Assert.Throws<NotSupportedException>(() => ParseKey(x => x.Note));
        }

        [Fact]
        public void ParseKey_EmptyAnonymousType_Throws()
        {
            Assert.Throws<NotSupportedException>(() => ParseKey(x => new { }));
        }

        [Fact]
        public void ParseKey_ConverterColumn_Throws()
        {
            // 存储值经转换器变换（enc: 前缀），分组语义不可信
            Assert.Throws<NotSupportedException>(() => ParseKey(x => x.Token));
        }

        [Fact]
        public void ParseKey_JsonColumn_Throws()
        {
            Assert.Throws<NotSupportedException>(() => ParseKey(x => x.Extra));
        }

        [Fact]
        public void ParseKey_ConcurrencyTokenColumn_Throws()
        {
            Assert.Throws<NotSupportedException>(() => ParseKey(x => x.Version));
        }

        [Fact]
        public void ParseKey_EnumAsStringColumn_Allowed()
        {
            // EnumAsString 的存储值就是语义值（名称/数值确定），键合法（与排序守卫口径一致）
            var keys = ParseKey(x => x.Level);

            Assert.Equal("Level", Assert.Single(keys).Column.ColumnName);
        }

        #endregion

        #region 聚合调用匹配

        [Fact]
        public void TranslateAgg_Count_ProducesCountStar()
        {
            var fragment = TranslateAgg((Expression<Func<IGrouping<long, GroupRow>, int>>)(g => g.Count()));

            Assert.Equal("COUNT(*)", fragment.Sql);
            Assert.Null(fragment.Column);
        }

        [Fact]
        public void TranslateAgg_LongCount_ProducesCountStar()
        {
            var fragment = TranslateAgg((Expression<Func<IGrouping<long, GroupRow>, long>>)(g => g.LongCount()));

            Assert.Equal("COUNT(*)", fragment.Sql);
            Assert.Null(fragment.Column);
        }

        [Fact]
        public void TranslateAgg_Sum_ProducesColumnFragment()
        {
            var fragment = TranslateAgg((Expression<Func<IGrouping<long, GroupRow>, decimal>>)(g => g.Sum(x => x.Amount)));

            Assert.Equal("SUM(Amount)", fragment.Sql);
            Assert.Equal("Amount", fragment.Column.ColumnName);
        }

        [Fact]
        public void TranslateAgg_Average_UsesDialectExpression()
        {
            // AVG 走方言表达式（SQL Server 整型列裸 AVG 截断小数，与 AverageAsync 同约定）；int 列的 Average 返回 double
            var fragment = TranslateAgg((Expression<Func<IGrouping<long, GroupRow>, double>>)(g => g.Average(x => x.Qty)));

            Assert.Equal("AVG(Qty)", fragment.Sql);
            Assert.Equal("Qty", fragment.Column.ColumnName);
        }

        [Fact]
        public void TranslateAgg_MaxAndMin_ProduceColumnFragments()
        {
            var max = TranslateAgg((Expression<Func<IGrouping<long, GroupRow>, string?>>)(g => g.Max(x => x.Region)));
            var min = TranslateAgg((Expression<Func<IGrouping<long, GroupRow>, int>>)(g => g.Min(x => x.Qty)));

            Assert.Equal("MAX(Region)", max.Sql);
            Assert.Equal("MIN(Qty)", min.Sql);
        }

        [Fact]
        public void TranslateAgg_ConditionalCount_Throws()
        {
            Assert.Throws<NotSupportedException>(
                () => TranslateAgg((Expression<Func<IGrouping<long, GroupRow>, int>>)(g => g.Count(x => x.Amount > 0))));
        }

        [Fact]
        public void TranslateAgg_ReceiverNotGroupParameter_Throws()
        {
            // 接收者必须是分组参数 g：闭包集合上的 Enumerable.Count 命中白名单形态但接收者非 g，显式拒绝
            var closureList = new List<int>();
            var selector = (Expression<Func<IGrouping<long, GroupRow>, int>>)(g => closureList.Count());

            Assert.Throws<NotSupportedException>(() => TranslateAgg(selector));
        }

        [Fact]
        public void TranslateAgg_NonEnumerableMethod_ReturnsNull()
        {
            var selector = (Expression<Func<IGrouping<long, GroupRow>, string?>>)(g => g.ToString());
            var body = selector.Body.StripConversions();

            var fragment = AggregateSqlTranslator.TranslateAggregateCall((MethodCallExpression)body, selector.Parameters[0], Metadata, Dialect);

            Assert.Null(fragment);
        }

        #endregion

        #region 投影项解析

        [Fact]
        public void ParseSelect_KeyMemberReference_ProducesKeyColumn()
        {
            var keys = ParseKey(x => new { x.CustomerId, x.Region });
            Expression<Func<IGrouping<CompositeKeyRow, GroupRow>, long>> selector = g => g.Key.CustomerId;

            var (columns, members, _, _) = ParseSelect(selector, keys);

            var column = Assert.Single(columns);
            Assert.Equal("CustomerId", column.Column.ColumnName);
            Assert.Null(column.Aggregate);
            Assert.Null(members);
        }

        [Fact]
        public void ParseSelect_SingleMemberKeyWhole_ProducesKeyColumn()
        {
            var keys = ParseKey((Expression<Func<GroupRow, long>>)(x => x.CustomerId));
            Expression<Func<IGrouping<long, GroupRow>, long>> selector = g => g.Key;

            var (columns, members, _, _) = ParseSelect(selector, keys);

            var column = Assert.Single(columns);
            Assert.Equal("CustomerId", column.Column.ColumnName);
            Assert.Null(members);
        }

        [Fact]
        public void ParseSelect_CompositeKeyWhole_ExpandsToAllKeyColumns()
        {
            var keys = ParseKey(x => new { x.CustomerId, x.Region });
            Expression<Func<IGrouping<CompositeKeyRow, GroupRow>, CompositeKeyRow>> selector = g => g.Key;

            var (columns, members, _, _) = ParseSelect(selector, keys);

            Assert.Equal(2, columns.Length);
            Assert.Equal("CustomerId", columns[0].Column.ColumnName);
            Assert.Equal("Region", columns[1].Column.ColumnName);
            // 命名结果类型走成员绑定（真实匿名键走位置构造映射，端到端测试覆盖），绑定目标为键成员名的解析产物
            Assert.Equal(new[] { "CustomerId", "Region" }, members.Select(m => m.Name));
        }

        [Fact]
        public void ParseSelect_CompositeKeyInsideMultiColumn_Throws()
        {
            var keys = ParseKey(x => new GroupKeyDto { CustomerId = x.CustomerId, Region = x.Region });
            Expression<Func<IGrouping<GroupKeyDto, GroupRow>, KeyWithCntDto>> selector = g => new KeyWithCntDto { Key = g.Key, Cnt = g.Count() };

            // 位置映射无法承载键展开：引导展开为 g.Key.X 逐成员
            Assert.Throws<NotSupportedException>(() => ParseSelect(selector, keys));
        }

        [Fact]
        public void ParseSelect_AggregateItems_ProduceFragments()
        {
            var keys = ParseKey(x => new { x.CustomerId });
            Expression<Func<IGrouping<CompositeKeyRow, GroupRow>, GroupStatDto>> selector =
                g => new GroupStatDto { CustomerId = g.Key.CustomerId, Cnt = g.Count(), Total = g.Sum(x => x.Amount) };

            var (columns, members, _, _) = ParseSelect(selector, keys);

            Assert.Equal(3, columns.Length);
            Assert.Equal("CustomerId", columns[0].Column.ColumnName);
            Assert.Equal("COUNT(*)", columns[1].Aggregate.Sql);
            Assert.Null(columns[1].Column);
            Assert.Equal("SUM(Amount)", columns[2].Aggregate.Sql);
            Assert.Equal("Amount", columns[2].Column.ColumnName);
            Assert.Equal(new[] { "CustomerId", "Cnt", "Total" }, members.Select(m => m.Name));
        }

        [Fact]
        public void ParseSelect_ConstantItem_Throws()
        {
            var keys = ParseKey((Expression<Func<GroupRow, long>>)(x => x.CustomerId));
            Expression<Func<IGrouping<long, GroupRow>, int>> selector = g => 5;

            // 投影项必须可落到键列或聚合（投影是列映射，不是任意计算）
            Assert.Throws<NotSupportedException>(() => ParseSelect(selector, keys));
        }

        #endregion

        #region 排序目标解析

        [Fact]
        public void ParseOrdering_CompositeKeyWhole_ExpandsToAllKeyColumns()
        {
            var keys = ParseKey(x => new { x.CustomerId, x.Region });
            Expression<Func<IGrouping<CompositeKeyRow, GroupRow>, CompositeKeyRow>> selector = g => g.Key;

            var targets = ParseOrdering(selector, keys, descending: true);

            Assert.Equal(2, targets.Count);
            Assert.All(targets, t => Assert.Null(t.Aggregate));
            Assert.All(targets, t => Assert.True(t.Descending));
            Assert.Equal("CustomerId", targets[0].KeyColumn.ColumnName);
            Assert.Equal("Region", targets[1].KeyColumn.ColumnName);
        }

        [Fact]
        public void ParseOrdering_Aggregate_ProducesAggregateTarget()
        {
            var keys = ParseKey((Expression<Func<GroupRow, long>>)(x => x.CustomerId));
            Expression<Func<IGrouping<long, GroupRow>, decimal>> selector = g => g.Sum(x => x.Amount);

            var targets = ParseOrdering(selector, keys);

            var target = Assert.Single(targets);
            Assert.Null(target.KeyColumn);
            Assert.Equal("SUM(Amount)", target.Aggregate.Sql);
            Assert.False(target.Descending);
        }

        [Fact]
        public void ParseOrdering_ComputedExpression_Throws()
        {
            var keys = ParseKey((Expression<Func<GroupRow, long>>)(x => x.CustomerId));
            Expression<Func<IGrouping<long, GroupRow>, int>> selector = g => 5;

            Assert.Throws<NotSupportedException>(() => ParseOrdering(selector, keys));
        }

        #endregion

        #region HAVING 谓词翻译

        [Fact]
        public void TranslateHaving_AggregateVersusConstant_IsParameterized()
        {
            var keys = ParseKey((Expression<Func<GroupRow, long>>)(x => x.CustomerId));
            Expression<Func<IGrouping<long, GroupRow>, bool>> predicate = g => g.Count() >= 5;

            var result = TranslateHaving(predicate, keys);

            Assert.Equal("COUNT(*) >= @Count", result.Fragment);
            var parameter = Assert.Single(result.Parameters);
            Assert.Equal("@Count", parameter.ParameterName);
            Assert.Equal(5, parameter.Value);
            // COUNT 无源列：DbType 不指定，交提供程序推断
            Assert.Equal(DbType.Object, parameter.DbType);
        }

        [Fact]
        public void TranslateHaving_AggregateVersusClosure_IsParameterizedNeverInlined()
        {
            var minCount = 3;
            var keys = ParseKey((Expression<Func<GroupRow, long>>)(x => x.CustomerId));
            Expression<Func<IGrouping<long, GroupRow>, bool>> predicate = g => g.Sum(x => x.Amount) > minCount;

            var result = TranslateHaving(predicate, keys);

            Assert.Equal("SUM(Amount) > @Amount", result.Fragment);
            // 闭包值按列转换链参数化（Amount 为 decimal 列，int 闭包值转为 decimal）
            Assert.Equal(3m, Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void TranslateHaving_OperandSwap_FlipsOperator()
        {
            var keys = ParseKey((Expression<Func<GroupRow, long>>)(x => x.CustomerId));
            Expression<Func<IGrouping<long, GroupRow>, bool>> predicate = g => 5 <= g.Count();

            var result = TranslateHaving(predicate, keys);

            Assert.Equal("COUNT(*) >= @Count", result.Fragment);
            Assert.Equal(5, Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void TranslateHaving_KeyMemberVersusConstant_IsParameterized()
        {
            var keys = ParseKey(x => new { x.CustomerId, x.Status });
            Expression<Func<IGrouping<CompositeKeyRow, GroupRow>, bool>> predicate = g => g.Key.CustomerId == 7L;

            var result = TranslateHaving(predicate, keys);

            Assert.Equal("CustomerId = @CustomerId", result.Fragment);
            Assert.Equal(7L, Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void TranslateHaving_KeyVersusKey_NoParameters()
        {
            var keys = ParseKey(x => new { x.CustomerId, x.Status });
            Expression<Func<IGrouping<CompositeKeyRow, GroupRow>, bool>> predicate = g => g.Key.CustomerId == g.Key.Status;

            var result = TranslateHaving(predicate, keys);

            Assert.Equal("CustomerId = Status", result.Fragment);
            Assert.Empty(result.Parameters);
        }

        [Fact]
        public void TranslateHaving_AggregateNullComparison_TranslatesToIsNull()
        {
            var keys = ParseKey((Expression<Func<GroupRow, long>>)(x => x.CustomerId));
            Expression<Func<IGrouping<long, GroupRow>, bool>> isNullPredicate = g => g.Sum(x => x.Balance) == null;
            Expression<Func<IGrouping<long, GroupRow>, bool>> isNotNullPredicate = g => g.Sum(x => x.Balance) != null;

            var isNull = TranslateHaving(isNullPredicate, keys);
            var isNotNull = TranslateHaving(isNotNullPredicate, keys);

            Assert.Equal("SUM(Balance) IS NULL", isNull.Fragment);
            Assert.Equal("SUM(Balance) IS NOT NULL", isNotNull.Fragment);
            Assert.Empty(isNull.Parameters);
        }

        [Fact]
        public void TranslateHaving_LogicalCombination_ParenthesizesOperands()
        {
            var keys = ParseKey(x => new { x.CustomerId, x.Status });
            Expression<Func<IGrouping<CompositeKeyRow, GroupRow>, bool>> predicate = g => g.Count() >= 2 && g.Key.Status != 0;

            var result = TranslateHaving(predicate, keys);

            // 比较谓词不自包裹，逻辑运算整体括号保证嵌套优先级（嵌套逻辑各自成对）
            Assert.Equal("(COUNT(*) >= @Count AND Status <> @Status)", result.Fragment);
            Assert.Equal(2, result.Parameters.Count);
        }

        [Fact]
        public void TranslateHaving_Negation_TranslatesToNot()
        {
            var keys = ParseKey((Expression<Func<GroupRow, long>>)(x => x.CustomerId));
            Expression<Func<IGrouping<long, GroupRow>, bool>> predicate = g => !(g.Count() > 1);

            var result = TranslateHaving(predicate, keys);

            Assert.Equal("NOT (COUNT(*) > @Count)", result.Fragment);
        }

        [Fact]
        public void TranslateHaving_AggregateToAggregateComparison_Throws()
        {
            var keys = ParseKey((Expression<Func<GroupRow, long>>)(x => x.CustomerId));
            Expression<Func<IGrouping<long, GroupRow>, bool>> predicate = g => g.Count() == g.Sum(x => x.Amount);

            Assert.Throws<NotSupportedException>(() => TranslateHaving(predicate, keys));
        }

        [Fact]
        public void TranslateHaving_ArithmeticOnAggregate_Throws()
        {
            var keys = ParseKey((Expression<Func<GroupRow, long>>)(x => x.CustomerId));
            Expression<Func<IGrouping<long, GroupRow>, bool>> predicate = g => g.Count() + 1 > 5;

            // 聚合上的算术运算（g.Count() + 1 > 5）不支持：聚合只能与常量/闭包值直接比较
            Assert.Throws<NotSupportedException>(() => TranslateHaving(predicate, keys));
        }

        [Fact]
        public void TranslateHaving_ConstantPredicate_Throws()
        {
            var keys = ParseKey((Expression<Func<GroupRow, long>>)(x => x.CustomerId));
            Expression<Func<IGrouping<long, GroupRow>, bool>> predicate = g => true;

            Assert.Throws<NotSupportedException>(() => TranslateHaving(predicate, keys));
        }

        [Fact]
        public void TranslateHaving_CompositeKeyWholeComparison_Throws()
        {
            var keys = ParseKey(x => new { x.CustomerId, x.Region });

            // 复合键整体不能参与比较（无单一列落点）：编译器对匿名键无 == 运算（编译期不可达），
            // 手搭 g.Key 整体比较的表达式树同样在运行时拒绝，引导比较具体键成员
            var gParameter = Expression.Parameter(typeof(IGrouping<CompositeKeyRow, GroupRow>), "g");
            var keyAccess = Expression.Property(gParameter, "Key");
            var predicate = Expression.Lambda<Func<IGrouping<CompositeKeyRow, GroupRow>, bool>>(
                Expression.Equal(keyAccess, Expression.Constant(null, typeof(CompositeKeyRow))), gParameter);

            Assert.Throws<NotSupportedException>(() => TranslateHaving(predicate, keys));
        }

        [Fact]
        public void TranslateHaving_BareBoolKeyPredicate_Throws()
        {
            var keys = ParseKey((Expression<Func<GroupRow, bool>>)(x => x.IsActive));
            Expression<Func<IGrouping<bool, GroupRow>, bool>> predicate = g => g.Key;

            Assert.Throws<NotSupportedException>(() => TranslateHaving(predicate, keys));
        }

        [Fact]
        public void TranslateHaving_AggregateVersusKeyColumn_DirectSplice()
        {
            // 聚合与键列的比较合法（分组列在组级作用域可见）：直拼无参数，与键列对键列同路
            var keys = ParseKey((Expression<Func<GroupRow, long>>)(x => x.CustomerId));
            Expression<Func<IGrouping<long, GroupRow>, bool>> predicate = g => g.Count() >= g.Key;

            var result = TranslateHaving(predicate, keys);

            Assert.Equal("COUNT(*) >= CustomerId", result.Fragment);
            Assert.Empty(result.Parameters);
        }

        [Fact]
        public void TranslateHaving_KeyColumnVersusAggregate_DirectSplice()
        {
            // 反向操作数序同合法：两侧均为分组操作数时不做交换（无参数化需求），按书写序直拼
            var keys = ParseKey((Expression<Func<GroupRow, long>>)(x => x.CustomerId));
            Expression<Func<IGrouping<long, GroupRow>, bool>> predicate = g => g.Key < g.Sum(x => x.Qty);

            var result = TranslateHaving(predicate, keys);

            Assert.Equal("CustomerId < SUM(Qty)", result.Fragment);
            Assert.Empty(result.Parameters);
        }

        [Fact]
        public void TranslateHaving_OrElseComposition_ParenthesizesOperands()
        {
            var keys = ParseKey((Expression<Func<GroupRow, int>>)(x => x.Status));
            Expression<Func<IGrouping<int, GroupRow>, bool>> predicate = g => g.Count() >= 2 || g.Key == 9;

            var result = TranslateHaving(predicate, keys);

            Assert.Equal("(COUNT(*) >= @Count OR Status = @Status)", result.Fragment);
            Assert.Equal(2, result.Parameters.Count);
        }

        [Fact]
        public void TranslateHaving_NullableKeyDotValue_UnwrapsToColumn()
        {
            // 可空单键的 g.Key.Value 解包落到键列（TryResolveKeyMember 的 .Value 解包在 HAVING 路径的形态）
            var keys = ParseKey((Expression<Func<GroupRow, decimal?>>)(x => x.Balance));
            Expression<Func<IGrouping<decimal?, GroupRow>, bool>> predicate = g => g.Key!.Value >= 5m;

            var result = TranslateHaving(predicate, keys);

            Assert.Equal("Balance >= @Balance", result.Fragment);
            Assert.Equal(5m, Assert.Single(result.Parameters).Value);
        }

        #endregion
    }
}
