# EntityContext 设计方案（未实现）

> 状态：设计定稿（含 2026-11-21 评审修订），待实施。本文档来自 2026-11-21 的方案讨论与评审，实施时以此为准。

## 1. 背景与动机

当前实体层的所有入口都是 `IDbAccess` 的扩展方法（[DbAccessEntityExtensions.cs](../Xbc.DbAccess/Entity/DbAccessEntityExtensions.cs)：`FromSql<T>()`、`Set<T>()`），实体配置全部依赖特性（`Table` / `PrimaryKey` / `Column` / `NotMapped` / `DbNullHandling`）。这带来三个结构性问题：

1. **认知入口错位**：想以 EF Core 方式使用框架的用户必须先理解基础设施概念（连接工厂、ProviderFactory）才能碰到实体层——学习路径是「建工厂 → 建 IDbAccess → 找扩展方法」，EF 的是「建 DbContext → 用 Set」
2. **接口过宽的暴露**：`IDbAccess` 同时承载 30+ 原生方法（Execute 四类 × 2 重载、事务、连接作用域、ServerInfo、存储过程）与实体入口，只想 CRUD 的用户面对整个原生面
3. **配置硬编码**：实体类必须可修改且挂特性——第三方 POCO（不可改源的类）无法接入

## 2. 目标与非目标

**目标**：

- 提供统一门面 `EntityContext`：一步构造、聚合实体操作与查询入口
- 提供 Fluent 配置 API（`OnModelCreating`），特性继续 100% 支持，Fluent 为覆盖层
- 零执行管线改动——门面是组合层，不新增抽象

**非目标**（明确不做，避免虚张声势）：

- **完整变更跟踪**（identity map、实体快照与脏检测、查询跟踪选项、只提交脏列）——框架定位是「表达式翻译 + 无状态映射」；UoW 采用**显式操作缓冲**而非自动跟踪（见 §3.4），改属性不调 `Update` 不会提交
- context 级独立模型（见 §4.3 的作用域决策，采用进程级全局模型）

## 3. 一期：EntityContext 门面

### 3.1 API 面

```csharp
public sealed class EntityContext
{
    // 一步构造（内部组装 工厂 + DbAccess，学习路径两步并一步）
    public EntityContext(string connectionString, IDbProviderFactory factory);

    // 注入既有实例（与 Xbc.Tenancy 租户路由衔接：路由结果直接注入）
    public EntityContext(IDbAccess dbAccess);

    // 实体操作（EF 心智；Set 按 T 缓存，幂等——两次取同一引用）
    public IEntitySet<T> Set<T>() where T : class;
    public IEntityQuery<T> FromSql<T>(string sql, object param = null) where T : class;

    // 写入与事务通道
    public Task<long> BulkInsertAsync(BulkInsertContext context, BulkInsertOptions options = null, CancellationToken ct = default);
    public Task<INestedDbTransaction> BeginTransactionAsync(CancellationToken ct = default);

    // 高级出口：原生能力显式降级（标量/DataSet/存储过程/连接泄漏/连接作用域）
    public IDbAccess Database { get; }
}
```

设计要点：

- **命名避开 `DbContext`**——用户同时引用 EF 时避免符号冲突与概念混淆
- **`Set<T>()` 幂等缓存**：context 实例内按 T 的字典缓存 `EntitySet<T>`，对齐 EF `context.Set<T>()` 的引用相等语义
- **`Database` 显式出口**：原生能力不消失，但从「默认可见」降为「显式索取」，解决接口过宽的认知问题
- **零管线改动**：`Set<T>()` 内部即现有 `new EntitySet<T>(dbAccess)`，`FromSql<T>` 同理，仅多一层转发与缓存

### 3.2 生命周期与拥有权

框架事实：`IDbAccess : IDisposable`（实现类 `DbAccess : DisposableBase`），**不持有连接**（池化、即取即还），Dispose 清理的是连接追踪器、异步流作用域管理器等内部基础设施。因此 `DbAccess` 是可释放的长生命周期对象。

| 维度 | 设计 |
| --- | --- |
| 创建责任 | 调用方 / DI 容器 |
| 是否 IDisposable | 是，语义跟随 IDbAccess |
| 拥有权规则 | **注入构造**：context 不拥有 dbAccess，Dispose 只清自身（Set 缓存），不处置注入对象——谁注入谁释放；**便捷构造**：context 拥有内部创建的 DbAccess，Dispose 级联释放。实现用 `ownsDatabase` 标志区分 |
| 连接 | 不在 context 持有，与 context 生命周期完全解耦 |
| 事务 | `BeginTransactionAsync` 返回的事务由调用方 `using` + Commit，独立于 context |
| Set 缓存 | 随 context 实例存亡 |

