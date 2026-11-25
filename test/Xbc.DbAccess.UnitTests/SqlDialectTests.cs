using System;
using System.Data;
using Xbc.DbAccess;
using Xbc.DbAccess.MySql;
using Xbc.DbAccess.Npgsql;
using Xbc.DbAccess.Oracle;
using Xbc.DbAccess.Sqlite;
using Xbc.DbAccess.SqlServer;
using Xunit;

namespace Xbc.DbAccess.UnitTests
{
    /// <summary>
    /// ISqlDialect 接口的单元测试。
    /// </summary>
    public class SqlDialectTests
    {
        #region SqliteDialect Tests

        public class SqliteDialectTests
        {
            [Fact]
            public void QuoteIdentifier_普通标识符_应使用双引号包裹()
            {
                var dialect = SqliteDialect.Instance;
                var result = dialect.QuoteIdentifier("Users");
                Assert.Equal("\"Users\"", result);
            }

            [Theory]
            [InlineData("table_name")]
            [InlineData("ColumnName")]
            [InlineData("schema.table")]
            public void QuoteIdentifier_各种标识符_应正确引用(string identifier)
            {
                var dialect = SqliteDialect.Instance;
                var result = dialect.QuoteIdentifier(identifier);
                Assert.Equal($"\"{identifier}\"", result);
            }

            /// <summary>
            /// 标识符内嵌双引号时必须双写转义，
            /// 否则生成提前闭合的非法标识符。
            /// </summary>
            [Theory]
            [InlineData("a\"b", "\"a\"\"b\"")]
            [InlineData("\"", "\"\"\"\"")]
            public void QuoteIdentifier_含双引号_应双写转义(string identifier, string expected)
            {
                var dialect = SqliteDialect.Instance;
                var result = dialect.QuoteIdentifier(identifier);
                Assert.Equal(expected, result);
            }

            /// <summary>
            /// RETURNING 子句自 SQLite 3.35.0 起支持：版本满足门槛返回 OutputClause，
            /// 版本不满足或未知（null、未解析）时保守降级为 SelectLastInsertId（SELECT last_insert_rowid() 路径）。
            /// </summary>
            [Theory]
            [InlineData(null, 0, 0, InsertIdentityMode.SelectLastInsertId)]// serverInfo 为 null
            [InlineData(0, 0, 0, InsertIdentityMode.SelectLastInsertId)]// 版本探测失败（MajorVersion 为 0）
            [InlineData(3, 34, 1, InsertIdentityMode.SelectLastInsertId)]// 3.34.1 低于门槛（如 Debian 11 系统 libsqlite3）
            [InlineData(3, 35, 0, InsertIdentityMode.OutputClause)]// 3.35.0 门槛边界
            [InlineData(3, 44, 2, InsertIdentityMode.OutputClause)]// 3.44.2（Microsoft.Data.Sqlite 8.x 自带引擎）
            [InlineData(4, 0, 0, InsertIdentityMode.OutputClause)]// 4.x 假想未来版本
            public void GetInsertIdentityMode_按引擎版本判定(int? major, int minor, int revision, InsertIdentityMode expected)
            {
                var dialect = SqliteDialect.Instance;
                DbServerInfo serverInfo = major.HasValue
                    ? new DbServerInfo { MajorVersion = major.Value, MinorVersion = minor, RevisionVersion = revision }
                    : null;

                Assert.Equal(expected, dialect.GetInsertIdentityMode(serverInfo));
            }

            /// <summary>
            /// LIMIT 家族方言使用 LIMIT/OFFSET 语法分页（自古支持），不支持 OFFSET/FETCH 标准语法，
            /// 返回 false 由各自构建器重写分页构建。
            /// </summary>
            [Fact]
            public void SupportsOffsetFetch_Limit语法方言_应返回False()
            {
                Assert.False(MySqlDialect.Instance.SupportsOffsetFetch(null));
                Assert.False(NpgsqlDialect.Instance.SupportsOffsetFetch(null));
                Assert.False(SqliteDialect.Instance.SupportsOffsetFetch(null));
            }

            [Fact]
            public void GetIdentitySelectSql_给定列名_应返回正确的SQL()
            {
                var dialect = SqliteDialect.Instance;
                var result = dialect.SelectLastInsertId("Id");
                Assert.Equal("SELECT LAST_INSERT_ROWID() AS \"Id\"", result);
            }

