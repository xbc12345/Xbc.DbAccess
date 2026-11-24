using System.Linq.Expressions;
using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// SetClauseTranslator 的纯翻译单元测试：直接断言赋值清单与守卫异常（不依赖数据库）。
    /// </summary>
    public class SetClauseTranslatorTests
    {
        #region 测试实体

        public enum SetRowStatus
        {
            Normal = 0,
            Locked = 1
        }

        [XbcDbEntity.Table("SetRows")]
        public class SetRow
        {
            [XbcDbEntity.PrimaryKey]// 纯主键（非自增）：主键赋值守卫的独立测试载体（TranslatorRow 的 Id 是 PK+identity 双重身份，identity 检查先命中）
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;

            public int Age { get; set; }

            public SetRowStatus Status { get; set; }

            [XbcDbEntity.Column(IsConcurrencyToken = true)]// 并发令牌列：令牌赋值守卫的测试载体（byte[] 为令牌列的强制类型）
            public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        }

        private const string ConstName = "const-name";
        private static readonly SetRowStatus MaskedStatus = (SetRowStatus)3;

        #endregion

        private static List<(ColumnMetadata Column, object DbValue)> Translate<TValues>(Expression<Func<SetRow, TValues>> assignment)
        {
            return SetClauseTranslator.Translate(assignment, EntityMetadataCache<SetRow>.Metadata);
        }

        [Fact]
        public void Translate_AnonymousType_ReturnsAssignmentsInOrder()
        {
            var assignments = Translate(r => new { Age = 18, Name = "Alice" });

            Assert.Equal(2, assignments.Count);
            Assert.Equal("Age", assignments[0].Column.PropertyName);
            Assert.Equal(18, assignments[0].DbValue);
            Assert.Equal("Name", assignments[1].Column.PropertyName);
            Assert.Equal("Alice", assignments[1].DbValue);
        }

        [Fact]
        public void Translate_ObjectInitializer_ReturnsAssignments()
        {
            var assignments = Translate(r => new SetRow { Name = "Bob" });

            var assignment = Assert.Single(assignments);
            Assert.Equal("Name", assignment.Column.PropertyName);
            Assert.Equal("Bob", assignment.DbValue);
        }

        [Fact]
        public void Translate_TupleForm_Throws()
        {
            // SET 按成员名寻址落列，元组形态（Create 调用）无元素名：显式拒绝并引导初始化器/匿名形态
            var ex = Assert.Throws<NotSupportedException>(
                () => Translate(r => ValueTuple.Create(18, "Alice")));

            Assert.Contains("元组", ex.Message);
        }

        [Fact]
        public void Translate_NullValue_ConvertedToDBNull()
        {
            var assignments = Translate(r => new { Name = (string)null });

            Assert.Equal(DBNull.Value, Assert.Single(assignments).DbValue);
        }

        [Fact]
        public void Translate_ConstructorWithArguments_Throws()
        {
            // 带参构造的对象初始化器：构造实参无法映射到 SET 赋值，显式失败优于静默丢弃
            Assert.Throws<NotSupportedException>(() => Translate(r => new InvalidOperationException("x") { }));
        }

        [Fact]
        public void Translate_PositionalConstructor_Throws()
        {
            // 位置参数构造（Members == null）无法获知成员名，显式失败
            Assert.Throws<NotSupportedException>(() => Translate(r => new List<int>(10)));
        }

        [Fact]
        public void Translate_EmptyAssignment_Throws()
        {
            Assert.Throws<NotSupportedException>(() => Translate(r => new SetRow { }));
        }

        [Fact]
        public void Translate_PrimaryKeyAssignment_Throws()
        {
            Assert.Throws<NotSupportedException>(() => Translate(r => new { Id = 5 }));
        }

        [Fact]
        public void Translate_IdentityAssignment_Throws()
        {
            // TranslatorRow 的 Id 为自增主键：标识列守卫先于主键守卫命中
            Assert.Throws<NotSupportedException>(() =>
                SetClauseTranslator.Translate(
                    (Expression<Func<WhereClauseTranslatorTests.TranslatorRow, object>>)(r => new { Id = 5 }),
                    EntityMetadataCache<WhereClauseTranslatorTests.TranslatorRow>.Metadata));
        }

        [Fact]
        public void Translate_UnmappedMember_Throws()
        {
            Assert.Throws<NotSupportedException>(() => Translate(r => new { Unknown = 1 }));
        }

        [Fact]
        public void Translate_DuplicateAssignment_Throws()
        {
            // C# 编译器禁止初始化器重复成员，手工构造表达式树模拟重复赋值（防御针对手工表达式）
            var parameter = Expression.Parameter(typeof(SetRow));
            var ageProperty = typeof(SetRow).GetProperty(nameof(SetRow.Age));
            var body = Expression.MemberInit(
                Expression.New(typeof(SetRow)),
                Expression.Bind(ageProperty, Expression.Constant(1)),
                Expression.Bind(ageProperty, Expression.Constant(2)));

            Assert.Throws<NotSupportedException>(
                () => SetClauseTranslator.Translate(
                    Expression.Lambda<Func<SetRow, object>>(body, parameter),
                    EntityMetadataCache<SetRow>.Metadata));
        }

        [Fact]
        public void Translate_NestedLambdaValue_Succeeds()
        {
            // 值侧含嵌套 lambda 的闭包表达式：嵌套参数（y）不是实体参数，可正常求值（与 WHERE 侧语义一致）
            var list = new List<string> { "a", "b" };

            var assignments = Translate(r => new { Name = string.Join(",", list.Select(y => y)) });

            Assert.Equal("a,b", Assert.Single(assignments).DbValue);
        }

        [Fact]
        public void Translate_EntityPropertyReference_Throws()
        {
            // 赋值右侧引用实体属性（其他列的值）不可求值，显式失败
            Assert.Throws<NotSupportedException>(() => Translate(r => new { Name = ReferenceName(r) }));
        }

        private static string ReferenceName(SetRow row) => row.Name;

        [Fact]
        public void Translate_ConditionalValue_Evaluates()
        {
            // 三元：值侧不引用实体参数即可求值（flag 为闭包变量，条件在翻译期求值）
            var flag = true;
            var assignments = Translate(r => new { Name = flag ? "a" : "b" });

            Assert.Equal("a", Assert.Single(assignments).DbValue);
        }

        [Fact]
        public void Translate_CoalesceValue_Evaluates()
        {
            // Coalesce（??）：值侧编译求值
            string? name = null;
            var assignments = Translate(r => new { Name = name ?? "fallback" });

            Assert.Equal("fallback", Assert.Single(assignments).DbValue);
        }

        [Fact]
        public void Translate_InterpolationValue_Evaluates()
        {
            // 字符串插值（编译为 string.Format 调用）：值侧编译求值
            var id = 42;
            var assignments = Translate(r => new { Name = $"{id:D5}" });

            Assert.Equal("00042", Assert.Single(assignments).DbValue);
        }

        [Fact]
        public void Translate_EnumBitwiseOr_Evaluates()
        {
            // 枚举位运算：常量折叠路径（编译期折叠为单常量）与运行期 Or 节点路径（静态只读字段）均可求值，
            // 基础类型枚举经 ConvertToDb 底层化为整数
            var folded = Translate(r => new { Status = SetRowStatus.Normal | SetRowStatus.Locked });
            Assert.Equal(1, Assert.Single(folded).DbValue);

            var runtime = Translate(r => new { Status = SetRowStatus.Normal | MaskedStatus });
            Assert.Equal(3, Assert.Single(runtime).DbValue);
        }

        [Fact]
        public void Translate_CrossEntityClosureReference_EvaluatesCurrentValue()
        {
            // 闭包引用另一个实体的属性：与嵌套 lambda 同理不引用 lambda 参数，读取闭包实体当前属性值
            var other = new SetRow { Name = "from-other" };
            var assignments = Translate(r => new { Name = other.Name });

            Assert.Equal("from-other", Assert.Single(assignments).DbValue);
        }

        [Fact]
        public void Translate_DbNullExplicitValue_PassesThrough()
        {
            // 显式 DBNull.Value 赋值：与 null 赋值等价（ConvertToDb 原样穿透 DBNull）
            var assignments = Translate(r => new { Name = DBNull.Value });

            Assert.Equal(DBNull.Value, Assert.Single(assignments).DbValue);
        }

        [Fact]
        public void Translate_ConstField_Evaluates()
        {
            // 常量字段（编译期折叠）与静态只读字段（闭包成员求值）
            var assignments = Translate(r => new { Name = ConstName });

            Assert.Equal("const-name", Assert.Single(assignments).DbValue);
        }

        [Fact]
        public void Translate_IntColumnAssignedLong_Passes()
        {
            // int 列赋 long：值经 ConvertToDb 按列类型归一为 Int32（与列同型，避免严格方言的类型不匹配）
            var assignments = Translate(r => new { Age = 18L });

            Assert.Equal(18, Assert.IsType<int>(Assert.Single(assignments).DbValue));
        }

        [Fact]
        public void Translate_NullToValueTypeColumn_Passes()
        {
            // null → DBNull：可空语义（NOT NULL 约束由数据库最终把关）
            var assignments = Translate(r => new { Age = (int?)null });

            Assert.Equal(DBNull.Value, Assert.Single(assignments).DbValue);
        }

        [Fact]
        public void Translate_ConcurrencyTokenAssignment_Throws()
        {
            // 令牌列由框架在更新时自动换发（与实体版更新跳过令牌列一致），手工赋值破坏乐观并发校验
            var ex = Assert.Throws<NotSupportedException>(() => Translate(r => new { RowVersion = new byte[] { 1, 2, 3 } }));

            Assert.Contains("RowVersion", ex.Message);
        }

        [Fact]
        public void Translate_EmptyAnonymousType_ThrowsWithEmptyAssignmentMessage()
        {
            // 空匿名类型：报"未包含任何赋值"，不再误报"位置参数构造"（后者以存在构造实参为前提）
            var ex = Assert.Throws<NotSupportedException>(() => Translate(r => new { }));

            Assert.Contains("未包含任何赋值", ex.Message);
        }
    }
}
