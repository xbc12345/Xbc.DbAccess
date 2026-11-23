# 使用 IEntitySet

本文档介绍如何使用 `IEntitySet<T>` 接口进行实体的增删改查操作。

## 目录

1. [IEntitySet 概述](#1-ientityset-概述)
2. [获取 IEntitySet](#2-获取-ientityset)
3. [查询操作](#3-查询操作)
4. [插入操作](#4-插入操作)
5. [更新操作](#5-更新操作)
6. [删除操作](#6-删除操作)
7. [Upsert 操作](#7-upsert-操作)
8. [批量操作](#8-批量操作)
9. [乐观并发控制](#9-乐观并发控制)
10. [完整示例](#10-完整示例)

## 1. IEntitySet 概述

`IEntitySet<T>` 是框架提供的实体集合接口，封装了实体的增删改查操作，自动根据实体元数据生成 SQL 语句。

### 1.1 主要特点

| 特点 | 说明 |
|------|------|
| 自动生成 SQL | 根据实体元数据自动构建 INSERT、UPDATE、DELETE 语句 |
| 自增列支持 | 插入后自动获取并填充自增 ID |
| 表达式写入 | 条件更新 / 条件删除用 lambda 表达式书写，值参数化、列名经方言引用 |
| Upsert | 存在则更新、不存在则插入，方言差异自动抹平 |
| 批量操作 | 支持批量插入、更新、删除；大批量数据有原生 bulk 路径（SqlBulkCopy / COPY） |
| 乐观并发 | 并发令牌列自动校验与换发，冲突抛 `DbConcurrencyException` |
| 事务保证 | 批量操作自动使用事务，确保数据一致性 |
| 仅提供异步方法 | 所有方法均为异步，全部支持 `CancellationToken` |

### 1.2 导航属性说明（重要）

**Insert/Update/Delete/Upsert 仅操作主实体本身，不处理导航属性数据；导航属性数据需业务代码显式管理。**

- 1:1 导航引用已赋值时，插入/更新会将其目标主键同步到主实体的外键列（纯内存同步，不查库、不递归）；
- 1:N 集合导航已赋值时，插入/更新后会同步对方实体的外键列（默认值回填本方主键值，已有值以对方外键为准）；对方实体的入库仍由调用方负责；
- 删除不级联删除子实体：若主实体被子记录的外键引用，删除将失败，需先删除子记录或使用数据库级联删除约束；
- 导航属性的读取请使用查询侧的 `Include`/`ThenInclude` 能力（见 [使用 IEntityQuery](10-EntityQuery.md)）。

### 1.3 方法一览

| 操作 | 单条 | 按条件 | 批量 |
|------|------|--------|------|
| 查询 | FindAsync | Where / AsQuery | - |
| 插入 | InsertAsync | - | InsertRangeAsync / BulkInsertAsync |
| 更新 | UpdateAsync（全列） | UpdateAsync（条件赋值） | UpdateRangeAsync |
| 删除 | DeleteAsync | DeleteAsync（条件） | DeleteRangeAsync |
| Upsert | UpsertAsync | - | - |

除查询方法外均返回 `Task<int>`（受影响行数；`BulkInsertAsync` 返回 `Task<long>`）。

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
- **更新不做变更追踪**：除主键与自增列外的所有列都会进入 SET 子句。实体须为完整查询（如 `FindAsync`）加载的完整实体——部分投影或手工构造的实体会以属性默认值覆盖数据库原值。

### 5.2 UpdateAsync - 条件更新

按条件将满足谓词的所有行的指定列更新为赋值表达式的值：

```csharp
// 将状态为 0 的用户全部激活（对象初始化器形态）
int rows = await db.Set<User>()
    .UpdateAsync(u => new User { Status = 1, UpdatedAt = DateTime.Now },
                 u => u.Status == 0);

// 匿名类型形态
int rows2 = await db.Set<User>()
    .UpdateAsync(u => new { Age = 26 }, u => u.Id == 1);
```

**规则**：

- 赋值表达式支持对象初始化器（`x => new User { Age = 18 }`）与匿名类型（`x => new { Age = 18 }`）；不支持元组与带参构造形态（两者无成员名承载「更新哪一列」的寻址语义）
- 赋值到主键列、标识列或导航属性、重复赋值同一列时抛出异常
- 条件表达式能力与 `Where` 谓词一致（比较/逻辑运算、布尔列、`IS [NOT] NULL`、集合 `Contains`（IN）、字符串匹配等）；闭包变量一律参数化

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

### 6.2 DeleteAsync - 条件删除

按谓词表达式删除满足条件的所有行：

```csharp
// 删除所有状态为 0 且从未登录的用户
int rows = await db.Set<User>()
    .DeleteAsync(u => u.Status == 0 && u.LastLoginAt == null);
```

条件表达式能力与 `Where` 谓词一致；闭包变量一律参数化。不级联删除对方实体（被外键引用时抛数据库原生外键约束异常）。

## 7. Upsert 操作

### 7.1 UpsertAsync - 存在则更新，不存在则插入

按主键做冲突判定：目标行存在则更新，不存在则插入。冲突键为实体主键，实体没有主键时抛出异常：

```csharp
var user = new User { UserId = 1, UserName = "张三", Email = "zhangsan@example.com", Age = 25 };

// UserId = 1 不存在时插入，存在时更新
int rows = await db.Set<User>().UpsertAsync(user);
```

**行为细节**：

- 更新列覆盖除主键、自增列外的**全部列**（不做变更追踪），调用方需自行避免覆盖并发修改
- 自增主键为默认值时必走插入路径并回填生成的 ID（取回机制与 `InsertAsync` 一致）；带显式值时按该值参与冲突判定（无需回填）
- 1:1 引用外键同步在写入前执行，1:N 集合外键同步在写入后执行（与 `InsertAsync` 一致）
- 配置并发令牌列时：每次 Upsert 换发新令牌写入；Upsert 本身不做令牌校验（冲突键恒为主键），失败时恢复实体的旧令牌

**方言差异**：

| 方言 | 冲突判定 | 更新路径受影响行数 |
|------|----------|--------------------|
| SQL Server / PostgreSQL / Oracle / SQLite | 严格按主键冲突 | 1 |
| MySQL | 覆盖**所有唯一键**（不只主键） | 2 |

> ⚠️ MySQL 的 `INSERT ... ON DUPLICATE KEY UPDATE` 语义会对任意唯一键冲突触发更新，业务上依赖「仅主键冲突」语义时请注意该差异。

## 8. 批量操作

### 8.1 InsertRangeAsync - 批量插入

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

### 8.2 UpdateRangeAsync - 批量更新

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

**注意**：与 `UpdateAsync` 一致，不做变更追踪、全列 SET，实体须为完整查询加载的完整实体。配置并发令牌列时逐条做乐观并发校验，任一冲突抛 `DbConcurrencyException` 并由外层事务整体回滚。

### 8.3 DeleteRangeAsync - 批量删除

```csharp
// 查询需要删除的用户
var users = await db.FromSql<User>(
    "SELECT * FROM Users WHERE Status = @Status",
    new { Status = 0 }).ToListAsync();

// 批量删除（使用 IN 子句，如 DELETE FROM Users WHERE user_id IN (@user_id_0, @user_id_1)）
int rowsAffected = await db.Set<User>().DeleteRangeAsync(users);
Console.WriteLine($"批量删除了 {rowsAffected} 条记录");
```

**批量删除策略**：使用 `IN` 子句一次性删除，比逐条删除更高效。配置并发令牌列时退化为事务内逐条删除（IN 子句无法逐行校验令牌），任一冲突抛 `DbConcurrencyException` 并整体回滚。

### 8.4 BulkInsertAsync - 高性能批量写入

面向大批量数据的高性能写入。有原生批量写入 API 的方言走原生路径，无原生 API 的方言回退多值 INSERT：

```csharp
// 大批量数据写入（如数据迁移、日志导入）
var bulkUsers = Enumerable.Range(1, 100_000)
    .Select(i => new User { UserName = $"user{i}", Email = $"user{i}@example.com", Age = 20 + i % 40 });

long inserted = await db.Set<User>().BulkInsertAsync(bulkUsers);
Console.WriteLine($"bulk 插入 {inserted} 条记录");

// 可选：指定批次大小与命令超时（仅原生路径生效，回退路径静默忽略）
var options = new BulkInsertOptions
{
    BatchSize = 5000,
    CommandTimeout = 60
};
await db.Set<User>().BulkInsertAsync(bulkUsers, options);
```

**与 `InsertRangeAsync` 的区别**：

| | InsertRangeAsync | BulkInsertAsync |
|---|---|---|
| 适用场景 | 中小批量、需要回填自增 ID | 大批量数据（数万行以上） |
| 自增 ID 回填 | 是 | **否**（自增列被忽略且不回填） |
| 写入路径 | 多值 INSERT / 逐条+事务 | SqlBulkCopy / COPY / 多值 INSERT 回退 |
| 导航属性外键同步 | 是 | 是 |
| 并发令牌 | 不涉及 | 为每个实体生成初始令牌 |

**注意**：

- 整批在事务中执行（已有活动事务时自动嵌套为保存点子事务）
- 全部列为自增列的退化场景抛出异常
- 部分方言的原生路径默认不校验 CHECK/外键约束、不触发触发器；NULL 值按字面写入
- 入参为 null、空集合或包含 null 元素时抛 `ArgumentException`
- **MySQL 前置条件**：原生路径基于 `MySqlBulkCopy`（LOAD DATA LOCAL），要求连接串显式加 `AllowLoadLocalInfile=true`，且服务端开启 `local_infile`（`SET GLOBAL local_infile = ON`）；缺省时抛出 `MySqlException`

## 9. 乐观并发控制

实体列配置并发令牌（`[Column(IsConcurrencyToken = true)]`，`byte[]` 类型）后，写操作自动进行乐观并发校验：

```csharp
public class User
{
    public int UserId { get; set; }

    [Column(IsConcurrencyToken = true)]
    public byte[] Version { get; set; }   // 并发令牌列

    public string UserName { get; set; }
}
```

**方言注意（Oracle）**：令牌校验通过 `WHERE 令牌列 = :旧令牌` 等值比较实现。Oracle 的 BLOB 列不可作等值比较键（ORA-22848），因此 Oracle 上令牌列应声明为 `RAW` 类型（如 `RAW(16)`，与 Guid 令牌长度匹配）；`varbinary`（SQL Server）、`bytea`（PostgreSQL）、`binary`（MySQL）、`BLOB`（SQLite）均可直接等值比较，无需特殊处理。

**行为**：

| 操作 | 并发行为 |
|------|----------|
| `UpdateAsync` | WHERE 子句附加旧令牌条件；更新成功后自动换发并回填新令牌 |
| `DeleteAsync` | WHERE 子句附加旧令牌条件 |
| `UpdateRangeAsync` / `DeleteRangeAsync` | 事务内逐条校验；任一冲突抛 `DbConcurrencyException` 并整体回滚 |
| `UpsertAsync` | 每次换发新令牌写入（本身不做校验，冲突键恒为主键） |
| `BulkInsertAsync` | 为每个实体生成初始令牌 |

影响行数为 0（目标行不存在**或令牌已被并发修改**）时抛出 `DbConcurrencyException`；未配置令牌列时静默返回 0：

```csharp
try
{
    var user = await db.Set<User>().FindAsync(1);
    user.Age = 26;
    await db.Set<User>().UpdateAsync(user);   // 令牌自动校验 + 换发
}
catch (DbConcurrencyException)
{
    // 目标行不存在或已被其他请求并发修改，按业务策略重试或提示
}
```

## 10. 完整示例

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

// Upsert（存在则更新，不存在则插入）
var upsertUser = new User { UserId = 1, UserName = "张三", Email = "zhangsan@example.com", Age = 26 };
int upsertRows = await userSet.UpsertAsync(upsertUser);

// 条件更新
int updated = await userSet.UpdateAsync(u => new { Status = 1 }, u => u.Status == 0);

// 条件删除
int deleted = await userSet.DeleteAsync(u => u.Status == 0);

// 批量插入
var users = new List<User>
{
    new User { UserName = "李四", Email = "lisi@example.com", Age = 30 },
    new User { UserName = "王五", Email = "wangwu@example.com", Age = 35 }
};
int batchRows = await userSet.InsertRangeAsync(users);

// 高性能批量写入（大批量数据，不回填自增 ID）
long bulkRows = await userSet.BulkInsertAsync(
    Enumerable.Range(1, 1000).Select(i => new User { UserName = $"u{i}", Age = i % 50 }));
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