            [Theory]
            [InlineData("ID")]
            [InlineData("UserId")]
            [InlineData("order_id")]
            public void GetIdentitySelectSql_各种列名_应正确嵌入(string columnName)
            {
                var dialect = SqliteDialect.Instance;
                var result = dialect.SelectLastInsertId(columnName);
                Assert.Contains($"\"{columnName}\"", result);
                Assert.Contains("LAST_INSERT_ROWID()", result);
            }

            [Fact]
            public void Instance_多次获取_应是同一实例()
            {
                var instance1 = SqliteDialect.Instance;
                var instance2 = SqliteDialect.Instance;
                Assert.Same(instance1, instance2);
            }

            /// <summary>
            /// 引擎版本随包依赖锁定（System.Data.SQLite >= 1.0.116 捆绑 SQLite 3.39+），
            /// 参数上限直接取 3.32.0 起的默认值 32766。
            /// </summary>
            [Fact]
            public void MaxParameterCount_应为32766()
            {
                Assert.Equal(32766, SqliteDialect.Instance.MaxParameterCount);
            }

            /// <summary>
            /// 批量插入逐条策略：仅 SQLite（进程内引擎，多值 INSERT 的语句准备开销高于逐条语句的复用执行）启用，
            /// 网络型数据库保持多值 INSERT（摊薄网络往返）。
            /// </summary>
            [Fact]
            public void PreferRowByRowBatchInsert_仅SQLite启用()
            {
                Assert.True(SqliteDialect.Instance.PreferRowByRowBatchInsert);
                Assert.False(SqlServerDialect.Instance.PreferRowByRowBatchInsert);
                Assert.False(MySqlDialect.Instance.PreferRowByRowBatchInsert);
                Assert.False(NpgsqlDialect.Instance.PreferRowByRowBatchInsert);
                Assert.False(OracleDialect.Instance.PreferRowByRowBatchInsert);
            }

            /// <summary>
            /// Second 日期部分提取为整秒语义（strftime('%S') 无小数秒），与五方言的整秒语义保持一致。
            /// </summary>
            [Fact]
            public void GetDatePartExpression_Second_应为整秒语义()
            {
                Assert.Equal("CAST(strftime('%S', \"CreatedAt\") AS INTEGER)", SqliteDialect.Instance.GetDatePartExpression("\"CreatedAt\"", DatePart.Second));
            }

            /// <summary>
            /// AVG 聚合表达式使用默认形式：SQLite 的 AVG 恒返回浮点数值，无整数截断问题。
            /// </summary>
            [Fact]
            public void BuildAverageExpression_应为默认Avg形式()
            {
                Assert.Equal("AVG(\"Amount\")", SqliteDialect.Instance.BuildAverageExpression("\"Amount\""));
            }
        }

        #endregion

        #region SqlDialectExtensions Tests

        /// <summary>
        /// 分批决策纯函数的单元测试：多值 INSERT 分批与 IN 子句键值分批的上限计算。
        /// </summary>
        public class SqlDialectExtensionsTests
        {
            [Theory]
            [InlineData(3, 696)]   // SQL Server：参数上限主导（2090 / 3 = 696 < 1000 行上限）
            [InlineData(2090, 1)]  // 每行参数数恰达上限：1 行
            [InlineData(3000, 1)]  // 每行参数数超上限：钳制到至少 1 行
            public void GetMaxRowsPerInsertBatch_SqlServer_参数上限主导(int parameterCountPerRow, int expected)
            {
                Assert.Equal(expected, SqlServerDialect.Instance.GetMaxRowsPerInsertBatch(parameterCountPerRow));
            }

            [Theory]
            [InlineData(3, 500)]   // SQLite：单语句 VALUES 行数上限主导（32766 / 3 = 10922 > 500）
            [InlineData(100, 327)] // 参数上限主导（32766 / 100 = 327 < 500）
            public void GetMaxRowsPerInsertBatch_Sqlite_行数上限主导(int parameterCountPerRow, int expected)
            {
                Assert.Equal(expected, SqliteDialect.Instance.GetMaxRowsPerInsertBatch(parameterCountPerRow));
            }

            [Fact]
            public void GetMaxRowsPerInsertBatch_Oracle_退化单行插入()
            {
                // Oracle 23c 前不支持多值 VALUES，行数上限 1 恒主导
                Assert.Equal(1, OracleDialect.Instance.GetMaxRowsPerInsertBatch(5));
            }

            [Fact]
            public void GetMaxRowsPerInsertBatch_无参数列_仅受行数上限约束()
            {
                // 全部为自增列的退化场景（每行 0 参数）不受参数数量限制
                Assert.Equal(500, SqliteDialect.Instance.GetMaxRowsPerInsertBatch(0));
                Assert.Equal(1000, SqlServerDialect.Instance.GetMaxRowsPerInsertBatch(0));
            }

