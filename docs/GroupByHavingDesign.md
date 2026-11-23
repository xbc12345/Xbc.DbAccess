# GroupBy + HAVING 设计方案（已评审定稿）

> 状态：已评审定稿（2026-11-21），实施时以此为准。评审修正：聚合 SELECT 项引入生成别名（五方言窗口构建器的 orderBy 均在分组查询之外的派生表层求值，裸聚合片段在外层不可引用，见 §4.2）；接口补 Skip/Take 链点、排序方法命名与签名对齐现有 API；AVG 走方言表达式防整型截断；COUNT(*) 投影经 mapper 裸读取分支。

## 1. 背景与动机

当前实体查询的聚合能力是刻意的半格：

- **整表聚合已有**（EntityQueryBase.cs L368-526）：`CountAsync / AnyAsync / AllAsync / SumAsync / AverageAsync / MaxAsync / MinAsync`——WHERE 范围内出单个值
- **分组聚合明确不支持**：`GroupBy` 在 WhereClauseTranslator.cs L64 与 IEntitySet.cs L31 被声明为「不支持（抛异常）」——当初是收窄而非遗漏

「按客户统计订单总额」「按状态分组计数」是查询类库的高频需求，目前只能手写完整 SQL + 手工读列。补上这一格后，表达式查询面（FromSql 路径）的能力矩阵即闭环：过滤（Where）→ 排序（OrderBy）→ 窗口（Skip/Take）→ 投影（Select）→ **分组聚合（GroupBy/Having）**。

### 1.1 本质：第三条 SQL 产出路径

| 路径 | SQL 形态 | 现有实现 |
| --- | --- | --- |
| 原样/包裹改写 | 源 SQL 不动或派生表 + 窗口包裹 | EntityQueryBase / BuildWindowSqlAsync |
| 投影 | `SELECT 列名组 FROM (查询) 别名`——列组是**实体列名的方言引用**，值映射在客户端按位置完成 | ProjectedEntityQuery |
| **分组聚合（本方案）** | `SELECT 键列, 聚合片段 FROM (查询) 别名 GROUP BY ... HAVING ...`——SELECT 列表含**聚合 SQL 片段**，聚合必须在服务端完成 | 新建 |

现有投影是客户端投影（SQL 只做列收窄，SUM 拉回全表在内存算是错误形态），因此 GROUP BY 不是投影的扩展，是一条独立产出路径——复杂度介于 WhereClauseTranslator 与窗口构建之间。

## 2. 目标与非目标

**目标**：

- `FromSql<T>()` 查询链获得 `GroupBy → Having → Select` 分组聚合能力，产出服务端聚合 SQL
- 复用 MemberMappingParser（键/投影配对）、ValidateWindowState/BuildWindowSqlAsync（窗口）、投影 mapper（位置读取）——地基已有约 80%

**非目标**（v1 明确不做，见 §5 拒绝清单）：

- EntitySet<T> 的独立 GroupBy 直链点（`set.GroupBy(...)`）——已核实 Set 的查询链本就桥接 IEntityQuery：`Where/AsQuery` 一跳生成 SQL 后返回 `dbAccess.FromSql<T>(...)`（EntitySet.Select.cs L45-46、L56-57），`set.Where(...).GroupBy(...)`、`set.AsQuery().GroupBy(...)` 经桥接自动获得分组能力，无需二期；`set.GroupBy(...)` 直链点仅为语法糖，v1 不加（文档引导 AsQuery）
- 聚合内表达式运算（`SUM(A * B)`）、聚合间运算（`SUM(A)/COUNT(*)`）、条件计数（`COUNT(CASE WHEN)`)
- keyset 分页（分组结果无稳定唯一键）
- 无 GroupBy 的裸 HAVING
- 声明式 Join 链点（`LeftJoin<Customer>((o, c) => ...)`）——多表连接属手写 SQL 职责（定位论证与组合形态见 §3.4）

## 3. API 面

### 3.1 链式形态（EF Core 用户心智）

