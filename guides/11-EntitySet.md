# 使用 IEntitySet

本文档介绍如何使用 `IEntitySet<T>` 接口进行实体的增删改查操作。

## 目录

1. [IEntitySet 概述](#1-ientityset-概述)
2. [获取 IEntitySet](#2-获取-ientityset)
3. [查询操作](#3-查询操作)
4. [插入操作](#4-插入操作)
5. [更新操作](#5-更新操作)
6. [删除操作](#6-删除操作)
7. [批量操作](#7-批量操作)
8. [完整示例](#8-完整示例)

## 1. IEntitySet 概述

`IEntitySet<T>` 是框架提供的实体集合接口，封装了实体的增删改查操作，自动根据实体元数据生成 SQL 语句。

### 1.1 主要特点

| 特点 | 说明 |
|------|------|
| 自动生成 SQL | 根据实体元数据自动构建 INSERT、UPDATE、DELETE 语句 |
| 自增列支持 | 插入后自动获取并填充自增 ID |
| 批量操作 | 支持批量插入、更新、删除，自动优化执行策略 |
| 事务保证 | 批量操作自动使用事务，确保数据一致性 |
| 仅提供异步方法 | 所有方法均为异步，全部支持 `CancellationToken` |

### 1.2 导航属性说明（重要）

**Insert/Update/Delete 仅操作主实体本身，不处理导航属性数据；导航属性数据需业务代码显式管理。**

- 1:1 导航引用已赋值时，插入/更新会将其目标主键同步到主实体的外键列（纯内存同步，不查库、不递归）；
- 1:N 集合导航已赋值时，插入/更新后会同步对方实体的外键列（默认值回填本方主键值，已有值以对方外键为准）；对方实体的入库仍由调用方负责；
- 删除不级联删除子实体：若主实体被子记录的外键引用，删除将失败，需先删除子记录或使用数据库级联删除约束；
- 导航属性的读取请使用查询侧的 `Include`/`ThenInclude` 能力（见 [使用 IEntityQuery](10-EntityQuery.md)）。

### 1.3 方法一览

| 操作 | 单条 | 批量 |
|------|------|------|
| 查询 | FindAsync | - |
| 插入 | InsertAsync | InsertRangeAsync |
| 更新 | UpdateAsync | UpdateRangeAsync |
| 删除 | DeleteAsync | DeleteRangeAsync |

所有方法均返回 `Task<int>`（受影响行数；单条操作即当前实体的受影响行数）。

## 2. 获取 IEntitySet

通过 `Set<T>()` 扩展方法获取 `IEntitySet<T>` 实例：

```csharp
// 获取 User 实体的集合对象
IEntitySet<User> userSet = db.Set<User>();
```

## 3. 查询操作

### 3.1 FindAsync - 根据主键查找

根据主键值查找实体，返回实体对象或 `null`：

```csharp
// 根据主键查找用户
User user = await db.Set<User>().FindAsync(1);

if (user != null)
{
    Console.WriteLine($"找到用户: {user.UserName}");
}
```

### 3.2 Where - 按谓词查询（含导航属性加载）

`FindAsync` 只按主键取单行；需要导航属性或更多条件时使用 `Where`（`IEntitySet<T>` 的接口方法，与 FindAsync 同族的取数原语），
返回可继续组合的查询对象：

```csharp
// 按主键查找并加载导航属性（等价于 FindAsync + Include，支持 ThenInclude 多级导航）
var user = await db.Set<User>().Where(u => u.UserId == 1)
    .Include(u => u.Orders)
    .ThenInclude(o => o.Details)
    .SingleOrDefaultAsync();

// 任意谓词条件
var adults = await db.Set<User>().Where(u => u.Age >= 18).ToListAsync();
```

### 3.3 复杂查询

`IEntitySet<T>` 提供主键查找（`FindAsync`）与谓词查询（`Where`，谓词能力与条件 UPDATE/DELETE 一致）；
更复杂的查询请使用 `db.FromSql<T>(sql, param)` 实体查询（`Xbc.DbAccess.Entity` 命名空间扩展），用法详见 [使用 IEntityQuery](10-EntityQuery.md)：

```csharp
// 使用 FromSql 进行复杂查询
var users = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Status = @Status",
    new { Status = 1 }).ToListAsync();
```

## 4. 插入操作

### 4.1 InsertAsync - 插入单条记录

```csharp
// 创建新用户
var newUser = new User
{
    UserName = "张三",
    Email = "zhangsan@example.com",
    Age = 25,
    Status = 1
};

// 插入用户
int rowsAffected = await db.Set<User>().InsertAsync(newUser);
Console.WriteLine($"插入了 {rowsAffected} 条记录");
```

### 4.2 自增列自动填充

如果实体有自增列（标识列），插入后框架会自动获取并填充自增 ID：

```csharp
var newUser = new User
{
    UserName = "李四",
    Email = "lisi@example.com",
    Age = 30
};

await db.Set<User>().InsertAsync(newUser);

// Id 是自增列，框架会自动填充
Console.WriteLine($"新用户 ID: {newUser.UserId}");
```

**自增 ID 获取策略**：

| 数据库 | 获取方式 |
|--------|----------|
| SQLite | 使用 `SELECT last_insert_rowid()` |
| SQL Server | 使用 `OUTPUT` 子句 |
| PostgreSQL | 使用 `RETURNING` 子句 |
| MySQL | 使用 `SELECT LAST_INSERT_ID()` |

## 5. 更新操作

### 5.1 UpdateAsync - 更新单条记录

根据主键更新实体，更新时自动排除主键列和自增列：

```csharp
// 查找用户
User user = await db.Set<User>().FindAsync(1);

if (user != null)
{
    // 修改属性
    user.Email = "newemail@example.com";
    user.Age = 26;

    // 更新用户
    int rowsAffected = await db.Set<User>().UpdateAsync(user);
    Console.WriteLine($"更新了 {rowsAffected} 条记录");
}
```

**注意**：

- 更新操作根据主键定位记录，实体必须有主键列。
- 主键列和自增列不会被更新。

## 6. 删除操作

### 6.1 DeleteAsync - 删除单条记录

根据主键删除实体：

```csharp
// 方式一：先查询再删除
User user = await db.Set<User>().FindAsync(1);
if (user != null)
{
    int rowsAffected = await db.Set<User>().DeleteAsync(user);
    Console.WriteLine($"删除了 {rowsAffected} 条记录");
}

// 方式二：直接创建实体对象进行删除（不需要先查询）
var userToDelete = new User { UserId = 1 };
int rowsAffected = await db.Set<User>().DeleteAsync(userToDelete);
```

**注意**：

- 删除操作根据主键定位记录，只需设置主键属性即可。
- 删除仅作用于主实体行，不级联删除子实体：若主实体被子记录的外键引用，删除将失败，需先删除子记录或使用数据库级联删除约束。

## 7. 批量操作

### 7.1 InsertRangeAsync - 批量插入

```csharp
// 创建多个用户
var users = new List<User>
{
    new User { UserName = "用户1", Email = "user1@example.com", Age = 20 },
    new User { UserName = "用户2", Email = "user2@example.com", Age = 25 },
    new User { UserName = "用户3", Email = "user3@example.com", Age = 30 }
};

// 批量插入
int rowsAffected = await db.Set<User>().InsertRangeAsync(users);
Console.WriteLine($"批量插入了 {rowsAffected} 条记录");

// 自增 ID 会被自动填充
foreach (var user in users)
{
    Console.WriteLine($"用户 ID: {user.UserId}");
}
```

**批量插入策略**：

| 场景 | 策略 |
|------|------|
| 无自增列 | 使用多值 INSERT 语句（`INSERT INTO ... VALUES (...), (...), ...`），性能最优 |
| 有自增列 | 逐条插入并获取 ID，使用事务保证一致性 |

### 7.2 UpdateRangeAsync - 批量更新

```csharp
// 查询需要更新的用户
var users = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Status = @Status",
    new { Status = 0 }).ToListAsync();

// 修改属性
foreach (var user in users)
{
    user.Status = 1;
}

// 批量更新（自动使用事务）
int rowsAffected = await db.Set<User>().UpdateRangeAsync(users);
Console.WriteLine($"批量更新了 {rowsAffected} 条记录");
```

### 7.3 DeleteRangeAsync - 批量删除

```csharp
// 查询需要删除的用户
var users = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Status = @Status",
    new { Status = 0 }).ToListAsync();

// 批量删除（使用 IN 子句，如 DELETE FROM Users WHERE user_id IN (@user_id_0, @user_id_1)）
int rowsAffected = await db.Set<User>().DeleteRangeAsync(users);
Console.WriteLine($"批量删除了 {rowsAffected} 条记录");
```

**批量删除策略**：使用 `IN` 子句一次性删除，比逐条删除更高效。

## 8. 完整示例

```csharp
using Xbc.DbAccess;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.Sqlite;

// 获取实体集合
var userSet = db.Set<User>();

// 插入
var newUser = new User { UserName = "张三", Email = "zhangsan@example.com", Age = 25 };
int insertRows = await userSet.InsertAsync(newUser);
Console.WriteLine($"插入 {insertRows} 条，新 ID: {newUser.UserId}");

// 查询
User user = await userSet.FindAsync(1);

// 更新
user.Email = "new@example.com";
int updateRows = await userSet.UpdateAsync(user);

// 删除
int deleteRows = await userSet.DeleteAsync(user);

// 批量插入
var users = new List<User>
{
    new User { UserName = "李四", Email = "lisi@example.com", Age = 30 },
    new User { UserName = "王五", Email = "wangwu@example.com", Age = 35 }
};
int batchRows = await userSet.InsertRangeAsync(users);
```

**完整代码**：可运行的完整示例代码请参见 [examples/11_EntitySet/Program.cs](./examples/11_EntitySet/Program.cs)

**运行示例**：

```bash
cd examples/11_EntitySet
dotnet run
```

## 最佳实践

### 1. 使用事务保证数据一致性

由于 Insert/Update/Delete 不处理导航属性，关联数据需在业务代码中显式写入，并建议放入同一事务：

```csharp
await using (var transaction = await db.BeginTransactionAsync())
{
    try
    {
        // 插入订单
        await db.Set<Order>().InsertAsync(order);

        // 插入订单项（子实体需显式管理）
        foreach (var item in orderItems)
        {
            item.OrderId = order.OrderId;
            await db.Set<OrderItem>().InsertAsync(item);
        }

        await transaction.CommitAsync();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}
```

### 2. 批量操作优先于循环单条操作

```csharp
// 好的做法 - 批量插入
var users = new List<User> { /* ... */ };
await db.Set<User>().InsertRangeAsync(users);

// 避免这样做 - 循环单条插入
foreach (var user in users)
{
    await db.Set<User>().InsertAsync(user);  // 性能较差
}
```

### 3. 使用取消令牌

```csharp
public async Task<int> CreateUserAsync(User user, CancellationToken cancellationToken)
{
    return await db.Set<User>().InsertAsync(user, cancellationToken);
}
```

### 4. 检查操作结果

```csharp
int rows = await db.Set<User>().UpdateAsync(user);

if (rows == 0)
{
    // 没有记录被更新
    Console.WriteLine("用户不存在或已被删除");
}
```

## 相关文档

- [实体类的定义](08-EntityDefinition.md) - 了解如何通过特性配置实体类，控制映射行为（含自定义类型转换器）。
- [实体映射与缓存治理](09-EntityMapping.md) - 了解实体映射机制与映射缓存治理。
- [使用 IEntityQuery](10-EntityQuery.md) - 了解实体查询的详细用法。
