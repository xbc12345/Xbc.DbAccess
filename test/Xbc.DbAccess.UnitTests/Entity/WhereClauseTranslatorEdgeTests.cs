using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using Xbc.DbAccess.Common;
using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// WhereClauseTranslator 的边界拒绝与手搭表达式树用例（变异测试 NoCoverage 补齐）：
    /// 单次翻译守卫、多参数 lambda、非常量谓词形态、二元/一元运算不支持、
    /// 非列成员、Contains 非直连列元素、字符串变换/日期归一/LIKE 的值类型守卫、
    /// 列间比较的全运算符映射、闭包求值异常还原。
    /// </summary>
    public class WhereClauseTranslatorEdgeTests
    {
        #region 测试实体

        [XbcDbEntity.Table("TranslatorRows")]
        public class EdgeRow
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
            public int Age { get; set; }
            public int? Score { get; set; }
            public bool IsActive { get; set; }
            public DateTime CreatedAt { get; set; }

            [XbcDbEntity.Column(ConverterType = typeof(EdgeTimestampConverter))]
            public DateTime SecuredAt { get; set; }

            [XbcDbEntity.NotMapped]
            public string Note { get; set; }
        }

        /// <summary>SecuredAt 列的模拟转换器：DateTime 以 ticks 长整型存储（数值化日期列）。</summary>
        private sealed class EdgeTimestampConverter : Xbc.DbAccess.Entity.IDbValueConverter<DateTime>
        {
            public DateTime ConvertFrom(object dbValue) => new DateTime(Convert.ToInt64(dbValue));
            public object ConvertTo(DateTime value) => value.Ticks;
        }

        /// <summary>非 IGrouping 的同形替身仅用于 AggregateSqlTranslator；本文件不需要。</summary>

        #endregion

        /// <summary>纯翻译用最简方言：标识符直通不引用。</summary>
        private sealed class NeutralEdgeDialect : SqlDialectBase
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

        private static readonly EntityMetadata Metadata = EntityMetadataCache<EdgeRow>.Metadata;
        private static readonly ISqlDialect Dialect = new NeutralEdgeDialect();

        private static WhereClauseTranslation Translate(Expression<Func<EdgeRow, bool>> predicate)
        {
            return NewTranslator().Translate(predicate);
        }

        private static WhereClauseTranslator NewTranslator()
        {
            return new WhereClauseTranslator(Metadata, Dialect, new PrefixParameterBuilder(), new ParameterNameAllocator());
        }

        [Fact]
        public void TranslatorInstanceIsSingleUse()
        {
            // 同一实例二次 Translate：片段与参数在实例上累积，显式拦截
            var translator = NewTranslator();
            Expression<Func<EdgeRow, bool>> first = r => r.Age == 18;
            translator.Translate(first);

            Expression<Func<EdgeRow, bool>> second = r => r.Age == 18;
            var ex = Assert.Throws<InvalidOperationException>(
                () => translator.Translate(second));

            Assert.Contains("单次 Translate", ex.Message);
        }

        [Fact]
        public void Translate_TwoParameters_Throws()
        {
            // 手搭双参数 lambda（编译器无法直接写出）
            var row = Expression.Parameter(typeof(EdgeRow), "row");
            var other = Expression.Parameter(typeof(EdgeRow), "other");
            var lambda = Expression.Lambda(Expression.Constant(true), row, other);

            var ex = Assert.Throws<NotSupportedException>(() => NewTranslator().Translate(lambda));

            Assert.Contains("只支持一个参数", ex.Message);
        }

        [Fact]
        public void Translate_ConstantPredicate_Throws()
        {
            // 常量谓词（恒真/恒假）：与数据库无关的条件显式拒绝
            var ex = Assert.Throws<NotSupportedException>(() => Translate(r => true));

            Assert.Contains("不支持常量谓词", ex.Message);
        }

        [Fact]
        public void Translate_NonBooleanColumnAsPredicate_Throws()
        {
            // 手搭裸 int 列谓词（int 不能隐转 bool，编译器挡住）——布尔列裸谓词合法、数值列拒绝
            var row = Expression.Parameter(typeof(EdgeRow), "row");
            var age = Expression.Property(row, nameof(EdgeRow.Age));
            var lambda = Expression.Lambda(age, row);

            var ex = Assert.Throws<NotSupportedException>(() => NewTranslator().Translate(lambda));

            Assert.Contains("不是布尔类型", ex.Message);
        }

        [Fact]
        public void Translate_ArithmeticInComparison_Throws()
        {
            // 比较中的算术运算（列 + 常量）：引用实体参数的子表达式是能力边界（求值路径显式拒绝、永不内联）
            var ex = Assert.Throws<NotSupportedException>(() => Translate(r => (r.Age + 1) > 5));

            Assert.Contains("子表达式", ex.Message);
        }

        [Fact]
        public void Translate_UnaryNegation_Throws()
        {
            // 一元负号：子表达式求值路径显式拒绝（仅支持逻辑非 !）
            var ex = Assert.Throws<NotSupportedException>(() => Translate(r => (-r.Age) > 5));

            Assert.Contains("子表达式", ex.Message);
        }

        [Fact]
        public void Translate_NonColumnMemberExpression_Throws()
        {
            // 引用实体参数但未映射为列的成员（NotMapped）：不是可翻译列，显式拒绝
            var ex = Assert.Throws<NotSupportedException>(() => Translate(r => r.Note == "x"));

            Assert.Contains("不是实体", ex.Message);
        }

        [Fact]
        public void Translate_ContainsNonDirectColumnElement_Throws()
        {
            // Contains 元素是算术表达式而非直连列（手搭：List.Contains(r.Age + 1)）
            var row = Expression.Parameter(typeof(EdgeRow), "row");
            var ids = Expression.Constant(new List<int> { 1, 2, 3 });
            var contains = typeof(List<int>).GetMethod(nameof(List<int>.Contains), new[] { typeof(int) });
            var call = Expression.Call(ids, contains, Expression.Add(
                Expression.Property(row, nameof(EdgeRow.Age)), Expression.Constant(1)));
            var lambda = Expression.Lambda(call, row);

            var ex = Assert.Throws<NotSupportedException>(() => NewTranslator().Translate(lambda));

            Assert.Contains("直连实体列", ex.Message);
        }

        [Fact]
        public void Translate_ColumnVsColumn_AllComparisonOperators()
        {
            // 列间比较（无参数化）：锁定全部比较运算符的 SQL 映射（==、!=、>、<、>=、<=）
            Assert.Equal("Age = Id", Translate(r => r.Age == r.Id).WhereClause);
            Assert.Equal("Age <> Id", Translate(r => r.Age != r.Id).WhereClause);
            Assert.Equal("Age > Id", Translate(r => r.Age > r.Id).WhereClause);
            Assert.Equal("Age < Id", Translate(r => r.Age < r.Id).WhereClause);
            Assert.Equal("Age >= Id", Translate(r => r.Age >= r.Id).WhereClause);
            Assert.Equal("Age <= Id", Translate(r => r.Age <= r.Id).WhereClause);
        }

        [Fact]
        public void Translate_StringTransformOnNonStringColumn_Throws()
        {
            // 数值列上调用 ToString：方法调用形态不在支持的集合内，显式拒绝
            // （「接收者不是字符串类型」分支被表达式构造层的类型兼容检查挡住，属不可达防御）
            var ex = Assert.Throws<NotSupportedException>(() => Translate(r => r.Age.ToString() == "5"));

            Assert.Contains("不支持方法", ex.Message);
        }

        [Fact]
        public void Translate_ValueSideArithmeticReferencingEntity_Throws()
        {
            // 操作数侧合法列、值侧是引用实体参数的算术子表达式（r.Id + 1）
            var ex = Assert.Throws<NotSupportedException>(() => Translate(r => r.Age == r.Id + 1));

            Assert.Contains("子表达式", ex.Message);
        }

        [Fact]
        public void Translate_ClosureEvaluationThrows_PreservesOriginalException()
        {
            // 值侧闭包求值抛出的原始异常被还原（不包装为 TargetInvocationException）
            var source = (string)null;
            Assert.Throws<NullReferenceException>(() => Translate(r => r.Age == source.Length));
        }
    }
}
