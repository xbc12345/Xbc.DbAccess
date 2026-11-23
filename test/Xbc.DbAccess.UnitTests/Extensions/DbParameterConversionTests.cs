using System.Collections;
using System.Data;
using System.Data.Common;
using Xunit;

namespace Xbc.DbAccess.UnitTests.Extensions
{
    /// <summary>
    /// DbParameterConversionExtensions 参数转换的单元测试：各参数形态（DbParameter/集合/字典/匿名对象）、
    /// IN 集合展开（含词边界、前缀撞名、空集合守卫）、StoredProcedure 不展开与守卫异常。
    /// </summary>
    public class DbParameterConversionTests : Infrastructure.SqliteTestBase
    {
        [Fact]
        public void Convert_NullParam_ReturnsNull()
        {
            string sql = "SELECT 1";
            Assert.Null(DbAccess.ConvertToDbParameters(null, ref sql));
        }

        [Fact]
        public void Convert_NullDbAccess_ThrowsArgumentNullException()
        {
            string sql = "SELECT 1";
            Assert.Throws<ArgumentNullException>(() => DbParameterConversionExtensions.ConvertToDbParameters(null, new { }, ref sql));
        }

        [Fact]
        public void Convert_DbParameterForms_PassedThrough()
        {
            var single = ProviderFactory.ParameterBuilder.CreateParameter("Id", 1);
            var array = new[] { single, ProviderFactory.ParameterBuilder.CreateParameter("Name", "Tom") };

            string sql = "SELECT 1";
            Assert.Same(array, DbAccess.ConvertToDbParameters(array, ref sql));// DbParameter[] 原样返回

            var wrapped = DbAccess.ConvertToDbParameters(single, ref sql);
            Assert.Single(wrapped);
            Assert.Same(single, wrapped[0]);

            var collection = DbAccess.NewParameters();
            collection.Add("Id", 1);
            var fromCollection = DbAccess.ConvertToDbParameters(collection, ref sql);
            Assert.Single(fromCollection);
            Assert.Equal("@Id", fromCollection[0].ParameterName);

            var fromEnumerable = DbAccess.ConvertToDbParameters(new List<DbParameter> { single }, ref sql);
            Assert.Single(fromEnumerable);
            Assert.Same(single, fromEnumerable[0]);
        }

        [Fact]
        public void Convert_Dictionary_ConvertsEntries_EmptyKeySkipped()
        {
            var dict = new Dictionary<string, object>
            {
                { "", 0 },// 空键跳过
                { "Name", "Tom" },
                { "Age", 20 }
            };

            string sql = "SELECT 1";
            var parameters = DbAccess.ConvertToDbParameters(dict, ref sql);

            Assert.Equal(2, parameters.Length);
            Assert.All(parameters, p => Assert.StartsWith("@", p.ParameterName));
            Assert.Contains(parameters, p => p.ParameterName == "@Name");
            Assert.Contains(parameters, p => p.ParameterName == "@Age");
        }

        [Fact]
        public void Convert_Hashtable_ConvertsEntries()
        {
            var hashtable = new Hashtable
            {
                ["Name"] = "Tom",
                ["Age"] = 20
            };

            string sql = "SELECT 1";
            var parameters = DbAccess.ConvertToDbParameters(hashtable, ref sql);

            Assert.Equal(2, parameters.Length);
            Assert.Contains(parameters, p => p.ParameterName == "@Name");
            Assert.Contains(parameters, p => p.ParameterName == "@Age");
        }

        [Fact]
        public void Convert_AnonymousObject_ConvertsProperties()
        {
            string sql = "SELECT 1";
            var parameters = DbAccess.ConvertToDbParameters(new { Name = "Tom", Age = 20 }, ref sql);

            Assert.Equal(2, parameters.Length);
            Assert.Contains(parameters, p => p.ParameterName == "@Name" && (string)p.Value == "Tom");
            Assert.Contains(parameters, p => p.ParameterName == "@Age" && (int)p.Value == 20);
        }

        [Fact]
        public void Convert_ObjectWithoutProperties_ReturnsEmptyArray()
        {
            string sql = "SELECT 1";

            Assert.Empty(DbAccess.ConvertToDbParameters(new object(), ref sql));
        }