```csharp
var rows = await dbAccess.FromSql<Order>("SELECT * FROM Orders WHERE CreateTime >= @from", new { from })
    .Where(x => x.Amount > 0)                          // 现有链点，作用于源查询
    .GroupBy(x => new { x.CustomerId, x.Status })      // 新链点：键 = 实体成员访问
    .Having(g => g.Count() >= 5)                       // 聚合谓词，常量参数化
    .Select(g => new
    {
        CustomerId = g.Key.CustomerId,                 // 键列引用
        Total      = g.Sum(x => x.Amount),             // SUM("Amount") AS "s_xxxx"（生成别名）
        Cnt        = g.Count(),                        // COUNT(*) AS "s_yyyy"
        Avg        = g.Average(x => x.Amount),         // 方言 AVG 表达式（防整型截断）AS "s_zzzz"
    })
    .OrderByDescending(g => g.Sum(x => x.Amount))      // 按聚合列的别名引用排序（命名与 IEntityQuery 一致）
    .Skip(0).Take(20)                                  // 窗口套在分组结果外
    .ToListAsync();
```

产出 SQL（SQL Server 2012+ 形态示意；聚合项的生成别名使排序引用在外层合法——窗口构建器对 orderBy 的求值位置见 §4.2）：

```sql
SELECT "CustomerId", "s_1a2b3c4d", "s_5f6e7d8c", "s_9a8b7c6d" FROM (
    SELECT * FROM (
        SELECT "CustomerId", "Status", SUM("Amount") AS "s_1a2b3c4d", COUNT(*) AS "s_5f6e7d8c",
               AVG(CAST("Amount" AS DECIMAL(38,6))) AS "s_9a8b7c6d"
        FROM ( /* 原 SQL（含 WHERE） */ ) t
        GROUP BY "CustomerId", "Status"
        HAVING COUNT(*) >= @p1
    ) q
    ORDER BY "s_1a2b3c4d" DESC
    OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY
) p
```

最外层裁剪 SELECT 恒在：分组层暴露「全部键列 + 引用到的聚合（别名）」超集，外层按 Select 项配对序裁剪为恰好 N 列，投影 mapper 的位置读取契约（FieldCount == 列数）由此保持。

### 3.2 接口签名

```csharp
// IEntityQuery<T> 新增链点（EntityQuery<T> 实现）
public interface IEntityQuery<T> where T : class
{
    IGroupedEntityQuery<T, TKey> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector);
}

// 新建接口文件 IGroupedEntityQuery.cs；链式派生均复制状态新建实例（与现有 OrderBy/Skip/Take 同规矩）；
// 排序方法命名与泛型签名与 IEntityQuery 的 OrderByDescending/ThenByDescending 一致（不用 object，避免值类型装箱 Convert 节点）
public interface IGroupedEntityQuery<T, TKey> where T : class
{
    // 多次调用按 AND 合并（与 Where 的多谓词语义一致）；分组接口不暴露 Where——分组后的过滤语义就是 Having
    IGroupedEntityQuery<T, TKey> Having(Expression<Func<IGrouping<TKey, T>, bool>> predicate);
    IGroupedEntityQuery<T, TKey> OrderBy<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> keySelector);
    IGroupedEntityQuery<T, TKey> OrderByDescending<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> keySelector);
    IGroupedEntityQuery<T, TKey> ThenBy<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> keySelector);
    IGroupedEntityQuery<T, TKey> ThenByDescending<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> keySelector);

    // 窗口作用于分组结果集（须先排序；与实体查询同校验：Skip 必配 Take，终结执行时 ValidateWindowState 复用）
    IGroupedEntityQuery<T, TKey> Skip(int count);
    IGroupedEntityQuery<T, TKey> Take(int count);

    IProjectedEntityQuery<TResult> Select<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> selector);

    // 终结：整表聚合语义在分组查询上的对应物 = 对分组结果集再聚合（SELECT COUNT(*) FROM (分组 SQL)），已设窗口时统计窗口内组数
    Task<long> CountAsync(CancellationToken ct = default);
}
```

### 3.3 lambda 参数类型用 BCL 的 `IGrouping<TKey, T>`（关键决策）

不定义自定义分组门面类型：`IGrouping<TKey, T>` 本身实现 `IEnumerable<T>`，`g.Sum(x => x.Amount)` 编译为 `Enumerable.Sum(g, x => x.Amount)` 静态方法调用、`g.Count()` 与 `g.LongCount()` 为 `Enumerable.Count/LongCount(g)`——翻译器按 `MethodCallExpression { Method.DeclaringType == typeof(Enumerable) }` 形态匹配即可。`g.Key` 为普通属性访问。用户侧表达式**真实可编译**（可单步调试、可静态检查），与框架「两侧形态自洽」哲学一致。

