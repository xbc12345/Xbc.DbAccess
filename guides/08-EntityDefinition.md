# 实体类的定义

本文档介绍如何定义实体类以支持 ORM 映射。

## 目录

1. [基本实体定义](#1-基本实体定义)
2. [表名映射](#2-表名映射)
   - [2.1 使用 Table 特性](#21-使用-table-特性)
   - [2.2 指定架构名](#22-指定架构名)
   - [2.3 默认表名规则](#23-默认表名规则)
3. [主键和自增列](#3-主键和自增列)
   - [3.1 自动主键识别](#31-自动主键识别)
   - [3.2 显式配置主键](#32-显式配置主键)
   - [3.3 自增列配置](#33-自增列配置)
4. [列映射与配置](#4-列映射与配置)
   - [4.1 列名映射](#41-列名映射)
   - [4.2 忽略列](#42-忽略列)
   - [4.3 NULL 值处理](#43-null-值处理)
   - [4.4 指定 DbType](#44-指定-dbtype)
   - [4.5 自定义类型转换器](#45-自定义类型转换器)
5. [导航属性](#5-导航属性)
   - [5.1 一对一关系](#51-一对一关系)
   - [5.2 一对多关系](#52-一对多关系)
   - [5.3 键的约定推断](#53-键的约定推断)
   - [5.4 手动指定键](#54-手动指定键)
   - [5.5 加载导航属性](#55-加载导航属性)
6. [完整示例](#6-完整示例)

## 1. 基本实体定义

最简单的实体类只需要包含与数据库表对应的属性，框架会自动按照属性名映射到同名的数据库列。

```csharp
// 用户实体 - 属性名与数据库列名一致
public class User
{
    public int Id { get; set; }
    public string UserName { get; set; }
    public string Email { get; set; }
    public int Age { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**默认映射规则：**

- 表名：`Users`（类名的复数形式）
- 主键：`Id`（自动识别，符合命名约定）
- 列名：与属性名相同

**对应的数据库表结构：**

```sql
CREATE TABLE Users (
    Id INTEGER PRIMARY KEY,
    UserName TEXT,
    Email TEXT,
    Age INTEGER,
    CreatedAt DATETIME
);
```

## 2. 表名映射

### 2.1 使用 Table 特性

使用 `[Table]` 特性显式指定实体对应的数据库表名。

```csharp
using Xbc.DbAccess.Entity;

// 映射到 "SysUsers" 表
[Table("SysUsers")]
public class User
{
    public int Id { get; set; }
    public string UserName { get; set; }
}
```

### 2.2 指定架构名

对于支持架构的数据库（如 SQL Server、PostgreSQL），可以指定架构名。

```csharp
// SQL Server 架构
[Table("Users", Schema = "dbo")]
public class User
{
    public int Id { get; set; }
    public string UserName { get; set; }
}

// PostgreSQL 架构
[Table("users", Schema = "public")]
public class User
{
    public int Id { get; set; }
    public string UserName { get; set; }
}
```

### 2.3 默认表名规则

如果不使用 `[Table]` 特性，框架会自动将类名转换为复数形式作为表名。

```csharp
// 自动映射到 "Users" 表（User 的复数形式）
public class User
{
    public int Id { get; set; }
}

// 自动映射到 "OrderItems" 表（OrderItem 的复数形式）
public class OrderItem
{
    public int Id { get; set; }
}

// 自动映射到 "People" 表（Person 的不规则复数）
public class Person
{
    public int Id { get; set; }
}
```

**复数化规则：**

| 规则                   | 示例                    | 复数形式       |
| -------------------- | --------------------- | ---------- |
| 一般情况加 -s             | User → Users          | Users      |
| 以 s/x/ch/sh 结尾加 -es  | Box → Boxes           | Boxes      |
| 辅音+y 结尾变 y 为 i 加 -es | Category → Categories | Categories |
| 不规则变化                | Person → People       | People     |
| 不可数名词保持不变            | Equipment → Equipment | Equipment  |

框架内部提供了常见的不可数以及不规则变化的类名，但难免有一些类名不在默认表名规则中，这时候需要手动指定表名。推荐做法是对于这些不可数以及不规则变化的类名，直接使用 `[Table]` 特性指定表名。

## 3. 主键和自增列

### 3.1 自动主键识别

框架支持自动识别主键，无需显式配置：

- 属性名为 `Id`（不区分大小写）
- 属性名为 `{类名}Id`（不区分大小写），如 `UserId`

```csharp
// 方式一：属性名为 Id
public class User
{
    public int Id { get; set; }  // 自动识别为主键
    public string UserName { get; set; }
}

// 方式二：属性名为 {类名}Id
public class Order
{
    public int OrderId { get; set; }  // 自动识别为主键
    public DateTime OrderDate { get; set; }
}
```

### 3.2 显式配置主键

使用 `[PrimaryKey]` 特性显式标记主键列。

```csharp
public class User
{
    [PrimaryKey("user_id")]
    public int UserId { get; set; }
    
    public string UserName { get; set; }
}
```

**注意**：当前版本只支持单主键，不支持复合主键。若指定了多个主键列，则会在进行实体映射操作时抛出异常。

### 3.3 自增列配置

使用 `[PrimaryKey(IsIdentity = true)]` 显式标记自增列。自增列必须同时是主键，框架不会自动识别自增列。

```csharp
public class User
{
    [PrimaryKey("user_id", IsIdentity = true)]
    public int UserId { get; set; }
    
    public string UserName { get; set; }
}
```

**对应的数据库表结构：**

```sql
-- SQLite
CREATE TABLE Users (
    user_id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserName TEXT,
    Email TEXT
);

-- SQL Server
CREATE TABLE Users (
    user_id INT IDENTITY(1,1) PRIMARY KEY,
    UserName NVARCHAR(100),
    Email NVARCHAR(200)
);
```

**注意：** 自增列需要显式配置 `IsIdentity = true`，且需要确保跟数据库的自增列类型一致，否则会有溢出风险。

## 4. 列映射与配置

### 4.1 列名映射

使用 `[Column]` 特性显式指定属性对应的数据库列名。如果不使用 `[Column]` 特性，框架会自动使用属性名作为列名。

```csharp
public class User
{
    [PrimaryKey("user_id")]
    public int Id { get; set; }
    
    [Column("user_name")]
    public string UserName { get; set; }
    
    [Column("email_address")]
    public string Email { get; set; }
}
```

### 4.2 忽略列

使用 `[NotMapped]` 标记不需要映射到数据库的属性，如计算属性。

```csharp
public class User
{
    public int Id { get; set; }
    public string UserName { get; set; }
    public string Email { get; set; }
    public int Age { get; set; }
    
    // 计算属性，不映射到数据库
    [NotMapped]
    public bool IsAdult => Age >= 18;
    
    // 计算属性，不映射到数据库
    [NotMapped]
    public string DisplayName => $"{UserName} ({Email})";
}
```

### 4.3 NULL 值处理

使用 `[Column(DbNullHandling = ...)]` 指定当数据库列值为 `NULL` 或类型无法转换时的处理方式。

**`Default`（默认行为）：**

不指定 `DbNullHandling` 时，框架根据属性类型自动选择处理方式：

| 类型 | 默认策略 | 说明 |
|------|----------|------|
| 非可空类型 | `UseDefaultValue` | 使用类型的默认值（0、false、DateTime.MinValue 等） |
| 可空类型（包括可空值类型和引用类型，例如：`int?`、`string`） | `SkipAssignment` | 跳过赋值，保持属性为初始值（null 或其他初始值） |

```csharp
public class User
{
    public int Id { get; set; }
    
    // 非可空类型默认行为：数据库列值为 NULL 时，使用默认值 0
    public int Age { get; set; }

    // 可空类型默认行为：数据库列值为 NULL 时，保持属性值为 null
    public DateTime? LastLoginAt { get; set; }

    // 可空类型默认行为：数据库列值为 NULL 时，保持属性值为 "unknown"
    public string NickName { get; set; } = "unknown";
}
```

**`ThrowException`（抛出异常）：**

当数据库列值为 `NULL` 时，无论类型是否可空，都抛出 `InvalidCastException`。

```csharp
public class User
{
    public int Id { get; set; }
    
    // 数据库列值为 NULL 时，抛出异常
    [Column(DbNullHandling = DbNullHandling.ThrowException)]
    public int Age { get; set; }
    
    // 即使是可空类型，数据库列值为 NULL 时也抛出异常
    [Column(DbNullHandling = DbNullHandling.ThrowException)]
    public DateTime? LastLoginAt { get; set; }
}
```

**`UseDefaultValue`（使用默认值）：**

当数据库列值为 `NULL` 时，使用类型的默认值（数值类型为 0、布尔类型为 false、日期类型为 DateTime.MinValue、引用类型为 null 等）。

```csharp
public class User
{
    public int Id { get; set; }
    
    // 数据库列值为 NULL 时，使用 null（string 的默认值）
    [Column(DbNullHandling = DbNullHandling.UseDefaultValue)]
    public string Bio { get; set; }
    
    // 数据库列值为 NULL 时，使用 0（int 的默认值）
    [Column(DbNullHandling = DbNullHandling.UseDefaultValue)]
    public int Age { get; set; }

    // 数据库列值为 NULL 时，使用 DateTime.MinValue（DateTime 的默认值）
    [Column(DbNullHandling = DbNullHandling.UseDefaultValue)]
    public DateTime LastLoginAt { get; set; }
}
```

**`SkipAssignment`（跳过赋值）：**

当数据库列值为 `NULL` 时，跳过属性赋值，保持属性为初始值。

```csharp
public class User
{
    public int Id { get; set; }
    
    // 数据库列值为 NULL 时，保持属性值为 "unknown"
    [Column(DbNullHandling = DbNullHandling.SkipAssignment)]
    public string NickName { get; set; } = "unknown";
        
    // 数据库列值为 NULL 时，保持属性值为 100
    [Column(DbNullHandling = DbNullHandling.SkipAssignment)]
    public int VisitCount { get; set; } = 100;
}
```

**`DbNullHandling` 处理策略对比：**

| 策略              | 说明                           | 默认适用场景       | 适用场景                     |
| ----------------- | ------------------------------ | ------------------ | ---------------------------- |
| Default           | 根据类型自动选择处理方式         | 所有未指定的属性   | 推荐，行为安全且合理         |
| ThrowException    | 抛出异常 | -                  | 必填字段，数据完整性要求高   |
| UseDefaultValue   | 使用类型默认值（0、false、null） | 非可空类型（Default） | 可选字段，允许使用类型默认值 |
| SkipAssignment    | 跳过赋值，保持属性值为初始值     | 可空类型（Default）   | 需要自定义默认值或保持初始值 |

### 4.4 指定 DbType

使用 `[Column(DbType = ...)]` 显式指定数据库列类型。`DbType` 主要用于指定参数 `DbParameter` 的数据库类型。

```csharp
public class User
{
    public int Id { get; set; }
    
    // 显式指定为 NVarChar 类型（SQL Server）
    [Column("user_name", DbType = DbType.String)]
    public string UserName { get; set; }
    
    // 显式指定为 DateTime2 类型（SQL Server）
    [Column("created_at", DbType = DbType.DateTime2)]
    public DateTime CreatedAt { get; set; }
    
    // 显式指定为 Decimal 类型
    [Column("amount", DbType = DbType.Decimal)]
    public decimal Amount { get; set; }
}
```

**注意：** 查询映射时，框架根据 .NET 类型自动推断数据库类型，无需指定 `DbType`。`DbType` 仅在 `IEntitySet` 执行插入、更新和删除操作时用于指定参数的数据库类型。

### 4.5 自定义类型转换器

使用 `[Column(ConverterType = typeof(...))]` 指定自定义类型转换器，用于处理特殊类型的映射。

**示例：JSON 字符串与对象的转换**

```csharp
// 定义地址类
public class Address
{
    public string Street { get; set; }
    public string City { get; set; }
    public string ZipCode { get; set; }
}

// 定义 JSON 转换器
public class JsonConverter<T> : IDbValueConverter<T> where T : class, new()
{
    public T ConvertFrom(object dbValue)
    {
        if (dbValue == null || dbValue == DBNull.Value || string.IsNullOrEmpty(dbValue.ToString()))
            return null;
        
        return JsonSerializer.Deserialize<T>(dbValue.ToString());
    }

    public object ConvertTo(T value)
    {
        if (value == null)
            return null;
        
        return JsonSerializer.Serialize(value);
    }
}

// 使用转换器
public class User
{
    public int Id { get; set; }
    
    // 将 Address 对象序列化为 JSON 字符串存储
    [Column("address_json", ConverterType = typeof(JsonConverter<Address>))]
    public Address Address { get; set; }
}
```

**适用场景：**

- JSON 字符串与对象的转换
- 加密/解密字段的处理
- 自定义枚举与数据库值的映射
- 其他需要自定义转换逻辑的场景

**全局注册转换器：**

如果某个类型需要在多个实体中复用，可以使用 `DbValueConverterRegistry` 全局注册转换器，无需在每个属性上指定 `ConverterType`：

```csharp
// 在应用程序启动时注册
DbValueConverterRegistry.Register(typeof(Address), value =>
{
    if (value == null || value == DBNull.Value)
        return null;
    return JsonSerializer.Deserialize<Address>(value.ToString());
}, value =>
{
    if (value == null)
        return null;
    return JsonSerializer.Serialize(value);
});

// 注册后，所有 Address 类型的属性自动使用该转换器
public class User
{
    public int Id { get; set; }
    
    // 无需指定 ConverterType
    [Column("address_json")]
    public Address Address { get; set; }
}

public class Company
{
    public int Id { get; set; }
    
    // 同样自动使用全局转换器
    [Column("address_json")]
    public Address Address { get; set; }
}
```

**转换器优先级：**

当同时存在属性级转换器和全局转换器时，优先级如下：

1. **属性级转换器**（`[Column(ConverterType = ...)]`）- 优先级最高
2. **全局转换器**（`DbValueConverterRegistry`）- 优先级次之
3. **内置转换** - 最后使用框架内置的类型转换

## 5. 导航属性

使用 `[Navigation]` 特性标记实体间的关联关系，框架通过分裂查询（Split Query）自动加载关联数据。

### 5.1 一对一关系

一对一关系：属性类型为单个实体类。当前实体持有外键，指向目标实体的主键。

```csharp
public class User
{
    public int Id { get; set; }
    public string UserName { get; set; }
    
    // 外键，按约定推断为 {导航属性名}Id
    public int DepartmentId { get; set; }
    
    // 用户所属的部门
    [Navigation]
    public Department Department { get; set; }
}

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; }
}
```

在此示例中，`User` 实体的 `DepartmentId` 属性作为外键（按约定推断），指向 `Department` 的主键 `Id`。

### 5.2 一对多关系

一对多关系：属性类型为实体集合（`List<T>` 或 `IList<T>`、`ICollection<T>` 等可由 `List<T>` 赋值的泛型接口）。

```csharp
public class User
{
    public int Id { get; set; }
    public string UserName { get; set; }
    
    // 用户的所有订单
    [Navigation]
    public List<Order> Orders { get; set; }
}

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime OrderDate { get; set; }
    
    // 订单的所有明细
    [Navigation]
    public List<OrderDetail> Details { get; set; }
}

public class OrderDetail
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}
```

在此示例中，`User` 的主键 `Id` 对应 `Order` 中名为 `UserId` 的属性（按约定推断），`Order` 的主键 `Id` 对应 `OrderDetail` 中名为 `OrderId` 的属性（按约定推断）。

**注意**：`Include` 支持通过 `ThenInclude` 加载多级导航（如 `Include(u => u.Orders).ThenInclude(o => o.Details)`），详见 [使用 IEntityQuery](10-EntityQuery.md)。

### 5.3 键的约定推断

如果不手动指定 `ThisKey` 和 `OtherKey`，框架按以下约定推断：

| 关系类型 | ThisKey（当前实体的键） | OtherKey（目标实体的键） |
|---------|----------------------|----------------------|
| 一对一（1:1） | 当前实体中名为 `{导航属性名}Id` 的属性 | 目标实体的主键 |
| 一对多（1:N） | 当前实体的主键 | 目标实体中名为 `{当前实体类型名}Id` 的属性 |

**1:1 约定示例：**

```csharp
public class User
{
    public int Id { get; set; }
    
    // 约定推断：ThisKey = "DepartmentId"（{导航属性名}Id）
    // 约定推断：OtherKey = Department 的主键 "Id"
    [Navigation]
    public Department Department { get; set; }
    
    // 此属性作为外键，由约定自动识别
    public int DepartmentId { get; set; }
}
```

**1:N 约定示例：**

```csharp
public class User
{
    public int Id { get; set; }
    
    // 约定推断：ThisKey = User 的主键 "Id"
    // 约定推断：OtherKey = Order 中的 "UserId"（{当前实体类型名}Id）
    [Navigation]
    public List<Order> Orders { get; set; }
}
```

### 5.4 手动指定键

当约定不适用时，使用 `[Navigation(ThisKey = "...", OtherKey = "...")]` 手动指定键属性名。

```csharp
public class User
{
    [PrimaryKey("user_id")]
    public int Id { get; set; }
    
    // 手动指定：当前实体的键为 "Id"，目标实体的键为 "CreatorId"
    [Navigation(ThisKey = "Id", OtherKey = "CreatorId")]
    public List<Order> CreatedOrders { get; set; }
}

public class Order
{
    [PrimaryKey("order_id")]
    public int Id { get; set; }
    
    [Column("creator_id")]
    public int CreatorId { get; set; }
}
```

### 5.5 加载导航属性

使用 `FromSql<T>().Include()` 方法加载导航属性。框架采用分裂查询策略：先查询主实体，再根据主外键值查询关联表，最后在内存中装配。

```csharp
// 查询用户并加载其所有订单（查询枚举仅提供异步方法）
var users = await db.FromSql<User>("SELECT * FROM Users")
    .Include(u => u.Orders)
    .ToListAsync();

foreach (var user in users)
{
    Console.WriteLine($"用户: {user.UserName}");
    foreach (var order in user.Orders)
    {
        Console.WriteLine($"  订单: {order.OrderDate}");
    }
}
```

**加载多个导航属性：**

```csharp
var users = await db.FromSql<User>("SELECT * FROM Users")
    .Include(u => u.Department)
    .Include(u => u.Orders)
    .ToListAsync();
```

**注意事项：**

- 导航属性不能同时标记 `[Column]` 或 `[PrimaryKey]` 特性
- 1:N 导航属性必须是 `List<T>` 或可由 `List<T>` 赋值的泛型接口（如 `IList<T>`、`ICollection<T>`）
- 分裂查询会在连接作用域中执行，确保多次查询复用同一连接

## 6. 完整示例

```csharp
using System;
using System.Collections.Generic;
using Xbc.DbAccess;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.Sqlite;

// 用户实体
[Table("Users")]
public class User
{
    [PrimaryKey("user_id", IsIdentity = true)]
    public int Id { get; set; }
    
    [Column("user_name")]
    public string UserName { get; set; }
    
    [Column("email")]
    public string Email { get; set; }
    
    [Column("age")]
    public int Age { get; set; }
    
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
    
    [Column("updated_at", DbNullHandling = DbNullHandling.UseDefaultValue)]
    public DateTime? UpdatedAt { get; set; }
    
    [NotMapped]
    public bool IsAdult => Age >= 18;
    
    // 导航属性：用户的所有订单
    [Navigation]
    public List<Order> Orders { get; set; }
}

// 订单实体
[Table("Orders")]
public class Order
{
    [PrimaryKey("order_id", IsIdentity = true)]
    public int OrderId { get; set; }
    
    [Column("user_id")]
    public int UserId { get; set; }
    
    [Column("order_date")]
    public DateTime OrderDate { get; set; }
    
    [Column("total_amount")]
    public decimal TotalAmount { get; set; }
    
    [Column("status")]
    public OrderStatus Status { get; set; }
}

public enum OrderStatus
{
    Pending = 0,
    Processing = 1,
    Shipped = 2,
    Delivered = 3,
    Cancelled = 4
}

// 使用示例
public class EntityDefinitionExample
{
    public static async Task Run()
    {
        var factory = SqliteAccessFactory.CreateSqliteFactory("Data Source=:memory:");
        
        using var db = factory.Create();
        
        // 创建表
        await db.ExecuteNonQueryAsync(@"
            CREATE TABLE Users (
                user_id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_name TEXT,
                email TEXT,
                age INTEGER,
                created_at TEXT,
                updated_at TEXT
            )");
        
        // 插入数据
        await db.ExecuteNonQueryAsync(
            @"INSERT INTO Users (user_name, email, age, created_at) 
               VALUES (@UserName, @Email, @Age, @CreatedAt)",
            new
            {
                UserName = "张三",
                Email = "zhangsan@example.com",
                Age = 25,
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        
        // 查询数据并映射到实体
        var users = await db.FromSql<User>("SELECT * FROM Users").ToListAsync();
        
        foreach (var user in users)
        {
            Console.WriteLine($"用户: {user.UserName}, 邮箱: {user.Email}, 年龄: {user.Age}, 成年: {user.IsAdult}");
        }
        
        // 查询用户并加载导航属性
        var usersWithOrders = await db.FromSql<User>("SELECT * FROM Users")
            .Include(u => u.Orders)
            .ToListAsync();
    }
}
```

**完整代码**：可运行的完整示例代码请参见 [examples/08_EntityDefinition/Program.cs](./examples/08_EntityDefinition/Program.cs)

**运行示例**：
```bash
cd examples/08_EntityDefinition
dotnet run
```

## 下一步

- [实体映射与缓存治理](09-EntityMapping.md) - 深入了解实体映射与缓存治理机制
- [实体查询](10-EntityQuery.md) - 使用 `FromSql<T>` 进行实体查询，包括导航属性加载
