# 连接池

本文档介绍如何使用 Xbc.DbAccess 框架的连接池功能，以及连接池的配置、连接生命周期管理等详细内容。

## 目录

1. [连接池概述](#1-连接池概述)
   - [1.1 什么是连接池](#11-什么是连接池)
   - [1.2 连接池的工作原理](#12-连接池的工作原理)
   - [1.3 连接池的优势](#13-连接池的优势)
   - [1.4 何时使用连接池](#14-何时使用连接池)
2. [连接池配置](#2-连接池配置)
   - [2.1 启用禁用连接池](#21-启用禁用连接池)
   - [2.2 连接池配置选项](#22-连接池配置选项)
3. [连接的获取与回收](#3-连接的获取与回收)
   - [3.1 基本 SQL 执行](#31-基本-sql-执行)
   - [3.2 连接作用域内](#32-连接作用域内)
   - [3.3 事务中](#33-事务中)
   - [3.4 使用 DataReader](#34-使用-datareader)
   - [3.5 嵌套事务与嵌套连接作用域](#35-嵌套事务与嵌套连接作用域)
   - [3.6 连接获取与回收时机汇总](#36-连接获取与回收时机汇总)
4. [连接池调优](#4-连接池调优)
   - [4.1 连接池大小调优](#41-连接池大小调优)
   - [4.2 连接超时与空闲时间调优](#42-连接超时与空闲时间调优)
   - [4.3 连接池的监控与诊断概述](#43-连接池的监控与诊断概述)
5. [完整示例](#5-完整示例)

## 1. 连接池概述

### 1.1 什么是连接池

连接池是一种用于管理和复用数据库连接的技术。在应用程序与数据库交互时，建立连接是一个相对耗时的操作。连接池通过预先创建并维护一组数据库连接，避免了频繁创建和关闭连接的开销，从而提 高应用程序的性能和响应速度。

### 1.2 连接池的工作原理

Xbc.DbAccess 框架的连接池采用**预热机制**和**状态管理**，确保连接的高效复用和资源合理分配。

#### 连接池内部结构

连接池由以下核心组件组成：

- **空闲连接池**：存储已回收的连接，可供复用
- **活动连接池**：跟踪正在使用的连接
- **并发控制器**：控制并发访问，确保不超过最大连接数限制
- **清理定时器**：定期检查并清理长时间空闲的连接

#### 连接的生命周期状态

框架中的每个连接都有明确的生命周期状态：

| 状态标识 | 状态名称 | 说明 |
|----------|----------|------|
| **`Created`** | 已创建 | 连接对象已创建，等待首次使用 |
| **`Active`** | 活动中 | 连接已打开，正在被应用程序使用 |
| **`Recycling`** | 回收中 | 连接正在返回连接池的过渡状态 |
| **`Recycled`** | 已回收 | 连接已回到空闲连接池，可供下次复用 |
| **`Disposed`** | 已释放 | 连接已实际关闭，物理连接已断开，资源已释放 |

#### 连接获取流程

当应用程序请求连接时，连接池按以下顺序处理：

1. **尝试从空闲连接池获取**：检查是否有可用的空闲连接，如有则直接返回
2. **等待可用连接**：如果空闲连接池为空，等待其他连接被释放（带超时）
3. **再次检查空闲连接池**：可能在等待期间有其他连接被回收
4. **创建新连接**：如果仍未获取到连接且未达最大连接数，创建新的物理连接

#### 连接回收流程

当连接被释放时（调用 `Close()` 或 `Dispose()`）：

1. **状态变更**：从 `Active` 变为 `Recycling`
2. **触发回收事件**：通知连接池处理回收
3. **移入空闲连接池**：从活动连接池中移除，加入空闲连接池
4. **状态标记**：变为 `Recycled`，等待下次复用

#### 连接清理机制

连接池通过定时器定期执行清理：

- **检查条件**：连接空闲时间超过 `MaxIdleTime` 且空闲连接数 > `MinPoolSize`
- **清理操作**：调用 `ActualDispose()` 真正关闭物理连接
- **资源释放**：释放连接占用的资源，允许新的连接请求

#### 关键特性

- **预热机制**：连接池初始化时创建并打开 `MinPoolSize` 个连接，避免首次请求时的延迟
- **状态线程安全**：使用 `Interlocked` 原子操作确保状态变更的线程安全
- **异常处理**：连接失效时自动从池中移除，确保连接池健康

### 1.3 连接池的优势

- **性能提升**：避免了频繁创建和关闭连接的开销，减少了数据库服务器的压力。
- **资源管理**：通过限制最大连接数，防止数据库服务器因连接过多而崩溃。
- **连接复用**：连接可以被多个请求复用，提高了资源利用率。
- **自动管理**：连接池自动处理连接的创建、释放和清理，简化了应用程序的开发。

### 1.4 何时使用连接池

| 场景 | 建议使用 | 说明 |
|------|---------|------|
| **Web 应用 / API 服务** | ✅ 使用 | 高并发，需要频繁访问数据库 |
| **Windows 服务 / 后台任务** | ✅ 使用 | 长时间运行，持续处理数据 |
| **桌面应用程序** | ✅ 使用 | 用户交互频繁，需要快速响应 |
| **微服务** | ✅ 使用 | 每个服务独立管理连接，连接池提高资源利用率 |
| **一次性脚本** | ❌ 不使用 | 数据迁移、定时任务等执行完就退出的程序 |
| **低并发工具** | ❌ 不使用 | 并发量很低，连接创建开销可以忽略 |
| **单元测试** | ❌ 不使用 | 临时测试，不需要长期保持连接 |

**决策建议**：
- 如果应用**频繁访问数据库**且**运行时间较长**，启用连接池
- 如果应用**偶尔访问数据库**或**执行完立即退出**，禁用连接池

## 2. 连接池配置

### 2.1 启用禁用连接池

连接池默认是禁用的，需要显式启用。在某些场景下（如一次性脚本），也可以显式禁用连接池：

```csharp
// 启用连接池（推荐用于生产环境）
var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
{
    options.ConnectionString = "...";
    options.PoolOptions = new DbConnectionPoolOptions
    {
        Enabled = true  // 启用连接池
    };
});

// 禁用连接池（适用于一次性脚本）
var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
{
    options.ConnectionString = "...";
    options.PoolOptions = new DbConnectionPoolOptions
    {
        Enabled = false  // 禁用连接池
    };
});
```

### 2.2 连接池配置选项

根据应用需求配置连接池的各项参数：

```csharp
var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
{
    options.ConnectionString = "...";
    options.PoolOptions = new DbConnectionPoolOptions
    {
        Enabled = true,                                 // 启用连接池（默认 false）
        MinPoolSize = 5,                                // 最小连接数（默认5）
        MaxPoolSize = 100,                              // 最大连接数（默认100）
        ConnectionWaitTimeout = TimeSpan.FromSeconds(3),// 连接等待超时（默认3秒）
        MaxIdleTime = TimeSpan.FromMinutes(15),         // 最大空闲时间（默认15分钟）
        CleanupInterval = TimeSpan.FromMinutes(5),      // 清理间隔（默认5分钟）
        ValidationSkipInterval = TimeSpan.FromSeconds(10), // 跳过健康检查的间隔（默认10秒）
        ValidationCommandTimeout = 3                    // 健康检查命令超时秒数（默认3）
    };
});
```

**配置参数说明**：

| 参数                      | 默认值     | 说明                    |
| ----------------------- | ------- | --------------------- |
| `Enabled`               | `false` | 是否启用连接池               |
| `MinPoolSize`           | `5`     | 最小连接数，应用启动时预创建的连接数    |
| `MaxPoolSize`           | `100`   | 最大连接数，连接池允许的最大连接数     |
| `ConnectionWaitTimeout` | `3秒`    | 连接等待超时，获取连接的最大等待时间    |
| `MaxIdleTime`           | `15分钟`  | 最大空闲时间，超过此时间的空闲连接将被关闭 |
| `CleanupInterval`       | `5分钟`   | 清理间隔，连接池检查并清理空闲连接的频率  |
| `ValidationSkipInterval` | `10秒`  | 激活连接时跳过健康检查的间隔：连接已打开且最近使用时间在此间隔内时跳过连通性验证直接复用；`TimeSpan.Zero` 表示每次激活都验证 |
| `ValidationCommandTimeout` | `3`   | 激活连接时执行健康检查命令的超时时间（秒），0 表示不限时 |

**健康检查（激活校验）**：连接从空闲池取出复用时会进行激活校验——若最近使用时间在 `ValidationSkipInterval` 之内则直接复用（减少往返），否则执行一条按方言生成的健康检查命令（`SELECT 1`，Oracle 为 `SELECT 1 FROM DUAL`），校验失败或超时的连接会被丢弃并创建新连接。间隔越长复用性能越好，但断线检测越滞后。

> ⚠️ **与原生连接池二选一**：内建池与 ADO.NET 提供程序的原生连接池（连接串 `Pooling` 开关）相互独立，同时启用会导致物理连接数放大。启用内建池时应关闭原生池（如 SqlClient 的 `Pooling=false`）。

> ⚠️ **会话状态残留（已知限制）**：连接回池复用时仅重置当前数据库上下文，临时表、`SET` 选项、`CONTEXT_INFO`、会话级锁等会话状态会残留给下一个借用者。需要会话级状态时，应经 `IDbAccess.ProviderFactory.DbProviderFactory.CreateConnection()` 创建独立的非池化连接。

## 3. 连接的获取与回收

使用 `IDbAccess` 执行数据库操作时，框架会自动管理连接的获取和回收。了解不同场景下连接何时被获取、何时被回收，有助于编写高效的数据库代码。

### 3.1 基本 SQL 执行

**连接获取时机**：执行 `db.ExecuteXxxAsync()` 时从连接池获取连接

**连接回收时机**：SQL 执行完成后立即返回连接池

**流程**：

```
执行 SQL 前: 从连接池获取连接
   |
   v
执行 SQL: 连接处于使用中
   |
   v
SQL 执行完成: 连接立即返回连接池
```

**示例代码**：

```csharp
using (var db = factory.Create())
{
    // 第1次执行 SQL：从连接池获取连接
    await db.ExecuteScalarAsync("SELECT 1");
    // SQL 执行完成：连接立即返回池中

    // 第2次执行 SQL：从连接池获取连接
    // 注意：可能是同一连接，也可能是其他空闲连接
    await db.ExecuteScalarAsync("SELECT 2");
    // SQL 执行完成：连接再次返回池中
}
// db 释放：清理上下文资源，连接已在池中
```

### 3.2 连接作用域内

**连接获取时机**：调用 `db.BeginConnectionScopeAsync()` 时从连接池获取连接

**连接回收时机**：连接作用域结束时（`scope.Dispose()`）

**流程**：

```
开启连接作用域: 从连接池获取连接
   |
   v
执行多个 SQL: 连接持续处于使用中（复用同一连接）
   |
   v
关闭连接作用域: 连接返回连接池
```

**示例代码**：

```csharp
using (var db = factory.Create())
using (var scope = await db.BeginConnectionScopeAsync())
{
    // 开启连接作用域：从连接池获取连接

    await db.ExecuteScalarAsync("SELECT 1");
    // SQL 执行完成：连接不返回池中，继续被作用域持有

    await db.ExecuteScalarAsync("SELECT 2");
    // SQL 执行完成：连接仍被作用域持有

} // 关闭连接作用域：连接返回连接池
```

### 3.3 事务中

**连接获取时机**：调用 `db.BeginTransactionAsync()` 时从连接池获取连接

**连接回收时机**：事务对象释放时（`transaction.Dispose()`）

**流程**：

```
开启事务: 从连接池获取连接
   |
   v
执行多个 SQL: 连接持续处于使用中（复用同一连接）
   |
   v
提交/回滚事务: 事务标记完成，连接仍被持有
   |
   v
释放事务对象: 连接返回连接池
```

**示例代码**：

```csharp
using (var db = factory.Create())
await using (var transaction = await db.BeginTransactionAsync())
{
    // 开启事务：从连接池获取连接

    await db.ExecuteNonQueryAsync("INSERT INTO Users (Name) VALUES ('Alice')");
    // SQL 执行完成：连接被事务持有

    await db.ExecuteNonQueryAsync("INSERT INTO Users (Name) VALUES ('Bob')");
    // SQL 执行完成：连接仍被事务持有

    await transaction.CommitAsync();
    // 事务提交后：事务标记为完成，但连接仍被事务持有

} // 释放事务对象：连接返回连接池
```

### 3.4 使用 DataReader

**连接获取时机**：调用 `db.ExecuteReaderAsync()` 时从连接池获取连接

**连接回收时机**：DataReader 释放时（`reader.Dispose()`）

**流程**：

```
ExecuteReaderAsync: 从连接池获取连接
   |
   v
读取数据: 连接持续处于使用中
   |
   v
释放 Reader: 连接返回连接池
```

**示例代码**：

```csharp
using (var db = factory.Create())
{
    // ExecuteReaderAsync：从连接池获取连接
    await using (var reader = await db.ExecuteReaderAsync("SELECT * FROM Users"))
    {
        while (reader.Read())
        {
            Console.WriteLine(reader["Name"]);
        }
        // 读取数据期间：连接持续处于使用中
    } // 释放 Reader：连接返回连接池
}
```

**注意**：如果在事务或连接作用域内使用 `DbDataReader`，释放 `DbDataReader` 不会回收连接，连接会保持被事务或作用域持有，直到事务或作用域结束才回收。

```csharp
// 在事务或连接作用域中使用 DataReader
using (var db = factory.Create())
await using (var transaction = await db.BeginTransactionAsync())
// using (var scope = await db.BeginConnectionScopeAsync())
{
    await using (var reader = await db.ExecuteReaderAsync("SELECT * FROM Users"))
    {
        while (reader.Read())
        {
            Console.WriteLine(reader["Name"]);
        }
    } // 释放 Reader：连接不回收，仍被事务或作用域持有

    // 继续执行其他操作...
    await db.ExecuteNonQueryAsync("UPDATE Users SET ...");

    await transaction.CommitAsync();
} // 释放事务或作用域对象：连接才返回连接池
```

### 3.5 嵌套事务与嵌套连接作用域

当存在嵌套事务或嵌套连接作用域时，**只有最外层才持有连接**，内层只是复用外层已获取的连接。

**连接获取时机**：最外层 `BeginTransactionAsync()` 或 `BeginConnectionScopeAsync()` 时从连接池获取连接

**连接回收时机**：最外层事务或连接作用域对象释放时

**流程**（以嵌套连接作用域为例）：

```
外层作用域 Begin: 从连接池获取连接
   |
   v
执行 SQL: 连接被外层持有
   |
   v
内层作用域 Begin: 复用同一连接，不获取新连接
   |
   v
执行 SQL: 连接仍被外层持有
   |
   v
内层作用域结束: 连接不回收，仍被外层持有
   |
   v
继续执行 SQL: 连接仍被外层持有
   |
   v
外层作用域结束: 连接返回连接池
```

#### 嵌套连接作用域示例

```csharp
using (var db = factory.Create())
using (var outerScope = await db.BeginConnectionScopeAsync()) // 最外层作用域：获取连接
{
    await db.ExecuteScalarAsync("SELECT 1");
    // 连接被外层作用域持有

    using (var innerScope = await db.BeginConnectionScopeAsync()) // 内层作用域：复用同一连接，不获取新连接
    {
        await db.ExecuteScalarAsync("SELECT 2");
        // 连接仍被外层作用域持有
    } // 内层作用域结束：连接不回收，仍被外层持有

    await db.ExecuteScalarAsync("SELECT 3");
    // 连接仍被外层作用域持有

} // 外层作用域结束：连接返回连接池
```

#### 嵌套事务示例

```csharp
using (var db = factory.Create())
await using (var outerTransaction = await db.BeginTransactionAsync()) // 最外层事务：获取连接
{
    await db.ExecuteNonQueryAsync("INSERT INTO Table1 VALUES (1)");
    // 连接被外层事务持有

    await using (var innerTransaction = await db.BeginTransactionAsync()) // 内层事务：复用同一连接，不获取新连接
    {
        await db.ExecuteNonQueryAsync("INSERT INTO Table2 VALUES (2)");
        // 连接仍被外层事务持有

        await innerTransaction.CommitAsync();
    } // 内层事务结束：连接不回收，仍被外层持有

    await db.ExecuteNonQueryAsync("INSERT INTO Table3 VALUES (3)");
    // 连接仍被外层事务持有

    await outerTransaction.CommitAsync();
} // 外层事务结束：连接返回连接池
```

#### 事务与连接作用域混合嵌套示例

```csharp
using (var db = factory.Create())
using (var scope = await db.BeginConnectionScopeAsync()) // 连接作用域：获取连接
{
    await db.ExecuteScalarAsync("SELECT 1");
    // 连接被作用域持有

    await using (var transaction = await db.BeginTransactionAsync()) // 事务：复用同一连接，不获取新连接
    {
        await db.ExecuteNonQueryAsync("INSERT INTO Users VALUES ('Alice')");
        // 连接仍被作用域持有（事务不持有连接，只是标记事务状态）

        await transaction.CommitAsync();
    } // 事务结束：连接不回收，仍被作用域持有

    await db.ExecuteScalarAsync("SELECT 2");
    // 连接仍被作用域持有

} // 作用域结束：连接返回连接池
```

**重要提示**：

- 嵌套时只有最外层获取连接，内层只是复用
- 连接的回收时机由最外层决定
- 避免过深的嵌套，保持代码清晰

### 3.6 连接获取与回收时机汇总

| 场景             | 获取连接时机                     | 回收连接时机     | 说明            |
| -------------- | -------------------------- | ---------- | ------------- |
| **基本 SQL 执行**  | `db.ExecuteXxxAsync()` 执行时 | SQL 执行完成时  | 每次执行都获取和回收    |
| **连接作用域**      | `BeginConnectionScopeAsync()` 时 | 作用域结束时     | 多个操作共享同一连接    |
| **嵌套连接作用域**    | 仅最外层获取                     | 最外层作用域结束时  | 内层复用外层连接      |
| **事务**         | `BeginTransactionAsync()` 时 | 事务对象释放时    | 事务提交/回滚后仍持有连接 |
| **嵌套事务**       | 仅最外层获取                     | 最外层事务结束时   | 内层复用外层连接      |
| **DataReader** | `ExecuteReaderAsync()` 时   | Reader 释放时 | 读取期间持续占用连接    |

**关键要点**：

- 连接在**执行 SQL 时**获取，在**SQL 执行完成时**回收（基本场景）
- 使用 `using` 语句确保资源及时释放
- 避免长时间占用连接（如长时间计算后再执行 SQL）
- 在事务或连接作用域中，连接的生命周期被延长到事务/作用域结束
- 嵌套时只有最外层获取和回收连接

## 4. 连接池调优

### 4.1 连接池大小调优

#### 4.1.1 基本原则

| 指标                        | 计算公式          | 说明            |
| ------------------------- | ------------- | ------------- |
| **MinPoolSize**           | 平均并发连接数       | 应用启动时预创建的连接数  |
| **MaxPoolSize**           | 峰值并发连接数 × 1.5 | 允许的最大连接数，留有余量 |
| **ConnectionWaitTimeout** | 3-5秒          | 获取连接的最大等待时间   |

#### 4.1.2 不同场景的推荐配置

**场景一：低并发 Web 应用**

```csharp
options.PoolOptions = new DbConnectionPoolOptions
{
    Enabled = true,
    MinPoolSize = 5,      // 平均并发较低
    MaxPoolSize = 20,     // 峰值并发约 10-15
    ConnectionWaitTimeout = TimeSpan.FromSeconds(3)
};
```

**场景二：高并发 API 服务**

```csharp
options.PoolOptions = new DbConnectionPoolOptions
{
    Enabled = true,
    MinPoolSize = 20,     // 平均并发较高
    MaxPoolSize = 100,    // 峰值并发约 60-80
    ConnectionWaitTimeout = TimeSpan.FromSeconds(5)
};
```

**场景三：后台任务处理**

```csharp
options.PoolOptions = new DbConnectionPoolOptions
{
    Enabled = true,
    MinPoolSize = 2,      // 后台任务并发低
    MaxPoolSize = 10,     // 偶尔有批量任务
    ConnectionWaitTimeout = TimeSpan.FromSeconds(10) // 允许等待
};
```

#### 4.1.3 调优检查清单

- [ ] 监控连接池使用率（建议保持在 70%-80%）
- [ ] 检查连接等待队列长度
- [ ] 观察连接创建频率（频繁创建说明 MinPoolSize 太小）
- [ ] 确保 MaxPoolSize 不超过数据库最大连接数限制

### 4.2 连接超时与空闲时间调优

#### 4.2.1 超时参数配置

```csharp
options.PoolOptions = new DbConnectionPoolOptions
{
    // 获取连接等待超时
    ConnectionWaitTimeout = TimeSpan.FromSeconds(3),
    
    // 连接最大空闲时间（超过此时间将被清理）
    MaxIdleTime = TimeSpan.FromMinutes(15),
    
    // 清理间隔（定期检查并清理空闲连接）
    CleanupInterval = TimeSpan.FromMinutes(5)
};
```

#### 4.2.2 调优建议

| 参数                      | 默认值  | 调优建议                |
| ----------------------- | ---- | ------------------- |
| `ConnectionWaitTimeout` | 3秒   | 高并发场景可适当增加，低延迟要求可减小 |
| `MaxIdleTime`           | 15分钟 | 连接稳定可减少，连接不稳定可增加    |
| `CleanupInterval`       | 5分钟  | 内存敏感可减小，CPU敏感可增加    |

### 4.3 连接池的监控与诊断概述

框架提供连接监控和诊断功能，帮助识别连接池问题和优化配置。

#### 4.3.1 启用统计信息

```csharp
// 启用统计
options.EnableStatistics = true;

// 获取统计信息（连接池模式下返回 PoolingDbConnectionStatistics）
var stats = factory.ConnectionManager.GetStatistics();

// 基础统计
Console.WriteLine($"活动连接数: {stats.ActiveConnections}");
Console.WriteLine($"存活连接数: {stats.AliveConnections}");

// 连接池统计
if (stats is PoolingDbConnectionStatistics pooling)
{
    Console.WriteLine($"连接池使用率: {pooling.PoolUsageRate:F2}%");
    Console.WriteLine($"连接池耗尽次数: {pooling.TotalExhausted}");
    Console.WriteLine($"连接命中率: {pooling.HitRate:F2}%");
}
```

#### 4.3.2 基于监控数据的调优建议

| 监控指标 | 统计数据（`PoolingDbConnectionStatistics`） | 类型 | 健康状态 | 异常状态 | 调优建议 |
|----------|----------|------|----------|----------|----------|
| **空闲连接数** | `IdleConnections` | 实时值 | 接近 MinPoolSize | 0 或 >MaxPoolSize | 空闲为0可能连接不足，过多浪费资源 |
| **活动连接数** | `ActiveConnections` | 实时值 | <MaxPoolSize*0.8 | 接近 MaxPoolSize | 接近上限时需关注连接释放情况 |
| **连接池使用率** | `PoolUsageRate` | 实时值 | 50%-80% | >90% 或 <20% | 调整 MinPoolSize/MaxPoolSize |
| **连接命中率** | `HitRate` | 实时值 | >80% | <50% | 命中率低说明连接复用率低，检查连接是否正确释放 |
| **连接池耗尽次数** | `TotalExhausted` | 累计值 | 0（或增长缓慢） | 持续增长 | 增加 MaxPoolSize 或优化 SQL |
| **创建失败率** | `CreateFailureRate` | 实时值 | 0% | >1% | 检查数据库连接配置或网络状况 |
| **排队率** | `QueueRate` | 实时值 | <20% | >50% | 增加 MaxPoolSize 或减少连接持有时间 |
| **排队请求数** | `TotalQueued` | 累计值 | 0（或增长缓慢） | 持续增长 | 频繁等待说明连接池可能不足 |

连接诊断（如连接泄漏识别）通过 OpenTelemetry 指标暴露（连接池账本与泄漏分级 gauge），由消费侧（如 Grafana）基于指标告警。

## 5. 完整示例

以下示例展示了如何配置连接池并监控其性能：

```csharp
using Xbc.DbAccess;
using Xbc.DbAccess.Sqlite;

// 创建工厂并配置连接池
var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
{
    options.ConnectionString = "Data Source=myapp02.db";
    options.PoolOptions = new DbConnectionPoolOptions
    {
        Enabled = true,
        MinPoolSize = 5,
        MaxPoolSize = 20,
        ConnectionWaitTimeout = TimeSpan.FromSeconds(3)
    };
    options.EnableStatistics = true;  // 启用统计
});

// 使用连接池执行数据库操作
using (var db = factory.Create())
{
    var result = await db.ExecuteScalarAsync("SELECT 1");
    Console.WriteLine($"连接成功: {result}");
}

// 获取连接池统计信息（连接池模式下返回 PoolingDbConnectionStatistics）
var stats = factory.ConnectionManager.GetStatistics();
Console.WriteLine($"存活连接数: {stats.AliveConnections}");
Console.WriteLine($"活动连接数: {stats.ActiveConnections}");

if (stats is PoolingDbConnectionStatistics pooling)
{
    Console.WriteLine($"连接池使用率: {pooling.PoolUsageRate:F2}%");
    Console.WriteLine($"连接命中率: {pooling.HitRate:F2}%");
}
```

**完整代码**：可运行的完整示例代码请参见 [examples/02_ConnectionPool/Program.cs](./examples/02_ConnectionPool/Program.cs)

**运行示例**：
```bash
cd examples/02_ConnectionPool
dotnet run
```

## 下一步

- [创建 IDbAccess 实例](03-CreateIDbAccess.md) - 如何创建 IDbAccess 实例
