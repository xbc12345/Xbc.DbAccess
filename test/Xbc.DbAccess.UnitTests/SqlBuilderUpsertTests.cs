using Xbc.DbAccess.MySql;
using Xbc.DbAccess.Npgsql;
using Xbc.DbAccess.Oracle;
using Xbc.DbAccess.Sqlite;
using Xbc.DbAccess.SqlServer;
using Xunit;

namespace Xbc.DbAccess.UnitTests
{
    /// <summary>
    /// 五方言 SQL 构建器 BuildUpsertSql 的单元测试（SQL 形状断言）。
    /// </summary>
    public class SqlBuilderUpsertTests
    {
        /// <summary>
        /// 未重写 BuildUpsertSqlCore 的构建器，用于验证基类默认抛出 NotSupportedException。
        /// </summary>
        private sealed class StubSqlBuilder : SqlBuilderBase
        {
            public override ISqlDialect SqlDialect => SqliteDialect.Instance;
        }

        /// <summary>
        /// StringBuilder.AppendLine 按平台产生换行（Windows 为 \r\n），规范化为 \n 便于断言。
        /// </summary>
        private static string NormalizeNewlines(string sql) => sql.Replace("\r\n", "\n");

        public class SqliteSqlBuilderTests
        {
            [Fact]
            public void BuildUpsertSql_无自增上下文_应生成OnConflictDoUpdate()
            {
                var sql = SqliteSqlBuilder.Instance.BuildUpsertSql(
                    "\"Users\"",
                    new List<string> { "\"Id\"", "\"Name\"", "\"Age\"" },
                    new List<string> { "@Id", "@Name", "@Age" },
                    "\"Id\"");

                Assert.Equal(
                    "INSERT INTO \"Users\"\n" +
                    "    (\"Id\", \"Name\", \"Age\")\n" +
                    "VALUES\n" +
                    "    (@Id, @Name, @Age)\n" +
                    "ON CONFLICT(\"Id\") DO UPDATE SET \"Name\" = excluded.\"Name\", \"Age\" = excluded.\"Age\"\n",
                    NormalizeNewlines(sql));
            }

            [Fact]
            public void BuildUpsertSql_自增上下文_应生成Returning()
            {
                var identity = InsertIdentityContext.ForRetrieval("\"Id\"");

                var sql = SqliteSqlBuilder.Instance.BuildUpsertSql(
                    "\"Users\"",
                    new List<string> { "\"Name\"", "\"Age\"" },
                    new List<string> { "@Name", "@Age" },
                    "\"Id\"",
                    identity);

                Assert.Equal(
                    "INSERT INTO \"Users\"\n" +
                    "    (\"Name\", \"Age\")\n" +
                    "VALUES\n" +
                    "    (@Name, @Age)\n" +
                    "ON CONFLICT(\"Id\") DO UPDATE SET \"Name\" = excluded.\"Name\", \"Age\" = excluded.\"Age\"\n" +
                    "RETURNING \"Id\"\n",
                    NormalizeNewlines(sql));
            }
        }

        public class NpgsqlSqlBuilderTests
        {
            [Fact]
            public void BuildUpsertSql_自增上下文_应生成Returning()
            {
                var identity = InsertIdentityContext.ForRetrieval("\"Id\"");

                var sql = NpgsqlSqlBuilder.Instance.BuildUpsertSql(
                    "\"Users\"",
                    new List<string> { "\"Id\"", "\"Name\"", "\"Age\"" },
                    new List<string> { "@Id", "@Name", "@Age" },
                    "\"Id\"",
                    identity);

                Assert.Equal(
                    "INSERT INTO \"Users\"\n" +
                    "    (\"Id\", \"Name\", \"Age\")\n" +
                    "VALUES\n" +
                    "    (@Id, @Name, @Age)\n" +
                    "ON CONFLICT (\"Id\") DO UPDATE SET \"Name\" = EXCLUDED.\"Name\", \"Age\" = EXCLUDED.\"Age\"\n" +
                    "RETURNING \"Id\"\n",
                    NormalizeNewlines(sql));
            }
        }

