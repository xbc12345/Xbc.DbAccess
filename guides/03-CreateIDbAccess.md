# 创建 IDbAccess 实例

## 什么是 IDbAccess

`IDbAccess` 是 Xbc.DbAccess 库的核心接口，定义了数据库访问的标准操作。它封装了 ADO.NET 的底层复杂性，提供了一套简洁、统一的 API 来执行数据库操作。

### 核心功能

| 功能类别 | 方法 | 说明 |
|---------|------|------|
| **SQL 执行** | `ExecuteNonQueryAsync` | 执行 INSERT、UPDATE、DELETE 语句，返回受影响行数 |
| | `ExecuteScalarAsync` | 执行查询，返回第一行第一列的值 |
| | `ExecuteReaderAsync` | 执行查询，返回 `DbDataReader` 进行流式读取 |
| | `ExecuteDataSetAsync` | 执行查询，返回 `DataSet` 包含所有结果集 |
| **事务管理** | `BeginTransactionAsync` | 开启数据库事务，确保操作的原子性 |
| **连接作用域** | `BeginConnectionScopeAsync` | 创建连接作用域，优化多操作性能 |
| **提供程序访问** | `ProviderFactory` | 获取数据库特定的工厂，创建参数和命令 |

### 设计特点

- **统一接口**：屏蔽不同数据库（SQL Server、MySQL、SQLite 等）的差异
- **资源管理**：实现 `IDisposable`，确保资源正确释放
- **纯异步设计**：仅提供异步方法（Async 后缀），支持在多个异步操作上下文中使用
- **类型安全**：通过 `DbParameter` 实现参数化查询，防止 SQL 注入
- **提供扩展方法**：`IDbAccess` 提供了一些扩展方法，如分页查询、存储过程执行等

### 使用流程

```
创建工厂 → 创建 IDbAccess → 执行操作 → 释放资源
```

本文档介绍如何使用工厂模式创建 `IDbAccess` 实例。

## 目录