            [Fact]
            public void GetMaxRowsPerInsertBatch_负数每行参数数_抛出ArgumentOutOfRangeException()
            {
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => SqliteDialect.Instance.GetMaxRowsPerInsertBatch(-1));
            }

            [Fact]
            public void GetMaxRowsPerInsertBatch_Null方言_抛出ArgumentNullException()
            {
                Assert.Throws<ArgumentNullException>(
                    () => ((ISqlDialect)null).GetMaxRowsPerInsertBatch(3));
            }

            [Fact]
            public void GetMaxKeysPerInClauseBatch_等于参数上限()
            {
                // IN 子句每个键值占 1 个参数，分批上限即参数数量上限
                Assert.Equal(32766, SqliteDialect.Instance.GetMaxKeysPerInClauseBatch());
                Assert.Equal(2090, SqlServerDialect.Instance.GetMaxKeysPerInClauseBatch());
            }

            [Fact]
            public void GetMaxKeysPerInClauseBatch_Null方言_抛出ArgumentNullException()
            {
                Assert.Throws<ArgumentNullException>(
                    () => ((ISqlDialect)null).GetMaxKeysPerInClauseBatch());
            }
        }

        #endregion

        #region SqlServerDialect Tests

        public class SqlServerDialectTests
        {
            [Fact]
            public void QuoteIdentifier_普通标识符_应使用方括号包裹()
            {
                var dialect = SqlServerDialect.Instance;
                var result = dialect.QuoteIdentifier("Users");
                Assert.Equal("[Users]", result);
            }

            [Theory]
            [InlineData("table_name")]
            [InlineData("ColumnName")]
            public void QuoteIdentifier_各种标识符_应正确引用(string identifier)
            {
                var dialect = SqlServerDialect.Instance;
                var result = dialect.QuoteIdentifier(identifier);
                Assert.Equal($"[{identifier}]", result);
            }

            /// <summary>
            /// 标识符内嵌 <c>]</c> 时必须双写转义，
            /// 否则生成 <c>[a]b]</c> 这类提前闭合的非法标识符。
            /// </summary>
            [Theory]
            [InlineData("a]b", "[a]]b]")]
            [InlineData("]", "[]]]")]
            [InlineData("a]b]c", "[a]]b]]c]")]
            public void QuoteIdentifier_含右方括号_应双写转义(string identifier, string expected)
            {
                var dialect = SqlServerDialect.Instance;
                var result = dialect.QuoteIdentifier(identifier);
                Assert.Equal(expected, result);
            }

            [Fact]
            public void QuoteTableName_无架构_应仅引用表名()
            {
                var dialect = SqlServerDialect.Instance;
                var result = dialect.QuoteTableName(null, "Users");
                Assert.Equal("[Users]", result);
            }

            [Fact]
            public void QuoteTableName_含架构_应引用架构与表名()
            {
                var dialect = SqlServerDialect.Instance;
                var result = dialect.QuoteTableName("dbo", "Users");
                Assert.Equal("[dbo].[Users]", result);
            }

            [Fact]
            public void SupportsSchema_应返回True()
            {
                Assert.True(SqlServerDialect.Instance.SupportsSchema);
            }

            /// <summary>
            /// OUTPUT 子句自 SQL Server 2005（版本 9.0）起支持：版本满足门槛返回 OutputClause；
            /// 仅 SQL Server 2000 及以下（版本 8）降级为 SelectLastInsertId（SCOPE_IDENTITY 路径）；
            /// 版本未知（null、未解析）时同样选择 OUTPUT（显式失败优于静默返回 NULL 的备用路径）。
            /// </summary>
            [Theory]
            [InlineData(null, InsertIdentityMode.OutputClause)]// serverInfo 为 null
            [InlineData(0, InsertIdentityMode.OutputClause)]// 版本探测失败（MajorVersion 为 0）
            [InlineData(8, InsertIdentityMode.SelectLastInsertId)]// SQL Server 2000，低于门槛
            [InlineData(9, InsertIdentityMode.OutputClause)]// SQL Server 2005（门槛边界）
            [InlineData(16, InsertIdentityMode.OutputClause)]// SQL Server 2022
            public void GetInsertIdentityMode_按引擎版本判定(int? major, InsertIdentityMode expected)
            {
                DbServerInfo serverInfo = major.HasValue ? new DbServerInfo { MajorVersion = major.Value } : null;
                Assert.Equal(expected, SqlServerDialect.Instance.GetInsertIdentityMode(serverInfo));
            }

