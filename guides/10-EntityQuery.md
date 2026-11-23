# 使用 IEntityQuery

本文档介绍如何使用 `IEntityQuery<T>` 接口进行实体查询：SQL 查询、表达式谓词查询、排序与分页、投影、聚合与分组、导航属性加载。

## 目录

1. [IEntityQuery 概述](#1-ientityquery-概述)
2. [创建查询对象](#2-创建查询对象)
3. [查询单条数据](#3-查询单条数据)
4. [查询多条数据](#4-查询多条数据)
5. [排序链](#5-排序链)
6. [窗口分页（Skip/Take）](#6-窗口分页skiptake)
7. [keyset 分页](#7-keyset-分页)
8. [投影（Select）](#8-投影select)
9. [聚合方法](#9-聚合方法)
10. [分组聚合（GroupBy / Having）](#10-分组聚合groupby--having)
11. [加载导航属性](#11-加载导航属性)
12. [使用取消令牌](#12-使用取消令牌)
13. [完整示例](#13-完整示例)

## 1. IEntityQuery 概述

`IEntityQuery<T>` 是框架提供的高级查询接口，封装了 SQL 执行和实体映射的完整流程。

### 1.1 主要特点

| 特点 | 说明 |
|------|------|
| 自动映射 | 内部使用 `EntityMapper<T>` 进行实体映射 |
| 链式构建 | 排序、窗口、投影、分组均链式组合（不可变构建器，原实例不变） |
| 参数化查询 | 支持匿名对象参数，自动转换；表达式中的值一律参数化 |
| 仅提供异步方法 | 所有枚举方法均为异步，全部支持 `CancellationToken` |
| 导航属性加载 | 支持 `Include` / `ThenInclude`（分裂查询） |
| 类型安全防注入 | 列名经方言标识符引用、值永不内联进 SQL 文本 |

### 1.2 使用约束

- 查询对象**不可并发执行**：链式派生的实例与原实例共享同一组参数，并发提交会引发参数归属冲突；串行复用安全。
- 聚合方法返回标量，不装配导航属性，链上已构建的 `Include`/`ThenInclude` 会被忽略。

## 2. 创建查询对象

查询对象有三个入口，均位于 `Xbc.DbAccess.Entity` 命名空间：

### 2.1 FromSql&lt;T&gt; - 手写 SQL 查询

以手写 SQL 创建实体查询（仅支持文本 SQL：分页与导航属性分裂查询均基于文本 SQL 改写/拼接）：

```csharp
// SQL 与参数在创建时指定，后续枚举方法无需再传 SQL
IEntityQuery<User> query = db.FromSql<User>(
    "SELECT * FROM Users WHERE Status = @Status",
    new { Status = 1 });
```

### 2.2 Set&lt;T&gt;().Where - 表达式谓词查询

`IEntitySet<T>` 的 `Where` 方法按谓词表达式构建查询：谓词经表达式翻译器转译为 WHERE 片段，生成全列 SELECT，返回可继续组合的 `IEntityQuery<T>`：

```csharp
// 谓词表达式 → 参数化 WHERE 子句
var adults = await db.Set<User>().Where(u => u.Age >= 18).ToListAsync();
```

**谓词表达式支持的能力**（与条件 UPDATE/DELETE 一致）：

| 能力 | 示例 | SQL 形态 |
|------|------|----------|
| 比较运算 | `u => u.Age > 18` | 参数化比较 |
| 逻辑运算 | `u => u.Age >= 18 && u.Status == 1` | `AND` / `OR` / `NOT` |
| 布尔列直接作谓词 | `u => u.IsActive` | 直接引用列 |
| 与 null 比较 | `u => u.Email == null` | `IS NULL` / `IS NOT NULL` |
| 可空列 HasValue | `u => u.Remark.HasValue` | `IS NOT NULL` |
| 集合 Contains | `u => ids.Contains(u.Id)` | `IN`（集合展开） |
| 字符串匹配 | `u => u.Name.Contains("张")` | `LIKE`（通配符自动全转义） |
| 前缀 / 后缀 | `u => u.Name.StartsWith("A")` / `EndsWith` | `LIKE` |
| 空串判断 | `u => string.IsNullOrEmpty(u.Name)` | 对应 SQL 形态 |
| 字符串变换 | `u => u.Name.ToLower() == "abc"` | `LOWER` 等函数 |
| 日期部分 | `u => u.CreatedAt.Year == 2026` | `YEAR` 等方言日期部分 |

**约束**：

- 闭包变量一律参数化，永不内联进 SQL 文本
- 谓词内不支持：投影、Join、子查询、算术运算、导航属性嵌套（抛出异常）
- 按主键查找等价于 `Where(u => u.Id == key).SingleOrDefaultAsync()`

### 2.3 Set&lt;T&gt;().AsQuery - 全表查询链

`AsQuery` 将实体集合转换为无条件的全表查询链（视角转换，不产生查询）：

```csharp
// 全表 keyset 分页等无条件链式操作的入口
var page = await db.Set<User>().AsQuery()
    .OrderBy(u => u.UserId)
    .KeysetAfterAsync(null, 20);
```

## 3. 查询单条数据

### 3.1 方法列表

这些方法与 LINQ 中的同名方法具有相同的语义，均为异步方法：

| 方法 | 无记录时 | 多条记录时 | 用途 |
|------|----------|------------|------|
| `FirstAsync` | 抛出异常 | 返回第一条 | 确保有记录 |
| `FirstOrDefaultAsync` | 返回 null | 返回第一条 | 可能无记录 |
| `SingleAsync` | 抛出异常 | 抛出异常 | 确保只有一条 |
| `SingleOrDefaultAsync` | 返回 null | 抛出异常 | 最多一条 |

已设置 `Take` 窗口时，`SingleAsync` / `SingleOrDefaultAsync` 以窗口截断结果为准。

### 3.2 示例

```csharp
// FirstAsync：查询第一条记录，若不存在则抛出异常
User user = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Age > @Age",
    new { Age = 18 }).FirstAsync();

// FirstOrDefaultAsync：若不存在返回 null
User found = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Email = @Email",
    new { Email = "test@example.com" }).FirstOrDefaultAsync();

// SingleOrDefaultAsync：多条抛出异常
User single = await db.Set<User>().Where(u => u.Id == 1).SingleOrDefaultAsync();
```

## 4. 查询多条数据

### 4.1 ToListAsync

```csharp
// 查询所有活跃用户
List<User> users = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Status = @Status",
    new { Status = 1 }).ToListAsync();

// 表达式谓词 + 排序
List<User> adults = await db.Set<User>()
    .Where(u => u.Age >= 18)
    .OrderBy(u => u.UserName)
    .ToListAsync();
```

### 4.2 窗口分页（Skip/Take）

分页通过查询级排序 + `Skip`/`Take` 窗口组合完成（`Skip` 为绝对行号偏移）：

```csharp
// 分页查询用户（按创建时间倒序，第 1 页每页 10 条）
List<User> users = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Status = @Status",
    new { Status = 1 })
    .OrderByDescending(u => u.CreatedAt)
    .Skip(0)          // 第 1 页跳过 0 行；第 2 页为 Skip(10)
    .Take(10)         // 每页 10 条
    .ToListAsync();

Console.WriteLine($"第 1 页，共 {users.Count} 条记录");
```

> **注意**：窗口（Skip/Take）要求已设置查询级排序（未设置时抛 `InvalidOperationException`）；
> `Skip` 必须配 `Take` 使用（单独 OFFSET 部分方言不支持）；
> 建议以唯一列（如主键）作为末级排序键，避免同值行跨页重复或遗漏。
> 排序键仅支持根实体的映射列（如 `u.CreatedAt`），导航属性、计算表达式与 JSON 列不支持。
> 深分页（页码很大）场景建议改用 keyset 分页，避免大偏移扫描。

## 5. 排序链

排序通过 `OrderBy` / `OrderByDescending` 开启排序链，`ThenBy` / `ThenByDescending` 在链尾追加次级排序键：

```csharp
// 先按状态升序，同状态内按创建时间倒序，同时间按主键决出稳定先后
List<User> users = await db.Set<User>().AsQuery()
    .OrderBy(u => u.Status)
    .ThenByDescending(u => u.CreatedAt)
    .ThenBy(u => u.UserId)          // 末级建议用唯一列
    .ToListAsync();
```

**规则**：

- 多次调用 `OrderBy` / `OrderByDescending` 时，后者**替换整条排序链**（与 LINQ 语义一致）
- 未先调用 `OrderBy` / `OrderByDescending` 就调用 `ThenBy` 时抛 `InvalidOperationException`
- 查询 SQL 已含 `ORDER BY` 时再设置排序抛 `InvalidOperationException`
- 排序键仅支持根实体的单级映射列；导航属性、嵌套路径、计算表达式与 JSON 列抛 `NotSupportedException`

## 6. keyset 分页

keyset（游标）分页按**键集排序**返回游标之后（或之前）的一页，深分页性能稳定（无大偏移扫描）：

```csharp
// 查询链：先设置排序（键集与方向即排序链）
var query = db.Set<User>().AsQuery().OrderBy(u => u.UserId);

// 第一页：cursor 传 null
List<User> firstPage = await query.KeysetAfterAsync(null, 20);

// 下一页：以上一页最后一条为游标
if (firstPage.Count > 0)
{
    List<User> nextPage = await query.KeysetAfterAsync(firstPage[^1], 20);
}

// 向前翻页：以下一页第一条为游标，结果按键集排序方向输出
List<User> prevPage = await query.KeysetBeforeAsync(nextPage[0], 20);
```

**规则**：

- 键集与排序方向即查询级排序链（`OrderBy`/`ThenBy`），须先设置排序
- 与 `Skip`/`Take` 窗口互斥（同一查询不可同时设置）
- `cursor` 传 `null` 表示从第一页开始（仅 `KeysetAfterAsync` 支持）；`KeysetBeforeAsync` 的 cursor 不能为 null
- 无更多行时返回空列表；`pageSize` 必须为正数

## 7. 投影（Select）

`Select` 将查询结果投影为映射列的值（`SELECT 列组 FROM (查询)`），避免读取整行：

```csharp
// 单列投影
List<string> names = await db.Set<User>().AsQuery()
    .Select(u => u.UserName)
    .ToListAsync();

// 多列投影 - 匿名类型（构造器按位置映射）
var briefs = await db.Set<User>().AsQuery()
    .Where(u => u.Age >= 18)
    .Select(u => new { u.UserName, u.Age })
    .ToListAsync();

// 元组（须用 ValueTuple.Create 形态，元组字面量在表达式树中不可编译）
var tuples = await db.Set<User>().AsQuery()
    .Select(u => ValueTuple.Create(u.UserName, u.Age))
    .ToListAsync();

// 命名类型 - 带参构造（按编译器实参绑定的构造函数位置映射）
var dtos = await db.Set<User>().AsQuery()
    .Select(u => new UserDto(u.UserName, u.Age))
    .ToListAsync();

// 命名类型 - 对象初始化器（要求公共无参构造与可写成员）
var dtos2 = await db.Set<User>().AsQuery()
    .Select(u => new UserDto { Name = u.UserName, Age = u.Age })
    .ToListAsync();
```

**规则**：

- 各成员限于根实体的单级映射列；不支持计算表达式、嵌套成员路径与聚合
- 与 `Include` 编译期互斥：`Include` 后的查询链类型不提供 `Select`（投影结果不是实体，无法装配导航属性）
- 各列值转换与实体赋值一致（转换器列、枚举名列、JSON 列按各自配置转换）
- 投影是链的最后一步构建操作，之后只能调用终结方法（`FirstAsync` / `FirstOrDefaultAsync` / `SingleAsync` / `SingleOrDefaultAsync` / `ToListAsync`）；此前已设置的排序与 Skip/Take 窗口在执行时生效

## 8. 聚合方法

`IExecutableEntityQuery<T>` 提供一组服务端聚合方法（COUNT / EXISTS / SUM / AVG / MAX / MIN）：

```csharp
// COUNT（返回 long）
long count = await db.Set<User>().Where(u => u.Age >= 18).CountAsync();

// EXISTS
bool hasAdults = await db.Set<User>().Where(u => u.Age >= 18).AnyAsync();

// 全部满足（NOT EXISTS 反语义；空结果集返回 true）
bool allAdults = await db.Set<User>().Where(u => u.Status == 1)
    .AllAsync(u => u.Age >= 18);

// SUM / AVG：无匹配行返回 null，NULL 值行不参与，结果统一 decimal
decimal? totalAmount = await db.Set<Order>().Where(o => o.Status == OrderStatus.Paid)
    .SumAsync(o => o.Amount);
decimal? avgAmount = await db.Set<Order>().Where(o => o.Status == OrderStatus.Paid)
    .AverageAsync(o => o.Amount);

// MAX / MIN：仅支持单级映射列；有非空与可空两种重载
int? maxAge = await db.Set<User>().Where(u => u.Status == 1).MaxAsync(u => u.Age);
DateTime? earliest = await db.Set<User>().AsQuery().MinAsync(u => u.CreatedAt);
```

**规则**：

- 已设置的窗口（Skip/Take）生效（如统计窗口内行数）
- 基础 SQL 不能包含顶层 `ORDER BY`（派生表包裹限制，含顶层 ORDER BY 时抛 `InvalidOperationException`）
- 全表聚合请使用 `.AsQuery().SumAsync(...)` 形态（无条件时不要用 `Where` 拼恒真条件）

## 9. 分组聚合（GroupBy / Having）

`GroupBy` 按实体列分组，产出服务端聚合 SQL（`SELECT 键列与聚合片段 FROM (查询) GROUP BY ... HAVING ...`）。lambda 参数为 BCL 的 `IGrouping<TKey, TElement>`，与 EF Core 心智一致：

```csharp
// 按客户分组统计：键列 + 聚合值 → 匿名类型
var stats = await db.Set<Order>().AsQuery()
    .GroupBy(o => o.CustomerId)
    .Select(g => new
    {
        CustomerId = g.Key,
        OrderCount = g.Count(),
        TotalAmount = g.Sum(x => x.Amount)
    })
    .ToListAsync();

// HAVING 过滤 + 排序 + 窗口
var topCustomers = await db.Set<Order>().AsQuery()
    .GroupBy(o => o.CustomerId)
    .Having(g => g.Count() >= 5)                     // 分组后过滤（HAVING）
    .OrderByDescending(g => g.Sum(x => x.Amount))    // 按聚合排序
    .Take(10)                                        // 取前 10 组
    .Select(g => new { g.Key, Total = g.Sum(x => x.Amount) })
    .ToListAsync();

// 复合键（匿名类型或对象初始化器）
var byCustomerAndStatus = await db.Set<Order>().AsQuery()
    .GroupBy(o => new { o.CustomerId, o.Status })
    .Select(g => new { g.Key.CustomerId, g.Key.Status, Cnt = g.Count() })
    .ToListAsync();

// 统计分组组数（HAVING 过滤生效）
long groupCount = await db.Set<Order>().AsQuery()
    .GroupBy(o => o.CustomerId)
    .Having(g => g.Sum(x => x.Amount) > 1000)
    .CountAsync();
```

**规则**：

- 键选择器仅支持根实体的映射列访问（单列、匿名类型或对象初始化器）；计算表达式、常量、导航路径、元组与带参构造形态抛 `NotSupportedException`
- `GroupBy` 之前不能设置排序或窗口（源行序对分组结果无意义）；分组后的排序与窗口在分组结果集上链
- 分组后的过滤链点是 `Having`（分组接口不暴露 `Where`，语义即 HAVING）；多次调用 `Having` 按 `AND` 合并
- `Having` / 排序键支持聚合比较（`g.Count() >= 5`、`g.Sum(x => x.Amount)`）、键列比较（`g.Key.CustomerId == 5`）与逻辑组合；聚合间运算与条件计数抛 `NotSupportedException`
- 排序键支持分组键引用（`g.Key.X`，复合键 `g.Key` 展开为全部键列）与聚合调用；窗口（Skip/Take）作用于分组结果集（须先排序，`Take` 为正数）
- 分组查询不物化组序列，经 `Select` 出扁平行或 `CountAsync` 统计组数
- SUM 建议用 `long`、AVG 建议用 `decimal` 承接（各引擎返回类型有差异）；聚合列无别名读取按位置映射

## 10. 加载导航属性

使用 `Include` 加载导航属性，返回 `IIncludableEntityQuery` 支持通过 `ThenInclude` 加载多级导航。框架采用分裂查询策略：先执行用户 SQL 获取主实体，再根据主外键值查询关联表，最后在内存中装配：

```csharp
// 加载一级导航属性
var users = await db.Set<User>().AsQuery()
    .Include(u => u.Orders)
    .ToListAsync();

// 加载多个导航属性
var users2 = await db.Set<User>().AsQuery()
    .Include(u => u.Department)
    .Include(u => u.Orders)
    .ToListAsync();

// 通过 ThenInclude 加载多级导航
var users3 = await db.Set<User>().AsQuery()
    .Include(u => u.Orders)
    .ThenInclude(o => o.Details)
    .ToListAsync();
```

**注意**：
- 导航属性需使用 `[Navigation]` 特性标记，详见 [实体类的定义](08-EntityDefinition.md)。
- 如需保证多次查询间的数据一致性，请在查询前手动开启事务（推荐使用可重复读或更高的隔离级别）。

## 11. 使用取消令牌

所有查询方法均支持 `CancellationToken`：

```csharp
public async Task<List<User>> GetUsersAsync(CancellationToken cancellationToken)
{
    return await db.FromSql<User>(
        "SELECT * FROM Users WHERE Status = @Status",
        new { Status = 1 }).ToListAsync(cancellationToken);
}
```

## 12. 完整示例

```csharp
using Xbc.DbAccess;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.Sqlite;

// 手写 SQL + 匿名对象参数
var user = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Id = @Id",
    new { Id = 1 }).FirstOrDefaultAsync();

// 表达式谓词查询
var adults = await db.Set<User>().Where(u => u.Age >= 18).ToListAsync();

// 集合展开参数
var ids = new[] { 1, 2, 3 };
var users = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Id IN (@Ids)",
    new { Ids = ids }).ToListAsync();

// 窗口分页
var pageUsers = await db.Set<User>().AsQuery()
    .OrderBy(u => u.UserId)
    .Skip(0)
    .Take(10)
    .ToListAsync();

// keyset 分页
var firstPage = await db.Set<User>().AsQuery()
    .OrderBy(u => u.UserId)
    .KeysetAfterAsync(null, 20);

// 投影
var briefs = await db.Set<User>().AsQuery()
    .Select(u => new { u.UserName, u.Age })
    .ToListAsync();

// 聚合
long total = await db.Set<User>().AsQuery().CountAsync();
decimal? avgAge = await db.Set<User>().AsQuery().AverageAsync(u => u.Age);

// 分组聚合
var stats = await db.Set<User>().AsQuery()
    .GroupBy(u => u.Status)
    .Select(g => new { Status = g.Key, Cnt = g.Count() })
    .ToListAsync();

// 加载导航属性
var usersWithOrders = await db.Set<User>().AsQuery()
    .Include(u => u.Orders)
    .ToListAsync();
```

**完整代码**：可运行的完整示例代码请参见 [examples/10\_EntityQuery/Program.cs](./examples/10_EntityQuery/Program.cs)

**运行示例**：
```bash
cd examples/10_EntityQuery
dotnet run
```

## 相关文档

- [实体类的定义](08-EntityDefinition.md) - 了解如何通过特性配置实体类，控制映射行为。
- [实体映射与缓存治理](09-EntityMapping.md) - 了解实体映射机制与映射缓存治理。

## 下一步

- [实体集操作](11-EntitySet.md) - 学习如何使用 `EntitySet<T>` 进行增删改查操作。
