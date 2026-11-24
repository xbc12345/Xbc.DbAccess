using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using Xbc.DbAccess.Common;
using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// AggregateSqlTranslator 的边界拒绝与手搭表达式树用例（变异测试 NoCoverage 补齐）：
    /// 覆盖多参数 lambda、非法聚合调用形态、命名类型字段绑定、未知键成员、HAVING 路由拒绝、
    /// 操作数交换的各方向运算符翻转与闭包求值异常还原。无法由编译器产生的形态（如 Enumerable 无
    /// string 的 Sum 重载、IGrouping 无 Key 之外的实例属性）属于不可达防御，不在此覆盖。
    /// </summary>
    public class AggregateSqlTranslatorEdgeTests
    {
        #region 测试实体与替身

        [XbcDbEntity.Table("GroupRows")]
        public class Row
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public long CustomerId { get; set; }

            public string Region { get; set; }

            public int Qty { get; set; }

            [XbcDbEntity.NotMapped]
            public string Note { get; set; }
        }

        /// <summary>复合键替身：Extra 属性存在但不参与键绑定（g.Key.Extra 不可寻址的载体）。</summary>
        public sealed class CompositeKeyExtra
        {
            public long CustomerId { get; set; }

            public string Region { get; set; }

            public int Extra { get; set; }
        }

        /// <summary>字段承载的命名投影 DTO：验证 resultType 公共字段绑定路径。</summary>
        public sealed class FieldDto
        {
            public long CustomerId;

            public int Total;
        }

        /// <summary>只读字段 DTO：手搭 MemberInit 绑定只读字段触发拒绝。</summary>
        public sealed class ReadOnlyFieldDto
        {
            public readonly long CustomerId;
        }

        /// <summary>非 IGrouping 的自定义分组替身：Key 成员同名但声明类型不符。</summary>
        public sealed class FakeGroup
        {
            public long Key { get; set; }
        }

        #endregion

        /// <summary>纯翻译用最简方言：标识符直通不引用、AVG 直出 AVG(col)。</summary>
        private sealed class NeutralDialect : SqlDialectBase
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

        /// <summary>纯翻译用参数构建器：@ 前缀 + 最小 DbParameter 桩。</summary>
        private sealed class PrefixParameterBuilder : IDbParameterBuilder
        {
            public string ParameterPrefix => "@";

            public string GetParameterName(string name) => "@" + name;

            public DbParameter CreateParameter(string name, object value, DbType? dbType = null)
                => CreateParameter(name, value, dbType, ParameterDirection.Input);

            public DbParameter CreateParameter(string name, object value, DbType? dbType, ParameterDirection direction, int? size = null)
            {
                return new StubParameter
                {
                    ParameterName = "@" + name,
                    Value = value ?? DBNull.Value
                };
            }
        }

        private sealed class StubParameter : DbParameter
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
            public override void ResetDbType() { DbType = DbType.Object; }
        }

        private static readonly ISqlDialect Dialect = new NeutralDialect();
        private static readonly IDbParameterBuilder ParameterBuilder = new PrefixParameterBuilder();
        private static readonly EntityMetadata Metadata = EntityMetadataCache<Row>.Metadata;

        private static List<GroupedKeyColumn> ParseKey<TKey>(Expression<Func<Row, TKey>> selector)
        {
            return AggregateSqlTranslator.ParseKeySelector(selector, Metadata);
        }

        private static AggregateFragment TranslateAgg(LambdaExpression aggregateSelector)
        {
            var body = aggregateSelector.Body.StripConversions();
            return AggregateSqlTranslator.TranslateAggregateCall((MethodCallExpression)body, aggregateSelector.Parameters[0], Metadata, Dialect);
        }

        private static AggregateSqlTranslator.HavingTranslation TranslateHaving(
            LambdaExpression predicate,
            List<GroupedKeyColumn> keyColumns,
            ParameterNameAllocator allocator = null)
        {
            return AggregateSqlTranslator.TranslateHaving(
                predicate, Metadata, keyColumns, Dialect, ParameterBuilder, allocator ?? new ParameterNameAllocator());
        }

        /// <summary>Enumerable.Sum(IEnumerable&lt;int&gt;) 单参重载（无选择器形态）。</summary>
        private static MethodInfo SumWithoutSelector()
        {
            return typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Single(m => m.Name == "Sum" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(IEnumerable<int>));
        }

        #region 键选择器解析边界

        [Fact]
        public void ParseKey_TwoParameters_Throws()
        {
            // 手搭双参数 lambda（编译器无法直接写出）
            var g = Expression.Parameter(typeof(IGrouping<long, Row>), "g");
            var row = Expression.Parameter(typeof(Row), "row");
            var lambda = Expression.Lambda(Expression.Constant(true), g, row);

            var ex = Assert.Throws<NotSupportedException>(
                () => AggregateSqlTranslator.ParseKeySelector(lambda, Metadata));

            Assert.Contains("只支持一个参数", ex.Message);
        }

        #endregion

        #region 聚合调用边界

        [Fact]
        public void TranslateAgg_SumWithoutSelector_Throws()
        {
            // g.Sum() 无选择器形态（Enumerable.Sum(IEnumerable<int>) 可编译）——需要显式列选择器
            var g = Expression.Parameter(typeof(IGrouping<long, Row>), "g");
            var call = Expression.Call(SumWithoutSelector(), Expression.Convert(g, typeof(IEnumerable<int>)));
            var lambda = Expression.Lambda<Func<IGrouping<long, Row>, int>>(call, g);

            var ex = Assert.Throws<NotSupportedException>(() => TranslateAgg(lambda));

            Assert.Contains("需要列选择器", ex.Message);
        }

        [Fact]
        public void TranslateAgg_SelectorNotLambda_Throws()
        {
            // 选择器位置传入非 lambda 表达式（手搭：g.Count(null 谓词)）——条件计数形态显式拒绝
            var g = Expression.Parameter(typeof(IGrouping<long, Row>), "g");
            var countOpen = typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Single(m => m.Name == "Count" && m.IsGenericMethodDefinition
                    && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 2);
            var countWithPredicate = countOpen.MakeGenericMethod(typeof(int));
            var call = Expression.Call(countWithPredicate, Expression.Convert(g, typeof(IEnumerable<int>)),
                Expression.Constant(null, typeof(Func<int, bool>)));
            var lambda = Expression.Lambda<Func<IGrouping<long, Row>, int>>(call, g);

            var ex = Assert.Throws<NotSupportedException>(() => TranslateAgg(lambda));

            Assert.Contains("条件计数", ex.Message);
        }

        #endregion

        #region 投影选择器边界

        [Fact]
        public void ParseSelect_TwoParameters_Throws()
        {
            var keys = ParseKey(x => new { x.CustomerId });
            var g = Expression.Parameter(typeof(IGrouping<long, Row>), "g");
            var row = Expression.Parameter(typeof(Row), "row");
            var lambda = Expression.Lambda(Expression.Constant(true), g, row);

            var ex = Assert.Throws<NotSupportedException>(
                () => AggregateSqlTranslator.ParseSelectSelector(lambda, keys, lambda.ReturnType, Metadata, Dialect));

            Assert.Contains("只支持一个参数", ex.Message);
        }

        [Fact]
        public void ParseSelect_EmptyAnonymous_Throws()
        {
            var keys = ParseKey(x => new { x.CustomerId });
            Expression<Func<IGrouping<long, Row>, object>> selector = g => new { };

            var ex = Assert.Throws<NotSupportedException>(
                () => AggregateSqlTranslator.ParseSelectSelector(selector, keys, selector.ReturnType, Metadata, Dialect));

            Assert.Contains("至少需要一列", ex.Message);
        }

        [Fact]
        public void ParseSelect_FieldBinding_MapsPosition()
        {
            // 命名 DTO 以公共字段承载绑定成员：GetField 路径合法（变异器对 BindingFlags 位运算的等价性由本用例锁定）
            var keys = ParseKey(x => x.CustomerId);
            Expression<Func<IGrouping<long, Row>, FieldDto>> selector =
                g => new FieldDto { CustomerId = g.Key, Total = g.Sum(x => x.Qty) };

            var (columns, members, constructor, shape) = AggregateSqlTranslator.ParseSelectSelector(selector, keys, selector.ReturnType, Metadata, Dialect);

            Assert.Equal(2, columns.Length);
            Assert.Equal(ProjectionPlan.Shape.MemberBinding, shape);
            Assert.Null(constructor);
            // 公共字段兜底路径：MemberInfo 解析产物为 FieldInfo，绑定序 = 列序
            Assert.Equal("CustomerId", members[0].Name);
            Assert.Equal("Total", members[1].Name);
        }

        [Fact]
        public void ParseSelect_ReadOnlyFieldBinding_Throws()
        {
            // 手搭 MemberInit 绑定只读字段（编译器初始化器不允许，运行时形态可达）
            var keys = ParseKey(x => x.CustomerId);
            var field = typeof(ReadOnlyFieldDto).GetField(nameof(ReadOnlyFieldDto.CustomerId));
            var init = Expression.MemberInit(
                Expression.New(typeof(ReadOnlyFieldDto)),
                Expression.Bind(field, Expression.Constant(1L)));
            var lambda = Expression.Lambda<Func<IGrouping<long, Row>, ReadOnlyFieldDto>>(init, Expression.Parameter(typeof(IGrouping<long, Row>), "g"));

            var ex = Assert.Throws<NotSupportedException>(
                () => AggregateSqlTranslator.ParseSelectSelector(lambda, keys, lambda.ReturnType, Metadata, Dialect));

            Assert.Contains("可写", ex.Message);
        }

        [Fact]
        public void ParseSelect_DuplicateMemberBinding_Throws()
        {
            // 手搭同一成员重复绑定（编译器初始化器拒绝，运行时形态可达）
            var keys = ParseKey(x => x.CustomerId);
            var field = typeof(FieldDto).GetField(nameof(FieldDto.CustomerId));
            var init = Expression.MemberInit(
                Expression.New(typeof(FieldDto)),
                Expression.Bind(field, Expression.Constant(1L)),
                Expression.Bind(field, Expression.Constant(2L)));
            var lambda = Expression.Lambda<Func<IGrouping<long, Row>, FieldDto>>(init, Expression.Parameter(typeof(IGrouping<long, Row>), "g"));

            Assert.Throws<NotSupportedException>(
                () => AggregateSqlTranslator.ParseSelectSelector(lambda, keys, lambda.ReturnType, Metadata, Dialect));
        }

        #endregion

        #region 排序选择器边界

        [Fact]
        public void ParseOrdering_CompositeKeyUnknownMember_Throws()
        {
            // g.Key.Extra：Extra 是替身真实属性但不在键绑定中（拼写错误诊断路径）
            var keys = ParseKey(x => new { x.CustomerId, x.Region });
            var g = Expression.Parameter(typeof(IGrouping<CompositeKeyExtra, Row>), "g");
            var keyAccess = Expression.Property(Expression.Property(g, "Key"), nameof(CompositeKeyExtra.Extra));
            var body = Expression.GreaterThanOrEqual(keyAccess, Expression.Constant(0));
            var lambda = Expression.Lambda(body, g);

            var ex = Assert.Throws<NotSupportedException>(
                () => AggregateSqlTranslator.ParseOrderingSelector(lambda, keys, Metadata, Dialect, descending: false));

            Assert.Contains("仅支持分组键引用", ex.Message);
        }

        #endregion

        #region HAVING 边界

        [Fact]
        public void TranslateHaving_TwoParameters_Throws()
        {
            var keys = ParseKey(x => new { x.CustomerId });
            var g = Expression.Parameter(typeof(IGrouping<long, Row>), "g");
            var other = Expression.Parameter(typeof(Row), "other");
            var lambda = Expression.Lambda(Expression.Constant(true), g, other);

            var ex = Assert.Throws<NotSupportedException>(() => TranslateHaving(lambda, keys));

            Assert.Contains("只支持一个参数", ex.Message);
        }

        [Fact]
        public void TranslateHaving_ArithmeticBody_Throws()
        {
            // 谓词整体是引用分组参数的算术子表达式（g.Sum(...) + 5）：求值路径显式拒绝
            var keys = ParseKey(x => new { x.CustomerId });
            Expression<Func<IGrouping<long, Row>, bool>> predicate = g => g.Sum(x => x.Qty) + 5 > 10;

            var ex = Assert.Throws<NotSupportedException>(() => TranslateHaving(predicate, keys));

            Assert.Contains("子表达式", ex.Message);
        }

        [Fact]
        public void TranslateHaving_NonAggregateMethodCall_Throws()
        {
            // 引用分组参数的非聚合方法调用（g.ToString()）
            var keys = ParseKey(x => new { x.CustomerId });
            var g = Expression.Parameter(typeof(IGrouping<long, Row>), "g");
            var body = Expression.Equal(
                Expression.Call(g, typeof(object).GetMethod(nameof(ToString), Type.EmptyTypes)),
                Expression.Constant("x"));
            var lambda = Expression.Lambda(body, g);

            var ex = Assert.Throws<NotSupportedException>(() => TranslateHaving(lambda, keys));

            Assert.Contains("不支持方法", ex.Message);
        }

        [Fact]
        public void TranslateHaving_KeyArithmeticOnValueSide_Throws()
        {
            // 操作数侧合法聚合、值侧是引用分组键的算术子表达式（g.Key.CustomerId + 1）：求值路径显式拒绝
            var keys = ParseKey(x => new { x.CustomerId, x.Region });
            Expression<Func<IGrouping<CompositeKeyExtra, Row>, bool>> predicate =
                g => g.Sum(x => x.Qty) == g.Key.CustomerId + 1;

            var ex = Assert.Throws<NotSupportedException>(() => TranslateHaving(predicate, keys));

            Assert.Contains("子表达式", ex.Message);
        }

        [Fact]
        public void TranslateHaving_FakeGroupKey_Throws()
        {
            // 同名 Key 成员但声明类型不是 IGrouping<,>：防误匹配守卫
            var keys = ParseKey(x => new { x.CustomerId });
            var fake = Expression.Parameter(typeof(FakeGroup), "g");
            var body = Expression.Equal(
                Expression.Property(fake, nameof(FakeGroup.Key)),
                Expression.Constant(5L));
            var lambda = Expression.Lambda(body, fake);

            var ex = Assert.Throws<NotSupportedException>(() => TranslateHaving(lambda, keys));

            Assert.Contains("不支持成员表达式", ex.Message);
        }

        [Fact]
        public void TranslateHaving_NullableConstantDotValue_TreatedAsConstant()
        {
            // 常量上的 .Value 访问（非键路径）：不误判为键引用，按两侧常量拒绝
            var keys = ParseKey((Expression<Func<Row, decimal?>>)(x => x.CustomerId));
            var g = Expression.Parameter(typeof(IGrouping<decimal?, Row>), "g");
            var constantValue = Expression.Property(Expression.Constant(5m, typeof(decimal?)), "Value");
            var body = Expression.Equal(constantValue, Expression.Constant(5m));
            var lambda = Expression.Lambda(body, g);

            var ex = Assert.Throws<NotSupportedException>(() => TranslateHaving(lambda, keys));

            Assert.Contains("两侧均为常量", ex.Message);
        }

        [Fact]
        public void TranslateHaving_ClosureEvaluationThrows_PreservesOriginalException()
        {
            // 值侧闭包求值抛出的原始异常被还原（不包装为 TargetInvocationException）
            var keys = ParseKey(x => new { x.CustomerId });
            var source = (string)null;
            Expression<Func<IGrouping<long, Row>, bool>> predicate =
                g => g.Count() > source.Length;

            Assert.Throws<NullReferenceException>(() => TranslateHaving(predicate, keys));
        }

        [Fact]
        public void TranslateHaving_OperandSwap_LessThan_FlipsToGreaterThan()
        {
            var keys = ParseKey(x => new { x.CustomerId });
            Expression<Func<IGrouping<long, Row>, bool>> predicate = g => 5 < g.Count();

            var result = TranslateHaving(predicate, keys);

            Assert.Equal("COUNT(*) > @Count", result.Fragment);
            Assert.Equal(5, Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void TranslateHaving_OperandSwap_GreaterThan_FlipsToLessThan()
        {
            var keys = ParseKey(x => new { x.CustomerId });
            Expression<Func<IGrouping<long, Row>, bool>> predicate = g => 5 > g.Count();

            var result = TranslateHaving(predicate, keys);

            Assert.Equal("COUNT(*) < @Count", result.Fragment);
        }

        [Fact]
        public void TranslateHaving_OperandSwap_LessThanOrEqual_FlipsToGreaterThanOrEqual()
        {
            var keys = ParseKey(x => new { x.CustomerId });
            Expression<Func<IGrouping<long, Row>, bool>> predicate = g => 5 <= g.Sum(x => x.Qty);

            var result = TranslateHaving(predicate, keys);

            Assert.Equal("SUM(Qty) >= @Qty", result.Fragment);
        }

        [Fact]
        public void TranslateHaving_TranslatorInstanceIsSingleUse()
        {
            // 单键的 g.Key 整体参与比较（合法路径），并验证同一翻译实例不可复用（防片段累积）——
            // 通过两次独立 TranslateHaving 均成功、同一片段一致来锁定公共入口每次新建实例的契约
            var keys = ParseKey(x => x.CustomerId);
            Expression<Func<IGrouping<long, Row>, bool>> predicate = g => g.Key >= 5;

            var first = TranslateHaving(predicate, keys);
            var second = TranslateHaving(predicate, keys);

            Assert.Equal("CustomerId >= @CustomerId", first.Fragment);
            Assert.Equal(first.Fragment, second.Fragment);
        }

        #endregion
    }
}