            /// <summary>
            /// OFFSET/FETCH 自 SQL Server 2012（版本 11.0）起支持：版本满足门槛返回 true，
            /// 版本不满足或未知（null、未解析）时保守返回 false，由基类分页模板降级为 ROW_NUMBER。
            /// </summary>
            [Theory]
            [InlineData(null, false)]// serverInfo 为 null
            [InlineData(0, false)]// 版本探测失败
            [InlineData(10, false)]// SQL Server 2008
            [InlineData(11, true)]// SQL Server 2012（门槛边界）
            [InlineData(16, true)]// SQL Server 2022
            public void SupportsOffsetFetch_按引擎版本判定(int? major, bool expected)
            {
                DbServerInfo serverInfo = major.HasValue ? new DbServerInfo { MajorVersion = major.Value } : null;
                Assert.Equal(expected, SqlServerDialect.Instance.SupportsOffsetFetch(serverInfo));
            }

            [Fact]
            public void MaxParameterCount_应为2090并预留内部参数余量()
            {
                // 引擎上限 2100 为 RPC 请求总参数量（含 sp_executesql 等内部参数），需预留余量避免恰好打满时报错
                Assert.Equal(2090, SqlServerDialect.Instance.MaxParameterCount);
            }

            [Fact]
            public void MaxRowsPerInsertValues_应为1000()
            {
                Assert.Equal(1000, SqlServerDialect.Instance.MaxRowsPerInsertValues);
            }

            [Fact]
            public void GetIdentitySelectSql_给定列名_应返回正确的SQL()
            {
                var dialect = SqlServerDialect.Instance;
                var result = dialect.SelectLastInsertId("Id");
                Assert.Equal("SELECT SCOPE_IDENTITY() AS [Id]", result);
            }

            /// <summary>
            /// Second 日期部分提取为整秒语义（DATEPART(SECOND) 无小数秒），作为 PG/Oracle 整秒化修复的对照基准。
            /// </summary>
            [Fact]
            public void GetDatePartExpression_Second_应为整秒语义()
            {
                Assert.Equal("DATEPART(SECOND, [CreatedAt])", SqlServerDialect.Instance.GetDatePartExpression("[CreatedAt]", DatePart.Second));
            }

            /// <summary>
            /// AVG 聚合表达式应无条件 CAST 为 DECIMAL(38,6)：SQL Server 的 AVG 继承参数的整数类型
            ///（引擎行为：AVG(int) 返回 int，1 与 2 的均值为 1 而非 1.5），CAST 恢复与其余方言一致的精确均值语义；
            /// decimal 列升位 DECIMAL(38,6) 无损（默认精度低于 38）。
            /// </summary>
            [Fact]
            public void BuildAverageExpression_应包裹DecimalCast()
            {
                Assert.Equal("AVG(CAST([Amount] AS DECIMAL(38,6)))", SqlServerDialect.Instance.BuildAverageExpression("[Amount]"));
            }

            [Fact]
            public void Instance_多次获取_应是同一实例()
            {
                var instance1 = SqlServerDialect.Instance;
                var instance2 = SqlServerDialect.Instance;
                Assert.Same(instance1, instance2);
            }
        }

        /// <summary>
        /// SqlServer 构建器 BuildInsertSql 的单元测试（IDENTITY_INSERT 包裹）。
        /// </summary>
        public class SqlServerSqlBuilderTests
        {
            /// <summary>
            /// StringBuilder.AppendLine 按平台产生换行（Windows 为 \r\n），规范化为 \n 便于断言。
            /// </summary>
            private static string NormalizeNewlines(string sql) => sql.Replace("\r\n", "\n");

            /// <summary>
            /// 自增列带显式值（进入列清单）时，语句以 SET IDENTITY_INSERT ON/OFF 包裹（同命令多语句批处理）。
            /// </summary>
            [Fact]
            public void BuildInsertSql_自增列带显式值_应以IdentityInsert包裹()
            {
                var sql = SqlServerSqlBuilder.Instance.BuildInsertSql(
                    "[Users]",
                    new List<string> { "[Id]", "[Name]", "[Age]" },
                    new List<string> { "@Id", "@Name", "@Age" },
                    InsertIdentityContext.ForExplicit("[Id]"));

                Assert.Equal(
                    "SET IDENTITY_INSERT [Users] ON;\n" +
                    "INSERT INTO [Users]\n" +
                    "    ([Id], [Name], [Age])\n" +
                    "VALUES\n" +
                    "    (@Id, @Name, @Age)\n" +
                    ";\n" +
                    "SET IDENTITY_INSERT [Users] OFF;\n",
                    NormalizeNewlines(sql));
            }