        [Fact]
        public void Convert_CollectionExpansion_ParenthesizedPlaceholder()
        {
            var dict = new Dictionary<string, object>
            {
                { "Ids", new List<int> { 1, 2, 3 } }
            };

            string sql = "SELECT * FROM T WHERE Id IN (@Ids)";
            var parameters = DbAccess.ConvertToDbParameters(dict, ref sql);

            Assert.Equal("SELECT * FROM T WHERE Id IN (@Ids_0, @Ids_1, @Ids_2)", sql);
            Assert.Equal(3, parameters.Length);
            Assert.Equal("@Ids_0", parameters[0].ParameterName);
            Assert.Equal(1, parameters[0].Value);
            Assert.Equal(3, parameters[2].Value);
        }

        [Fact]
        public void Convert_CollectionExpansion_DapperStyleAutoParentheses()
        {
            var dict = new Dictionary<string, object> { { "Ids", new[] { 1, 2 } } };

            // Dapper 风格：IN @Ids 无括号，展开时自动补括号
            string sql = "SELECT * FROM T WHERE Id IN @Ids";
            DbAccess.ConvertToDbParameters(dict, ref sql);

            Assert.Equal("SELECT * FROM T WHERE Id IN (@Ids_0, @Ids_1)", sql);
        }

        [Fact]
        public void Convert_CollectionExpansion_OutsideInClause_ReplacedWithoutParentheses()
        {
            var dict = new Dictionary<string, object> { { "Ids", new[] { 1, 2 } } };

            // 非子句语境（Dapper In 扩展风格）仅替换为参数列表，不补括号
            string sql = "SELECT * FROM T WHERE Id = @Ids";
            DbAccess.ConvertToDbParameters(dict, ref sql);

            Assert.Equal("SELECT * FROM T WHERE Id = @Ids_0, @Ids_1", sql);
        }

        [Fact]
        public void Convert_CollectionExpansion_PlaceholderInsideLiteral_NotReplaced()
        {
            var dict = new Dictionary<string, object> { { "Ids", new[] { 1, 2 } } };

            // 字面量内的占位符文本不计入替换（引用感知）：字面量内容保持原样，仅结构处被展开
            string sql = "SELECT * FROM T WHERE Memo = 'IN @Ids 教程' AND Id IN (@Ids)";
            DbAccess.ConvertToDbParameters(dict, ref sql);

            Assert.Equal("SELECT * FROM T WHERE Memo = 'IN @Ids 教程' AND Id IN (@Ids_0, @Ids_1)", sql);
        }

        [Fact]
        public void Convert_CollectionExpansion_OnlyLiteralPlaceholder_Throws()
        {
            var dict = new Dictionary<string, object> { { "Ids", new[] { 1, 2 } } };

            // 占位符仅存在于字面量内（引用感知不命中）→ 视为参数名未找到（此前会误改写字面量内容）
            string sql = "SELECT * FROM T WHERE Memo = '参考 @Ids 写法'";
            Assert.Throws<ArgumentException>(() =>
            {
                string candidate = sql;
                DbAccess.ConvertToDbParameters(dict, ref candidate);
            });
            Assert.Equal("SELECT * FROM T WHERE Memo = '参考 @Ids 写法'", sql);
        }

        [Fact]
        public void Convert_ScalarAndCollectionKeys_BothPreservedByWordBoundary()
        {
            var dict = new Dictionary<string, object>
            {
                { "Id", 99 },
                { "Ids", new[] { 1, 2 } }
            };

            string sql = "SELECT * FROM T WHERE Id = @Id AND Key IN (@Ids)";
            var parameters = DbAccess.ConvertToDbParameters(dict, ref sql);

            // @Ids 展开不得破坏 @Id（词边界保护），标量参数与展开参数共存
            Assert.Equal("SELECT * FROM T WHERE Id = @Id AND Key IN (@Ids_0, @Ids_1)", sql);
            Assert.Equal(3, parameters.Length);
            Assert.Contains(parameters, p => p.ParameterName == "@Id" && (int)p.Value == 99);
        }

        [Fact]
        public void Convert_PrefixOnlyMatch_ThrowsArgumentException()
        {
            var dict = new Dictionary<string, object> { { "Ids", new[] { 1 } } };

            // SQL 中的 @IdsList 包含 @Ids 前缀但非独立占位符：找不到可展开的占位符
            string sql = "SELECT * FROM T WHERE Id IN (@IdsList)";
            var ex = Assert.Throws<ArgumentException>(() => DbAccess.ConvertToDbParameters(dict, ref sql));
            Assert.Contains("@Ids", ex.Message);
        }

        [Fact]
        public void Convert_CollectionExpansion_EmptyCollection_ThrowsArgumentException()
        {
            var dict = new Dictionary<string, object> { { "Ids", new int[0] } };

            string sql = "SELECT * FROM T WHERE Id IN (@Ids)";
            Assert.Throws<ArgumentException>(() => DbAccess.ConvertToDbParameters(dict, ref sql));
        }