**EF Core 心智迁移成本（仅两点差异）**：① EF 的「GroupBy 后 Where」即 HAVING——本框架分组接口不暴露 Where，过滤显式用 `Having`（语义相同，编译期可发现）；② EF 可物化组序列（`ToListAsync` 出 `IGrouping` 列表），本框架分组查询不物化组，一律经 `Select` 出扁平行（接口形态自然呈现，无需记忆）。其余（`g.Key`、聚合方法名、`ThenBy`、`Skip/Take`、`First/Single`）与 EF 心智一致；EF 允许的排序引用未投影聚合、`Select(g => g.Key)` 复合键展开等形态本方案均支持（见 §4.1、§4.2）。

### 3.4 带连接的分组：LEFT JOIN 写在源 SQL，结果落扁平视图实体

分组聚合最常见的实战形态是跨表统计（订单 × 客户）。本框架的连接职责在**手写源 SQL**——FromSql 的定位就是「手写 SQL + 表达式链改写」，JOIN 不需要也无法被表达式链替代。声明式 Join 链点要求多实体元数据解析、表别名分配、列名限定（`"o"."CustomerId"`）、双参数表达式翻译与 JOIN×WHERE×GROUP BY 组装矩阵——等于在表达式层重建一个小查询引擎，且 WhereClauseTranslator 对 Join 的「不支持」声明正是这条边界的既有体现。

组合形态（无需任何新机制，v1 设计直接覆盖）：

```csharp
// 扁平视图实体：把连接结果集的列声明在 T 上（查询专用、无主键；EntityMetadataCache 对 no-key 实体记录告警日志，查询路径不受影响）
public class OrderStatRow
{
    [Column("CustomerId")] public long CustomerId { get; set; }
    [Column("CustomerName")] public string CustomerName { get; set; }
    [Column("Amount")] public decimal Amount { get; set; }
}

var rows = await dbAccess.FromSql<OrderStatRow>(@"
    SELECT o.CustomerId, c.Name AS CustomerName, o.Amount
    FROM Orders o
    LEFT JOIN Customers c ON c.Id = o.CustomerId
    WHERE o.CreateTime >= @from", new { from })
    .GroupBy(x => new { x.CustomerId, x.CustomerName })
    .Having(g => g.Count() >= 3)
    .Select(g => new
    {
        g.Key.CustomerName,
        Cnt   = g.Count(),
        Total = g.Sum(x => x.Amount),
    })
    .ToListAsync();
```

键列与聚合列都从 T 的元数据解析（方言引用），对派生表包裹层而言是普通列引用——连接发生在内层 SQL，外层 `GROUP BY / HAVING / 窗口` 完全无感。

**引用连接列的表达式形态（仅一种）**：GroupBy/聚合表达式的成员解析落点是 T 的列元数据——连接列必须以**列的形态**声明在 T（扁平视图实体）上，表达式即 `x.CustomerName` 属性访问。**导航路径表达式（`x.Customer.Name`）不支持**：手写 SQL 的别名契约在框架视野之外，导航路径无法推导出结果集列名（`c.Name AS CustomerName` 的映射不可知），且左右表同名列在派生表中撞名无法消解——按约定「导航末端列名 = 结果集列名」是猜测，拒绝并引导扁平视图实体（把子实体的列以列形态显式声明，表达式才有确定落点）。