            /// <summary>
            /// 无自增列参与上下文（自增列为默认值被排除出列清单，或无自增列）时不包裹：包裹仅在显式写入形态（列在清单中）时需要。
            /// </summary>
            [Fact]
            public void BuildInsertSql_无自增列上下文_不应包裹()
            {
                var sql = SqlServerSqlBuilder.Instance.BuildInsertSql(
                    "[Users]",
                    new List<string> { "[Name]", "[Age]" },
                    new List<string> { "@Name", "@Age" });

                Assert.Equal(
                    "INSERT INTO [Users]\n" +
                    "    ([Name], [Age])\n" +
                    "VALUES\n" +
                    "    (@Name, @Age)\n",
                    NormalizeNewlines(sql));
            }
        }

        #endregion

        #region MySqlDialect Tests

        public class MySqlDialectTests
        {
            [Fact]
            public void QuoteIdentifier_普通标识符_应使用反引号包裹()
            {
                var dialect = MySqlDialect.Instance;
                var result = dialect.QuoteIdentifier("Users");
                Assert.Equal("`Users`", result);
            }

            /// <summary>
            /// 标识符内嵌反引号时必须双写转义，
            /// 否则生成提前闭合的非法标识符。
            /// </summary>
            [Theory]
            [InlineData("a`b", "`a``b`")]
            [InlineData("`", "````")]
            public void QuoteIdentifier_含反引号_应双写转义(string identifier, string expected)
            {
                var dialect = MySqlDialect.Instance;
                var result = dialect.QuoteIdentifier(identifier);
                Assert.Equal(expected, result);
            }

            [Fact]
            public void GetIdentitySelectSql_给定列名_应返回正确的SQL()
            {
                var dialect = MySqlDialect.Instance;
                var result = dialect.SelectLastInsertId("Id");
                Assert.Equal("SELECT LAST_INSERT_ID() AS `Id`", result);
            }

            [Fact]
            public void MaxParameterCount_应为65535()
            {
                Assert.Equal(65535, MySqlDialect.Instance.MaxParameterCount);
            }

            /// <summary>
            /// Second 日期部分提取为整秒语义（SECOND() 函数无小数秒），作为 PG/Oracle 整秒化修复的对照基准。
            /// </summary>
            [Fact]
            public void GetDatePartExpression_Second_应为整秒语义()
            {
                Assert.Equal("SECOND(`CreatedAt`)", MySqlDialect.Instance.GetDatePartExpression("`CreatedAt`", DatePart.Second));
            }

            /// <summary>
            /// AVG 聚合表达式使用默认形式：MySQL 的 AVG 恒返回精确数值（DECIMAL），无整数截断问题。
            /// </summary>
            [Fact]
            public void BuildAverageExpression_应为默认Avg形式()
            {
                Assert.Equal("AVG(`Amount`)", MySqlDialect.Instance.BuildAverageExpression("`Amount`"));
            }
        }

        #endregion

        #region NpgsqlDialect Tests

        public class NpgsqlDialectTests
        {
            [Fact]
            public void QuoteIdentifier_普通标识符_应使用双引号包裹()
            {
                var dialect = NpgsqlDialect.Instance;
                var result = dialect.QuoteIdentifier("Users");
                Assert.Equal("\"Users\"", result);
            }

            /// <summary>
            /// 标识符内嵌双引号时必须双写转义，
            /// 否则生成提前闭合的非法标识符。
            /// </summary>
            [Theory]
            [InlineData("a\"b", "\"a\"\"b\"")]
            [InlineData("\"", "\"\"\"\"")]
            public void QuoteIdentifier_含双引号_应双写转义(string identifier, string expected)
            {
                var dialect = NpgsqlDialect.Instance;
                var result = dialect.QuoteIdentifier(identifier);
                Assert.Equal(expected, result);
            }

            [Fact]
            public void GetIdentitySelectSql_给定列名_应返回正确的SQL()
            {
                var dialect = NpgsqlDialect.Instance;
                var result = dialect.SelectLastInsertId("Id");
                Assert.Equal("SELECT lastval() AS \"Id\"", result);
            }

            [Fact]
            public void MaxParameterCount_应为32767()
            {
                Assert.Equal(32767, NpgsqlDialect.Instance.MaxParameterCount);
            }