        public class MySqlSqlBuilderTests
        {
            [Fact]
            public void BuildUpsertSql_无自增上下文_应生成OnDuplicateKeyUpdate()
            {
                var sql = MySqlSqlBuilder.Instance.BuildUpsertSql(
                    "`Users`",
                    new List<string> { "`Id`", "`Name`", "`Age`" },
                    new List<string> { "@Id", "@Name", "@Age" },
                    "`Id`");

                Assert.Equal(
                    "INSERT INTO `Users`\n" +
                    "    (`Id`, `Name`, `Age`)\n" +
                    "VALUES\n" +
                    "    (@Id, @Name, @Age)\n" +
                    "ON DUPLICATE KEY UPDATE `Name` = VALUES(`Name`), `Age` = VALUES(`Age`)\n",
                    NormalizeNewlines(sql));
            }

            /// <summary>
            /// 自增主键为默认值的 Upsert 由调用方直接走插入路径取回（不构造取回上下文）：
            /// 构建器对取回上下文一概忽略，不生成任何取回子句。
            /// </summary>
            [Fact]
            public void BuildUpsertSql_自增上下文_应忽略不生成取回子句()
            {
                var identity = InsertIdentityContext.ForRetrieval("`Id`");

                var sql = MySqlSqlBuilder.Instance.BuildUpsertSql(
                    "`Users`",
                    new List<string> { "`Id`", "`Name`", "`Age`" },
                    new List<string> { "@Id", "@Name", "@Age" },
                    "`Id`",
                    identity);

                Assert.Equal(
                    "INSERT INTO `Users`\n" +
                    "    (`Id`, `Name`, `Age`)\n" +
                    "VALUES\n" +
                    "    (@Id, @Name, @Age)\n" +
                    "ON DUPLICATE KEY UPDATE `Name` = VALUES(`Name`), `Age` = VALUES(`Age`)\n",
                    NormalizeNewlines(sql));
            }
        }

        public class SqlServerSqlBuilderTests
        {
            [Fact]
            public void BuildUpsertSql_无自增上下文_应生成Merge且以分号结尾()
            {
                var sql = SqlServerSqlBuilder.Instance.BuildUpsertSql(
                    "[Users]",
                    new List<string> { "[Id]", "[Name]", "[Age]" },
                    new List<string> { "@Id", "@Name", "@Age" },
                    "[Id]");

                Assert.Equal(
                    "MERGE INTO [Users] WITH (HOLDLOCK) AS t\n" +
                    "USING (SELECT @Id AS [Id], @Name AS [Name], @Age AS [Age]) AS src\n" +
                    "    ON t.[Id] = src.[Id]\n" +
                    "WHEN MATCHED THEN\n" +
                    "    UPDATE SET t.[Name] = src.[Name], t.[Age] = src.[Age]\n" +
                    "WHEN NOT MATCHED THEN\n" +
                    "    INSERT ([Id], [Name], [Age])\n" +
                    "    VALUES (src.[Id], src.[Name], src.[Age])\n" +
                    ";\n",
                    NormalizeNewlines(sql));
            }

            /// <summary>
            /// 自增主键被排除出插入列时，源表对应列以 NULL 填充（ON 永不匹配，恒走插入路径）。
            /// </summary>
            [Fact]
            public void BuildUpsertSql_自增主键被排除_源表应以Null填充且生成Output()
            {
                var identity = InsertIdentityContext.ForRetrieval("[Id]");

                var sql = SqlServerSqlBuilder.Instance.BuildUpsertSql(
                    "[Users]",
                    new List<string> { "[Name]", "[Age]" },
                    new List<string> { "@Name", "@Age" },
                    "[Id]",
                    identity);

                Assert.Equal(
                    "MERGE INTO [Users] WITH (HOLDLOCK) AS t\n" +
                    "USING (SELECT @Name AS [Name], @Age AS [Age], NULL AS [Id]) AS src\n" +
                    "    ON t.[Id] = src.[Id]\n" +
                    "WHEN MATCHED THEN\n" +
                    "    UPDATE SET t.[Name] = src.[Name], t.[Age] = src.[Age]\n" +
                    "WHEN NOT MATCHED THEN\n" +
                    "    INSERT ([Name], [Age])\n" +
                    "    VALUES (src.[Name], src.[Age])\n" +
                    "OUTPUT INSERTED.[Id];\n",
                    NormalizeNewlines(sql));
            }
        }

