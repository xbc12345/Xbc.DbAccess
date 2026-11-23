# 使用 EntityMapper

本文档介绍 `EntityMapper` 将 `DbDataReader` 映射为实体类的机制，以及应用侧使用这一能力的公开入口。

## 目录

1. [EntityMapper 概述](#1-entitymapper-概述)
2. [泛型版本（EntityMapper<T>）](#2-泛型版本entitymapper)
   - [2.1 映射实体](#21-映射实体)
3. [非泛型版本（EntityMapper）](#3-非泛型版本entitymapper)
   - [3.1 映射实体](#31-映射实体)
4. [DbDataReader 与 EntityMapper](#4-dbdatareader-与-entitymapper)
   - [4.1 Reader 状态管理](#41-reader-状态管理)
   - [4.2 多结果集处理](#42-多结果集处理)
   - [4.3 部分读取场景](#43-部分读取场景)
   - [4.4 最佳实践](#44-最佳实践)
5. [清理缓存](#5-清理缓存)
6. [完整示例](#6-完整示例)

## 1. EntityMapper 概述

`EntityMapper` 是框架提供的实体映射工具，用于将 `DbDataReader` 读取的数据库记录映射为 .NET 实体类实例。

**重要：** `EntityMapper` 与 `EntityMapper<T>` 是框架内部的映射核心（`internal`），框架的实体查询扩展方法内部均基于它实现。应用代码一般无需（也无法）直接调用，而是通过 `IDbAccess` 的以下公开扩展方法使用其映射能力：

- `db.Query<T>(sql, param)` - 实体查询（单条/多条/分页/导航属性）
- `db.QueryScalarAsync<T, TResult>(selector, sql, param)` - 标量值转换
- `db.ExecuteMultipleAsync(sql, param)` - 多结果集读取（`EntityMultiReader`）

### 主要特点

| 特点 | 说明 |
|------|------|
| **高性能** | 使用表达式树编译技术，映射性能接近手写代码 |
| **自动缓存** | 自动缓存映射委托，相同结构的查询复用缓存 |
| **仅提供异步方法** | 所有映射方法均为异步（`MapAsync` / `MapListAsync`），避免阻塞线程 |
| **灵活配置** | 通过实体特性控制映射行为 |

### 两个版本

框架提供两个版本的 `EntityMapper`：

- **`EntityMapper<T>`**（泛型版本）- 推荐，类型安全，无装箱/拆箱开销
- **`EntityMapper`**（非泛型版本）- 适用于运行时类型不确定的场景，有轻微的 `DynamicInvoke` 开销

**泛型 vs 非泛型对比：**

| 特性 | `EntityMapper<T>` | `EntityMapper` |
|------|-------------------|----------------|
| 类型安全 | 是，编译时检查 | 否，运行时转换 |
| 返回值 | `T` / `List<T>` | `object` / `List<object>` |
| 调用方式 | 直接委托调用 | `DynamicInvoke` 调用 |
| 性能 | 无额外开销 | 轻微的 `DynamicInvoke` 开销 |
| 缓存机制 | 统一使用 `EntityMapperCore` 缓存 | 统一使用 `EntityMapperCore` 缓存 |
| 推荐使用 | 静态类型已知时（推荐） | 动态类型场景 |

## 2. 泛型版本（EntityMapper<T>）

### 方法列表

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `MapAsync(reader, cancellationToken)` | `Task<T>` | 异步读取单条记录并映射为实体，无数据时返回 `null` |
| `MapListAsync(reader, cancellationToken)` | `Task<List<T>>` | 异步读取所有记录并映射为实体列表 |

**说明：**
- 仅提供异步映射方法（`MapAsync` / `MapListAsync`），无同步版本
- 所有方法均支持 `cancellationToken` 参数
- 列映射统一经 `EntityMetadataCache` 构建元数据（含列名判重等构建期校验）后缓存复用

### 2.1 映射实体

#### 映射单条数据

使用 `MapAsync` 方法将 `DbDataReader` 的第一条记录映射为实体：

```csharp
await using var reader = await db.ExecuteReaderAsync(
    "SELECT * FROM Users WHERE Id = @Id",
    new { Id = 1 });

// 异步映射单条数据，如果没有数据返回 null
var user = await EntityMapper<User>.MapAsync(reader);

if (user != null)
{
    Console.WriteLine($"用户: {user.UserName}");
}
```

#### 映射多条数据

使用 `MapListAsync` 方法将 `DbDataReader` 的所有记录映射为实体列表：

```csharp
await using var reader = await db.ExecuteReaderAsync("SELECT * FROM Users");

// 异步映射多条数据，返回 List<T>
var users = await EntityMapper<User>.MapListAsync(reader);

foreach (var user in users)
{
    Console.WriteLine($"用户: {user.UserName}");
}
```

#### 应用侧公开入口

应用代码通常不直接调用 `EntityMapper`，而是使用 `db.Query<T>` 扩展方法，其内部自动完成 Reader 生命周期管理与实体映射：

```csharp
// 单条（无数据返回 null）
var user = await db.Query<User>(
    "SELECT * FROM Users WHERE Id = @Id",
    new { Id = 1 }).FirstOrDefaultAsync();

// 多条
var users = await db.Query<User>("SELECT * FROM Users").ToListAsync();
```

## 3. 非泛型版本（EntityMapper）

### 方法列表

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `MapAsync(entityType, reader, cancellationToken)` | `Task<object>` | 异步读取单条记录并映射为实体 |
| `MapListAsync(entityType, reader, cancellationToken)` | `Task<List<object>>` | 异步读取所有记录并映射为实体列表 |

**说明：**
- 仅提供异步映射方法，无同步版本
- 所有方法均支持 `cancellationToken` 参数

### 3.1 映射实体

#### 映射单条数据

使用 `MapAsync` 方法将 `DbDataReader` 的第一条记录映射为实体：

```csharp
Type entityType = typeof(User);

await using var reader = await db.ExecuteReaderAsync(
    "SELECT * FROM Users WHERE Id = @Id",
    new { Id = 1 });

// 非泛型版本返回 object，需要自行转换
object userObj = await EntityMapper.MapAsync(entityType, reader);
User user = (User)userObj;

if (user != null)
{
    Console.WriteLine($"用户: {user.UserName}");
}
```

#### 映射多条数据

使用 `MapListAsync` 方法将 `DbDataReader` 的所有记录映射为实体列表：

```csharp
Type entityType = typeof(User);

await using var reader = await db.ExecuteReaderAsync("SELECT * FROM Users");

// 异步映射多条数据，返回 List<object>
List<object> userObjs = await EntityMapper.MapListAsync(entityType, reader);

// 转换为具体类型
List<User> users = userObjs.Cast<User>().ToList();

foreach (var user in users)
{
    Console.WriteLine($"用户: {user.UserName}");
}
```

#### 应用侧公开入口

运行时类型不确定的场景，可使用 `EntityMultiReader.ReadAsync(Type)` 非泛型重载（见[多结果集处理](#42-多结果集处理)）。

**适用场景：**
- 动态类型映射
- 通用数据访问层封装
- 反射场景

## 4. DbDataReader 与 EntityMapper

`EntityMapper` 通过 `DbDataReader` 读取数据库数据，了解它们之间的关系有助于正确使用映射功能。

### 4.1 Reader 状态管理

**重要：** `EntityMapper` 会改变 `DbDataReader` 的读取位置：

| 方法 | 对 Reader 的影响 |
|------|-----------------|
| `MapAsync` | 调用 `reader.ReadAsync()` 一次，读取第一条记录 |
| `MapListAsync` | 循环调用 `reader.ReadAsync()`，直到读完所有记录 |

映射完成后，`reader` 处于已读完状态（`reader.ReadAsync()` 返回 `false`）。

```csharp
await using var reader = await db.ExecuteReaderAsync("SELECT * FROM Users");

// 第一次映射 - 读取所有数据
var users = await EntityMapper<User>.MapListAsync(reader);
Console.WriteLine($"读取到 {users.Count} 条记录");

// 此时 reader 已经读取完毕
// 再次映射不会读取到任何数据
var users2 = await EntityMapper<User>.MapListAsync(reader);
Console.WriteLine($"第二次读取到 {users2.Count} 条记录"); // 输出 0

// 如果需要重新读取，必须重新执行查询
await using var reader2 = await db.ExecuteReaderAsync("SELECT * FROM Users");
var users3 = await EntityMapper<User>.MapListAsync(reader2);
```

### 4.2 多结果集处理

当使用存储过程或批量查询返回多个结果集时，框架提供了两种处理方式。

#### 方式一：手动使用 NextResultAsync

直接使用 `NextResultAsync` 配合 `EntityMapper`：

```csharp
await using var reader = await db.ExecuteReaderAsync("SELECT * FROM Users; SELECT * FROM Orders");

// 映射第一个结果集
var users = await EntityMapper<User>.MapListAsync(reader);

// 移动到下一个结果集
if (await reader.NextResultAsync())
{
    // 映射第二个结果集
    var orders = await EntityMapper<Order>.MapListAsync(reader);
}
```

#### 方式二：使用 ExecuteMultipleAsync + EntityMultiReader（推荐）

框架提供了 `ExecuteMultipleAsync` 扩展方法和 `EntityMultiReader` 类，封装了多结果集的读取逻辑，使用更简洁：

```csharp
// EntityMultiReader 仅实现 IDisposable，使用 using 释放
using var multiResult = await db.ExecuteMultipleAsync("SELECT * FROM Users; SELECT * FROM Orders");

// 依次异步读取每个结果集，每次读取后自动移动到下一个结果集
var users = await multiResult.ReadAsync<User>();
var orders = await multiResult.ReadAsync<Order>();
```

**`ExecuteMultipleAsync` 方法**：

| 方法 | 说明 |
|------|------|
| `ExecuteMultipleAsync(sql, param, cancellationToken)` | 异步执行 SQL 语句，返回多结果集映射器 |
| `ExecuteMultipleAsync(sql, commandType, param, cancellationToken)` | 异步执行 SQL 语句或存储过程，返回多结果集映射器 |

**`EntityMultiReader` 方法**：

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `ReadAsync<T>(cancellationToken)` | `Task<List<T>>` | 异步读取当前结果集的所有记录，然后移动到下一个结果集 |
| `ReadAsync(entityType, cancellationToken)` | `Task<List<object>>` | 非泛型版本，异步读取当前结果集的所有记录 |
| `ReadFirstAsync<T>(cancellationToken)` | `Task<T>` | 异步读取当前结果集的第一条记录，然后移动到下一个结果集 |
| `ReadFirstAsync(entityType, cancellationToken)` | `Task<object>` | 非泛型版本，异步读取当前结果集的第一条记录 |

**示例**：

```csharp
using var multiResult = await db.ExecuteMultipleAsync("SELECT * FROM Users; SELECT * FROM Orders");

var users = await multiResult.ReadAsync<User>();
var orders = await multiResult.ReadAsync<Order>();
```

**存储过程多结果集示例**：

```csharp
// 存储过程返回多个结果集
using var multiResult = await db.ExecuteMultipleAsync("sp_GetUserAndOrders", CommandType.StoredProcedure,
    new { UserId = 1 });

var user = await multiResult.ReadFirstAsync<User>();   // 第一个结果集：用户信息
var orders = await multiResult.ReadAsync<Order>();      // 第二个结果集：订单列表
```

**注意**：
- `EntityMultiReader` 实现了 `IDisposable`，使用完毕后需要释放（推荐使用 `using` 语句）。
- `ReadAsync` / `ReadFirstAsync` 方法会自动调用 `NextResultAsync` 移动到下一个结果集，无需手动调用。
- 读取顺序必须与 SQL 语句返回的结果集顺序一致。

### 4.3 部分读取场景

如果需要在映射前检查数据或跳过某些行，可以手动控制 `DbDataReader`：

```csharp
await using var reader = await db.ExecuteReaderAsync("SELECT * FROM Users");

// 手动跳过前 10 条记录
for (int i = 0; i < 10 && await reader.ReadAsync(); i++) { }

// 从第 11 条开始映射剩余记录
var remainingUsers = await EntityMapper<User>.MapListAsync(reader);
```

**注意：** 手动调用 `reader.ReadAsync()` 后再使用 `MapAsync` 会导致跳过第一条记录，应改用 `MapListAsync` 或重新执行查询。

### 4.4 最佳实践

1. **每个 Reader 只映射一次** - 避免重复读取同一 `DbDataReader`
2. **使用 `await using` 语句** - 确保 `DbDataReader` 及时释放
3. **按需选择方法** - 单条用 `MapAsync`，多条用 `MapListAsync`，单列用 `QueryScalarAsync`
4. **注意多结果集** - 使用 `NextResultAsync()` 切换到下一个结果集后再映射

## 5. 清理缓存

`EntityMapper` 会自动缓存映射委托以提升性能，一般情况下无需手动管理。仅在特殊情况下（如实体类型定义动态变化）需要清理缓存：

```csharp
// 清理指定类型的缓存（泛型版本）
EntityMapper<User>.ClearCache();

// 清理指定类型的缓存（非泛型版本）
EntityMapper.ClearCache(typeof(User));

// 清理所有类型的缓存
EntityMapper.ClearAllCache();
```

**注意：** 一般情况下不需要手动清理缓存。

## 6. 完整示例

```csharp
using Xbc.DbAccess;
using Xbc.DbAccess.Entity;

// 映射单条数据（无数据返回 null）
var user = await db.Query<User>(
    "SELECT * FROM Users WHERE Id = @Id",
    new { Id = 1 }).FirstOrDefaultAsync();

// 映射多条数据
var users = await db.Query<User>("SELECT * FROM Users").ToListAsync();

// 标量值转换
var userNames = await db.QueryScalarAsync<User, string>(
    u => u.UserName,
    "SELECT user_name FROM Users");

// 多结果集
using var multiResult = await db.ExecuteMultipleAsync("SELECT * FROM Users; SELECT * FROM Orders");
var allUsers = await multiResult.ReadAsync<User>();
var allOrders = await multiResult.ReadAsync<Order>();
```

**完整代码**：可运行的完整示例代码请参见 [examples/09_EntityMapper/Program.cs](./examples/09_EntityMapper/Program.cs)

**运行示例**：
```bash
cd examples/09_EntityMapper
dotnet run
```

## 相关文档

- [实体类的定义](08-EntityDefinition.md) - 了解如何通过特性配置实体类，控制映射行为。

## 下一步

- [使用 IEntityQuery](10-EntityQuery.md) - 使用 `IEntityQuery` 进行更加便捷的查询操作。
