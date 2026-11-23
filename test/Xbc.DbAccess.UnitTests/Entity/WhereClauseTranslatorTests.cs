using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using Xbc.DbAccess.Common;
using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// WhereClauseTranslator 的纯翻译单元测试：直接断言 SQL 文本与参数（不依赖数据库）。
    /// </summary>
    public class WhereClauseTranslatorTests
    {
        #region 测试实体

        [XbcDbEntity.Table("TranslatorRows")]
        public class TranslatorRow
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;
            public int Age { get; set; }
            public int? Score { get; set; }
            public bool IsActive { get; set; }
            public bool? IsApproved { get; set; }
            public byte[] Payload { get; set; }
            public Guid Uid { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? DeletedAt { get; set; }
            public RowStatus Status { get; set; }

            [XbcDbEntity.Column(EnumAsString = true)]
            public RowStatus Level { get; set; }
            public char Grade { get; set; }

            // 带自定义转换器的列：LIKE/字符串变换/日期部分三处守卫的测试载体（存储值与属性值不同构）
            [XbcDbEntity.Column(ConverterType = typeof(RowTokenConverter))]
            public string Token { get; set; }

            [XbcDbEntity.Column(ConverterType = typeof(RowTimestampConverter))]
            public DateTime SecuredAt { get; set; }

            [XbcDbEntity.NotMapped]
            public string Note { get; set; }
        }

        /// <summary>
        /// Token 列的模拟转换器：属性值加前缀后存储（模拟加密等不同构存储）。
        /// </summary>
        private sealed class RowTokenConverter : Xbc.DbAccess.Entity.IDbValueConverter<string>
        {
            public string ConvertFrom(object dbValue) => dbValue?.ToString() ?? string.Empty;
            public object ConvertTo(string value) => value == null ? null! : "enc:" + value;
        }

        /// <summary>
        /// SecuredAt 列的模拟转换器：DateTime 以 ticks 长整型存储（模拟日期列的数值化存储）。
        /// </summary>
        private sealed class RowTimestampConverter : Xbc.DbAccess.Entity.IDbValueConverter<DateTime>
        {
            public DateTime ConvertFrom(object dbValue) => new DateTime(Convert.ToInt64(dbValue));
            public object ConvertTo(DateTime value) => value.Ticks;
        }

        public enum RowStatus
        {
            Normal = 0,
            Locked = 1
        }

        #endregion

        /// <summary>
        /// 以"不引用标识符 + @ 前缀"的最简方言执行翻译，保证断言的 SQL 文本稳定。
        /// </summary>
        private static WhereClauseTranslation Translate(Expression<Func<TranslatorRow, bool>> predicate, ParameterNameAllocator sharedNames = null, ISqlDialect dialect = null)
        {
            return new WhereClauseTranslator(
                EntityMetadataCache<TranslatorRow>.Metadata,
                dialect ?? new NeutralDialect(),
                new PrefixOnlyParameterBuilder(),
                sharedNames ?? new ParameterNameAllocator())
                .Translate(predicate);
        }

        /// <summary>
        /// 纯翻译测试用最简方言：标识符直通不引用、参数上限 999，翻译器用不到的成员一律抛出；
        /// 日期部分提取发射 <c>PART(col)</c> 形态（如 YEAR(CreatedAt)）便于断言。
        /// </summary>
        private sealed class NeutralDialect : SqlDialectBase
        {
            private readonly bool treatsEmptyStringAsNull;

            public NeutralDialect(bool treatsEmptyStringAsNull = false)
            {
                this.treatsEmptyStringAsNull = treatsEmptyStringAsNull;
            }

            public override bool SupportsSchema => false;
            public override int MaxParameterCount => 999;
            public override int MaxRowsPerInsertValues => int.MaxValue;
            public override bool TreatsEmptyStringAsNull => treatsEmptyStringAsNull;

            public override string QuoteIdentifier(string identifier) => identifier;
            public override string QuoteTableName(string schema, string tableName) => tableName;
            public override InsertIdentityMode GetInsertIdentityMode(DbServerInfo serverInfo) => InsertIdentityMode.SelectLastInsertId;
            public override string SelectLastInsertId(string columnName) => throw new NotSupportedException();
            public override string GetDatePartExpression(string quotedColumn, DatePart part) => part.ToString().ToUpperInvariant() + "(" + quotedColumn + ")";
            public override string BuildAverageExpression(string quotedColumn) => throw new NotSupportedException();
        }

        /// <summary>
        /// 纯翻译测试用参数构建器：@ 前缀 + 最小 DbParameter 桩（仅承载 ParameterName/Value/DbType 供断言）。
        /// </summary>
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

        /// <summary>
        /// 断言用途的最小 DbParameter 桩：仅承载 ParameterName/Value/DbType，其余成员为空实现；
        /// DbType 未指定时保持默认值 Object（用于断言"参数未沿用日期列的 DbType"）。
        /// </summary>
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

        #region 比较运算

        [Fact]
        public void Translate_Equal_TranslatesToParameterizedEquality()
        {
            var result = Translate(r => r.Age == 18);

            Assert.Equal("Age = @Age", result.WhereClause);
            var parameter = Assert.Single(result.Parameters);
            Assert.Equal("@Age", parameter.ParameterName);
            Assert.Equal(18, parameter.Value);
        }

        [Theory]
        [InlineData(ExpressionType.NotEqual, "<>")]
        [InlineData(ExpressionType.LessThan, "<")]
        [InlineData(ExpressionType.LessThanOrEqual, "<=")]
        [InlineData(ExpressionType.GreaterThan, ">")]
        [InlineData(ExpressionType.GreaterThanOrEqual, ">=")]
        public void Translate_ComparisonOperators_TranslateToSqlOperators(ExpressionType nodeType, string expectedOperator)
        {
            Expression<Func<TranslatorRow, bool>> predicate = nodeType switch
            {
                ExpressionType.NotEqual => r => r.Age != 18,
                ExpressionType.LessThan => r => r.Age < 18,
                ExpressionType.LessThanOrEqual => r => r.Age <= 18,
                ExpressionType.GreaterThan => r => r.Age > 18,
                _ => r => r.Age >= 18
            };

            var result = Translate(predicate);

            Assert.Equal($"Age {expectedOperator} @Age", result.WhereClause);
            Assert.Equal(18, Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_OperandSwapOnRightColumn_FlipsOperator()
        {
            // 18 < x.Age 翻译为 x.Age > 18，保证列在左侧、值在右侧
            var result = Translate(r => 18 < r.Age);

            Assert.Equal("Age > @Age", result.WhereClause);
            Assert.Equal(18, Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_ColumnVersusColumn_NoParameters()
        {
            var result = Translate(r => r.Age == r.Id);

            Assert.Equal("Age = Id", result.WhereClause);
            Assert.Empty(result.Parameters);
        }

        [Fact]
        public void Translate_ClosureVariable_IsParameterizedNeverInlined()
        {
            var minAge = 18;

            var result = Translate(r => r.Age >= minAge);

            Assert.Equal("Age >= @Age", result.WhereClause);
            Assert.Equal(18, Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_EnumConstant_ConvertsToUnderlyingValue()
        {
            var result = Translate(r => r.Status == RowStatus.Locked);

            Assert.Equal("Status = @Status", result.WhereClause);
            Assert.Equal(1, Assert.Single(result.Parameters).Value);
        }

        #endregion

        #region null 特判

        [Fact]
        public void Translate_EqualNull_TranslatesToIsNull()
        {
            var result = Translate(r => r.Score == null);

            Assert.Equal("Score IS NULL", result.WhereClause);
            Assert.Empty(result.Parameters);
        }

        [Fact]
        public void Translate_NotEqualNull_TranslatesToIsNotNull()
        {
            var result = Translate(r => r.Score != null);

            Assert.Equal("Score IS NOT NULL", result.WhereClause);
            Assert.Empty(result.Parameters);
        }

        [Fact]
        public void Translate_NullClosureVariableEqualToNull_TranslatesToIsNull()
        {
            int? expectedScore = null;

            var result = Translate(r => r.Score == expectedScore);

            Assert.Equal("Score IS NULL", result.WhereClause);
            Assert.Empty(result.Parameters);
        }

        [Fact]
        public void Translate_NullComparisonWithOrdering_Throws()
        {
            // 经闭包变量传入 null，避免编译期常量折叠产生的 CS0464 警告
            int? upperBound = null;

            Assert.Throws<InvalidOperationException>(() => Translate(r => r.Score > upperBound));
        }

        #endregion

        #region 逻辑运算与布尔列

        [Fact]
        public void Translate_LogicalCombination_AlwaysParenthesizesLogicalOperators()
        {
            var result = Translate(r => r.Age > 18 && r.Name == "Alice" || !r.IsActive);

            // 逻辑运算恒加括号保证嵌套语义一目了然；布尔列谓词与 NOT 均为单层括号（无冗余包裹）
            Assert.Equal("((Age > @Age AND Name = @Name) OR NOT (IsActive = @IsActive))", result.WhereClause);
            Assert.Equal(3, result.Parameters.Count);
            Assert.Equal("@Age", result.Parameters[0].ParameterName);
            Assert.Equal(18, result.Parameters[0].Value);
            Assert.Equal("@Name", result.Parameters[1].ParameterName);
            Assert.Equal("Alice", result.Parameters[1].Value);
            Assert.Equal("@IsActive", result.Parameters[2].ParameterName);
            Assert.Equal(true, result.Parameters[2].Value);
        }

        [Fact]
        public void Translate_OrNestedUnderAnd_Parenthesizes()
        {
            var result = Translate(r => r.Age < 30 && (r.Name == "Alice" || r.IsActive));

            Assert.Equal("(Age < @Age AND (Name = @Name OR IsActive = @IsActive))", result.WhereClause);
            Assert.Equal(3, result.Parameters.Count);
        }

        [Fact]
        public void Translate_AndNestedUnderOr_Parenthesizes()
        {
            var result = Translate(r => r.Age < 30 || r.Name == "Alice" && r.IsActive);

            Assert.Equal("(Age < @Age OR (Name = @Name AND IsActive = @IsActive))", result.WhereClause);
            Assert.Equal(3, result.Parameters.Count);
        }

        [Fact]
        public void Translate_BooleanColumnAsPredicate_TranslatesToParameterizedComparison()
        {
            var result = Translate(r => r.IsActive);

            // SQL Server 的 bit 列不能直接作谓词，布尔列统一翻译为参数化比较（对所有方言合法）
            Assert.Equal("IsActive = @IsActive", result.WhereClause);
            Assert.Single(result.Parameters);
            Assert.Equal("@IsActive", result.Parameters[0].ParameterName);
            Assert.Equal(true, result.Parameters[0].Value);
        }

        #endregion

        #region 集合 Contains → IN

        [Fact]
        public void Translate_ListContains_TranslatesToInExpansion()
        {
            var ids = new List<int> { 1, 2, 3 };

            var result = Translate(r => ids.Contains(r.Id));

            Assert.Equal("Id IN (@Id_0, @Id_1, @Id_2)", result.WhereClause);
            Assert.Equal(3, result.Parameters.Count);
            Assert.Equal("@Id_0", result.Parameters[0].ParameterName);
            Assert.Equal(1, result.Parameters[0].Value);
            Assert.Equal("@Id_2", result.Parameters[2].ParameterName);
            Assert.Equal(3, result.Parameters[2].Value);
        }

        [Fact]
        public void Translate_ArrayContains_TranslatesToInExpansion()
        {
            // 数组的 Contains 编译为静态扩展调用 Enumerable.Contains(ids, x.Id)
            var ids = new[] { 5, 6 };

            var result = Translate(r => ids.Contains(r.Id));

            Assert.Equal("Id IN (@Id_0, @Id_1)", result.WhereClause);
            Assert.Equal(2, result.Parameters.Count);
        }

        [Fact]
        public void Translate_EmptyCollection_Throws()
        {
            var ids = new List<int>();

            Assert.Throws<InvalidOperationException>(() => Translate(r => ids.Contains(r.Id)));
        }

        [Fact]
        public void Translate_CollectionWithNullElement_Throws()
        {
            var ids = new List<int?> { 1, null };

            // 同族值侧 null 守卫（LIKE/变换/日期部分）统一 InvalidOperationException
            Assert.Throws<InvalidOperationException>(() => Translate(r => ids.Contains(r.Score)));
        }

        [Fact]
        public void Translate_EnumAsStringOrderComparison_TranslatesByStoredValue()
        {
            // 枚举名称字符串列的大小比较放行（「合法就不拒绝」）：按存储值（名称字典序）翻译，
            // 与枚举数值序的差异由用户自行把握——语义与 SUM/AVG 枚举聚合的放行同哲学
            var result = Translate(r => r.Level < RowStatus.Locked);

            Assert.Contains("Level < @", result.WhereClause);
            Assert.Equal("Locked", result.Parameters.Single().Value);
        }

        [Fact]
        public void Translate_InWithTooManyElements_Throws()
        {
            // NeutralDialect 参数上限 999：单条件 IN 展开超出上限时在翻译期报错（组合超限由 DbAccess 执行入口兜底）
            var ids = Enumerable.Range(1, 1000).ToList();

            Assert.Throws<InvalidOperationException>(() => Translate(r => ids.Contains(r.Id)));
        }

        [Fact]
        public void Translate_InAtExactLimitBoundary_Succeeds()
        {
            // IN 展开 999 个参数恰好等于方言上限（上限为包含边界）不抛出
            var ids = Enumerable.Range(1, 999).ToList();

            var result = Translate(r => ids.Contains(r.Id));

            Assert.StartsWith("Id IN (@Id_0", result.WhereClause);
            Assert.Equal(999, result.Parameters.Count);
        }

        #endregion

        #region 字符串匹配 → LIKE

        [Fact]
        public void Translate_StringContains_TranslatesToLikeWithEscaping()
        {
            var result = Translate(r => r.Name.Contains("a%c"));

            Assert.Equal(@"Name LIKE @Name ESCAPE '!'", result.WhereClause);
            var parameter = Assert.Single(result.Parameters);
            Assert.Equal("@Name", parameter.ParameterName);
            Assert.Equal(@"%a!%c%", parameter.Value);
        }

        [Fact]
        public void Translate_StringStartsWith_AppendsWildcardAtEnd()
        {
            var result = Translate(r => r.Name.StartsWith("ab"));

            Assert.Equal(@"Name LIKE @Name ESCAPE '!'", result.WhereClause);
            Assert.Equal("ab%", Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_StringEndsWith_PrependsWildcardAtStart()
        {
            var result = Translate(r => r.Name.EndsWith("cd"));

            Assert.Equal(@"Name LIKE @Name ESCAPE '!'", result.WhereClause);
            Assert.Equal("%cd", Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_LikeEscapesUnderscoreAndEscapeChar()
        {
            var result = Translate(r => r.Name.Contains("a_b!c"));

            Assert.Equal(@"%a!_b!!c%", Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_LikeEscapesBracket()
        {
            // '[' 是 SQL Server 方言的 LIKE 通配符（字符区间起始），统一转义为 '![' 保证跨方言按字面匹配
            var result = Translate(r => r.Name.Contains("a[b"));

            Assert.Equal(@"%a![b%", Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_LikeOnConverterColumn_Throws()
        {
            // 转换器列的存储值与属性值不同构（如加密），LIKE 作用于存储文本语义不明确，守卫显式禁止
            Assert.Throws<NotSupportedException>(() => Translate(r => r.Token.Contains("a")));
        }

        [Fact]
        public void Translate_StringTransformOnConverterColumn_Throws()
        {
            Assert.Throws<NotSupportedException>(() => Translate(r => r.Token.ToLower() == "abc"));
        }

        [Fact]
        public void Translate_DatePartOnConverterColumn_Throws()
        {
            Assert.Throws<NotSupportedException>(() => Translate(r => r.SecuredAt.Year == 2024));
        }

        [Fact]
        public void Translate_IsNullOrEmptyOnConverterColumn_Throws()
        {
            // 转换器列的空串判断作用于存储文本（如密文）与 C# 按属性值的语义不一致，守卫显式禁止
            Assert.Throws<NotSupportedException>(() => Translate(r => string.IsNullOrEmpty(r.Token)));
        }

        [Fact]
        public void Translate_ClosureStringTransformMethod_TranslatesAsValue()
        {
            // 闭包变量上的字符串变换方法是值侧可求值表达式（值侧应用相同变换后参数化），不应被字符串变换分支误拒
            var localName = " Alice ";

            var result = Translate(r => r.Name == localName.Trim());

            Assert.Equal("Name = @Name", result.WhereClause);
            Assert.Equal("Alice", Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_LikeWithNullValue_Throws()
        {
            string pattern = null;

            Assert.Throws<InvalidOperationException>(() => Translate(r => r.Name.Contains(pattern)));
        }

        #endregion

        #region string 空值判断

        [Fact]
        public void Translate_StringIsNullOrEmpty_TranslatesToNullOrEmpty()
        {
            var result = Translate(r => string.IsNullOrEmpty(r.Name));

            Assert.Equal("(Name IS NULL OR Name = '')", result.WhereClause);
            Assert.Empty(result.Parameters);
        }

        [Fact]
        public void Translate_StringIsNullOrWhiteSpace_TranslatesToNullOrTrimmedEmpty()
        {
            var result = Translate(r => string.IsNullOrWhiteSpace(r.Name));

            Assert.Equal("(Name IS NULL OR LTRIM(RTRIM(Name)) = '')", result.WhereClause);
            Assert.Empty(result.Parameters);
        }

        [Fact]
        public void Translate_StringIsNullOrEmpty_OnOracleDegeneratesToNullCheck()
        {
            // Oracle 的 '' 即 NULL，IsNullOrEmpty 退化为纯 NULL 判断
            var result = Translate(r => string.IsNullOrEmpty(r.Name), dialect: new NeutralDialect(treatsEmptyStringAsNull: true));

            Assert.Equal("(Name IS NULL)", result.WhereClause);
            Assert.Empty(result.Parameters);
        }

        [Fact]
        public void Translate_StringIsNullOrWhiteSpace_OnOracleUsesTrimIsNull()
        {
            // Oracle 中 TRIM(NULL) 与 TRIM('  ') 均为 NULL，空白判断用 TRIM(col) IS NULL
            var result = Translate(r => string.IsNullOrWhiteSpace(r.Name), dialect: new NeutralDialect(treatsEmptyStringAsNull: true));

            Assert.Equal("(Name IS NULL OR TRIM(Name) IS NULL)", result.WhereClause);
            Assert.Empty(result.Parameters);
        }

        [Fact]
        public void Translate_NegatedIsNullOrEmpty_WrapsWithNot()
        {
            var result = Translate(r => !string.IsNullOrEmpty(r.Name));

            Assert.Equal("NOT (Name IS NULL OR Name = '')", result.WhereClause);
        }

        [Fact]
        public void Translate_StringIsNullOrEmptyOnMethodResult_Throws()
        {
            // 参数必须是直连字符串列，方法调用结果（如 x.Name.ToLower()）不支持
            Assert.Throws<NotSupportedException>(() => Translate(r => string.IsNullOrEmpty(r.Name.ToLower())));
        }

        [Fact]
        public void Translate_StringIsNullOrEmptyOnNotMappedProperty_Throws()
        {
            // 未映射成员在列解析处显式失败，报错直接指出成员不是映射列
            var exception = Assert.Throws<NotSupportedException>(() => Translate(r => string.IsNullOrEmpty(r.Note)));

            Assert.Contains("不是实体 '", exception.Message);
            Assert.Contains("TranslatorRow' 的映射列", exception.Message);
        }

        [Fact]
        public void Translate_StringIsNullOrEmptyOnClosureVariable_ThrowsWithParameterFormHint()
        {
            var localName = "abc";

            // 方法在白名单内，报错指向参数形态（闭包变量）而非能力不支持
            var exception = Assert.Throws<NotSupportedException>(() => Translate(r => string.IsNullOrEmpty(localName)));

            Assert.Contains("直连的字符串实体列", exception.Message);
        }

        #endregion

        #region 字符串变换

        [Fact]
        public void Translate_ToLowerEqual_TranslatesToLowerFunctionWithValueTransformed()
        {
            var result = Translate(r => r.Name.ToLower() == "Alice");

            // 值侧应用与列侧相同的变换：用户意图是忽略大小写匹配，仅变换列侧在区分大小写排序规则下恒不命中
            Assert.Equal("LOWER(Name) = @Name", result.WhereClause);
            var parameter = Assert.Single(result.Parameters);
            Assert.Equal("@Name", parameter.ParameterName);
            Assert.Equal("alice", parameter.Value);
        }

        [Fact]
        public void Translate_ToUpperNotEqual_TranslatesToUpperFunctionWithValueTransformed()
        {
            var result = Translate(r => r.Name.ToUpper() != "Bob");

            Assert.Equal("UPPER(Name) <> @Name", result.WhereClause);
            Assert.Equal("BOB", Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_ToLowerWithClosureVariable_TransformsEvaluatedValue()
        {
            var expected = "Alice";

            var result = Translate(r => r.Name.ToLower() == expected);

            Assert.Equal("LOWER(Name) = @Name", result.WhereClause);
            Assert.Equal("alice", Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_ToLowerWithNullValue_Throws()
        {
            string expected = null;

            Assert.Throws<InvalidOperationException>(() => Translate(r => r.Name.ToLower() == expected));
        }

        [Fact]
        public void Translate_ToLowerChainedLike_Throws()
        {
            // 链式调用不支持：x.Name.ToLower().StartsWith 的接收者不是直连字符串列
            Assert.Throws<NotSupportedException>(() => Translate(r => r.Name.ToLower().StartsWith("a")));
        }

        [Fact]
        public void Translate_ToLowerColumnVersusColumn_Throws()
        {
            Assert.Throws<NotSupportedException>(() => Translate(r => r.Name.ToLower() == r.Name));
        }

        [Fact]
        public void Translate_ToLowerWithCulture_Throws()
        {
            // 仅支持无参重载；带 CultureInfo 的重载不在白名单
            Assert.Throws<NotSupportedException>(() => Translate(r => r.Name.ToLower(CultureInfo.InvariantCulture) == "a"));
        }

        [Fact]
        public void Translate_TrimEqual_TranslatesToNestedTrimWithValueTransformed()
        {
            var result = Translate(r => r.Name.Trim() == "Alice");

            // Trim 发射 LTRIM(RTRIM(col)) 而非 TRIM(col)——标准 TRIM 需 SQL Server 2017+，嵌套形式全方言兼容
            Assert.Equal("LTRIM(RTRIM(Name)) = @Name", result.WhereClause);
            var parameter = Assert.Single(result.Parameters);
            Assert.Equal("@Name", parameter.ParameterName);
            Assert.Equal("Alice", parameter.Value);
        }

        [Fact]
        public void Translate_TrimWithSurroundingSpaces_TransformsEvaluatedValue()
        {
            var expected = "  Alice  ";

            var result = Translate(r => r.Name.Trim() == expected);

            // 值侧应用与列侧相同的变换（C# 侧两侧去空白后比较）
            Assert.Equal("LTRIM(RTRIM(Name)) = @Name", result.WhereClause);
            Assert.Equal("Alice", Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_TrimStartEqual_TranslatesToLeftTrim()
        {
            var result = Translate(r => r.Name.TrimStart() == "Alice ");

            Assert.Equal("LTRIM(Name) = @Name", result.WhereClause);
            Assert.Equal("Alice ", Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_TrimEndNotEqual_TranslatesToRightTrim()
        {
            var result = Translate(r => r.Name.TrimEnd() != " Alice");

            Assert.Equal("RTRIM(Name) <> @Name", result.WhereClause);
            Assert.Equal(" Alice", Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_TrimWithCharactersArgument_Throws()
        {
            // 仅支持无参重载；带字符参数的 Trim 不在白名单
            Assert.Throws<NotSupportedException>(() => Translate(r => r.Name.Trim('x') == "a"));
        }

        [Fact]
        public void Translate_TrimChainedStartsWith_Throws()
        {
            // 链式调用不支持：x.Name.Trim().StartsWith 的接收者不是直连字符串列
            Assert.Throws<NotSupportedException>(() => Translate(r => r.Name.Trim().StartsWith("a")));
        }

        #endregion

        #region 日期部分提取

        [Fact]
        public void Translate_YearEqual_TranslatesToDatePartFunction()
        {
            var result = Translate(r => r.CreatedAt.Year == 2024);

            Assert.Equal("YEAR(CreatedAt) = @CreatedAt", result.WhereClause);
            var parameter = Assert.Single(result.Parameters);
            Assert.Equal("@CreatedAt", parameter.ParameterName);
            Assert.Equal(2024, parameter.Value);
            // 整数参数不沿用日期列的 DbType，保持桩的默认 Object（未指定）
            Assert.Equal(DbType.Object, parameter.DbType);
        }

        [Fact]
        public void Translate_MonthGreaterThan_TranslatesToDatePartFunction()
        {
            var result = Translate(r => r.CreatedAt.Month > 6);

            Assert.Equal("MONTH(CreatedAt) > @CreatedAt", result.WhereClause);
            Assert.Equal(6, Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_DayLessThanOrEqual_TranslatesToDatePartFunction()
        {
            var result = Translate(r => r.CreatedAt.Day <= 15);

            Assert.Equal("DAY(CreatedAt) <= @CreatedAt", result.WhereClause);
            Assert.Equal(15, Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_DateEqual_TranslatesToDateNormalization()
        {
            var expected = new DateTime(2024, 1, 2);

            var result = Translate(r => r.CreatedAt.Date == expected);

            Assert.Equal("DATE(CreatedAt) = @CreatedAt", result.WhereClause);
            var parameter = Assert.Single(result.Parameters);
            Assert.Equal("@CreatedAt", parameter.ParameterName);
            Assert.Equal(expected, parameter.Value);
        }

        [Fact]
        public void Translate_NullableColumnDatePart_TranslatesViaValueAccess()
        {
            // 可空日期列经 .Value 访问（C# 语法要求），翻译时剥去 .Value 直接作用于列
            var result = Translate(r => r.DeletedAt!.Value.Year == 2024);

            Assert.Equal("YEAR(DeletedAt) = @DeletedAt", result.WhereClause);
            Assert.Equal(2024, Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_NullableColumnDatePartWithNullValue_Throws()
        {
            // C# 提升比较对非 null 的 int 与 null 恒为 false（不存在选中 NULL 日期行的合法写法），
            // IS NULL 特判会产生与 C# 相反的行集，统一拒绝并引导显式判空
            int? expectedYear = null;

            Assert.Throws<InvalidOperationException>(() => Translate(r => r.DeletedAt!.Value.Year == expectedYear));
        }

        [Fact]
        public void Translate_StaticMemberNamedAsDatePart_TranslatesAsValue()
        {
            // 静态成员名恰为日期部分名（Settings.Month）不是实体实例的日期部分，按值侧求值参数化（不应 NRE）
            var result = Translate(r => r.Age == DateConstants.Month);

            Assert.Equal("Age = @Age", result.WhereClause);
            Assert.Equal(DateConstants.Month, Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_TypeBinaryPredicate_Throws()
        {
            // x is T（恒真/可降形态仅编译警告）：未覆写节点会静默产出空 WHERE（查询路径静默全表），节点白名单显式拒绝
            Assert.Throws<NotSupportedException>(() => Translate(r => r is TranslatorRow));
        }

        [Fact]
        public void Translate_ConditionalPredicate_Throws()
        {
            // 三元谓词：未覆写节点会产出无分隔符的拼接垃圾 SQL，节点白名单显式拒绝（值侧三元不受影响）
            Assert.Throws<NotSupportedException>(() => Translate(r => r.IsActive ? r.Age > 5 : r.Age < 3));
        }

        [Fact]
        public void Translate_CharValueOnCharColumn_NormalizesToChar()
        {
            // char 列 == 'A'：编译器把 char 提升为 int，值须按列类型归一回 char（Int32 在 MySQL/SQLite 字符列上静默不命中）
            var result = Translate(r => r.Grade == 'A');

            Assert.Equal("Grade = @Grade", result.WhereClause);
            Assert.Equal('A', Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_ContainsCharValue_TranslatesAsLike()
        {
            // net8 的 Contains('A') char 重载：归一为单字符字符串走 LIKE（与其余字符串白名单方法一致）
            var result = Translate(r => r.Name.Contains('A'));

            Assert.Equal("Name LIKE @Name ESCAPE '!'", result.WhereClause);
            Assert.Equal("%A%", Assert.Single(result.Parameters).Value);
        }

        private static class DateConstants
        {
            public const int Month = 6;
        }

        [Fact]
        public void Translate_DatePartColumnVersusColumn_Throws()
        {
            Assert.Throws<NotSupportedException>(() => Translate(r => r.CreatedAt.Year == r.Id));
        }

        [Fact]
        public void Translate_DatePartWithNonIntegerValue_Throws()
        {
            // int == double 编译为 double 比较，值侧为 2024.5，非整数显式失败
            Assert.Throws<InvalidOperationException>(() => Translate(r => r.CreatedAt.Year == 2024.5));
        }

        [Fact]
        public void Translate_DatePartWithNullOrdering_Throws()
        {
            int? upperBound = null;

            Assert.Throws<InvalidOperationException>(() => Translate(r => r.DeletedAt!.Value.Year < upperBound));
        }

        [Fact]
        public void Translate_HourEqual_TranslatesToDatePartFunction()
        {
            var result = Translate(r => r.CreatedAt.Hour == 9);

            Assert.Equal("HOUR(CreatedAt) = @CreatedAt", result.WhereClause);
            var parameter = Assert.Single(result.Parameters);
            Assert.Equal("@CreatedAt", parameter.ParameterName);
            Assert.Equal(9, parameter.Value);
            Assert.Equal(DbType.Object, parameter.DbType);
        }

        [Fact]
        public void Translate_MinuteGreaterThan_TranslatesToDatePartFunction()
        {
            var result = Translate(r => r.CreatedAt.Minute > 30);

            Assert.Equal("MINUTE(CreatedAt) > @CreatedAt", result.WhereClause);
            Assert.Equal(30, Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_SecondLessThanOrEqual_TranslatesToDatePartFunction()
        {
            var result = Translate(r => r.CreatedAt.Second <= 59);

            Assert.Equal("SECOND(CreatedAt) <= @CreatedAt", result.WhereClause);
            Assert.Equal(59, Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_NullableColumnHour_TranslatesViaValueAccess()
        {
            // 可空日期列经 .Value 访问（C# 语法要求），翻译时剥去 .Value 直接作用于列
            var result = Translate(r => r.DeletedAt!.Value.Hour == 18);

            Assert.Equal("HOUR(DeletedAt) = @DeletedAt", result.WhereClause);
            Assert.Equal(18, Assert.Single(result.Parameters).Value);
        }

        #endregion

        #region 可空列 HasValue

        [Fact]
        public void Translate_HasValue_TranslatesToIsNotNull()
        {
            var result = Translate(r => r.Score.HasValue);

            Assert.Equal("(Score IS NOT NULL)", result.WhereClause);
            Assert.Empty(result.Parameters);
        }

        [Fact]
        public void Translate_NegatedHasValue_TranslatesToIsNull()
        {
            var result = Translate(r => !r.Score.HasValue);

            // 直接翻译为 IS NULL，避免 NOT (col IS NOT NULL) 的双重否定
            Assert.Equal("(Score IS NULL)", result.WhereClause);
            Assert.Empty(result.Parameters);
        }

        [Fact]
        public void Translate_HasValueOnNullableDateColumn_TranslatesToIsNotNull()
        {
            var result = Translate(r => r.DeletedAt.HasValue);

            Assert.Equal("(DeletedAt IS NOT NULL)", result.WhereClause);
            Assert.Empty(result.Parameters);
        }

        [Fact]
        public void Translate_HasValueWithinLogicalCombination_TranslatesInline()
        {
            var result = Translate(r => r.IsActive && r.Score.HasValue);

            Assert.Equal("(IsActive = @IsActive AND (Score IS NOT NULL))", result.WhereClause);
            var parameter = Assert.Single(result.Parameters);
            Assert.Equal("@IsActive", parameter.ParameterName);
            Assert.Equal(true, parameter.Value);
        }

        #endregion

        #region 参数名分配

        [Fact]
        public void Translate_SameColumnTwice_AllocatesSuffixedParameterName()
        {
            var result = Translate(r => r.Age > 18 && r.Age < 30);

            Assert.Equal("(Age > @Age AND Age < @Age_2)", result.WhereClause);
            Assert.Equal("@Age", result.Parameters[0].ParameterName);
            Assert.Equal("@Age_2", result.Parameters[1].ParameterName);
        }

        [Fact]
        public void Translate_SharedAllocator_AvoidsConflictWithSetSideNames()
        {
            // 模拟条件更新场景：SET 侧已占用 Age，WHERE 侧同名列参数追加后缀
            var sharedNames = new ParameterNameAllocator();
            sharedNames.Allocate("Age");

            var result = Translate(r => r.Age == 18, sharedNames);

            Assert.Equal("Age = @Age_2", result.WhereClause);
            Assert.Equal("@Age_2", Assert.Single(result.Parameters).ParameterName);
        }

        #endregion

        #region 不支持的表达式

        [Fact]
        public void Translate_ConstantPredicate_Throws()
        {
            Assert.Throws<NotSupportedException>(() => Translate(r => true));
        }

        [Fact]
        public void Translate_ClosureBooleanPredicate_ThrowsWithConstantPredicateMessage()
        {
            // 闭包布尔变量作谓词：过滤结果与数据库无关，常量谓词守卫给出可定位消息（而非成员表达式兜底的张冠李戴）
            bool flag = true;

            var ex = Assert.Throws<NotSupportedException>(() => Translate(r => flag));

            Assert.Contains("常量谓词", ex.Message);
        }

        [Fact]
        public void Translate_ClosureBooleanAsLogicalOperand_ThrowsWithConstantPredicateMessage()
        {
            // 逻辑运算的操作数为闭包布尔：同样落入常量谓词守卫
            bool flag = false;

            var ex = Assert.Throws<NotSupportedException>(() => Translate(r => r.IsActive && flag));

            Assert.Contains("常量谓词", ex.Message);
        }

        [Fact]
        public void Translate_ArithmeticOnColumn_Throws()
        {
            Assert.Throws<NotSupportedException>(() => Translate(r => r.Age + 1 > 10));
        }

        [Fact]
        public void Translate_NestedMemberPath_Throws()
        {
            // Millisecond 不在日期部分白名单（仅 Year/Month/Day/Date/Hour/Minute/Second），仍按嵌套成员路径显式失败
            Assert.Throws<NotSupportedException>(() => Translate(r => r.CreatedAt.Millisecond == 5));
        }

        [Fact]
        public void Translate_UnmappedMember_Throws()
        {
            Assert.Throws<NotSupportedException>(() => Translate(r => r.Note == "x"));
        }

        [Fact]
        public void Translate_UnsupportedMethod_Throws()
        {
            // Substring 不在字符串方法白名单（Contains/StartsWith/EndsWith/IsNullOrEmpty/IsNullOrWhiteSpace/字符串变换族）
            Assert.Throws<NotSupportedException>(() => Translate(r => r.Name.Substring(0, 2) == "A"));
        }

        [Fact]
        public void Translate_StringMethodWithComparisonArgument_Throws()
        {
            Assert.Throws<NotSupportedException>(() => Translate(r => r.Name.StartsWith("A", StringComparison.Ordinal)));
        }

        [Fact]
        public void Translate_BothSidesNonColumn_Throws()
        {
            var age = 18;

            Assert.Throws<NotSupportedException>(() => Translate(r => age == 19));
        }

        #endregion

        #region 形态矩阵回归（第四轮审查沉淀）

        [Fact]
        public void Translate_LongValueOnIntColumn_ParameterizesAsLongValue()
        {
            // int 列 vs long 字面值：编译器把列提升为 long 后比较，值按列类型归一为 Int32（与列同型，避免严格方言的类型不匹配）
            var result = Translate(r => r.Age == 18L);

            Assert.Equal("Age = @Age", result.WhereClause);
            var parameter = Assert.Single(result.Parameters);
            Assert.Equal(18, Assert.IsType<int>(parameter.Value));
        }

        [Fact]
        public void Translate_EnumValueOnIntColumn_ConvertsToUnderlyingValue()
        {
            // (RowStatus)x.Id == RowStatus.Locked：列侧显式转换被 StripConversions 剥离，值侧枚举按底层整数参数化
            var result = Translate(r => (RowStatus)r.Id == RowStatus.Locked);

            Assert.Equal("Id = @Id", result.WhereClause);
            Assert.Equal(1, Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_DeferredLinqContains_TranslatesToInAfterEvaluation()
        {
            // 延迟 LINQ 的 ids.Where(...).Contains(x.Id)：值侧子树整体求值（含嵌套 lambda 与延迟枚举）后按元素展开
            var ids = new List<int> { 1, 2, 3, 4 };

            var result = Translate(r => ids.Where(i => i > 2).Contains(r.Id));

            Assert.Equal("Id IN (@Id_0, @Id_1)", result.WhereClause);
            Assert.Equal(3, result.Parameters[0].Value);
            Assert.Equal(4, result.Parameters[1].Value);
        }

        [Fact]
        public void Translate_ValueSideConditional_FoldsToSingleParameter()
        {
            // 值侧条件表达式：不引用实体参数，整体求值后参数化
            var useShort = true;

            var result = Translate(r => r.Name == (useShort ? "a" : "b"));

            Assert.Equal("Name = @Name", result.WhereClause);
            Assert.Equal("a", Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_ValueSideInterpolation_FoldsToSingleParameter()
        {
            // 值侧字符串插值（string.Format 调用）：整体求值后参数化
            var prefix = "pre";

            var result = Translate(r => r.Name == $"{prefix}_sfx");

            Assert.Equal("Name = @Name", result.WhereClause);
            Assert.Equal("pre_sfx", Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_NullableBoolColumnComparedToTrue_Parameterizes()
        {
            var result = Translate(r => r.IsApproved == true);

            Assert.Equal("IsApproved = @IsApproved", result.WhereClause);
            Assert.Equal(true, Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_CastToBoolOnNullableBoolColumn_TranslatesAsBoolPredicate()
        {
            // (bool)x.IsApproved：可空布尔列的显式转换透传后仍走布尔列谓词分支（[Col] = @p），NULL 行排除与 C# 一致
            var result = Translate(r => (bool)r.IsApproved);

            Assert.Equal("IsApproved = @IsApproved", result.WhereClause);
            Assert.Equal(true, Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_HasValueOnNullableBoolColumn_TranslatesToIsNotNull()
        {
            var result = Translate(r => r.IsApproved.HasValue);

            Assert.Equal("(IsApproved IS NOT NULL)", result.WhereClause);
        }

        [Fact]
        public void Translate_HasValueEqualToTrue_TranslatesToIsNotNull()
        {
            // 比较位置的 HasValue 布尔比较：Equal(true) 化简为 IS NOT NULL（与谓词位置语义一致）
            var result = Translate(r => r.Score.HasValue == true);

            Assert.Equal("(Score IS NOT NULL)", result.WhereClause);
        }

        [Fact]
        public void Translate_HasValueNotEqualToClosureFalse_TranslatesToIsNotNull()
        {
            // NotEqual(false) 同样是 IS NOT NULL；值侧闭包布尔先求值再分派
            bool flag = false;

            var result = Translate(r => r.Score.HasValue != flag);

            Assert.Equal("(Score IS NOT NULL)", result.WhereClause);
        }

        [Fact]
        public void Translate_HasValueEqualToFalse_TranslatesToIsNull()
        {
            var result = Translate(r => r.Score.HasValue == false);

            Assert.Equal("(Score IS NULL)", result.WhereClause);
        }

        [Fact]
        public void Translate_NullableValueUnwrap_TranslatesAsInnerColumnComparison()
        {
            // 可空列 .Value 解包：剥离后按内层列翻译（null 语义与 SQL 三值逻辑一致，NULL 行不命中）
            var result = Translate(r => r.Score.Value > 80);

            Assert.Equal("Score > @Score", result.WhereClause);
            Assert.Equal(80, result.Parameters[0].Value);
        }

        [Fact]
        public void Translate_NullableBoolValueUnwrapAsPredicate_TranslatesAsBoolColumnPredicate()
        {
            // 可空布尔列 .Value 直接作谓词：剥离后走布尔列谓词分支（与 (bool)x.IsApproved 的透传语义一致）
            var result = Translate(r => r.IsApproved.Value);

            Assert.Equal("IsApproved = @IsApproved", result.WhereClause);
            Assert.Equal(true, result.Parameters.Single().Value);
        }

        [Fact]
        public void Translate_ByteArrayColumnComparison_ParameterizesByteArray()
        {
            var payload = new byte[] { 1, 2, 3 };

            var result = Translate(r => r.Payload == payload);

            Assert.Equal("Payload = @Payload", result.WhereClause);
            var parameter = Assert.Single(result.Parameters);
            Assert.Equal(payload, parameter.Value);
        }

        [Fact]
        public void Translate_TypeAsOnColumn_StrippedForPlainComparison()
        {
            // 列侧 as 转换（TypeAs 节点）被 StripConversions 剥离，按直连列与值比较
            var name = "Alice";

            var result = Translate(r => (r.Name as object) == (object)name);

            Assert.Equal("Name = @Name", result.WhereClause);
            Assert.Equal("Alice", Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_ConvertCheckedOnColumn_StrippedForPlainComparison()
        {
            // checked 上下文的显式转换（ConvertChecked）同样剥离，按整列与值比较
            var result = Translate(r => checked((short)r.Id) == 5);

            Assert.Equal("Id = @Id", result.WhereClause);
            Assert.Equal(5, Assert.Single(result.Parameters).Value);
        }

        [Fact]
        public void Translate_ColumnSideCoalesce_Throws()
        {
            // 列侧 null 合并不翻译为 SQL COALESCE：引用实体参数的子表达式显式拒绝
            Assert.Throws<NotSupportedException>(() => Translate(r => (r.Name ?? "d") == "a"));
        }

        #endregion

        #region 有损舍入防护（浮点/decimal 谓词值）

        [Fact]
        public void Translate_FractionalDoubleOnIntColumn_KeepsOriginalValue()
        {
            // r.Age == 3.7：Age 提升为 double 与常量比较，值带非零小数部分——若归一为 int 会静默舍入成 4，
            // WHERE Age = 4 改写匹配行集（错数据）。防护保持原值 3.7 交数据库隐式比较（自然不命中，与手写 SQL 一致）
            var result = Translate(r => r.Age == 3.7);

            Assert.Equal("Age = @Age", result.WhereClause);
            var parameter = Assert.Single(result.Parameters);
            Assert.Equal(3.7, Assert.IsType<double>(parameter.Value));
        }

        [Fact]
        public void Translate_FractionalClosureDoubleOnIntColumn_KeepsOriginalValue()
        {
            // 闭包 double 变量是最真实的触发形态（配置阈值/计算值在运行时到达）
            double limit = 3.7;

            var result = Translate(r => r.Age == limit);

            Assert.Equal("Age = @Age", result.WhereClause);
            Assert.Equal(3.7, Assert.IsType<double>(Assert.Single(result.Parameters).Value));
        }

        [Fact]
        public void Translate_FractionalDecimalOnIntColumn_KeepsOriginalValue()
        {
            // decimal 源同族防护：37.5 不得被舍入成 38
            var result = Translate(r => r.Age == 37.5m);

            Assert.Equal("Age = @Age", result.WhereClause);
            Assert.Equal(37.5m, Assert.IsType<decimal>(Assert.Single(result.Parameters).Value));
        }

        [Fact]
        public void Translate_FractionalFloatOnIntColumn_KeepsOriginalValue()
        {
            // float 源同族防护
            var result = Translate(r => r.Age == 3.7f);

            Assert.Equal("Age = @Age", result.WhereClause);
            Assert.Equal(3.7f, Assert.IsType<float>(Assert.Single(result.Parameters).Value));
        }

        [Fact]
        public void Translate_IntegralDoubleOnIntColumn_NormalizesLosslessly()
        {
            // 42.0 无小数部分：无损归一为 int 42（与列同型，保留索引比较；拦截它反而误伤合法写法）
            var result = Translate(r => r.Age == 42.0);

            Assert.Equal("Age = @Age", result.WhereClause);
            Assert.Equal(42, Assert.IsType<int>(Assert.Single(result.Parameters).Value));
        }

        [Fact]
        public void Translate_FractionalDoubleOnNullableIntColumn_KeepsOriginalValue()
        {
            // 可空 int 列同防护（可空解包后走同一守卫）
            var result = Translate(r => r.Score == 3.7);

            Assert.Equal("Score = @Score", result.WhereClause);
            Assert.Equal(3.7, Assert.IsType<double>(Assert.Single(result.Parameters).Value));
        }

        [Fact]
        public void Translate_FractionalDoubleWithComparisonOperator_KeepsOriginalValue()
        {
            // 守卫在 ConvertToDb 值归一层，与比较运算符无关（>、<、>= 同理）
            var result = Translate(r => r.Age > 3.7);

            Assert.Equal("Age > @Age", result.WhereClause);
            Assert.Equal(3.7, Assert.IsType<double>(Assert.Single(result.Parameters).Value));
        }

        #endregion
    }
}