```csharp
// ✅ 支持：连接列以列形态声明进 T，表达式 = 属性访问
public class OrderStatRow                     // 扁平视图实体（查询专用、无主键）
{
    [Column("CustomerId")]   public long CustomerId { get; set; }
    [Column("CustomerName")] public string CustomerName { get; set; }   // 来自 c.Name AS CustomerName
    [Column("Region")]       public string Region { get; set; }              // 来自连接表列
    [Column("Amount")]       public decimal Amount { get; set; }
}

await dbAccess.FromSql<OrderStatRow>(@"
    SELECT o.CustomerId, c.Name AS CustomerName, c.Region, o.Amount
    FROM Orders o
    LEFT JOIN Customers c ON c.Id = o.CustomerId")
    .GroupBy(x => new { x.CustomerName, x.Region })      // → GROUP BY "CustomerName", "Region"
    .Having(g => g.Count() >= 3)
    .Select(g => new
    {
        g.Key.CustomerName,
        Total = g.Sum(x => x.Amount),                    // → SUM("Amount")
        Cnt   = g.Count(),                               // → COUNT(*)
    })
    .ToListAsync();

// ❌ 拒绝：经导航引用子实体列（Order.Customer 是导航属性，指向实体而非列）
dbAccess.FromSql<Order>("...")
    .GroupBy(x => x.Customer.Name)                       // NotSupportedException：
                                                         // 手写 SQL 别名契约不可知（c.Name AS CustomerName 的映射
                                                         // 框架无法推导），左右表同名列撞名无法消解；
                                                         // 消息引导：将连接列以列形态声明进视图实体（见设计文档 §3.4）
```

手写连接同时把两个 SQL 级陷阱留在用户可控的 SQL 层（框架不改写连接语义，文档注明）：

1. **扇出陷阱**：LEFT JOIN 1:N 使左表行按右表匹配行数重复，`SUM(o.Amount)`/`COUNT(*)` 被放大——须在源 SQL 层消解（右表预聚合子查询、EXISTS 存在性连接）；框架不自动 DISTINCT（`SUM(DISTINCT)` 对金额语义是错的）
2. **WHERE 杀 NULL**：对右表列的 WHERE 条件会把 LEFT JOIN 退化为 INNER JOIN（NULL 行被过滤）——右表过滤条件须写进 ON 子句

**两个入口的分工**：Set 桥接后的源 SQL 是框架生成的单表全列 SELECT（无连接入口）——Set 起点的分组天然是**单表分组**（按状态计数、按日期汇总），跨表分组必须 FromSql 手写 JOIN。这是入口定位而非能力缺口：单表走 Set（生成式），跨表走 FromSql（手写式）。

**EntitySet 与连接的边界（Include 装配 ≠ SQL JOIN）**：Set 侧表达「表间关联」的唯一形态是导航属性 + Include——分裂查询（多条独立 SQL）装配对象图，无扇出问题，但不产出扁平行集、不参与分组聚合。需要 LEFT JOIN 行集时，手写连接的唯一入口是 FromSql（Set 生成的源 SQL 无连接子句）。**口径区分：分组能力本身有两个入口**——`FromSql<T>(...)` 与 `Set<T>().Where(...)/AsQuery()` 都返回 `IEntityQuery<T>` 而达分组链（见 §6）；「唯一 FromSql」仅指源 SQL 需含 JOIN 的跨表场景。不提供 `Join/LeftJoin` 链点的理由：① Include 已覆盖「取关联对象」高频需求且天然规避扇出；② Join 链点 = 多实体元数据 + 表别名分配 + 列名限定 + ON 表达式翻译 + 连接类型 × WHERE × GROUP BY 组装矩阵，在表达式层重建小查询引擎；③ 连接行集的场景伴随连接细节诉求（预聚合消解扇出、ON 过滤、列裁剪），本就需要用户直接控 SQL。

| | Include（现有） | LEFT JOIN（FromSql 手写） | 声明式 Join 链点（不做） |
| --- | --- | --- | --- |
| 产出 | 对象图（主实体 + 导航集合） | 扁平行集 | 扁平行集 |
| SQL 形态 | 多条独立 SQL（分裂查询） | 单条 JOIN SQL | 单条 JOIN SQL |
| 1:N 行为 | 无扇出（子行装进集合属性） | 左表行按匹配数重复 | 同左 |
| 参与分组/聚合 | 否（导航在内存侧） | ✓ | 理论上可以 |
| ON 条件 | 按元数据外键固定 | 任意（用户写） | 需表达式翻译 |

## 4. SQL 产出路径设计

### 4.1 翻译规则（AggregateSqlTranslator，新建）

