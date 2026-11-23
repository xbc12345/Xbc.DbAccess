using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 查询链单列投影（Select）的 SQLite 真库测试：
    /// 验证投影取数、排序/窗口组合、单行终结语义、列值转换（转换器/枚举/NULL）与 Include 互斥、非映射列守卫，
    /// 以及多列投影各形态（匿名类型/元组/命名类型带参构造/命名类型初始化器）的取数与守卫。
    /// </summary>
    public class EntityQuerySelectTests : EntityTestBase
    {
        #region 测试实体

        [XbcDbEntity.Table("SelectUsers")]
        public class UserEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;

            public int Score { get; set; }

            public string Tag { get; set; }

            // 带自定义转换器的列：投影值应与实体赋值一致（存储值 enc: 前缀被剥掉）
            [XbcDbEntity.Column(ConverterType = typeof(SelectTokenConverter))]
            public string Token { get; set; }

            // 枚举名称字符串列：投影应按名称还原枚举值
            [XbcDbEntity.Column(EnumAsString = true)]
            public SelectUserStatus Status { get; set; }

            [XbcDbEntity.NotMapped]
            public string Note { get; set; }

            // 1:N 导航：Include 与 Select 互斥守卫的测试载体
            [XbcDbEntity.Navigation(ThisKey = "Id", OtherKey = "UserId")]
            public List<OrderEntity> Orders { get; set; }
        }

        [XbcDbEntity.Table("SelectOrders")]
        public class OrderEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int OrderId { get; set; }

            public int UserId { get; set; }

            public string Product { get; set; }
        }

        /// <summary>名称字典序（Nine &lt; One）与数值序相反的枚举，验证投影按名称还原而非数值。</summary>
        public enum SelectUserStatus
        {
            One = 1,
            Nine = 9
        }

        /// <summary>Token 列的模拟转换器：存储值带 enc: 前缀。</summary>
        private sealed class SelectTokenConverter : Xbc.DbAccess.Entity.IDbValueConverter<string>
        {
            public string ConvertFrom(object dbValue) => dbValue == null ? null! : dbValue.ToString()!.Substring(4);
            public object ConvertTo(string value) => value == null ? null! : "enc:" + value;
        }

        /// <summary>命名类型投影的测试载体：无参构造 + 可写属性（初始化器按成员名映射）；带参构造用于按编译器绑定构造函数的位置映射测试。</summary>
        public sealed class UserDto
        {
            public string Name { get; set; }

            public int Score { get; set; }

            // 与列名不同的成员名载体：验证映射按绑定关系（成员名 = u.Name 的绑定）而非名字巧合
            public string Nickname { get; set; }

            public UserDto()
            {
            }

            public UserDto(string name)
            {
                Name = name;
            }
        }

        /// <summary>带参构造投影的测试载体：单构造，按实参位置映射。</summary>
        public sealed class SingleCtorDto
        {
            public string Name { get; }

            public SingleCtorDto(string name)
            {
                Name = name;
            }
        }

        /// <summary>带参构造投影的测试载体：四个构造重载由编译器按实参类型各自消歧（同名属性只读，构造体内赋值）。</summary>
        public sealed class CtorDto
        {
            public string Name { get; }

            public int Score { get; }

            public CtorDto(string name)
            {
                Name = name;
            }

            public CtorDto(int score)
            {
                Score = score;
            }

            public CtorDto(string name, int score)
            {
                Name = name;
                Score = score;
            }

            public CtorDto(int score, string name)
            {
                Score = score;
                Name = name;
            }
        }

        /// <summary>带参构造投影的测试载体：同参个数重载（两个 int / 两个 string），验证编译期按实参类型消歧而非参数个数。</summary>
        public sealed class TwinCtorDto
        {
            public int A { get; }

            public int B { get; }

            public string S1 { get; }

            public string S2 { get; }

            public TwinCtorDto(int a, int b)
            {
                A = a;
                B = b;
            }

            public TwinCtorDto(string s1, string s2)
            {
                S1 = s1;
                S2 = s2;
            }
        }

        /// <summary>带参构造投影的测试载体：混合列型（转换器列/枚举名列/可空列）经构造实参映射。</summary>
        public sealed class MixedCtorDto
        {
            public string Token { get; }

            public SelectUserStatus Status { get; }

            public string Tag { get; }

            public MixedCtorDto(string token, SelectUserStatus status, string tag)
            {
                Token = token;
                Status = status;
                Tag = tag;
            }
        }

        /// <summary>带参构造投影的测试载体：internal 构造（同程序集可编译绑定，映射器以绑定结果直接装配）。</summary>
        public sealed class InternalCtorDto
        {
            public int Id { get; }

            internal InternalCtorDto(int id)
            {
                Id = id;
            }
        }

        /// <summary>分组投影带参构造的测试载体：键列 + 聚合列按构造实参位置映射。</summary>
        public sealed class GroupCtorDto
        {
            public SelectUserStatus Status { get; }

            public int Count { get; }

            public GroupCtorDto(SelectUserStatus status, int count)
            {
                Status = status;
                Count = count;
            }
        }

        /// <summary>构造函数归属校验载体：基类构造 + 派生结果类型（手搭树可绑定基类构造，构建期拒绝）。</summary>
        public class CtorBaseDto
        {
            public string Name { get; }

            public CtorBaseDto(string name)
            {
                Name = name;
            }
        }

        /// <summary>构造函数归属校验载体：派生类型（自身构造绑定正常，手搭基类构造树被拒绝）。</summary>
        public sealed class CtorDerivedDto : CtorBaseDto
        {
            public CtorDerivedDto(string name)
                : base(name)
            {
            }
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE SelectUsers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Score INTEGER NOT NULL,
                    Tag TEXT NULL,
                    Token TEXT NULL,
                    Status TEXT NOT NULL
                );
                INSERT INTO SelectUsers (Name, Score, Tag, Token, Status) VALUES ('Alice', 30, 't1', 'enc:a', 'One');
                INSERT INTO SelectUsers (Name, Score, Tag, Token, Status) VALUES ('Bob', 10, NULL, 'enc:b', 'Nine');
                INSERT INTO SelectUsers (Name, Score, Tag, Token, Status) VALUES ('Cara', 30, 't3', 'enc:c', 'Nine');
                INSERT INTO SelectUsers (Name, Score, Tag, Token, Status) VALUES ('Dave', 20, 't4', 'enc:d', 'One');
                INSERT INTO SelectUsers (Name, Score, Tag, Token, Status) VALUES ('Eve', 30, 't5', 'enc:e', 'One');

                CREATE TABLE SelectOrders (
                    OrderId INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    Product TEXT NOT NULL
                );
                INSERT INTO SelectOrders (UserId, Product) VALUES (1, 'Book');
            ");
        }

        private IEntityQuery<UserEntity> Query()
        {
            return DbAccess.Set<UserEntity>().AsQuery();
        }

        #region ToList（列表投影）

        [Fact]
        public async Task ToList_NoFilter_ReturnsAllValues()
        {
            var names = await Query().Select(u => u.Name).ToListAsync();

            Assert.Equal(new[] { "Alice", "Bob", "Cara", "Dave", "Eve" }, names);
        }

        [Fact]
        public async Task ToList_WithWhere_FiltersRows()
        {
            var names = await DbAccess.Set<UserEntity>()
                .Where(u => u.Score >= 30)
                .Select(u => u.Name)
                .ToListAsync();

            Assert.Equal(new[] { "Alice", "Cara", "Eve" }, names);
        }

        [Fact]
        public async Task ToList_WithOrdering_ReturnsSortedValues()
        {
            var scores = await Query()
                .OrderBy(u => u.Score)
                .ThenBy(u => u.Id)
                .Select(u => u.Score)
                .ToListAsync();

            Assert.Equal(new[] { 10, 20, 30, 30, 30 }, scores);
        }

        [Fact]
        public async Task ToList_WithSkipTake_ReturnsWindowSegment()
        {
            var names = await Query()
                .OrderBy(u => u.Score)
                .ThenBy(u => u.Id)
                .Skip(1)
                .Take(2)
                .Select(u => u.Name)
                .ToListAsync();

            // 全序（Score, Id）：Bob(10), Dave(20), Alice(30), Cara(30), Eve(30)，窗口 [1,3) = Dave, Alice
            Assert.Equal(new[] { "Dave", "Alice" }, names);
        }

        [Fact]
        public async Task ToList_ConverterColumn_AppliesEntityConversion()
        {
            // 投影值与实体赋值一致：存储值 enc: 前缀被转换器剥掉
            var tokens = await Query().OrderBy(u => u.Id).Select(u => u.Token).ToListAsync();

            Assert.Equal(new[] { "a", "b", "c", "d", "e" }, tokens);
        }

        [Fact]
        public async Task ToList_EnumAsStringColumn_RestoresEnumValues()
        {
            var statuses = await Query().OrderBy(u => u.Id).Select(u => u.Status).ToListAsync();

            Assert.Equal(new[] { SelectUserStatus.One, SelectUserStatus.Nine, SelectUserStatus.Nine, SelectUserStatus.One, SelectUserStatus.One }, statuses);
        }

        [Fact]
        public async Task ToList_NullableColumn_NullValuesAsDefault()
        {
            var tags = await Query().OrderBy(u => u.Id).Select(u => u.Tag).ToListAsync();

            Assert.Equal(new[] { "t1", null, "t3", "t4", "t5" }, tags);
        }

        #endregion

        #region First / Single（单行投影）

        [Fact]
        public async Task First_WithOrdering_ReturnsFirstProjectedValue()
        {
            var score = await Query().OrderBy(u => u.Score).ThenBy(u => u.Id).Select(u => u.Score).FirstAsync();

            Assert.Equal(10, score);
        }

        [Fact]
        public async Task FirstOrDefault_EmptyResult_ReturnsDefault()
        {
            var name = await DbAccess.Set<UserEntity>()
                .Where(u => u.Score > 100)
                .Select(u => u.Name)
                .FirstOrDefaultAsync();

            Assert.Null(name);
        }

        [Fact]
        public async Task First_EmptyResult_Throws()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => DbAccess.Set<UserEntity>().Where(u => u.Score > 100).Select(u => u.Name).FirstAsync());
        }

        [Fact]
        public async Task Single_UniqueRow_ReturnsValue()
        {
            var name = await DbAccess.Set<UserEntity>().Where(u => u.Score == 20).Select(u => u.Name).SingleAsync();

            Assert.Equal("Dave", name);
        }

        [Fact]
        public async Task Single_MultipleRows_Throws()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => DbAccess.Set<UserEntity>().Where(u => u.Score == 30).Select(u => u.Name).SingleAsync());
        }

        [Fact]
        public async Task Single_TakeTruncatedWindow_DoesNotThrowOnMultiple()
        {
            // Take(1) 截断后 Single 以窗口结果为准，不再抛多条（与实体查询语义一致）
            var name = await Query().OrderBy(u => u.Score).Take(1).Select(u => u.Name).SingleAsync();

            Assert.Equal("Bob", name);
        }

        [Fact]
        public async Task SingleOrDefault_EmptyResult_ReturnsDefault()
        {
            var name = await DbAccess.Set<UserEntity>().Where(u => u.Score == 999).Select(u => u.Name).SingleOrDefaultAsync();

            Assert.Null(name);
        }

        #endregion

        #region 多列投影（匿名类型）

        [Fact]
        public async Task MultiColumn_ToList_NoFilter_ReturnsAllRows()
        {
            var rows = await Query().Select(u => new { u.Name, u.Score }).ToListAsync();

            Assert.Equal(new[] { "Alice", "Bob", "Cara", "Dave", "Eve" }, rows.Select(r => r.Name));
            Assert.Equal(new[] { 30, 10, 30, 20, 30 }, rows.Select(r => r.Score));
        }

        [Fact]
        public async Task MultiColumn_ToList_WithWhere_FiltersRows()
        {
            var rows = await DbAccess.Set<UserEntity>()
                .Where(u => u.Score >= 30)
                .Select(u => new { u.Name, u.Score })
                .ToListAsync();

            Assert.Equal(new[] { "Alice", "Cara", "Eve" }, rows.Select(r => r.Name));
            Assert.All(rows, r => Assert.Equal(30, r.Score));
        }

        [Fact]
        public async Task MultiColumn_ToList_WithOrderingSkipTake_ReturnsWindowSegment()
        {
            var rows = await Query()
                .OrderBy(u => u.Score)
                .ThenBy(u => u.Id)
                .Skip(1)
                .Take(2)
                .Select(u => new { u.Name, u.Score })
                .ToListAsync();

            // 全序（Score, Id）：Bob(10), Dave(20), Alice(30), Cara(30), Eve(30)，窗口 [1,3) = Dave, Alice
            Assert.Equal(new[] { "Dave", "Alice" }, rows.Select(r => r.Name));
            Assert.Equal(new[] { 20, 30 }, rows.Select(r => r.Score));
        }

        [Fact]
        public async Task MultiColumn_ConverterEnumNullableColumns_AppliesEntityConversion()
        {
            var rows = await Query().OrderBy(u => u.Id).Select(u => new { u.Token, u.Status, u.Tag }).ToListAsync();

            // 转换器列剥 enc: 前缀，枚举名列按名称还原，NULL 列投影为 null
            Assert.Equal(new[] { "a", "b", "c", "d", "e" }, rows.Select(r => r.Token));
            Assert.Equal(new[] { SelectUserStatus.One, SelectUserStatus.Nine, SelectUserStatus.Nine, SelectUserStatus.One, SelectUserStatus.One }, rows.Select(r => r.Status));
            Assert.Equal(new[] { "t1", null, "t3", "t4", "t5" }, rows.Select(r => r.Tag));
        }

        [Fact]
        public async Task MultiColumn_RenamedMembers_MapsByPosition()
        {
            var rows = await Query().OrderBy(u => u.Id).Select(u => new { Id2 = u.Id, Name2 = u.Name }).ToListAsync();

            Assert.Equal(1, rows[0].Id2);
            Assert.Equal("Alice", rows[0].Name2);
        }

        [Fact]
        public async Task MultiColumn_First_WithOrdering_ReturnsFirstRow()
        {
            var row = await Query().OrderBy(u => u.Score).ThenBy(u => u.Id).Select(u => new { u.Name, u.Score }).FirstAsync();

            Assert.Equal("Bob", row.Name);
            Assert.Equal(10, row.Score);
        }

        [Fact]
        public async Task MultiColumn_Single_UniqueRow_ReturnsRow()
        {
            var row = await DbAccess.Set<UserEntity>().Where(u => u.Score == 20).Select(u => new { u.Name, u.Score }).SingleAsync();

            Assert.Equal("Dave", row.Name);
            Assert.Equal(20, row.Score);
        }

        [Fact]
        public async Task MultiColumn_FirstOrDefault_EmptyResult_ReturnsDefault()
        {
            var row = await DbAccess.Set<UserEntity>().Where(u => u.Score > 100).Select(u => new { u.Name, u.Score }).FirstOrDefaultAsync();

            Assert.Null(row);
        }

        [Fact]
        public async Task SingleColumn_AnonymousType_ReturnsWrappedValues()
        {
            var rows = await Query().OrderBy(u => u.Id).Select(u => new { u.Name }).ToListAsync();

            Assert.Equal(new[] { "Alice", "Bob", "Cara", "Dave", "Eve" }, rows.Select(r => r.Name));
        }

        #endregion

        #region 元组投影（ValueTuple.Create）

        [Fact]
        public async Task TupleProjection_ToList_ReturnsPositionalValues()
        {
            var rows = await Query().OrderBy(u => u.Id).Select(u => ValueTuple.Create(u.Name, u.Score)).ToListAsync();

            // 位置映射：Item1/Item2 与解构消费两种形态
            Assert.Equal(new[] { "Alice", "Bob", "Cara", "Dave", "Eve" }, rows.Select(r => r.Item1));
            Assert.Equal(new[] { 30, 10, 30, 20, 30 }, rows.Select(r => r.Item2));
            var (name, score) = rows[0];
            Assert.Equal("Alice", name);
            Assert.Equal(30, score);
        }

        [Fact]
        public async Task TupleProjection_ConverterEnumNullableColumns_AppliesEntityConversion()
        {
            var rows = await Query().OrderBy(u => u.Id).Select(u => ValueTuple.Create(u.Token, u.Status, u.Tag)).ToListAsync();

            // 转换器列剥 enc: 前缀，枚举名列按名称还原，NULL 列投影为 null——与匿名类型投影语义一致
            Assert.Equal(new[] { "a", "b", "c", "d", "e" }, rows.Select(r => r.Item1));
            Assert.Equal(new[] { SelectUserStatus.One, SelectUserStatus.Nine, SelectUserStatus.Nine, SelectUserStatus.One, SelectUserStatus.One }, rows.Select(r => r.Item2));
            Assert.Equal(new[] { "t1", null, "t3", "t4", "t5" }, rows.Select(r => r.Item3));
        }

        [Fact]
        public async Task TupleProjection_SingleElement_ReturnsOneTuple()
        {
            var rows = await Query().OrderBy(u => u.Id).Select(u => ValueTuple.Create(u.Score)).ToListAsync();

            Assert.Equal(new[] { 30, 10, 30, 20, 30 }, rows.Select(r => r.Item1));
        }

        [Fact]
        public void TupleProjection_EightElements_Throws()
        {
            // 8 元素即 TRest 嵌套形态（ValueTuple<T1..T7, ValueTuple<int>>）：构造第 8 参为嵌套 ValueTuple 而非元素，
            // 位置装配无法承载，Select 构建期即拒绝（引导 7 元素以内或匿名类型）
            Assert.Throws<NotSupportedException>(
                () => Query().Select(u => ValueTuple.Create(u.Id, u.Score, u.Id, u.Score, u.Id, u.Score, u.Id, u.Score)));
        }

        [Fact]
        public void TupleProjection_ComputedElement_Throws()
        {
            // 元素必须是映射列：计算表达式由 ResolveColumn 以对应异常拒绝（与匿名类型同规矩）
            Assert.Throws<ArgumentException>(
                () => Query().Select(u => ValueTuple.Create(u.Name, u.Score * 2)));
        }

        [Fact]
        public void TupleProjection_NestedCreate_Throws()
        {
            // 9 元素必经嵌套 Create：外层结果类型为 TRest 嵌套形态（8 泛型参数），构建期即拒绝——
            // TRest 守卫先于逐列解析触发，比"内层调用不是映射列"更准确（9 元素本身已超出 7 元素平铺上限）
            Assert.Throws<NotSupportedException>(
                () => Query().Select(u => ValueTuple.Create(u.Id, u.Score, u.Id, u.Score, u.Id, u.Score, u.Id, ValueTuple.Create(u.Score, u.Score))));
        }

        [Fact]
        public void TupleProjection_Empty_Throws()
        {
            Assert.Throws<NotSupportedException>(
                () => Query().Select(u => ValueTuple.Create()));
        }

        [Fact]
        public async Task TupleProjection_HandBuiltNewExpression_RecognizedAsTuple()
        {
            // 手搭 ValueTuple 构造函数 New 表达式：与 Create 调用形态同语义（解析器识别为元组构造）
            var parameter = Expression.Parameter(typeof(UserEntity), "u");
            var constructor = typeof(ValueTuple<int, int>).GetConstructor(new[] { typeof(int), typeof(int) });
            var body = Expression.New(constructor, Expression.Property(parameter, nameof(UserEntity.Id)), Expression.Property(parameter, nameof(UserEntity.Score)));
            var selector = (Expression<Func<UserEntity, (int, int)>>)Expression.Lambda(body, parameter);

            var rows = await Query().OrderBy(u => u.Id).Select(selector).ToListAsync();

            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, rows.Select(r => r.Item1));
            Assert.Equal(new[] { 30, 10, 30, 20, 30 }, rows.Select(r => r.Item2));
        }

        #endregion

        #region 带参构造投影（命名类型构造）

        [Fact]
        public async Task CtorProjection_SingleArgument_MapsByPosition()
        {
            var rows = await Query().OrderBy(u => u.Id).Select(u => new SingleCtorDto(u.Name)).ToListAsync();

            Assert.Equal(new[] { "Alice", "Bob", "Cara", "Dave", "Eve" }, rows.Select(r => r.Name));
        }

        [Fact]
        public async Task CtorProjection_MultiArguments_MapsByArgumentOrder()
        {
            var rows = await Query().OrderBy(u => u.Id).Select(u => new CtorDto(u.Name, u.Score)).ToListAsync();

            Assert.Equal(new[] { "Alice", "Bob", "Cara", "Dave", "Eve" }, rows.Select(r => r.Name));
            Assert.Equal(new[] { 30, 10, 30, 20, 30 }, rows.Select(r => r.Score));
        }

        [Fact]
        public async Task CtorProjection_ReversedArguments_BindsDistinctOverload()
        {
            // 实参序即列序：new CtorDto(u.Score, u.Name) 绑定 (int, string) 重载，第 0 列落 Score、第 1 列落 Name
            var rows = await Query().OrderBy(u => u.Id).Select(u => new CtorDto(u.Score, u.Name)).ToListAsync();

            Assert.Equal(new[] { 30, 10, 30, 20, 30 }, rows.Select(r => r.Score));
            Assert.Equal(new[] { "Alice", "Bob", "Cara", "Dave", "Eve" }, rows.Select(r => r.Name));
        }

        [Fact]
        public async Task CtorProjection_SingleArgumentOverloads_DisambiguatedByCompiler()
        {
            // 同参个数不同实参类型的重载：Roslyn 各自绑定（u.Name → CtorDto(string)，u.Score → CtorDto(int)），
            // 各 lambda 按各自的绑定结果装配，未承接的属性保持默认值
            var byName = await Query().OrderBy(u => u.Id).Select(u => new CtorDto(u.Name)).ToListAsync();
            Assert.Equal(new[] { "Alice", "Bob", "Cara", "Dave", "Eve" }, byName.Select(r => r.Name));
            Assert.All(byName, r => Assert.Equal(0, r.Score));

            var byScore = await Query().OrderBy(u => u.Id).Select(u => new CtorDto(u.Score)).ToListAsync();
            Assert.Equal(new[] { 30, 10, 30, 20, 30 }, byScore.Select(r => r.Score));
            Assert.All(byScore, r => Assert.Null(r.Name));
        }

        [Fact]
        public async Task CtorProjection_SameArityOverloads_DisambiguatedByArgTypes()
        {
            // 同为双参的重载（(int, int) 与 (string, string)）：按实参类型各自绑定，列序跟随实参序
            var byInts = await Query().OrderBy(u => u.Id).Select(u => new TwinCtorDto(u.Id, u.Score)).ToListAsync();
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, byInts.Select(r => r.A));
            Assert.Equal(new[] { 30, 10, 30, 20, 30 }, byInts.Select(r => r.B));

            var byStrings = await Query().OrderBy(u => u.Id).Select(u => new TwinCtorDto(u.Name, u.Tag)).ToListAsync();
            Assert.Equal(new[] { "Alice", "Bob", "Cara", "Dave", "Eve" }, byStrings.Select(r => r.S1));
            Assert.Equal(new[] { "t1", null, "t3", "t4", "t5" }, byStrings.Select(r => r.S2));
        }

        [Fact]
        public async Task CtorProjection_ConverterEnumNullableColumns_AppliesEntityConversion()
        {
            // 转换器列剥 enc: 前缀，枚举名列按名称还原，NULL 列投影为 null——与匿名类型投影语义一致
            var rows = await Query().OrderBy(u => u.Id).Select(u => new MixedCtorDto(u.Token, u.Status, u.Tag)).ToListAsync();

            Assert.Equal(new[] { "a", "b", "c", "d", "e" }, rows.Select(r => r.Token));
            Assert.Equal(new[] { SelectUserStatus.One, SelectUserStatus.Nine, SelectUserStatus.Nine, SelectUserStatus.One, SelectUserStatus.One }, rows.Select(r => r.Status));
            Assert.Equal(new[] { "t1", null, "t3", "t4", "t5" }, rows.Select(r => r.Tag));
        }

        [Fact]
        public async Task CtorProjection_InternalConstructor_MapsByPosition()
        {
            // internal 构造同程序集可编译绑定：映射器以绑定的构造结果直接装配，不要求公共构造
            var rows = await Query().OrderBy(u => u.Id).Select(u => new InternalCtorDto(u.Id)).ToListAsync();

            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, rows.Select(r => r.Id));
        }

        [Fact]
        public async Task CtorProjection_WithOrderingWindow_ReturnsWindowRows()
        {
            // 带参构造与排序/窗口组合：映射分支独立于 SQL 构建路径，窗口行集照常装配
            // （Score DESC, Id ASC 全序为 Alice/Cara/Eve/Dave/Bob，Skip(1).Take(2) 取 Cara/Eve）
            var rows = await Query().OrderByDescending(u => u.Score).ThenBy(u => u.Id)
                .Skip(1).Take(2)
                .Select(u => new CtorDto(u.Name, u.Score))
                .ToListAsync();

            Assert.Equal(new[] { "Cara", "Eve" }, rows.Select(r => r.Name));
            Assert.Equal(new[] { 30, 30 }, rows.Select(r => r.Score));
        }

        [Fact]
        public void CtorProjection_ZeroArgument_Throws()
        {
            // 零实参退化为空配对（与零实参匿名构造同一出口）：投影至少需要一列
            Assert.Throws<NotSupportedException>(
                () => Query().Select(u => new UserDto()));
        }

        [Fact]
        public void CtorProjection_ComputedArgument_Throws()
        {
            // 实参必须是映射列：计算表达式由 ResolveColumn 以对应异常拒绝（与匿名类型同规矩）
            Assert.Throws<ArgumentException>(
                () => Query().Select(u => new CtorDto(u.Name, u.Score + 1)));
        }

        [Fact]
        public void CtorProjection_HandBuiltBaseConstructorTree_Throws()
        {
            // 手搭树绑定基类构造（编译器不可达形态）：newobj 按 DeclaringType 精确创建实例，
            // 基类构造无法产出派生结果类型实例，构建期即拒绝
            var parameter = Expression.Parameter(typeof(UserEntity), "u");
            var baseConstructor = typeof(CtorBaseDto).GetConstructor(new[] { typeof(string) });
            var body = Expression.New(baseConstructor, Expression.Property(parameter, nameof(UserEntity.Name)));
            var selector = (Expression<Func<UserEntity, CtorDerivedDto>>)Expression.Lambda(
                Expression.Convert(body, typeof(CtorDerivedDto)), parameter);

            Assert.Throws<NotSupportedException>(
                () => Query().Select(selector));
        }

        #endregion

        #region 守卫校验

        // Include 与 Select 的互斥由接口类型在编译期拦截：Include 后的链为 IIncludableEntityQuery，
        // 不继承 IEntityQuery，Select 方法不可达，无需运行时测试。

        [Fact]
        public void Select_NotMappedColumn_Throws()
        {
            Assert.Throws<NotSupportedException>(
                () => Query().Select(u => u.Note));
        }

        [Fact]
        public void Select_NestedPath_Throws()
        {
            // 列解析统一走 EntityMetadata.ResolveColumn：解析失败（嵌套/计算/常量）抛 ArgumentException，未映射成员抛 NotSupportedException
            Assert.Throws<ArgumentException>(
                () => Query().Select(u => u.Orders.Count));
        }

        [Fact]
        public async Task Select_NamedTypeInitializer_MapsByMemberName()
        {
            var rows = await Query().OrderBy(u => u.Id).Select(u => new UserDto { Name = u.Name }).ToListAsync();

            Assert.Equal(new[] { "Alice", "Bob", "Cara", "Dave", "Eve" }, rows.Select(r => r.Name));
        }

        [Fact]
        public async Task Select_NamedTypeInitializer_MultiMemberBindingOrderIndependent()
        {
            // 绑定序（Score 在前）与实体列定义序不同：映射按绑定关系逐成员赋值，列序跟随绑定序
            var rows = await Query().OrderBy(u => u.Id).Select(u => new UserDto { Score = u.Score, Name = u.Name }).ToListAsync();

            Assert.Equal(new[] { "Alice", "Bob", "Cara", "Dave", "Eve" }, rows.Select(r => r.Name));
            Assert.Equal(new[] { 30, 10, 30, 20, 30 }, rows.Select(r => r.Score));
        }

        [Fact]
        public async Task Select_NamedTypeInitializer_DifferentMemberName_MapsByBinding()
        {
            // 成员名（Nickname）与列名（Name）不同：按绑定关系映射而非名字巧合
            var rows = await Query().OrderBy(u => u.Id).Select(u => new UserDto { Nickname = u.Name }).ToListAsync();

            Assert.Equal(new[] { "Alice", "Bob", "Cara", "Dave", "Eve" }, rows.Select(r => r.Nickname));
        }

        [Fact]
        public async Task Select_NamedTypeInitializer_NullColumn_MapsNullToReferenceMember()
        {
            // Tag 为 NULL 的行：引用类型成员得到 null（投影语境的 NULL 语义以目标类型为准）
            var rows = await Query().OrderBy(u => u.Id).Select(u => new UserDto { Nickname = u.Tag }).ToListAsync();

            Assert.Null(rows[1].Nickname);
            Assert.Equal("t1", rows[0].Nickname);
        }

        [Fact]
        public async Task Select_NamedTypeInitializer_WithOrderingWindow_ReturnsWindowRows()
        {
            // 命名类型投影与排序/窗口组合：映射分支独立于 SQL 构建路径，窗口行集照常装配
            // （Score DESC, Id ASC 全序为 Alice/Cara/Eve/Dave/Bob，Skip(1).Take(2) 取 Cara/Eve）
            var rows = await Query().OrderByDescending(u => u.Score).ThenBy(u => u.Id)
                .Skip(1).Take(2)
                .Select(u => new UserDto { Name = u.Name, Score = u.Score })
                .ToListAsync();

            Assert.Equal(new[] { "Cara", "Eve" }, rows.Select(r => r.Name));
            Assert.Equal(new[] { 30, 30 }, rows.Select(r => r.Score));
        }

        [Fact]
        public async Task Select_NamedTypeConstructor_MapsByBoundConstructor()
        {
            // 原命名类型裸构造用例的反面：UserDto(string) 由编译器按实参类型绑定，按实参位置映射
            var rows = await Query().OrderBy(u => u.Id).Select(u => new UserDto(u.Name)).ToListAsync();

            Assert.Equal(new[] { "Alice", "Bob", "Cara", "Dave", "Eve" }, rows.Select(r => r.Name));
        }

        [Fact]
        public void Select_NamedTypeInitializer_ParameterizedConstructor_Throws()
        {
            Assert.Throws<NotSupportedException>(
                () => Query().Select(u => new UserDto(u.Name) { Score = u.Score }));
        }

        [Fact]
        public void Select_EmptyAnonymousType_Throws()
        {
            Assert.Throws<NotSupportedException>(
                () => Query().Select(u => new { }));
        }

        [Fact]
        public void MultiColumn_ComputedArgument_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => Query().Select(u => new { S = u.Score + 1 }));
        }

        [Fact]
        public void MultiColumn_NestedPathArgument_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => Query().Select(u => new { C = u.Orders.Count }));
        }

        [Fact]
        public void MultiColumn_NotMappedColumn_Throws()
        {
            Assert.Throws<NotSupportedException>(
                () => Query().Select(u => new { u.Name, u.Note }));
        }

        [Fact]
        public void Select_NullSelector_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => Query().Select<string>(null));
        }

        [Fact]
        public async Task Set_CtorForm_Throws()
        {
            // SET 按成员名寻址落列，构造实参不承载「更新哪一列」的语义（位置即映射），带参构造形态显式拒绝
            await Assert.ThrowsAsync<NotSupportedException>(
                () => DbAccess.Set<UserEntity>().UpdateAsync(u => new UserDto(u.Name), u => u.Id == 1));
        }

        [Fact]
        public void GroupBy_CtorKey_Throws()
        {
            // 分组键的成员名是 g.Key.X 的寻址落点，构造实参不携带成员名（位置即映射），带参构造键显式拒绝
            Assert.Throws<NotSupportedException>(
                () => Query().GroupBy(u => new CtorDto(u.Name)));
        }

        [Fact]
        public async Task GroupProjection_CtorDto_ReturnsKeyAndAggregateValues()
        {
            // 分组投影的带参构造：g.Key（键列）与 g.Count()（聚合列）按构造实参位置映射（One 组 3 行、Nine 组 2 行，按组数降序）
            var rows = await Query().GroupBy(u => u.Status)
                .OrderByDescending(g => g.Count())
                .Select(g => new GroupCtorDto(g.Key, g.Count()))
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.Equal(SelectUserStatus.One, rows[0].Status);
            Assert.Equal(3, rows[0].Count);
            Assert.Equal(SelectUserStatus.Nine, rows[1].Status);
            Assert.Equal(2, rows[1].Count);
        }

        #endregion
    }
}