        public class OracleSqlBuilderTests
        {
            [Fact]
            public void BuildUpsertSql_应生成Merge且无Output无分号()
            {
                var sql = OracleSqlBuilder.Instance.BuildUpsertSql(
                    "\"Users\"",
                    new List<string> { "\"Id\"", "\"Name\"", "\"Age\"" },
                    new List<string> { ":Id", ":Name", ":Age" },
                    "\"Id\"");

                Assert.Equal(
                    "MERGE INTO \"Users\" t\n" +
                    "USING (SELECT :Id AS \"Id\", :Name AS \"Name\", :Age AS \"Age\" FROM DUAL) src\n" +
                    "ON (t.\"Id\" = src.\"Id\")\n" +
                    "WHEN MATCHED THEN\n" +
                    "    UPDATE SET t.\"Name\" = src.\"Name\", t.\"Age\" = src.\"Age\"\n" +
                    "WHEN NOT MATCHED THEN\n" +
                    "    INSERT (\"Id\", \"Name\", \"Age\")\n" +
                    "    VALUES (src.\"Id\", src.\"Name\", src.\"Age\")\n",
                    NormalizeNewlines(sql));
            }

            /// <summary>
            /// MERGE 不支持 RETURNING INTO：identity 上下文被忽略，不生成任何取回子句；
            /// 自增主键被排除时源表以 NULL 填充（ON 永不匹配，恒走插入路径）。
            /// </summary>
            [Fact]
            public void BuildUpsertSql_自增主键被排除_应忽略Identity且源表以Null填充()
            {
                var identity = InsertIdentityContext.ForRetrieval("\"Id\"", ":out_Id");

                var sql = OracleSqlBuilder.Instance.BuildUpsertSql(
                    "\"Users\"",
                    new List<string> { "\"Name\"", "\"Age\"" },
                    new List<string> { ":Name", ":Age" },
                    "\"Id\"",
                    identity);

                Assert.Equal(
                    "MERGE INTO \"Users\" t\n" +
                    "USING (SELECT :Name AS \"Name\", :Age AS \"Age\", NULL AS \"Id\" FROM DUAL) src\n" +
                    "ON (t.\"Id\" = src.\"Id\")\n" +
                    "WHEN MATCHED THEN\n" +
                    "    UPDATE SET t.\"Name\" = src.\"Name\", t.\"Age\" = src.\"Age\"\n" +
                    "WHEN NOT MATCHED THEN\n" +
                    "    INSERT (\"Name\", \"Age\")\n" +
                    "    VALUES (src.\"Name\", src.\"Age\")\n",
                    NormalizeNewlines(sql));
            }
        }

        public class SqlBuilderBaseTests
        {
            /// <summary>
            /// 未重写 Upsert 钩子的方言显式失败优于静默生成不支持的 SQL。
            /// </summary>
            [Fact]
            public void BuildUpsertSql_未重写钩子_应抛出NotSupportedException()
            {
                var builder = new StubSqlBuilder();

                Assert.Throws<NotSupportedException>(() => builder.BuildUpsertSql(
                    "\"Users\"",
                    new List<string> { "\"Id\"", "\"Name\"" },
                    new List<string> { "@Id", "@Name" },
                    "\"Id\""));
            }

            [Fact]
            public void BuildUpsertSql_值与列数不匹配_应抛出ArgumentException()
            {
                Assert.Throws<ArgumentException>(() => SqliteSqlBuilder.Instance.BuildUpsertSql(
                    "\"Users\"",
                    new List<string> { "\"Id\"", "\"Name\"" },
                    new List<string> { "@Id" },
                    "\"Id\""));
            }

            /// <summary>
            /// 除冲突键外没有可更新的列（仅主键且非自增）时无法生成更新子句，显式失败优于生成非法 SQL。
            /// </summary>
            [Fact]
            public void BuildUpsertSql_仅冲突键列_应抛出ArgumentException()
            {
                Assert.Throws<ArgumentException>(() => SqliteSqlBuilder.Instance.BuildUpsertSql(
                    "\"Users\"",
                    new List<string> { "\"Id\"" },
                    new List<string> { "@Id" },
                    "\"Id\""));
            }
        }
    }
}