| 表达式形态 | SQL 产出 | 说明 |
| --- | --- | --- |
| `g.Key`（键为单成员 `x => x.A`） | `"A"`（方言引用） | 键列直接引用 |
| `g.Key.CustomerId`（键为匿名/初始化器） | `"CustomerId"` | MemberMappingParser 解析键形态后按成员名对应键列 |
| `g.Key`（键为匿名/初始化器，作为 Select 整体或排序目标） | 全部键列展开（`"CustomerId", "Status"`） | EF 平价：`Select(g => g.Key)` 复合键整体出列（匿名键走位置构造映射、命名键走成员绑定解析产物绑定）、`OrderBy(g => g.Key)` 全键同向排序；**多列 Select 内的复合 `g.Key` 拒绝**（位置映射无法承载展开，消息引导展开为 `g.Key.X` 逐成员） |
| `g.Count()` / `g.LongCount()` | `COUNT(*)` | |
| `g.Sum(x => x.Amount)` | `SUM("Amount")` | 属性须为数值列（按列类型校验，非数值抛 NotSupportedException） |
| `g.Average(x => x.Amount)` | 方言 `BuildAverageExpression`（SQL Server 产出 `AVG(CAST("Amount" AS DECIMAL(38,6)))`） | 与现有 `AverageAsync` 同约定——SQL Server 整型列裸 AVG 会截断小数，必须走方言表达式 |
| `g.Max(x => x.A)` / `g.Min(x => x.A)` | `MAX("A")` / `MIN("A")` | |
| `Having(g => g.Key.CustomerId == 5)` | `"CustomerId" = @p1` | HAVING 中引用键列合法（SQL 标准）；对键列条件与前置 Where 的行集语义等价（键列条件不改变组内聚合），EF 同形态也落 HAVING；键列间比较（`g.Key.A == g.Key.B`）同支持 |
| 比较运算 `g.Count() >= 5` | `COUNT(*) >= @p1` | 常量/闭包走 WhereClauseTranslator 同款参数化机制（ParameterNameAllocator 分配）；与 null 的 =/!= 翻译为 IS [NOT] NULL（`SUM` 全 NULL 值组返回 NULL，合法形态）；操作数交换（`5 <= g.Count()`）支持 |
| 逻辑组合 `a && b` | `a AND b` | 仅 && / \|\|；取反 `!` 支持 |

**别名机制（评审修正，SQL 合法性的关键）**：五方言的窗口构建器（SqlBuilderBase 的 OFFSET/FETCH 与 ROW_NUMBER 形态、MySQL/PostgreSQL/SQLite 的 LIMIT 形态、Oracle 两种形态）对 orderBy 的求值位置**都在分组查询之外的派生表层**——外层无 GROUP BY、也看不到内层列，裸聚合片段（`ORDER BY SUM("Amount")`）在外层是非法 SQL。因此：

- **聚合 SELECT 项一律生成别名**（`SqlIdentifierHelper.Generate("s")` + 方言引用，结构性防注入；键列保持自然列名不别名）；同一聚合片段在 Select 与排序中重复出现时按片段文本去重共用别名
- **排序片段 = 对分组 SELECT 列的引用**：键列 → 自然名引用、聚合 → 别名引用（链类型仍为 `(string SqlFragment, bool Descending)`，片段内容为引用而非聚合表达式）
- **分组 SELECT 列表暴露全部排序目标**：全部键列（自然名）+ Select/OrderBy 引用到的聚合（别名）；HAVING 聚合无需进 SELECT（组级求值合法）。副作用为正：分组 SELECT ⊆ GROUP BY 列 ∨ 聚合，MySQL 5.7+ ONLY_FULL_GROUP_BY 天然合规
- **投影裁剪包裹恒在**（见 §4.2）：外层按 Select 项配对序裁剪，位置读取契约保持

键选择器解析复用 MemberMappingParser 三形态分派：裸成员访问（None 形态，单键列）、匿名类型（Positional）、对象初始化器（Initializer）；键为常量/计算表达式（如 `x => x.A.ToUpper()`）拒绝——GROUP BY 键只能是实体列，防注入立场不变。成员解析的落点是 T 的列元数据（含扁平视图实体声明的连接列）；导航路径（`x.Customer.Name`）不是列元数据，拒绝（见 §3.4、§5）。键列与聚合列沿用 `ResolveMappedColumn` 同款资格守卫（IsJson/并发令牌/转换器变换列拒绝、EnumAsString 放行），Sum/Average 追加数值类型校验；Having 中的常量谓词（`g => true`）与布尔键列裸谓词按 WhereClauseTranslator 同款拒绝并引导比较运算。

