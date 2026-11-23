# 连接配置选项

本文档介绍如何配置 Xbc.DbAccess 框架的数据库连接。

## 目录

1. [连接字符串](#1-连接字符串)
   - [1.1 使用连接字符串](#11-使用连接字符串)
   - [1.2 常用数据库连接字符串](#12-常用数据库连接字符串)
2. [连接配置选项](#2-连接配置选项)
   - [2.1 基础连接选项](#21-基础连接选项)
   - [2.2 连接池配置选项](#22-连接池配置选项)
   - [2.3 事务配置选项](#23-事务配置选项)
   - [2.4 诊断配置选项](#24-诊断配置选项)
3. [完整示例](#3-完整示例)

## 1. 连接字符串

### 1.1 使用连接字符串

连接字符串是配置数据库连接的基础，不同数据库有不同的格式。

**内存数据库**（适用于测试和临时数据处理）：

```csharp
// SQLite 内存数据库
var factory = SqliteAccessFactory.CreateSqliteFactory("Data Source=:memory:");
```

**文件数据库**（适用于单机应用程序）：

```csharp
// SQLite 文件数据库
var factory = SqliteAccessFactory.CreateSqliteFactory("Data Source=myapp.db");
```

**网络数据库**（适用于生产环境）：

```csharp
// SQL Server
var factory = SqlAccessFactory.CreateSqlServerFactory("Server=localhost;Database=mydb;...");

// MySQL
var factory = MySqlAccessFactory.CreateMySqlFactory("Server=localhost;Database=mydb;...");
```

### 1.2 常用数据库连接字符串

#### SQLite

```
内存数据库: Data Source=:memory:
文件数据库: Data Source=myapp.db
完整路径:   Data Source=C:\Data\myapp.db
只读模式:   Data Source=myapp.db;Mode=ReadOnly
读写模式:   Data Source=myapp.db;Mode=ReadWrite
```

#### SQL Server

```
Windows 身份验证:
  Server=localhost;Database=MyDatabase;Integrated Security=true;TrustServerCertificate=true

SQL Server 身份验证:
  Server=localhost;Database=MyDatabase;User Id=sa;Password=your_password;TrustServerCertificate=true

指定端口:
  Server=localhost,1433;Database=MyDatabase;User Id=sa;Password=your_password
```

#### MySQL

```
基础配置:
  Server=localhost;Database=mydb;User=root;Password=your_password;Port=3306

包含字符集:
  Server=localhost;Database=mydb;User=root;Password=your_password;Charset=utf8mb4
```

#### PostgreSQL

```
基础配置:
  Host=localhost;Database=mydb;Username=postgres;Password=your_password;Port=5432

SSL 模式:
  Host=localhost;Database=mydb;Username=postgres;Password=your_password;SSL Mode=Require
```

#### Oracle

```
基础配置 (使用 TNS):
  Data Source=MyOracleDB;User Id=myUsername;Password=myPassword;

使用 EZ Connect:
  Data Source=//hostname:1521/service_name;User Id=myUsername;Password=myPassword;

使用 TNSNames.ora:
  Data Source=ORCL;User Id=myUsername;Password=myPassword;

集成 Windows 身份验证:
  Data Source=MyOracleDB;Integrated Security=yes;
```

## 2. 连接配置选项

### 2.1 基础连接选项

```csharp
var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
{
    // 连接字符串
    options.ConnectionString = "...";
});
```

### 2.2 连接池配置选项

```csharp
var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
{
    options.ConnectionString = "...";

    // 连接池配置
    options.PoolOptions = new DbConnectionPoolOptions
    {
        Enabled = true,                                     // 启用连接池
        MinPoolSize = 5,                                    // 最小连接数
        MaxPoolSize = 100,                                  // 最大连接数
        ConnectionWaitTimeout = TimeSpan.FromSeconds(3),    // 连接等待超时
        MaxIdleTime = TimeSpan.FromMinutes(15),             // 最大空闲时间
        CleanupInterval = TimeSpan.FromMinutes(5)           // 清理间隔
    };
});
```

### 2.3 事务配置选项

框架始终支持嵌套事务（实际使用保存点实现），无需额外配置：

```csharp
var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
{
    options.ConnectionString = "...";
});
```

**说明**：

- 嵌套事务开箱即用：在事务内部开启新事务时，框架自动使用保存点实现。
- 嵌套事务依赖数据库的保存点机制，部分数据库可能不支持。

### 2.4 诊断配置选项

```csharp
var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
{
    options.ConnectionString = "...";

    // 启用连接追踪
    options.ConnectionTrackingOptions = new DbConnectionTrackingOptions
    {
        Enabled = true,             // 启用连接追踪
        RecordStackTrace = true     // 记录堆栈信息（有性能开销）
    };

    // 连接泄漏判定阈值（用于 OTel 泄漏指标判定，连接诊断通过 OpenTelemetry 指标暴露）
    options.PossibleLeakHoldTimeThreshold = TimeSpan.FromMinutes(5);   // 可能泄漏阈值（默认 5 分钟）
    options.CriticalLeakHoldTimeThreshold = TimeSpan.FromMinutes(10);  // 严重泄漏阈值（默认 10 分钟）

    // 启用统计
    options.EnableStatistics = true;
});
```

## 3. 完整示例

以下是一个完整的连接配置示例：

```csharp
using Xbc.DbAccess;
using Xbc.DbAccess.Sqlite;

// 创建工厂并配置连接选项
var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
{
    // 连接字符串
    options.ConnectionString = "Data Source=myapp01.db";

    // 连接池配置（不启用）
    options.PoolOptions = new DbConnectionPoolOptions
    {
        Enabled = false,
    };

    // 启用统计
    options.EnableStatistics = true;
});

// 使用工厂创建 IDbAccess 实例
using (var db = factory.Create())
{
    // 执行数据库操作
    var result = await db.ExecuteScalarAsync("SELECT 1");
    Console.WriteLine($"连接成功: {result}");

    // 查看连接统计
    var stats = factory.ConnectionManager.GetStatistics();
    Console.WriteLine($"总请求数: {stats.TotalRequests}");
    Console.WriteLine($"创建连接数: {stats.TotalCreated}");
    Console.WriteLine($"释放连接数: {stats.TotalReleased}");
}
```

**完整代码**：可运行的完整示例代码请参见 [examples/01\_ConnectionConfiguration/Program.cs](./examples/01_ConnectionConfiguration/Program.cs)

**运行示例**：

```bash
cd examples/01_ConnectionConfiguration
dotnet run
```

## 下一步

- [连接池](02-ConnectionPool.md) - 了解如何使用连接池优化数据库连接性能