            /// <summary>
            /// Second 日期部分提取应包裹 FLOOR 归一整秒：PostgreSQL 的 EXTRACT(SECOND) 返回含小数秒的 numeric
            ///（如 30.5），与 C# DateTime.Second 的整秒语义分裂（含小数秒的行会命中错误的整秒值），
            /// 包裹后与 SQL Server DATEPART(SECOND)/MySQL SECOND()/SQLite strftime('%S') 的整秒语义一致。
            /// </summary>
            [Fact]
            public void GetDatePartExpression_Second_应包裹Floor归一整秒()
            {
                Assert.Equal("FLOOR(EXTRACT(SECOND FROM \"CreatedAt\"))", NpgsqlDialect.Instance.GetDatePartExpression("\"CreatedAt\"", DatePart.Second));
            }

            /// <summary>
            /// AVG 聚合表达式使用默认形式：PostgreSQL 的 AVG 对整数列返回 numeric（精确数值），无整数截断问题。
            /// </summary>
            [Fact]
            public void BuildAverageExpression_应为默认Avg形式()
            {
                Assert.Equal("AVG(\"Amount\")", NpgsqlDialect.Instance.BuildAverageExpression("\"Amount\""));
            }
        }

        #endregion

        #region OracleDialect Tests

        public class OracleDialectTests
        {
            [Fact]
            public void QuoteIdentifier_普通标识符_应使用双引号包裹()
            {
                var dialect = OracleDialect.Instance;
                var result = dialect.QuoteIdentifier("Users");
                Assert.Equal("\"Users\"", result);
            }

            /// <summary>
            /// 标识符内嵌双引号时必须双写转义，
            /// 否则生成提前闭合的非法标识符。
            /// </summary>
            [Theory]
            [InlineData("a\"b", "\"a\"\"b\"")]
            [InlineData("\"", "\"\"\"\"")]
            public void QuoteIdentifier_含双引号_应双写转义(string identifier, string expected)
            {
                var dialect = OracleDialect.Instance;
                var result = dialect.QuoteIdentifier(identifier);
                Assert.Equal(expected, result);
            }

            /// <summary>
            /// OFFSET/FETCH 自 Oracle 12c（版本 12.0）起支持：版本满足门槛返回 true，
            /// 版本不满足或未知（null、未解析）时保守返回 false，由基类分页模板降级为 ROW_NUMBER。
            /// </summary>
            [Theory]
            [InlineData(null, false)]// serverInfo 为 null
            [InlineData(0, false)]// 版本探测失败
            [InlineData(11, false)]// Oracle 11g
            [InlineData(12, true)]// Oracle 12c（门槛边界）
            [InlineData(19, true)]// Oracle 19c
            public void SupportsOffsetFetch_按引擎版本判定(int? major, bool expected)
            {
                DbServerInfo serverInfo = major.HasValue ? new DbServerInfo { MajorVersion = major.Value } : null;
                Assert.Equal(expected, OracleDialect.Instance.SupportsOffsetFetch(serverInfo));
            }

            /// <summary>
            /// Oracle 不支持通过 SELECT 语句获取自增 ID，调用应抛出 <see cref="NotSupportedException"/>。
            /// </summary>
            [Fact]
            public void GetIdentitySelectSql_应抛出NotSupportedException()
            {
                Assert.Throws<NotSupportedException>(() => OracleDialect.Instance.SelectLastInsertId("Id"));
            }

            [Fact]
            public void MaxRowsPerInsertValues_应为1()
            {
                Assert.Equal(1, OracleDialect.Instance.MaxRowsPerInsertValues);
            }

            /// <summary>
            /// Oracle 使用 RETURNING INTO 输出参数方式取回自增 ID，与引擎版本无关。
            /// </summary>
            [Theory]
            [InlineData(null)]// serverInfo 为 null
            [InlineData(11)]// Oracle 11g
            [InlineData(19)]// Oracle 19c
            public void GetInsertIdentityMode_始终返回ReturningInto(int? major)
            {
                DbServerInfo serverInfo = major.HasValue ? new DbServerInfo { MajorVersion = major.Value } : null;
                Assert.Equal(InsertIdentityMode.ReturningInto, OracleDialect.Instance.GetInsertIdentityMode(serverInfo));
            }

            /// <summary>
            /// Second 日期部分提取应包裹 TRUNC 归一整秒：Oracle 的 EXTRACT(SECOND FROM TIMESTAMP) 返回含小数秒的值
            ///（如 30.5），与 C# DateTime.Second 的整秒语义分裂（含小数秒的行会命中错误的整秒值）；
            /// 秒域非负，向零截断与取整等价，包裹后与 SQL Server DATEPART(SECOND)/MySQL SECOND()/SQLite strftime('%S')
            /// 的整秒语义一致（CAST AS TIMESTAMP 维持既有形态：对 DATE 列的 SECOND 提取要求 TIMESTAMP 来源）。
            /// </summary>
            [Fact]
            public void GetDatePartExpression_Second_应包裹Trunc归一整秒()
            {
                Assert.Equal("TRUNC(EXTRACT(SECOND FROM CAST(\"CreatedAt\" AS TIMESTAMP)))", OracleDialect.Instance.GetDatePartExpression("\"CreatedAt\"", DatePart.Second));
            }

