# 使用 IEntityQuery

本文档介绍如何使用 `IEntityQuery<T>` 接口进行实体查询。

## 目录

1. [IEntityQuery 概述](#1-ientityquery-概述)
2. [查询单条数据](#2-查询单条数据)
3. [查询多条数据](#3-查询多条数据)
4. [加载导航属性](#4-加载导航属性)
5. [使用取消令牌](#5-使用取消令牌)
6. [完整示例](#6-完整示例)

## 1. IEntityQuery 概述

`IEntityQuery<T>` 是框架提供的高级查询接口，封装了 SQL 执行和实体映射的完整流程。

### 1.1 主要特点

| 特点 | 说明 |
|------|------|
| 自动映射 | 内部使用 `EntityMapper<T>` 进行实体映射 |
| 参数化查询 | 支持匿名对象参数，自动转换 |
| 仅提供异步方法 | 所有枚举方法均为异步，全部支持 `CancellationToken` |
| 导航属性加载 | 支持 `Include` / `ThenInclude`（分裂查询） |

### 1.2 获取 IEntityQuery

通过 `Query<T>()` 扩展方法获取 `IEntityQuery<T>` 实例。SQL 语句与参数在创建查询时指定，后续枚举方法无需再传 SQL：

```csharp
// 获取 User 实体的查询对象（SQL 在创建时指定）
IEntityQuery<User> query = db.FromSql<User>(
    "SELECT * FROM Users",
    new { Status = 1 });
```

### 1.3 参数传递

`Query<T>()` 支持使用匿名对象传递参数（推荐）：

```csharp
var user = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Id = @Id AND Status = @Status",
    new { Id = 1, Status = 1 }).FirstOrDefaultAsync();
```

更多参数传递方式（字典、集合展开等）请参考 [使用参数化查询](04-ExecuteSql.md#1-使用参数化查询)。

## 2. 查询单条数据

### 2.1 方法列表

这些方法与 LINQ 中的同名方法具有相同的语义，均为异步方法：

| 方法 | 无记录时 | 多条记录时 | 用途 |
|------|----------|------------|------|
| `FirstAsync` | 抛出异常 | 返回第一条 | 确保有记录 |
| `FirstOrDefaultAsync` | 返回 null | 返回第一条 | 可能无记录 |
| `SingleAsync` | 抛出异常 | 抛出异常 | 确保只有一条 |
| `SingleOrDefaultAsync` | 返回 null | 抛出异常 | 最多一条 |

### 2.2 FirstAsync

查询第一条记录，若不存在则抛出异常：

```csharp
// 查询年龄大于 18 的第一个用户
User user = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Age > @Age",
    new { Age = 18 }).FirstAsync();

Console.WriteLine($"用户: {user.UserName}");
```

### 2.3 FirstOrDefaultAsync

查询第一条记录，若不存在返回 null：

```csharp
// 查询指定邮箱的用户
User user = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Email = @Email",
    new { Email = "test@example.com" }).FirstOrDefaultAsync();

if (user != null)
{
    Console.WriteLine($"找到用户: {user.UserName}");
}
```

### 2.4 SingleAsync

查询单条记录，若不存在或多条都抛出异常：

```csharp
// 查询指定 ID 的用户（应该只有一条）
User user = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Id = @Id",
    new { Id = 1 }).SingleAsync();

Console.WriteLine($"用户: {user.UserName}");
```

### 2.5 SingleOrDefaultAsync

查询单条记录，若不存在返回 null，多条抛出异常：

```csharp
// 查询指定邮箱的用户
User user = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Email = @Email",
    new { Email = "unique@example.com" }).SingleOrDefaultAsync();

if (user != null)
{
    Console.WriteLine($"找到用户: {user.UserName}");
}
```

## 3. 查询多条数据

### 3.1 ToListAsync

查询所有记录：

```csharp
// 查询所有活跃用户
List<User> users = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Status = @Status",
    new { Status = 1 }).ToListAsync();

foreach (var user in users)
{
    Console.WriteLine($"用户: {user.UserName}, 邮箱: {user.Email}");
}
```

### 3.2 不带参数的查询

```csharp
// 查询所有用户
List<User> allUsers = await db.FromSql<User>("SELECT * FROM Users").ToListAsync();
```

### 3.3 分页查询

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
> 建议以唯一列（如主键）作为末级排序键，避免同值行跨页重复或遗漏。
> 排序键仅支持根实体的映射列（如 `u.CreatedAt`），导航属性、计算表达式与 JSON 列不支持。
> 深分页（页码很大）场景建议改用 keyset 分页（`KeysetAfterAsync`），避免大偏移扫描。

## 4. 加载导航属性

使用 `Include` 加载导航属性，返回 `IIncludableEntityQuery` 支持通过 `ThenInclude` 加载多级导航。框架采用分裂查询策略：先执行用户 SQL 获取主实体，再根据主外键值查询关联表，最后在内存中装配：

```csharp
// 加载一级导航属性
var users = await db.FromSql<User>("SELECT * FROM Users")
    .Include(u => u.Orders)
    .ToListAsync();

// 加载多个导航属性
var users2 = await db.FromSql<User>("SELECT * FROM Users")
    .Include(u => u.Department)
    .Include(u => u.Orders)
    .ToListAsync();

// 通过 ThenInclude 加载多级导航
var users3 = await db.FromSql<User>("SELECT * FROM Users")
    .Include(u => u.Orders)
    .ThenInclude(o => o.Details)
    .ToListAsync();
```

**注意**：
- 导航属性需使用 `[Navigation]` 特性标记，详见 [实体类的定义](08-EntityDefinition.md)。
- 如需保证多次查询间的数据一致性，请在查询前手动开启事务（推荐使用可重复读或更高的隔离级别）。

## 5. 使用取消令牌

所有查询方法均支持 `CancellationToken`：

```csharp
public async Task<List<User>> GetUsersAsync(CancellationToken cancellationToken)
{
    return await db.FromSql<User>(
        "SELECT * FROM Users WHERE Status = @Status",
        new { Status = 1 }).ToListAsync(cancellationToken);
}
```

## 6. 完整示例

```csharp
using Xbc.DbAccess;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.Sqlite;

// 匿名对象参数
var user = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Id = @Id",
    new { Id = 1 }).FirstOrDefaultAsync();

// 集合展开参数
var ids = new[] { 1, 2, 3 };
var users = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Id IN (@Ids)",
    new { Ids = ids }).ToListAsync();

// 分页查询
var pageUsers = await db.FromSql<User>("SELECT * FROM Users")
    .OrderBy(u => u.UserId)
    .Skip(0)
    .Take(10)
    .ToListAsync();

// 加载导航属性
var usersWithOrders = await db.FromSql<User>("SELECT * FROM Users")
    .Include(u => u.Orders)
    .ToListAsync();
```

**完整代码**：可运行的完整示例代码请参见 [examples/10_EntityQuery/Program.cs](./examples/10_EntityQuery/Program.cs)

**运行示例**：
```bash
cd examples/10_EntityQuery
dotnet run
```

## 相关文档

- [实体类的定义](08-EntityDefinition.md) - 了解如何通过特性配置实体类，控制映射行为。
- [实体映射器](09-EntityMapper.md) - 了解 `EntityMapper<T>` 的底层映射机制。

## 下一步

- [实体集操作](11-EntitySet.md) - 学习如何使用 `EntitySet<T>` 进行增删改查操作。
