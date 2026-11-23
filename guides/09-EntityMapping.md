# 实体映射与缓存治理

本文介绍框架如何把 `DbDataReader` 的查询结果映射为实体（或投影）对象，以及映射委托缓存的治理方式。

## 目录

1. [映射如何发生](#1-映射如何发生)
2. [映射缓存机制](#2-映射缓存机制)
3. [缓存治理（EntityMapperCachePolicy）](#3-缓存治理entitymappercachepolicy)
4. [多结果集与 Reader 生命周期](#4-多结果集与-reader-生命周期)
5. [内部实现一览](#5-内部实现一览)
6. [完整示例](#6-完整示例)

## 1. 映射如何发生

应用侧不直接调用映射器，而是通过 `IDbAccess` 的公开扩展方法触发映射（命名空间 `Xbc.DbAccess.Entity`）：

| 公开入口 | 说明 |
|------|------|
| `db.FromSql<T>(sql, param)` | 手写 SQL 实体查询，返回 `IEntityQuery<T>`（单条/列表/排序窗口/`Select` 投影等） |
| `db.Set<T>()` | 表达式驱动的实体集合查询，返回 `IEntitySet<T>`，翻译为 SQL 后经同一映射机制执行 |
| `db.ExecuteMultipleAsync(sql, ...)` | 多结果集读取，返回 `EntityMultiReader`（见第 4 节） |

映射过程全自动：

- **列名 ↔ 属性自动对齐**：按实体元数据（`Table` / `Column` 等特性，见[实体类的定义](08-EntityDefinition.md)）把结果集列与实体属性对齐，`DBNull` 归一为 `null`。
- **按需编译委托**：首次遇到某个查询形状时，用表达式树编译一份强类型读取委托（性能接近手写赋值代码），之后相同形状直接复用。
- **投影同一机制**：`FromSql<T>(...).Select(u => u.UserName)` 的单列投影与匿名类型等多列投影，由同一套编译与缓存机制覆盖。

## 2. 映射缓存机制

编译出的映射委托按**查询形状**缓存在进程级缓存中，跨请求复用：

- **实体映射**：缓存键包含实体类型、结果集列架构（同一实体在不同 SQL 下列集合/顺序/字段类型可能不同）与全局值转换器注册版本。
- **投影映射**：缓存键包含列元数据、各位置字段类型与成员绑定；与结果集列数、顺序无关，同一列组跨查询形态共享同一份委托。

| 时刻 | 行为 | 频率 |
|------|------|------|
| 缓存 miss | 编译新委托并写入缓存 | 低频（首次遇到新查询形状） |
| 缓存命中 | 直接复用委托，仅刷新最后访问时间戳 | 高频（每次查询） |

注册自定义值转换器会使受影响的缓存条目自动失效重建，无需人工干预。

## 3. 缓存治理（EntityMapperCachePolicy）

默认缓存无上限。若应用存在**动态拼接裸 SQL、查询形状不可控**的场景（如报表引擎、动态筛选导出），委托缓存可能随形状增长而无界膨胀，此时应配置公开的 `EntityMapperCachePolicy`（静态类，建议启动期设置一次）：

```csharp
using Xbc.DbAccess.Entity;

// 缓存最大条目数（实体映射、投影映射两个缓存各自独立计算）；null = 无上限（默认）
// 超出时按最后访问时间移除最旧条目（LRU 语义：刚命中的条目天然最不容易被淘汰）
EntityMapperCachePolicy.MaxEntries = 1000;                       // 推荐值：常规应用 1000

// 条目空闲过期时长（自最后访问起算）；null = 不过期（默认）
EntityMapperCachePolicy.IdleExpiration = TimeSpan.FromHours(2);  // 推荐值：常规应用 2 小时
```

**触发时机**：清理在缓存插入期惰性触发（即 miss——编译新委托的低频时刻），命中路径永不触发；另有显式清扫入口 `EntityMapperCachePolicy.Sweep()`，供宿主（如 ASP.NET 的 `HostedService`）在长驻且无新查询形状产生的场景下定时调用——有查询流量时无需手动调用。

**淘汰是良性的**：

- 已持有的委托引用不受淘汰影响，行为不变；
- 被淘汰的形状再次被查询时走正常 miss → 重新编译路径，成本与首次编译一致。

**注意**：条目上限为软上限（扫描与插入间无原子性，并发插入可能使瞬时条目数略超上限）；策略属性写入无原子性保证，运行期热改仅作启发式生效。一般应用保持默认（不配置）即可。

## 4. 多结果集与 Reader 生命周期

存储过程或批量 SQL 可返回多个结果集，公开入口是 `db.ExecuteMultipleAsync` + `EntityMultiReader`：

```csharp
// 文本 SQL 多结果集
using var multi = await db.ExecuteMultipleAsync("SELECT * FROM Users; SELECT * FROM Orders");

var users = await multi.ReadAsync<User>();    // 第一个结果集 → List<User>，随后自动移动到下一结果集
var orders = await multi.ReadAsync<Order>();  // 第二个结果集 → List<Order>

// 存储过程多结果集
using var sp = await db.ExecuteMultipleAsync(
    "sp_GetUserAndOrders", CommandType.StoredProcedure, new { UserId = 1 });
var user = await sp.ReadFirstAsync<User>();   // 当前结果集第一条，随后自动移动
```

`EntityMultiReader` 公开方法：

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `ReadAsync<T>(cancellationToken)` | `Task<List<T>>` | 读取当前结果集全部记录，然后自动移动到下一结果集 |
| `ReadFirstAsync<T>(cancellationToken)` | `Task<T>` | 读取当前结果集第一条记录，然后自动移动到下一结果集 |
| `ReadAsync(entityType, cancellationToken)` | `Task<List<object>>` | 非泛型重载（运行时类型不确定时使用） |
| `ReadFirstAsync(entityType, cancellationToken)` | `Task<object>` | 非泛型重载 |

生命周期要点：

- `EntityMultiReader` 实现 `IDisposable`，用 `using` 释放（会一并释放底层 `DbDataReader`）；
- 读取顺序必须与 SQL 返回的结果集顺序一致；越过最后一个结果集后再读取会抛出 `InvalidOperationException`（避免结果集数量与读取次数不匹配时静默返回空结果）；
- 读取动作会消费 Reader——需要重新读取时必须重新执行查询。

## 5. 内部实现一览

框架的映射核心（`EntityMapper<T>` / `EntityMapper` / `EntityMapperCore`）均为 `internal`，应用代码不可直接调用；它们被 `FromSql<T>` / `Set<T>` / `ExecuteMultipleAsync` 内部使用。应用侧唯一可编排的扩展点是第 3 节的映射缓存策略 `EntityMapperCachePolicy`。

## 6. 完整示例

```csharp
using System.Data;
using Xbc.DbAccess;
using Xbc.DbAccess.Entity;

// 启动期配置一次映射缓存治理（按需；默认无界缓存）
EntityMapperCachePolicy.MaxEntries = 1000;
EntityMapperCachePolicy.IdleExpiration = TimeSpan.FromHours(2);

// 手写 SQL 实体查询（无数据返回 null）
var user = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Id = @Id",
    new { Id = 1 }).FirstOrDefaultAsync();
var users = await db.FromSql<User>("SELECT * FROM Users").ToListAsync();

// 投影
var names = await db.FromSql<User>("SELECT * FROM Users")
    .Select(u => u.UserName)
    .ToListAsync();

// 多结果集
using var multi = await db.ExecuteMultipleAsync("SELECT * FROM Users; SELECT * FROM Orders");
var allUsers = await multi.ReadAsync<User>();
var allOrders = await multi.ReadAsync<Order>();
```

**完整代码**：可运行的完整示例参见 [examples/09_EntityMapper/Program.cs](./examples/09_EntityMapper/Program.cs)，运行方式：

```bash
cd guides/examples/09_EntityMapper
dotnet run
```

## 相关文档

- [实体类的定义](08-EntityDefinition.md) - 通过特性配置实体类，控制列映射行为。
- [使用 IEntityQuery](10-EntityQuery.md) - `FromSql<T>` 返回的查询对象的完整能力。

## 下一步

- [使用 IEntitySet](11-EntitySet.md) - 使用 `db.Set<T>()` 进行表达式驱动的查询与写入。