### 4.2 SQL 组装顺序（与投影路径同构）

```
innerSql = PrepareWrappableBaseSql(源 SQL)          // 复用：手写顶层 ORDER BY 守卫 + TrimSql
groupedSql = SELECT [全部键列 + 引用到的聚合(别名)] FROM (innerSql) 别名
             GROUP BY 键列组
             [HAVING 谓词片段]
窗口态？→ wrappedSql = BuildWindowSqlAsync(groupedSql, skip, take, ORDER BY [排序引用片段], appendRowOrder: false)
executedSql = SELECT [Select 项引用（键=列名，聚合=别名）] FROM (wrappedSql ?? groupedSql) 别名
              [ORDER BY [排序引用片段]]             // 纯排序时在外层兑现行序（appendRowOrder=false 的行序契约在此补齐）
```

- **裁剪包裹恒在**：分组层暴露「全部键列 + 引用到的聚合」超集，外层按 Select 项配对序裁剪为恰好 N 列——投影 mapper 的 FieldCount == 列数契约由此保持；这也是「无状态」形态（ProjectedEntityQuery 的 WrapDerivedTable 本就无条件包裹，逐行同构）
- **窗口包裹成立的前提**：groupedSql 内无裸 ORDER BY——PrepareWrappableBaseSql 已守卫，排序链全部作用于窗口层与外层（与投影路径 WrapDerivedTable 的行序契约完全一致）
- **分组查询无窗口态校验豁免**：ValidateWindowState 原样适用（窗口必须配排序）；窗口的 orderBy 与外层 ORDER BY 使用同一组排序引用片段（别名/键列名在包裹层均可见）
- **GroupBy 链点前置状态校验**：调用 GroupBy 时排序链/窗口状态必须为空（`OrderBy(...).GroupBy(...)`、`Skip/Take` 先于 GroupBy 均拒绝）——源行序对分组结果无意义，分组结果序由 GroupBy 之后的排序链决定；窗口语义属分组后结果集
- **排序链类型独立**：分组排序项是 `(string SqlFragment, bool Descending)`（片段为分组 SELECT 列的方言引用：键列名/聚合别名），不复用 `(ColumnMetadata, bool)`、不进 BuildOrderClause——避免为聚合片段泛化现有排序链类型
- **CountAsync**：无 Select 时 groupedSql 的 SELECT 列表 = 全部键列 + 排序目标聚合（别名）；窗口态生效时包裹窗口（行序无关，appendRowOrder: false），否则忽略排序链（与 EntityQueryBase 聚合路径忽略排序的约定一致）

### 4.3 结果读取：位置读取直接复用投影 mapper（已核实）

EntityMapperCore 的投影 mapper 按**位置**读取（L185-186 强制 `reader.FieldCount == 列数`，getter 按 `reader.GetFieldType(i)` + 目标成员类型编译，Members 解析产物注释明确「Members[0] = 第 0 列的落点」）。聚合计算列天然适配：

- SELECT 列表顺序 = Select lambda 配对序（MemberMappingParser 绑定序/构造参数序）
- 聚合列带生成别名 → 读取器列名与投影列元数据名不符 → mapper 仅记录诊断告警不抛异常（EntityMapperCore L188-196 明示「聚合列的列名与投影属性名不同属合法形态」）；键列自然名引用则名实相符零告警
- `SUM(int)` 多数方言返回 bigint、`AVG` 返回 numeric/decimal——getter 按 reader 报告类型编译，DTO 属性用 `long`/`decimal` 即可（文档注明）
- 键列不加聚合也合法（`SELECT SUM(...) FROM t GROUP BY "A"` 标准语义），位置映射下 Select 可省略键（分组层恒暴露全部键列，外层裁剪后不出现）

**COUNT(*) 的源列元数据缺口（mapper 唯一扩展点）**：投影 mapper 每个位置需要 `ColumnMetadata`（转换链、getter 选择从它出发），但 `g.Count()/LongCount()` 没有源列。方案：`GetOrAddProjectionMapper`/`CreateScalarReadExpression` 增加「列元数据为 null = 裸读取」分支——`EntityValueConverter.ConvertFrom(reader.GetValue(i), targetType)` + DBNull → `default(targetType)`（COUNT 结果恒非 NULL、整数族，裸读取语义完备）；列名告警循环跳过 null 元数据；`ProjectionMapperKey` 对 null 元素天然安全（`ReferenceEquals` 与 `RuntimeHelpers.GetHashCode(null)` 均成立）。mapper 缓存键注释本就预留了「COUNT(*) 等聚合列」形态，本扩展是该预期的落地。