生命周期建议**按使用模式二分**（评审修订：Set 缓存无状态可安全单例——`EntitySet<T>` 已核实全 readonly——但 UoW 缓冲队列引入了真正的实例状态）：

| 使用模式 | 推荐注册 | 理由 |
| --- | --- | --- |
| 仅立即通道（`Set<T>()` 方法、`FromSql<T>`、BulkInsert） | **Singleton 安全** | 无状态、线程安全，Set 缓存跨请求复用 |
| 使用 UoW 缓冲通道（`Add/Update/Remove/SaveChangesAsync`） | **必须 Scoped / 每操作新建** | 缓冲队列非线程安全，单例下跨请求共享即数据串号（A 请求 Add 的实体进入 B 请求的 SaveChanges）——EF 用 Scoped 天然规避，同理 |

### 3.3 现有 API 并存

`dbAccess.Set<T>()` / `dbAccess.FromSql<T>()` 全部保留，`context.Set<T>()` 是新增入口不是替代。两个入口在实现上共享同一套类型。

### 3.4 工作单元（UoW）：显式操作缓冲

定位：**操作缓冲，非变更跟踪**。缓冲「(操作类型, 实体引用)」条目，`SaveChangesAsync` 聚合提交获得原子性——UoW 的核心价值（一次业务动作多个增删改共享事务、失败全回滚）在此，变更跟踪的自动化部分明确不在。

#### API

```csharp
public sealed class EntityContext
{
    // 缓冲通道（同步注册，零 IO——不执行任何 SQL，纯内存入队）
    public void Add<T>(T entity) where T : class;
    public void AddRange<T>(IEnumerable<T> entities) where T : class;
    public void Update<T>(T entity) where T : class;      // 注册全列更新
    public void Remove<T>(T entity) where T : class;      // 注册按主键删除
    public Task<int> SaveChangesAsync(CancellationToken ct = default);   // 返回受影响行数
}
```

#### 数据结构与双通道

```csharp
private readonly List<BufferedOperation> bufferedOperations = new();
private readonly record struct BufferedOperation(Type EntityType, BufferedOpKind Kind, object Entity);
private enum BufferedOpKind { Add, Update, Remove }
```

- **缓冲实体引用而非拷贝**：注册后、SaveChanges 前对实体属性的修改在提交时生效（与 EF 一致）
- **双通道并存**（EF 用户心智陷阱——`Update` 在 EF 里是缓冲的，此处立即方法仅靠 `Async` 后缀区分）：`context.Set<T>().InsertAsync/UpdateAsync/DeleteAsync(...)` 等现有方法**立即执行**（契约不动，绕过缓冲）；顶层 `Add/Update/Remove` 是缓冲通道，`SaveChangesAsync` 时才借道 Set 的方法执行——队列提供操作清单，Set 实例缓存（§3.1）提供执行载体。立即方法的 doc 需加一行「立即执行，不经 SaveChangesAsync 缓冲」

#### SaveChangesAsync 编排与事务策略（对齐 EF Core）

| 缓冲条数 | 行为 | 理由 |
| --- | --- | --- |
| 0 | 返回 0，不开事务 | 无操作无代价 |
| 1 | **直接执行该操作**，不开显式事务 | 单条操作恰为单条 SQL，依赖数据库单语句原子性，省 `BEGIN/COMMIT` 往返（EF Core 同策略） |
| ≥ 2 | `BeginTransactionAsync` → 按注册顺序逐条执行 → Commit；异常 Rollback | 聚合提交的原子性；外部已有活动事务时经现有保存点机制嵌套为子事务 |