        [Fact]
        public void Convert_StringAndByteArray_NotExpanded()
        {
            var dict = new Dictionary<string, object>
            {
                { "Name", "IN (@Ids)" },// 字符串虽是 IEnumerable<char>，按标量处理
                { "Blob", new byte[] { 1, 2 } },
                { "Chars", new[] { 'a', 'b' } }
            };

            string sql = "SELECT 1";
            var parameters = DbAccess.ConvertToDbParameters(dict, ref sql);

            Assert.Equal(3, parameters.Length);
            Assert.Contains(parameters, p => p.ParameterName == "@Name" && Equals(p.Value, "IN (@Ids)"));
            Assert.Contains(parameters, p => p.ParameterName == "@Blob");
            Assert.Contains(parameters, p => p.ParameterName == "@Chars");
        }

        [Fact]
        public void Convert_NullPropertyValue_AddedAsScalar()
        {
            var dict = new Dictionary<string, object> { { "Name", null } };

            string sql = "SELECT 1";
            var parameters = DbAccess.ConvertToDbParameters(dict, ref sql);

            Assert.Single(parameters);
            Assert.Equal("@Name", parameters[0].ParameterName);
            // null 值经参数构建器归一为 DBNull
            Assert.Equal(DBNull.Value, parameters[0].Value);
        }

        [Fact]
        public void Convert_StoredProcedure_NoCollectionExpansion()
        {
            var dict = new Dictionary<string, object> { { "Ids", new[] { 1, 2 } } };

            // 存储过程的集合参数由过程自身处理，不做 IN 展开
            string sql = "usp_GetByIds";
            var parameters = DbAccess.ConvertToDbParameters(dict, ref sql, CommandType.StoredProcedure);

            Assert.Equal("usp_GetByIds", sql);
            Assert.Single(parameters);
            Assert.Equal("@Ids", parameters[0].ParameterName);
        }

        [Fact]
        public void Convert_AnonymousObject_WithCollectionProperty_Expanded()
        {
            // 匿名对象的集合属性同样展开为 IN 参数（对象路径与字典路径共用展开逻辑）
            var obj = new { Ids = new[] { 1, 2 }, Name = "Tom" };
            string sql = "SELECT * FROM T WHERE Name = @Name AND Id IN (@Ids)";

            var parameters = DbAccess.ConvertToDbParameters(obj, ref sql);

            Assert.Equal("SELECT * FROM T WHERE Name = @Name AND Id IN (@Ids_0, @Ids_1)", sql);
            Assert.Equal(3, parameters.Length);
            Assert.Contains(parameters, p => p.ParameterName == "@Name" && (string)p.Value == "Tom");
            Assert.Contains(parameters, p => p.ParameterName == "@Ids_0" && (int)p.Value == 1);
            Assert.Contains(parameters, p => p.ParameterName == "@Ids_1" && (int)p.Value == 2);
        }

        [Fact]
        public void Convert_CollectionExpansion_PlaceholderAtSqlStart_NoParentheses()
        {
            var dict = new Dictionary<string, object> { { "Ids", new[] { 1, 2 } } };

            // 占位符位于 SQL 起始（前方无任何单词）：不补括号
            string sql = "@Ids";
            DbAccess.ConvertToDbParameters(dict, ref sql);

            Assert.Equal("@Ids_0, @Ids_1", sql);
        }

        [Fact]
        public void Convert_CollectionExpansion_ParentWordIsNotKeywordIn_NoParentheses()
        {
            var dict = new Dictionary<string, object> { { "Ids", new[] { 1 } } };

            // 前置单词段为 2 个字母且恰为 IN，但其前是标识符字符（下划线）：词边界保护，不补括号
            string sql = "SELECT * FROM T WHERE Val_IN @Ids";
            DbAccess.ConvertToDbParameters(dict, ref sql);

            Assert.Equal("SELECT * FROM T WHERE Val_IN @Ids_0", sql);
        }

        [Fact]
        public void Convert_UnicodeKeyName_WordBoundaryMatch()
        {
            var dict = new Dictionary<string, object> { { "名称", "值" } };

            // 中文参数名的词边界判断按 Unicode 字母处理
            string sql = "SELECT * FROM T WHERE 名称 = @名称";
            var parameters = DbAccess.ConvertToDbParameters(dict, ref sql);

            Assert.Single(parameters);
            Assert.Equal("@名称", parameters[0].ParameterName);
            Assert.Equal("值", parameters[0].Value);
        }
    }
}