## 5. v1 拒绝清单（与「合法就不拒绝」的边界）

| 拒绝项 | 消息引导 |
| --- | --- |
| 无 GroupBy 直接 Having | Having 必须跟在 GroupBy 之后（裸 HAVING 语义罕见，徒增分支） |
| 键/聚合内计算表达式（`x.A.ToUpper()`、`x.A * x.B`） | 键只能是实体列、聚合内只能是单成员访问——开算术需搬入整个运算翻译，收益不成比例 |
| 聚合间运算（`SUM(A) + SUM(B)`）与聚合间比较（`g.Count() == g.Sum(x => x.A)`） | 同上；注意聚合与键列的比较（`SUM(A) >= B`）合法支持——分组列在组级作用域可见，与键列对键列一样直拼 |
| 条件计数 `g.Count(x => x.A > 0)` | CASE WHEN 形态留 v1.5 |
| 无选择器聚合 `g.Sum()/g.Average()/g.Max()/g.Min()`（1 参形态） | 实体 T 非数值类型时编译期不可达；手搭表达式树可达时同样拒绝（分组聚合必须有明确列落点） |
| Having 常量谓词（`g => true`）与布尔键列裸谓词 | 过滤结果与数据库无关/裸列名不是合法谓词——引导比较运算（WhereClauseTranslator 同款守卫） |
| 多列 Select 内的复合 `g.Key`（`new { g.Key, Cnt = g.Count() }`） | 位置映射无法承载键展开——引导展开为 `g.Key.X` 逐成员；`Select(g => g.Key)` 整体形态支持 |
| 空键选择器（`GroupBy(x => new {})`，零配对形态） | 分组键至少需要一列 |
| keyset 分页 | 分组结果无行级唯一键，跨方言多列行值比较不可移植 |
| `set.GroupBy(...)` 直链点（`IEntitySet<T>` 上无此链点） | Set 用户经 `Where/AsQuery` 桥接即达分组（见 §2、§6），直链点仅为语法糖，v1 不加 |
| GroupBy 后再 Where | 分组后的过滤语义就是 Having，不再接受 Where 链点 |
| GroupBy 前已有排序/窗口状态（`OrderBy/Skip/Take` 先于 GroupBy） | 源行序对分组无意义、窗口属分组后结果集——排序/窗口在 GroupBy 之后链 |
| 键/聚合引用导航路径（`x.Customer.Name`） | 手写 SQL 别名契约不可知、同名列撞名无法消解——连接列以列形态声明进扁平视图实体（见 §3.4） |

## 6. 交互矩阵

| 组合 | 语义 | v1 支持 |
| --- | --- | --- |
| Where → GroupBy | 过滤作用于源行 | ✓（现有链点天然前置） |
| GroupBy + Having | 分组后过滤 | ✓（聚合条件与键列条件均支持，多次 Having AND 合并） |
| GroupBy + OrderBy/ThenBy（键列或聚合） | 分组结果排序 | ✓（排序引用片段 = 键列名/聚合别名，窗口层与外层均可引用；未投影的聚合自动注入分组 SELECT 并由外层裁剪） |
| GroupBy + Skip/Take | 分组结果分页 | ✓（窗口包裹，ValidateWindowState 适用） |
| GroupBy + First/Single 系 | 取首个分组 | ✓（经 Select，同投影单条路径） |
| GroupBy + CountAsync | 分组组数 | ✓（`SELECT COUNT(*) FROM (分组 SQL)`；已设窗口时统计窗口内组数） |
| `Select(g => g.Key)`（复合键整体出列） | 键对象构造 | ✓（匿名键位置构造、命名键成员绑定解析产物绑定） |
| GroupBy + Include/导航 | 不适用 | 不支持——类型级不可达（IIncludableEntityQuery 只继承 IExecutableEntityQuery，无 GroupBy 链点） |
| GroupBy + keyset | 不适用 | 不支持（见 §5） |
| GroupBy 键含 NULL | SQL 标准：NULL 分为一组 | 支持（文档注明） |
| 源 SQL 含手写 JOIN + 扁平视图实体 | 跨表分组统计 | ✓（见 §3.4，键/聚合列按 T 元数据解析，外层包裹无感） |
| `Set<T>().Where/AsQuery → GroupBy` | 单表分组统计 | ✓（Set 查询链桥接 IEntityQuery，开箱即用——EntitySet.Select.cs L45-57 已核实） |

