using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq.Expressions;
using System.Linq;
using System.Threading.Tasks;
using Xbc.DbAccess.Entity;
using Xunit;
using XbcDbEntity = Xbc.DbAccess.Entity;

namespace Xbc.DbAccess.UnitTests.Entity
{
    /// <summary>
    /// 查询链守卫与投影校验的覆盖测试（Stryker NoCoverage 补测）：
    /// 并发令牌列拒绝排序、Include 与 Select 互斥防御、命名类型投影成员合法性校验
    ///（非公共可写成员/只读字段/重复绑定）、窗口缺排序的终结端防御校验、空排序链构建守卫。
    /// </summary>
    public class EntityQueryGuardTests : EntityTestBase
    {
        #region 测试实体

        /// <summary>并发令牌列载体：[Column(IsConcurrencyToken = true)] 要求 byte[] 类型。</summary>
        [XbcDbEntity.Table("GuardConcurrencyItems")]
        public class ConcurrencyProbeEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public string Name { get; set; } = string.Empty;

            // 并发令牌由框架乐观锁专用管理，排序/聚合解析端显式拒绝
            [XbcDbEntity.Column(IsConcurrencyToken = true)]
            public byte[] RowVer { get; set; }
        }

        /// <summary>1:N 导航根实体：Include 后 Select 互斥防御的测试载体。</summary>
        [XbcDbEntity.Table("GuardNavRoots")]
        public class NavRootEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            [XbcDbEntity.Navigation(ThisKey = "Id", OtherKey = "RootId")]
            public List<NavChildEntity> Children { get; set; }
        }

        /// <summary>1:N 导航子实体。</summary>
        public class NavChildEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int ChildId { get; set; }

            public int RootId { get; set; }
        }

        /// <summary>投影解析与执行载体。</summary>
        [XbcDbEntity.Table("GuardProjectionItems")]
        public class ProjectionProbeEntity
        {
            [XbcDbEntity.PrimaryKey(IsIdentity = true)]
            public int Id { get; set; }

            public int Score { get; set; }

            public string Name { get; set; } = string.Empty;
        }

        #endregion

        #region 投影结果类型载体

        /// <summary>
        /// 命名类型投影载体：可写公共字段是编译器允许的初始化器绑定形态（属性回查失败后走字段回查的合法路径）；
        /// 只读字段绑定无法经 C# 初始化器语法构造（CS 编译错误），需手工构建表达式树触发拒绝分支。
        /// </summary>
        public sealed class FieldHolderDto
        {
            public int FieldId;

            public int FieldScore;

            public readonly int ReadonlyField = 0;
        }

        /// <summary>
        /// internal setter 属性载体：同程序集内初始化器赋值合法（可编译），但解析层按公共 setter 查找
        ///（GetSetMethod(nonPublic: false)）失败，再回查字段仍为 null，应显式拒绝。
        /// </summary>
        public sealed class NonPublicSetterDto
        {
            public int Hidden { get; internal set; }
        }

        #endregion

        protected override async Task InitializeDatabaseAsync()
        {
            await DbAccess.ExecuteNonQueryAsync(@"
                CREATE TABLE GuardProjectionItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Score INTEGER NOT NULL,
                    Name TEXT NOT NULL
                );
                INSERT INTO GuardProjectionItems (Score, Name) VALUES (30, 'Alice');
                INSERT INTO GuardProjectionItems (Score, Name) VALUES (10, 'Bob');
                INSERT INTO GuardProjectionItems (Score, Name) VALUES (20, 'Cara');
            ");
        }

        private IEntityQuery<T> Query<T>() where T : class
        {
            return DbAccess.Set<T>().AsQuery();
        }

        #region 排序/聚合键守卫（ResolveMappedColumn）

        [Fact]
        public void OrderBy_ConcurrencyTokenColumn_Throws()
        {
            // 并发令牌由框架乐观锁专用管理（换发与乐观锁条件均在框架内部），不参与排序
            var ex = Assert.Throws<NotSupportedException>(
                () => Query<ConcurrencyProbeEntity>().OrderBy(u => u.RowVer));

            Assert.Contains("并发令牌", ex.Message);
            Assert.Contains("RowVer", ex.Message);
        }

        [Fact]
        public void ThenBy_ConcurrencyTokenColumn_Throws()
        {
            // ThenBy 与 OrderBy 共用解析核心：令牌列作为次级排序键同样被拒
            Assert.Throws<NotSupportedException>(
                () => Query<ConcurrencyProbeEntity>().OrderBy(u => u.Name).ThenBy(u => u.RowVer));
        }

        #endregion

        #region Include 与 Select 互斥防御

        [Fact]
        public void IncludeQuery_Select_Throws()
        {
            // 接口层面 Include 后的链已失去 Select（编译期互斥）；基类防御拦截经基类引用强转触达，
            // 保证投影结果为非实体类型时无法装配导航属性
            var includable = DbAccess.Set<NavRootEntity>().AsQuery().Include(u => u.Children);
            var baseQuery = (EntityQueryBase<NavRootEntity>)includable;

            var ex = Assert.Throws<InvalidOperationException>(() => baseQuery.Select(u => u.Id));

            Assert.Contains("互斥", ex.Message);
        }

        #endregion

        #region 命名类型投影成员合法性校验

        [Fact]
        public void Projection_InternalSetterProperty_Throws()
        {
            // internal setter 属性在本程序集内可编译初始化，但映射器按公共 setter 赋值无法生效：
            // 属性回查失败后回查公共字段仍为 null，应显式拒绝
            var ex = Assert.Throws<NotSupportedException>(
                () => Query<ProjectionProbeEntity>().Select(u => new NonPublicSetterDto { Hidden = u.Id }));

            Assert.Contains("Hidden", ex.Message);
            Assert.Contains("投影赋值", ex.Message);
        }

        [Fact]
        public void Projection_ReadonlyFieldBinding_Throws()
        {
            // 只读字段绑定被 C# 初始化器语法禁止（编译期错误），手工构建表达式树构造该非法形态，
            // 解析层经字段回查命中只读字段（IsInitOnly），应显式拒绝
            var parameter = Expression.Parameter(typeof(ProjectionProbeEntity), "u");
            var binding = Expression.Bind(
                typeof(FieldHolderDto).GetField(nameof(FieldHolderDto.ReadonlyField)),
                Expression.Property(parameter, nameof(ProjectionProbeEntity.Id)));
            var body = Expression.MemberInit(Expression.New(typeof(FieldHolderDto)), binding);
            var selector = Expression.Lambda<Func<ProjectionProbeEntity, FieldHolderDto>>(body, parameter);

            var ex = Assert.Throws<NotSupportedException>(() => Query<ProjectionProbeEntity>().Select(selector));

            Assert.Contains("ReadonlyField", ex.Message);
        }

        [Fact]
        public void Projection_DuplicateMemberBinding_Throws()
        {
            // 重复初始化同一成员被 C# 初始化器语法禁止（编译期错误），手工构建含重复绑定的表达式树，
            // 解析层按绑定序判重，应在第二次绑定时显式拒绝
            var parameter = Expression.Parameter(typeof(ProjectionProbeEntity), "u");
            var member = typeof(FieldHolderDto).GetField(nameof(FieldHolderDto.FieldScore));
            var bindings = new MemberBinding[]
            {
                Expression.Bind(member, Expression.Property(parameter, nameof(ProjectionProbeEntity.Id))),
                Expression.Bind(member, Expression.Property(parameter, nameof(ProjectionProbeEntity.Score)))
            };
            var body = Expression.MemberInit(Expression.New(typeof(FieldHolderDto)), bindings);
            var selector = Expression.Lambda<Func<ProjectionProbeEntity, FieldHolderDto>>(body, parameter);

            var ex = Assert.Throws<NotSupportedException>(() => Query<ProjectionProbeEntity>().Select(selector));

            Assert.Contains("重复赋值", ex.Message);
            Assert.Contains("FieldScore", ex.Message);
        }

        [Fact]
        public async Task Projection_WritableFieldBinding_MapsValues()
        {
            // 可写公共字段是编译器允许的绑定形态：属性回查失败后字段回查成功放行，
            // 映射器按字段回查绑定落点，逐行赋值应与列值一致
            var rows = await Query<ProjectionProbeEntity>().OrderBy(u => u.Id)
                .Select(u => new FieldHolderDto { FieldId = u.Id, FieldScore = u.Score })
                .ToListAsync();

            Assert.Equal(new[] { 1, 2, 3 }, rows.Select(r => r.FieldId).ToArray());
            Assert.Equal(new[] { 30, 10, 20 }, rows.Select(r => r.FieldScore).ToArray());
        }

        #endregion

        #region 窗口状态终结端防御校验（ValidateWindowState）

        [Fact]
        public async Task ToList_WindowWithoutOrdering_Throws()
        {
            // 正常链式调用（Skip/Take 前置校验）不可达「窗口无排序」状态；直接构造带窗口状态、无排序链的查询，
            // 终结端防御校验应在执行前显式失败
            var query = new EntityQuery<ProjectionProbeEntity>(
                DbAccess,
                "SELECT Id FROM GuardProjectionItems",
                Array.Empty<DbParameter>(),
                orderings: null,
                skip: 0,
                take: 5);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToListAsync());

            Assert.Contains("稳定行序", ex.Message);
        }

        [Fact]
        public async Task Count_WindowWithoutOrdering_Throws()
        {
            // 聚合目标 SQL 构建同样经终结端窗口校验：窗口态无排序在执行前失败
            var query = new EntityQuery<ProjectionProbeEntity>(
                DbAccess,
                "SELECT Id FROM GuardProjectionItems",
                Array.Empty<DbParameter>(),
                orderings: null,
                skip: 0,
                take: 5);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => query.CountAsync());

            Assert.Contains("稳定行序", ex.Message);
        }

        #endregion

        #region 排序子句构建守卫（BuildOrderClause）

        [Fact]
        public void BuildOrderClause_NullOrderings_Throws()
        {
            var dialect = DbAccess.ProviderFactory.SqlDialect;

            Assert.Throws<ArgumentException>(
                () => EntityQuerySqlHelpers.BuildOrderClause(null, dialect));
        }

        [Fact]
        public void BuildOrderClause_EmptyOrderings_Throws()
        {
            // 空链静默返回空串会让下游拼出裸 "ORDER BY"：共享构建器自防御显式失败
            var dialect = DbAccess.ProviderFactory.SqlDialect;

            Assert.Throws<ArgumentException>(
                () => EntityQuerySqlHelpers.BuildOrderClause(
                    new List<(ColumnMetadata Column, bool Descending)>(), dialect));
        }

        #endregion
    }
}