            /// <summary>
            /// AVG 聚合表达式使用默认形式：Oracle 的 AVG 恒返回精确数值（NUMBER），无整数截断问题。
            /// </summary>
            [Fact]
            public void BuildAverageExpression_应为默认Avg形式()
            {
                Assert.Equal("AVG(\"Amount\")", OracleDialect.Instance.BuildAverageExpression("\"Amount\""));
            }
        }

        public class OracleSqlBuilderTests
        {
            /// <summary>
            /// 提供自增 ID 取回上下文时，INSERT 语句尾部应生成 RETURNING INTO 子句（标识列 + 输出参数占位符）。
            /// </summary>
            [Fact]
            public void BuildInsertSql_输出自增ID_应生成ReturningInto子句()
            {
                var sql = OracleSqlBuilder.Instance.BuildInsertSql(
                    "\"Users\"",
                    new List<string> { "\"Name\"", "\"Age\"" },
                    new List<string> { ":Name", ":Age" },
                    InsertIdentityContext.ForRetrieval("\"Id\"", ":out_Id"));

                Assert.Contains("INSERT INTO \"Users\"", sql);
                Assert.Contains("(:Name, :Age)", sql);
                Assert.Contains("RETURNING \"Id\" INTO :out_Id", sql);
            }

            /// <summary>
            /// 未启用自增 ID 输出时，应生成不含 RETURNING INTO 子句的普通 INSERT 语句。
            /// </summary>
            [Fact]
            public void BuildInsertSql_未输出自增ID_应为普通Insert()
            {
                var sql = OracleSqlBuilder.Instance.BuildInsertSql(
                    "\"Users\"",
                    new List<string> { "\"Name\"" },
                    new List<string> { ":Name" });

                Assert.Contains("INSERT INTO \"Users\"", sql);
                Assert.DoesNotContain("RETURNING", sql);
            }

            /// <summary>
            /// RETURNING INTO 依赖标识列名与输出参数名生成合法子句，缺失时显式抛出
            /// <see cref="ArgumentException"/> 优于静默生成必然执行失败的 SQL。
            /// </summary>
            [Theory]
            [InlineData(null, ":out_Id")]// 缺标识列名
            [InlineData("\"Id\"", null)]// 缺输出参数名
            public void BuildInsertSql_输出自增ID但缺少必要参数_应抛出ArgumentException(string? identityColumn, string? outputParameterName)
            {
                Assert.Throws<ArgumentException>(() => OracleSqlBuilder.Instance.BuildInsertSql(
                    "\"Users\"",
                    new List<string> { "\"Name\"" },
                    new List<string> { ":Name" },
                    InsertIdentityContext.ForRetrieval(identityColumn, outputParameterName)));
            }

            /// <summary>
            /// 列数与值数不匹配时应抛出 <see cref="ArgumentException"/>。
            /// </summary>
            [Fact]
            public void BuildInsertSql_列数与值数不匹配_应抛出ArgumentException()
            {
                Assert.Throws<ArgumentException>(() => OracleSqlBuilder.Instance.BuildInsertSql(
                    "\"Users\"",
                    new List<string> { "\"Name\"", "\"Age\"" },
                    new List<string> { ":Name" }));
            }
        }

        /// <summary>
        /// Oracle 保存点方言的单元测试：RELEASE SAVEPOINT 能力声明与防御实现。
        /// </summary>
        public class OracleSavepointSqlDialectTests
        {
            /// <summary>
            /// Oracle 不支持 RELEASE SAVEPOINT（官方 SQL Language Reference 无此语法：保存点仅由事务提交/回滚
            /// 统一清理，执行该语句即报 ORA-00900 无效 SQL），能力声明为 false 后框架跳过释放。
            /// </summary>
            [Fact]
            public void IsReleaseSavepointSupported_应为False()
            {
                Assert.False(OracleSavepointSqlDialect.Instance.IsReleaseSavepointSupported);
            }