1. [工厂模式介绍](#1-工厂模式介绍)
   - [1.1 IDbAccessFactory 接口](#11-IDbAccessFactory-接口)
   - [1.2 创建 IDbAccess 实例](#12-创建-IDbAccess-实例)
2. [依赖注入集成](#2-依赖注入集成)
   - [2.1 何时使用依赖注入](#21-何时使用依赖注入)
   - [2.2 生命周期管理（重要）](#22-生命周期管理重要)
   - [2.3 ASP.NET Core 配置](#23-aspnet-core-配置)
   - [2.4 在控制器中使用](#24-在控制器中使用)
   - [2.5 使用工厂直接创建](#25-使用工厂直接创建)
3. [最佳实践](#3-最佳实践)
   - [3.1 连接配置选项](#31-连接配置选项)
   - [3.2 使用工厂创建 IDbAccess](#32-使用工厂创建-IDbAccess)
   - [3.3 注册 IDbAccess 为 Scoped 生命周期](#33-注册-IDbAccess-为-Scoped-生命周期)
   - [3.4 使用 ConnectionManager 手动管理连接](#34-使用-ConnectionManager-手动管理连接)
   - [3.5 多数据库场景](#35-多数据库场景)
   - [3.6 在单例服务中使用 IDbAccessFactory](#36-在单例服务中使用-IDbAccessFactory)
4. [完整示例](#4-完整示例)

## 1. 工厂模式介绍

Xbc.DbAccess 使用工厂模式创建数据库访问对象。每个数据库提供程序都有自己的工厂类。

### 1.1 IDbAccessFactory 接口

```csharp
public interface IDbAccessFactory
{
    /// <summary>
    /// 数据库访问提供程序工厂。
    /// </summary>
    DbAccessProviderFactory ProviderFactory { get; }

    /// <summary>
    /// 连接管理器。
    /// </summary>
    IDbConnectionManager ConnectionManager { get; }

    /// <summary>
    /// 获取数据库服务器信息：首次调用时获取并缓存，失败与已取消的任务不缓存，下次调用重新探测。
    /// </summary>
    /// <returns>数据库服务器信息。</returns>
    Task<DbServerInfo> GetServerInfoAsync();

    /// <summary>
    /// 创建数据库访问对象。
    /// </summary>
    /// <returns></returns>
    IDbAccess Create();
}
```

### 1.2 创建 IDbAccess 实例

**基本流程**：

```csharp
using Xbc.DbAccess.Sqlite;

// 1. 创建工厂
var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
{
    options.ConnectionString = "...";
});

// 2. 创建 IDbAccess 实例
using var db = factory.Create();

// 3. 执行数据库操作
var result = await db.ExecuteScalarAsync("SELECT 1");
```

**不同数据库的工厂创建方式**：

| 数据库            | 工厂类                   | 创建方法                                                          |
| -------------- | --------------------- | ------------------------------------------------------------- |
| **SQLite**     | `SqliteAccessFactory` | `SqliteAccessFactory.CreateSqliteFactory(options => { ... })` |
| **SQL Server** | `SqlAccessFactory`    | `SqlAccessFactory.CreateSqlServerFactory(options => { ... })` |
| **MySQL**      | `MySqlAccessFactory`  | `MySqlAccessFactory.CreateMySqlFactory(options => { ... })`   |
| **PostgreSQL** | `NpgsqlAccessFactory` | `NpgsqlAccessFactory.CreateNpgsqlFactory(options => { ... })` |
| **Oracle**     | `OracleAccessFactory` | `OracleAccessFactory.CreateOracleFactory(options => { ... })` |

## 2. 依赖注入集成

### 2.1 何时使用依赖注入

**使用依赖注入的场景**：

| 场景                        | 说明                                               |
| ------------------------- | ------------------------------------------------ |
| **ASP.NET Core Web 应用**   | 框架内置 DI 容器，生命周期管理方便，自动处理释放                       |
| **使用了其他 DI 容器，如 Autofac** | 自定义 DI 容器，需要手动注册 IDbAccessFactory 和 IDbAccess 实例 |

**总之**：当需要在多个服务中共享同一个 IDbAccess 实例（Scoped 生命周期）时。例如，在一个 HTTP 请求中，多个控制器或服务需要使用同一个事务或连接作用域来执行数据库操作。

**使用工厂模式的场景**：

| 场景               | 说明                                      |
| ---------------- | --------------------------------------- |
| **单例服务/后台任务**    | 单例服务生命周期长，应使用工厂在需要时创建 IDbAccess，用完后立即释放 |
| **控制台应用/脚本**     | 简单直接，无需配置 DI 容器                         |
| **需要动态创建连接**     | 根据不同条件创建不同的数据库连接                        |
| **需要精细控制连接生命周期** | 明确控制何时创建和释放连接                           |

**核心原则**：

- **依赖注入**：适用于需要在多个组件间共享 IDbAccess 的场景（如一个请求内），由框架管理生命周期
- **工厂模式**：适用于需要明确控制连接生命周期的场景，特别是单例服务和后台任务

### 2.2 生命周期管理（重要）

在使用依赖注入时，正确的生命周期配置至关重要：

| 组件 | 推荐生命周期 | 说明 |
|------|-------------|------|
| `IDbAccessFactory` | **单例 (Singleton)** | 工厂包含连接管理器、追踪器等，应该全局共享 |
| `IDbAccess` | **作用域 (Scoped)** 或 **瞬态 (Transient)** | 每次请求或每次操作创建，用完后立即释放 |

⚠️ **警告**：`IDbAccess` 的生命周期**不能**比 `IDbAccessFactory` 长，否则会导致资源泄漏。例如，不要将 `IDbAccess` 注册为单例，而工厂注册为瞬态。

**错误示例**：
```csharp
// ❌ 错误：工厂是瞬态，IDbAccess 是单例
services.AddTransient<IDbAccessFactory>(...);
services.AddSingleton<IDbAccess>(provider => 
    provider.GetRequiredService<IDbAccessFactory>().Create());
// 这会导致单例的 IDbAccess 持有瞬态工厂的引用，造成内存泄漏
```

**正确示例**：
```csharp
// ✅ 正确：工厂是单例，IDbAccess 是作用域
services.AddSingleton<IDbAccessFactory>(...);
services.AddScoped<IDbAccess>(provider => 
    provider.GetRequiredService<IDbAccessFactory>().Create());
```

### 2.3 ASP.NET Core 配置

框架在各方言包中内置了 DI 注册扩展（`AddSqliteDbAccess` / `AddSqlServerDbAccess` / `AddMySqlDbAccess` / `AddNpgsqlDbAccess` / `AddOracleDbAccess`），自动注册工厂（单例）与 `IDbAccess`（Scoped），**推荐优先使用**：

```csharp
// appsettings.json
{
    "ConnectionStrings": {
        "Default": "Data Source=myapp.db"
    }
}

// Program.cs
using Xbc.DbAccess.Sqlite;

// 从配置文件中读取连接字符串
var connectionString = builder.Configuration.GetConnectionString("Default");

// 一行完成注册：工厂（Singleton）+ IDbAccess（Scoped）
builder.Services.AddSqliteDbAccess(connectionString);
```

也可以用配置委托做完整连接配置：

```csharp
builder.Services.AddSqliteDbAccess(options =>
{
    options.ConnectionString = connectionString;
    options.PoolOptions = new DbConnectionPoolOptions { Enabled = true };
});
```

**其他 DI 容器**（如 Autofac）：可手动注册，生命周期约定不变——工厂单例、`IDbAccess` Scoped：

```csharp
// 手动注册（框架内置扩展不可用时）
builder.Services.AddSingleton<IDbAccessFactory>(provider =>
    SqliteAccessFactory.CreateSqliteFactory(options =>
        options.ConnectionString = connectionString));

builder.Services.AddScoped<IDbAccess>(provider =>
    provider.GetRequiredService<IDbAccessFactory>().Create());
```

### 2.4 在控制器中使用

```csharp
// 在控制器中注入 IDbAccess（推荐用于 Web 应用）
public class UsersController : ControllerBase
{
    private readonly IDbAccess _dbAccess;
    
    public UsersController(IDbAccess dbAccess)
    {
        _dbAccess = dbAccess;
    }
    
    [HttpGet]
    public IActionResult GetUsers()
    {
        // 使用 _dbAccess 执行数据库操作
        // 注意：IDbAccess 的生命周期由 DI 容器管理
    }
}
```

### 2.5 使用工厂直接创建

```csharp
// 在服务中注入工厂（推荐用于后台任务或单例服务）
public class UserService
{
    private readonly IDbAccessFactory _factory;
    
    public UserService(IDbAccessFactory factory)
    {
        _factory = factory;
    }
    
    public void ProcessUsers()
    {
        // 每次需要时创建新的 IDbAccess 实例
        using (var db = _factory.Create())
        {
            // 使用 db 执行数据库操作
            // 注意：使用 using 确保及时释放
        }
    }
}
```

## 3. 最佳实践

### 3.1 连接配置选项

在生产环境中，所有连接配置参数应从配置文件获取：

```csharp
// appsettings.json
{
  "DbOptions": {
    "ConnectionString": "...",
    "PoolOptions": {
      "Enabled": true,
      "MinPoolSize": 5,
      "MaxPoolSize": 100
    },
    "EnableStatistics": true
  }
}

// Program.cs
var options = builder.Configuration
    .GetSection("DbOptions")
    .Get<DbConnectionOptions>() ?? new DbConnectionOptions();

// 注册工厂（单例生命周期）
builder.Services.AddSingleton<IDbAccessFactory>(provider =>
{
     return SqliteAccessFactory.CreateSqliteFactory(options);
});
```

### 3.2 使用工厂创建 IDbAccess

```csharp
// 在服务中注入工厂并创建 IDbAccess
public class OrderService
{
    private readonly IDbAccessFactory _factory;

    public OrderService(IDbAccessFactory factory)
    {
        _factory = factory;
    }

    public async Task ProcessOrdersAsync()
    {
        // 好的做法：使用 using 语句
        using (var db = _factory.Create())
        {
            await using (var reader = await db.ExecuteReaderAsync("SELECT * FROM Orders"))
            {
                while (reader.Read())
                {
                    // 处理订单数据
                }
            }
        } // db 自动释放
    }

    public async Task BadExampleAsync()
    {
        // 避免这样做：可能忘记释放
        var db = _factory.Create();
        var reader = await db.ExecuteReaderAsync("SELECT * FROM Orders");
        // 忘记调用 db.Dispose()
    }
}
```

### 3.3 注册 IDbAccess 为 Scoped 生命周期

如果你希望在服务中直接注入 `IDbAccess` 而不是工厂，可以将其注册为 Scoped 生命周期：

```csharp
// Program.cs - 注册 IDbAccess 为 Scoped
builder.Services.AddScoped<IDbAccess>(provider =>
{
    var factory = provider.GetRequiredService<IDbAccessFactory>();
    return factory.Create();
});

// 在控制器或服务中直接使用 IDbAccess
public class UserController : ControllerBase
{
    private readonly IDbAccess _db;

    public UserController(IDbAccess db)
    {
        _db = db;
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        await using (var reader = await _db.ExecuteReaderAsync("SELECT * FROM Users"))
        {
            while (reader.Read())
            {
                // 处理用户数据
            }
        }

        return Ok( ... );
    }
}
```

**注意事项**：

- `IDbAccess` 注册为 Scoped，每个 HTTP 请求会创建一个实例
- `IDbAccess` 支持异步操作，多个异步操作会共享相关上下文信息
- 请求结束时，框架会自动释放 `IDbAccess`
- 适用于大多数 Web 应用场景

### 3.4 使用 ConnectionManager 手动管理连接

`IDbAccessFactory` 提供了 `ConnectionManager` 属性，允许你直接获取数据库连接进行手动管理。这种方式适用于需要脱离框架标准流程的特殊场景。

**适用场景**：

- 需要使用特定的 ADO.NET 功能
- 需要与其他数据库工具集成
- 需要完全控制连接生命周期

**示例**：

```csharp
public class AdvancedDataService
{
    private readonly IDbAccessFactory _factory;
    
    public AdvancedDataService(IDbAccessFactory factory)
    {
        _factory = factory;
    }
    
    public async Task ExecuteWithRawConnectionAsync()
    {
        // 通过 ConnectionManager 获取原始连接
        var connectionManager = _factory.ConnectionManager;

        // 获取或创建数据库连接（需要手动管理）
        // GetOrCreateAsync 会自动打开连接
        await using (var connection = await connectionManager.GetOrCreateAsync())
        {
            // 这里可以使用原始的 DbConnection 进行自定义操作
            // 例如：使用特定的 ADO.NET 功能

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Users";
                await using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        // 处理数据
                    }
                }
            }
            // 连接需要手动关闭和释放
        }
    }
}
```

**注意事项**：

- `GetOrCreateAsync` 会自动打开连接，无需手动打开
- 手动管理连接时，需要自行处理连接的关闭和释放
- 建议始终使用 `using` 语句确保连接正确释放
- 这种方式脱离了框架的连接池和连接作用域管理，需谨慎使用
- 特别需要注意的是，当启用了连接池时，如果连接未被正确释放，将会导致连接池资源耗尽

### 3.5 多数据库场景

**注册多个命名数据库**

框架支持命名注册，配合 `IDbAccessFactoryProvider` 按名称解析。在各方言扩展上指定名称即可：

```csharp
// appsettings.json
{
  "DbOptions": {
    "PrimaryDb": {
      "ConnectionString": "Server=localhost;Database=Primary;User Id=sa;Password=***",
      "PoolOptions": {
        "Enabled": true,
        "MinPoolSize": 10,
        "MaxPoolSize": 200
      },
      "EnableStatistics": true
    },
    "LogDb": {
      "ConnectionString": "Data Source=logs.db"
    }
  }
}

// Program.cs - 命名注册（默认数据库 + 命名数据库可并存）
builder.Services.AddSqlServerDbAccess(options =>
    builder.Configuration.GetSection("DbOptions:PrimaryDb").Bind(options));

builder.Services.AddSqliteDbAccess("LogDb", options =>
    builder.Configuration.GetSection("DbOptions:LogDb").Bind(options));
```

也可以直接传连接字符串：

```csharp
builder.Services.AddSqlServerDbAccess("PrimaryDb", "Server=localhost;Database=Primary;...");
builder.Services.AddSqliteDbAccess("LogDb", "Data Source=logs.db");
```

**在服务中按名称使用**

注入 `IDbAccessFactoryProvider`，按名称取工厂或直接创建 `IDbAccess`：

```csharp
public class MultiDbService
{
    private readonly IDbAccessFactoryProvider _factoryProvider;

    public MultiDbService(IDbAccessFactoryProvider factoryProvider)
    {
        _factoryProvider = factoryProvider;
    }

    public async Task ProcessDataAsync()
    {
        // 按名称创建 IDbAccess 实例，用完释放
        using (var primaryDb = _factoryProvider.Create("PrimaryDb"))
        {
            await using var reader = await primaryDb.ExecuteReaderAsync("SELECT * FROM Users");
        }

        using (var logDb = _factoryProvider.Create("LogDb"))
        {
            await logDb.ExecuteNonQueryAsync("INSERT INTO Logs (Message) VALUES ('Operation completed')");
        }
    }
}
```

也可以按名称取工厂后在需要时手动创建：

```csharp
IDbAccessFactory factory = _factoryProvider.GetFactory("LogDb");
using var db = factory.Create();
```

**注意事项**：

- 命名注册只注册 `IDbAccessFactoryProvider`，不提供默认的 `IDbAccessFactory` / `IDbAccess` 注入；如需默认数据库，再额外调用一次不带名称的 `AddXxxDbAccess` 重载
- `_factoryProvider.Names` 可枚举所有已注册的数据库名称
- 名称需唯一，重复注册同一名称在构建工厂提供器或调用 `Register` 时抛出 `InvalidOperationException`
- 多数据库场景下，每个 `IDbAccess` 实例独立管理自己的连接，确保用完释放

### 3.6 在单例服务中使用 IDbAccessFactory

**建议在单例服务中使用** **`IDbAccessFactory`** **而不是** **`IDbAccess`**：

```csharp
// 推荐：在单例服务中使用工厂
public class BackgroundTaskService : BackgroundService
{
    private readonly IDbAccessFactory _factory;
    
    public BackgroundTaskService(IDbAccessFactory factory)
    {
        _factory = factory;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // 每次循环创建新的 IDbAccess 实例
            using (var db = _factory.Create())
            {
                await using var reader = await db.ExecuteReaderAsync("SELECT * FROM Tasks WHERE Status = 'Pending'");
                // 处理任务...
            }
            
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
```

**为什么不建议在单例服务中直接注入** **`IDbAccess`**：

- `IDbAccess` 注册为 Scoped 生命周期，与单例服务的生命周期不匹配
- 单例服务中持有 `IDbAccess` 可能导致连接泄漏或状态不一致
- 使用工厂可以确保每次需要时创建新的实例，避免这些问题

## 4. 完整示例

以下示例展示了如何使用工厂模式创建 IDbAccess 实例：

```csharp
using Xbc.DbAccess;
using Xbc.DbAccess.Sqlite;

// 1. 创建工厂
var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
{
    options.ConnectionString = "Data Source=myapp03.db";
    options.PoolOptions = new DbConnectionPoolOptions
    {
        Enabled = true,
        MinPoolSize = 5,
        MaxPoolSize = 20
    };
});

// 2. 创建 IDbAccess 实例并执行操作
using (var db = factory.Create())
{
    var result = await db.ExecuteScalarAsync("SELECT 1");
    Console.WriteLine($"连接成功: {result}");
}
```

**完整代码**：可运行的完整示例代码请参见 [examples/03\_CreateDbAccess/Program.cs](./examples/03_CreateDbAccess/Program.cs)

**运行示例**：

```bash
cd examples/03_CreateDbAccess
dotnet run
```

## 下一步

- [执行 SQL](04-ExecuteSql.md) - 如何使用 IDbAccess 执行 SQL