- **顺序 = 注册顺序**，不做依赖拓扑排序（无外键感知）——引用完整性由数据库约束或注册顺序保证
- **成功后清空缓冲**（EF 同语义：变更已应用，重复 SaveChanges 返回 0）
- **失败保留缓冲**：事务 Rollback 后数据库回到干净状态，**不清空队列**——用户修正原因后重试 `SaveChangesAsync` 等价首次提交（EF 同语义）
- 执行时按 Kind 分派到 Set 的现有方法：`Add → InsertAsync(entity)`、`Update → UpdateAsync(entity)`（全列）、`Remove → DeleteAsync(entity)`（按主键）
- **缓冲通道 0 行统一抛异常**：现有 `UpdateAsync`/`DeleteAsync` 仅在配置并发令牌时对 0 行抛 `DbConcurrencyException`，无令牌列 0 行静默——缓冲通道对 Update/Remove 影响 0 行**统一抛**（显式操作语义下静默更危险，对齐 EF 的 `DbUpdateConcurrencyException` 习惯）；立即通道保持现有契约不变
- **非线程安全**：缓冲列表单线程使用（与 EF DbContext 相同约束）

#### 缓冲操作边界语义：顺序日志，不猜意图

缓冲是**操作日志**而非状态机——用户写什么执行什么，严格按注册顺序分派。同键/同实例的乱序操作逐个推演（假设自增主键实体先 Add）：

| 操作序列 | 顺序执行结果 | 评价 |
| --- | --- | --- |
| `Add(u)` → `Update(u)`（同实例） | INSERT（回填 Id）→ UPDATE 同实例 | ✓ 语义正确——引用同实例使回填对后续操作可见 |
| `Add(u)` → `Update(v)`（不同实例，显式主键） | INSERT u → UPDATE v | ✓ 净效果 = v |
| `Add(u)` → `Remove(u)`（同实例） | INSERT → DELETE | ⚠️ 结果正确但**真实执行插入又删除**——自增序号消耗、触发器/审计留痕；EF 的状态机会合并为不执行，本框架的日志语义不合并 |
| `Update(u)` → `Remove(u)` | UPDATE 全列 → DELETE | ✓ 净效果正确（多一次无谓 UPDATE） |
| 同实例连续 `Update(u)` × 2 | UPDATE × 2 | ✓ 第二次覆盖，仅多一次往返 |

**两条防御规则**（SaveChanges 分派时执行）：

1. **Update/Remove 前校验主键可定位**：自增主键仍为 CLR 默认值（未回填）时抛明确异常——否则 `UPDATE WHERE Id=0` 会静默更新 0 行
2. **影响行数语义（评审修订）**：缓冲通道对 Update/Remove 影响 0 行**统一抛异常**（见「SaveChangesAsync 编排与事务策略」），立即通道对 0 行的现有契约原样沿用

**不做状态机合并（EF 的 Add+Remove 抵消）的理由**：合并即猜测意图——`Add(u) → Remove(u)` 是「取消插入」还是「插入后删除」（触发器/审计需要痕迹），框架无法分辨；合并对前者友好、对后者是错误行为。操作日志语义不猜：可推理、可预测，想取消就别 Add——与「合法就不拒绝」的哲学一致。

**自增列策略（已核实）**：`InsertAsync` 对自增列值为 CLR 默认值时**省略该列**（数据库生成），有显式值时**显式携带**（EntitySet.Insert.cs 构建列清单处）——`Remove(u) → Add(u)` 删除重建场景安全（Id 不被改写）。注意**回填时机 = `SaveChangesAsync` 执行时**：缓冲期间读 `u.Id` 是 CLR 默认值（EF 用负数临时键标记，本框架无临时标记），用户不得在缓冲期间用 Id 做业务判断。

#### 与 EF 的两条已知差距（自觉取舍，文档需注明）

1. **显式 vs 自动**：EF 改属性不调 `Update` 也会提交（脏检测）；本方案不说就不提交——「操作缓冲，非变更跟踪」必须写入用户文档
2. **Update 全列 SET**：EF 只提交脏列；本方案走现有全列更新——并发写冲突面更宽（乐观令牌兜底）

## 4. 二期：Fluent 配置

### 4.1 API 面

```csharp
public class AppContext : EntityContext
{
    protected override void OnModelCreating(EntityModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("t_user");
            b.HasKey(x => x.Id, isIdentity: true);
            b.Property(x => x.Name).HasColumnName("user_name");
            b.Property(x => x.Number).HasNullHandling(DbNullHandling.ProviderException);
            b.Ignore(x => x.Temp);
        });
    }
}
```

覆盖能力与现有特性一一对应：`ToTable`（`TableAttribute`）、`HasKey`（`PrimaryKeyAttribute`）、`HasColumnName`（`ColumnAttribute`）、`Ignore`（`NotMappedAttribute`）、`HasNullHandling`（`ColumnAttribute.DbNullHandling`）。第三方 POCO（不可改源）由此接入。