            /// <summary>
            /// ReleaseSavepoint 为接口契约的防御实现（运行路径不触达：框架对不支持的方言跳过释放），
            /// 误用时显式抛出优于静默生成必然失败的 SQL。
            /// </summary>
            [Fact]
            public void ReleaseSavepoint_应抛出NotSupportedException()
            {
                Assert.Throws<NotSupportedException>(() => OracleSavepointSqlDialect.Instance.ReleaseSavepoint("SP1"));
            }

            /// <summary>
            /// 保存点创建与回滚语句应保持既有语法（SAVEPOINT / ROLLBACK TO SAVEPOINT）。
            /// </summary>
            [Fact]
            public void CreateAndRollbackSavepoint_应保持既有语法()
            {
                Assert.Equal("SAVEPOINT SP1", OracleSavepointSqlDialect.Instance.CreateSavepoint("SP1"));
                Assert.Equal("ROLLBACK TO SAVEPOINT SP1", OracleSavepointSqlDialect.Instance.RollbackToSavepoint("SP1"));
            }
        }

        /// <summary>
        /// OracleParameterBuilder 的单元测试：bool 参数值的 1/0 归一
        ///（ODP.NET 会把 bool 绑定为 PL/SQL BOOLEAN，与 NUMBER(1) 布尔列比较抛 ORA-00932）。
        /// </summary>
        public class OracleParameterBuilderTests
        {
            /// <summary>
            /// bool 参数值应归一为 decimal 的 1/0：与 NUMBER 布尔列域一致，读路径对 NUMBER 布尔列按非零转 true 已兼容。
            /// </summary>
            [Theory]
            [InlineData(true, 1)]
            [InlineData(false, 0)]
            public void CreateParameter_布尔值_应归一为decimal的1或0(bool value, decimal expected)
            {
                var parameter = OracleParameterBuilder.Instance.CreateParameter("Active", value);

                Assert.Equal(":Active", parameter.ParameterName);
                Assert.Equal(expected, parameter.Value);
                Assert.Equal(typeof(decimal), parameter.Value.GetType());
            }

            /// <summary>
            /// 显式配置的 DbType.Boolean 应随归一剥离：ODP.NET 对 Boolean DbType 同样映射 PL/SQL BOOLEAN，
            /// 不剥离会使参数类型回到 BOOLEAN 域。
            /// </summary>
            [Fact]
            public void CreateParameter_布尔值带BooleanDbType_应剥离DbType()
            {
                var parameter = OracleParameterBuilder.Instance.CreateParameter("Active", true, DbType.Boolean);

                Assert.Equal(1m, parameter.Value);
                Assert.NotEqual(DbType.Boolean, parameter.DbType);
            }

            /// <summary>
            /// 非 bool 值不经归一路径（原样到达底层参数，保持既有行为）。
            /// </summary>
            [Fact]
            public void CreateParameter_非布尔值_应保持原值()
            {
                var parameter = OracleParameterBuilder.Instance.CreateParameter("Age", 20);

                Assert.Equal(20, parameter.Value);
            }
        }

        #endregion

        #region 会话状态重置命令 Tests

        /// <summary>
        /// 各方言的会话状态重置命令契约（回池时按序执行，任一失败连接销毁）：
        /// SQL Server 用 sp_reset_connection 一次性复位；PostgreSQL 用 DISCARD ALL；
        /// MySQL 用 ROLLBACK 兜底 + 常用会话变量复位（无原生等价机制）；
        /// Oracle（临时表为全局临时表对象、事务隔离为事务级设置）与 SQLite（无会话级可变状态）默认空序列。
        /// </summary>
        public class SessionStateResetCommandsTests
        {
            [Fact]
            public void SqlServerDialect_应返回sp_reset_connection()
            {
                Assert.Equal(new[] { "EXEC sp_reset_connection" }, SqlServerDialect.Instance.SessionStateResetCommands);
            }

            [Fact]
            public void NpgsqlDialect_应返回DiscardAll()
            {
                Assert.Equal(new[] { "DISCARD ALL" }, NpgsqlDialect.Instance.SessionStateResetCommands);
            }

            [Fact]
            public void MySqlDialect_应返回Rollback与会话变量复位()
            {
                Assert.Equal(new[] { "ROLLBACK", "SET SESSION sql_mode = DEFAULT, SESSION time_zone = DEFAULT" },
                    MySqlDialect.Instance.SessionStateResetCommands);
            }

            [Fact]
            public void OracleDialect_默认空序列()
            {
                Assert.Empty(OracleDialect.Instance.SessionStateResetCommands);
            }

            [Fact]
            public void SqliteDialect_默认空序列()
            {
                Assert.Empty(SqliteDialect.Instance.SessionStateResetCommands);
            }
        }

        #endregion
    }
}