## 7. 实施落点

| 文件 | 动作 | 内容 |
| --- | --- | --- |
| Entity/Query/GroupedEntityQuery.cs | 新建 | 分组源查询：键列解析（MemberMappingParser）、Having/排序链收集、Skip/Take、Select 分派、CountAsync 终结 |
| Entity/Query/GroupedProjectionQuery.cs | 新建 | 分组投影终结执行器，**与 ProjectedEntityQuery 逐行同构**（本轮重构立的规矩）：ExecuteSingleAsync/ExecuteSingleCoreAsync/ToListAsync/BuildSql/WrapDerivedTable 骨架一致，差异仅 SQL 组装层（分组层 SELECT + GROUP BY/HAVING + 恒裁剪包裹，见 §4.2） |
| Entity/Query/IGroupedEntityQuery.cs | 新建 | 分组查询公共接口（§3.2 签名；独立文件与 IProjectedEntityQuery.cs 同组织） |
| Entity/SqlTranslation/AggregateSqlTranslator.cs | 新建 | §4.1 翻译规则单点：键选择器解析（三形态 + 资格守卫）、聚合调用匹配（Enumerable 静态调用白名单）、Having 谓词翻译（比较/逻辑/null 特判/常量拒绝）、排序目标解析 |
| Entity/Query/IEntityQuery.cs、EntityQuery.cs | 修改 | GroupBy 链点（前置状态校验 + 键解析 + GroupedEntityQuery 构造） |
| Entity/Mapper/EntityMapperCore.cs | 修改 | 投影 mapper「null 列元数据 = 裸读取」分支（COUNT(*) 无源列，见 §4.3；约 15 行，GetOrAddProjectionMapper 告警循环 + CreateScalarReadExpression 两处） |
| Entity/Query/IProjectedEntityQuery.cs | 复用 | 分组投影终结面直接实现该接口（First/Single/ToList 契约不变） |
| Entity/Query/EntityQuerySqlHelpers.cs | 不动 | ValidateWindowState/PrepareWrappableBaseSql/BuildWindowSqlAsync 原样复用 |
| docs 与 guides | 修改 | WhereClauseTranslator/IEntitySet 的「不支持 GroupBy」声明补充指向新能力范围说明（两处声明针对 WHERE 表达式翻译与 Set 路径，语义仍成立） |

## 8. 测试要点

1. **翻译正确性**：单键/复合键/初始化器键的 GROUP BY 列组；各聚合方法的片段形态（Count/LongCount → COUNT(*)、Sum/Max/Min 裸片段、Average 方言表达式）；聚合 SELECT 项生成别名且同片段去重共用；排序引用片段 = 键列名/聚合别名（裸聚合片段永不进入 ORDER BY）；Having 常量参数化（值不出现在 SQL 文本）与键列条件
2. **拒绝清单**：§5 每一项抛 NotSupportedException/InvalidOperationException 且消息可引导
3. **交互回归**：GroupBy + 窗口 + 排序的三层包裹 SQL 快照（五方言：裁剪层 × 窗口层 × 分组层）；GroupBy 后 keyset/Where 链点不可达（类型级）
4. **位置读取回归**：Select 省略键列、成员序与键序不一致、SUM 映射 long/AVG 映射 decimal、COUNT(*) 裸读取分支（null 元数据）、`Select(g => g.Key)` 复合键展开（匿名/命名键）
5. **集成**：SQLite/可用数据库的真分组结果正确性（计数、求和、NULL 键分组、HAVING 过滤、排序 + 窗口分页的组合正确性）
6. **连接组合**：源 SQL 含 LEFT JOIN + 扁平视图实体的分组端到端（键含右表列、聚合含左表列）；§3.4 两个陷阱的示例形态（右表预聚合子查询消解扇出）可正确执行