### 4.2 底层落点

`EntityMetadataCache` 的元数据构建管道加一层：**特性扫描 → Fluent 应用（同名成员以 Fluent 为准）→ 冻结**。

- 特性 100% 继续支持，Fluent 是覆盖层不是替代
- 同名成员冲突不抛异常，Fluent 优先（文档注明）

### 4.3 配置作用域：进程级单模型（关键决策）

EF 是 context 级模型，但本框架的 `EntityMetadataCache<T>` 是进程级按实体类型静态缓存——context 级覆盖会导致缓存键复杂化，且**多个 context 子类各自配置同一实体会静默互相覆盖**（元数据缓存只有一份）。**决策：进程级单模型制**——

- 全局仅构建**一次**模型，所有 context 共享
- `OnModelCreating` 是全局配置的便捷入口，但全局仅执行**一次**（首个被使用的 context 类型触发）
- 第二个定义了 `OnModelCreating` 的 context 子类被使用时**抛明确异常**（防静默覆盖）——文档注明「一个进程一个模型」，多租户差异化走 Tenancy 的连接路由而非模型差异

### 4.4 OnModelCreating 触发机制（全局单次）

```
首次需要实体元数据（如第一次 Set<User>() 触发映射）
  → EnsureModelBuilt()                     // 进程级构建标志（全局单次，不按 context 类型缓存）
  → 未构建？执行首个被使用 context 的 OnModelCreating(modelBuilder)
  → Fluent 配置应用进 EntityMetadataCache（特性扫描 → Fluent 覆盖 → 冻结）
  → 标记已构建，进程内永不再调；后续 context 子类定义的 OnModelCreating 触发即抛异常
```

- **默认懒触发**：首次使用实体时构建；缺点是配置错误（列名拼错、重复绑定）延迟到首次使用才暴露，且首个请求承担构建延迟
- **强建议**：应用启动期调用 `context.SealModel()` 把全部模型构建前移到启动期——不是可选优化，是标准用法

## 5. 实施注意事项

1. **命名**：门面类不叫 DbContext；`EntityModelBuilder` 可与 EF 的 `ModelBuilder` 同名（不同命名空间无冲突）
2. **测试**：一期测门面转发、Set 幂等、Dispose 拥有权（注入不级联 / 便捷构造级联）；二期测 Fluent 覆盖特性、与特性混用、`OnModelCreating` 单次执行、SealModel 预热
3. **文档**：guides/Xbc.DbAccess 增加快速上手章节，按「连接 → 手写 SQL → 实体 CRUD（EntityContext）→ 表达式查询」分层引导
4. **分期实施**：一期纯加法（一个新类 + 测试），二期动 `EntityMetadataCache` 构建管道（真正需要设计的部分），两期可独立交付

## 6. 设计评审结论（2026-11-21）

P0 两项（生命周期二分 → §3.2、单模型制 → §4.3）与 P1 三项（缓冲通道 0 行统一抛、双通道方法名心智、自增列策略核实 → §3.4）已修订入正文。以下为文档注明即可的次要项：

| 级别 | 结论 | 处置 |
| --- | --- | --- |
| P2 | context Dispose 后 Set 缓存仍可复用，撞已释放的 dbAccess | doc 注明「Dispose 后不可再用」（DisposableBase 惯例） |
| P2 | `SaveChangesAsync` 返回**行数**，EF 返回**实体数**（无级联时两者相等，级联场景断言口径不同） | doc 注明口径 |
| P2 | BulkInsert 永远立即执行、不进缓冲 | doc 注明防混用 |
| P3 | `FromSql` 匿名对象参数每次执行物化（50 参数 4.7μs，可忽略） | 观察 |
| P3 | `Database` 出口可绕过门面直接开事务 | 同一 dbAccess 实例 + 保存点机制统一兜底，无隔离问题 |

评审核实依据（代码探查，2026-11-21）：`EntitySet<T>` 全 readonly 无状态（EntitySet.cs L13-50）；自增列 INSERT 分流——默认值省略 / 显式值携带（EntitySet.Insert.cs L34-47、L424-436）；并发令牌 0 行抛 `DbConcurrencyException`（EntitySet.Concurrency.cs L58-68）；事务嵌套为保存点（NestedDbTransactionManager.cs L89-118）。
