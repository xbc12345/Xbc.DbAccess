# 可观测性诊断

本文介绍框架的可观测性能力：日志路由、慢查询告警、指标（Metrics）、链路追踪（ActivitySource）与连接泄漏追踪，以及连接统计数据的查看方式。

## 目录

1. [可观测性总览](#1-可观测性总览)
2. [配置诊断](#2-配置诊断)
3. [日志输出](#3-日志输出)
4. [慢查询告警](#4-慢查询告警)
5. [指标（Metrics）](#5-指标metrics)
6. [链路追踪（ActivitySource）](#6-链路追踪activitysource)
7. [连接泄漏追踪](#7-连接泄漏追踪)
8. [连接统计](#8-连接统计)
9. [完整示例](#9-完整示例)

## 1. 可观测性总览

框架内置四条可观测性通道，均默认关闭或静默（无接入时零开销），按需接入：

| 通道 | 载体 | 典型用途 | 接入方式 |
|------|------|----------|----------|
| 日志 | `ILoggerFactory`（类别 `Xbc.DbAccess`） | 内部 Warn/Error、慢查询告警 | `AddDbAccess` 自动接入或 `ConfigureLogging` |
| 指标 | `Meter`（名称 `Xbc.DbAccess`） | SQL 耗时/次数、连接池水位、泄漏仪表 | OpenTelemetry 指标订阅 |
| 链路 | `ActivitySource`（名称 `Xbc.DbAccess`） | 慢 SQL 定位、分布式追踪串联 | OpenTelemetry Tracing 订阅 |
| 连接追踪 | 每工厂的连接追踪器 | 泄漏连接定位（含获取堆栈） | `ConnectionTrackingOptions` |

指标与链路遵循 OpenTelemetry 语义约定（`db.system` / `db.operation` / `db.statement` / `error.type` 等），可直接接入 OTel 生态。

## 2. 配置诊断

### 2.1 DI 场景（推荐）

经 `AddDbAccessDiagnostics` 注册诊断选项，在首次解析数据库工厂时自动应用（幂等）：

```csharp
// Program.cs
builder.Services.AddDbAccessDiagnostics(options =>
{
    // 慢查询告警阈值；null（默认）禁用
    options.SlowQueryThreshold = TimeSpan.FromSeconds(1);
});

builder.Services.AddSqliteDbAccess(connectionString);   // 诊断在首次解析工厂时生效
```

同时，`AddDbAccess` 的解析钩子会把容器中的 `ILoggerFactory` 自动接入框架日志——无需额外配置即有 Warn/Error 输出。

### 2.2 非 DI 场景

不使用 DI 时，通过静态 `DbAccessDiagnostics` 配置：

```csharp
using Xbc.DbAccess.Diagnostics;

// 慢查询告警阈值
DbAccessDiagnostics.SlowQueryThreshold = TimeSpan.FromSeconds(1);

// 配置日志工厂（传 null 停止输出）
DbAccessDiagnostics.ConfigureLogging(loggerFactory);
```

## 3. 日志输出

框架内部 Warn / Error（如泄漏连接告警、事务回滚异常）与慢查询告警统一路由到 `ILoggerFactory`，日志类别为 `"Xbc.DbAccess"`。可按类别做日志过滤或独立目的地：

```csharp
builder.Logging.AddFilter("Xbc.DbAccess", LogLevel.Warning);
```

**重要**：显式调用 `ConfigureLogging` 的配置优先于容器自动接入（自动接入只在"从未配置"时填补空窗，不会覆盖显式配置）。

## 4. 慢查询告警

`SlowQueryThreshold` 设置后，SQL 执行耗时达到阈值时输出 Warn 日志，内容包含操作名、SQL 文本（参数化占位符形式，不含参数值）与耗时：

```csharp
DbAccessDiagnostics.SlowQueryThreshold = TimeSpan.FromSeconds(1);
// 达到阈值时的输出示例：
// warn: Xbc.DbAccess[0]
//       SQL 执行耗时 1203.45ms，达到慢查询阈值 1000ms：ExecuteNonQueryAsync SELECT * FROM ...
```

**计时口径**：与 `xbc.dbaccess.sql.duration` 指标一致——只计 SQL 命令执行（`DbCommand` ExecuteXxx），**不含连接获取时间**；连接获取慢请看第 5 节的连接获取指标。

## 5. 指标（Metrics）

框架经 `DbAccessMetrics`（`Meter` 名称 `Xbc.DbAccess`）发布指标，各指标常量名可编程引用：

| 指标名称 | 类型 | 说明 | Tags |
|----------|------|------|------|
| `xbc.dbaccess.sql.duration` | 直方图（ms） | SQL 执行耗时 | provider / operation / status |
| `xbc.dbaccess.sql.count` | 计数器 | SQL 执行次数 | provider / operation / status |
| `xbc.dbaccess.connection.acquire` | 直方图（ms） | 连接获取耗时 | provider / status（ok / error） |
| `xbc.dbaccess.pool.active_connections` | 仪表 | 当前活动连接数 | provider |
| `xbc.dbaccess.pool.idle_connections` | 仪表 | 当前空闲连接数（仅连接池模式上报） | provider |
| `xbc.dbaccess.pool.max_size` | 仪表 | 连接池最大容量（仅连接池模式上报） | provider |
| `xbc.dbaccess.connection.possible_leak` | 仪表 | 可能泄漏连接数（持有超 5 分钟） | provider |
| `xbc.dbaccess.connection.critical_leak` | 仪表 | 严重泄漏连接数（持有超 10 分钟） | provider |

**OpenTelemetry 接入示例**：

```csharp
// Install-Package OpenTelemetry.Extensions.Hosting
using OpenTelemetry.Metrics;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(DbAccessMetrics.MeterName)     // "Xbc.DbAccess"
        .AddOtlpExporter());
```

**泄漏判定只发布指标数据，不主动告警**——告警条件（何时报警、通知谁）由消费侧（如 OTel Collector 告警规则）定义。

## 6. 链路追踪（ActivitySource）

框架经 `DbAccessActivitySource`（`ActivitySource` 名称 `Xbc.DbAccess`）发布 SQL 执行 span：

- **span 名称**：`Xbc.DbAccess.Execute`（`ActivityKind.Client`）
- **OTel 语义约定 tags**：`db.system`（提供程序名，如 SqlClient）、`db.operation`（操作名，如 ExecuteNonQuery）、`db.statement`（SQL 文本）
- **SQL 文本只进 span 不进指标**（指标限高基数），且不含参数值（框架全参数化执行）
- 失败时记录 OTel 约定的 `exception` 事件（type / message / stacktrace）与 `error.type` tag
- **零开销**：未接入 `ActivityListener` 时 span 创建直接返回 null，不产生任何分配

**OpenTelemetry 接入示例**：

```csharp
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(DbAccessActivitySource.Name)  // "Xbc.DbAccess"
        .AddOtlpExporter());
```

## 7. 连接泄漏追踪

连接泄漏的定位依赖两处配置（均为默认关闭）：

```csharp
var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
{
    options.ConnectionString = connectionString;

    // 启用连接追踪与统计（泄漏查询的前提）
    options.EnableStatistics = true;
    options.ConnectionTrackingOptions = new DbConnectionTrackingOptions
    {
        Enabled = true,               // 启用连接追踪
        RecordStackTrace = true       // 记录获取连接时的堆栈（定位泄漏源头的关键）
    };

    // 泄漏阈值（沿用默认值时无需设置）
    options.PossibleLeakHoldTimeThreshold = TimeSpan.FromMinutes(5);    // 可能泄漏
    options.CriticalLeakHoldTimeThreshold = TimeSpan.FromMinutes(10);   // 严重泄漏
});
```

**查询当前泄漏快照**（跨所有工厂，无状态查询）：

```csharp
IReadOnlyList<LeakedConnectionInfo> leaks = DbAccessDiagnostics.GetLeakedConnections();

foreach (var leak in leaks)
{
    // IsCritical：true=达到严重阈值（默认 10 分钟），false=仅达到可能阈值（默认 5 分钟）
    Console.WriteLine($"[{(leak.IsCritical ? "严重" : "可能")}] {leak.ProviderName} " +
        $"连接 {leak.TrackingInfo.ConnectionId} 已持有 " +
        $"{(DateTime.Now - leak.TrackingInfo.AcquiredAt).TotalMinutes:F1} 分钟，" +
        $"获取堆栈：{leak.TrackingInfo.AcquireStackTrace}");
}
```

`LeakedConnectionInfo.TrackingInfo`（`DbConnectionTrackingInfo`）包含连接 ID、所属管理器 ID、创建/借出/归还/释放时间、借出耗时、复用次数、当前状态、最后 SQL 与三段堆栈（借出/回收/释放），足以定位泄漏源头。

**生产建议**：以 `HostedService` 定时巡检，`IsCritical` 的泄漏接入告警；仅排查问题时临时开启 `RecordStackTrace`（有堆栈采集开销）：

```csharp
public sealed class LeakMonitorService(IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var critical = DbAccessDiagnostics.GetLeakedConnections()
                .Where(l => l.IsCritical)
                .ToList();

            if (critical.Count > 0)
                logger.LogWarning("发现 {Count} 个严重泄漏连接，最早已持有 {Minutes} 分钟",
                    critical.Count,
                    critical.Max(l => (DateTime.Now - l.TrackingInfo.AcquiredAt).TotalMinutes));

            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }
}
```

> **注意**：`GetLeakedConnections` 只统计已启用连接追踪与统计（`ConnectionTrackingOptions.Enabled` + `EnableStatistics`）的工厂；未启用的工厂不参与泄漏判定。

## 8. 连接统计

启用 `EnableStatistics` 后，可从工厂的连接管理器实时读取连接统计：

```csharp
// 非连接池管理器（DefaultDbConnectionManager）与连接池管理器（PoolingDbConnectionManager）均支持
var stats = factory.ConnectionManager.GetStatistics();

Console.WriteLine($"总请求：{stats.TotalRequests}");           // 连接请求总数
Console.WriteLine($"总创建：{stats.TotalCreated}");            // 物理连接创建数（高频增长说明池化失效或池过小）
Console.WriteLine($"创建失败：{stats.TotalCreateFailed}");     // 创建失败数
Console.WriteLine($"总归还：{stats.TotalReleased}");           // 连接归还数
Console.WriteLine($"活动连接：{stats.AliveConnections}");      // 当前存活连接数
Console.WriteLine($"总取消：{stats.TotalCancelled}");          // 被取消的获取请求
```

## 9. 完整示例

```csharp
using Xbc.DbAccess;
using Xbc.DbAccess.Diagnostics;
using Xbc.DbAccess.Sqlite;

// ---- 启动期配置 ----

// 诊断选项（DI 场景经 AddDbAccessDiagnostics；此处为手动形态）
DbAccessDiagnostics.SlowQueryThreshold = TimeSpan.FromSeconds(1);
DbAccessDiagnostics.ConfigureLogging(loggerFactory);

// 泄漏追踪需要显式开启
var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
{
    options.ConnectionString = "Data Source=app.db";
    options.EnableStatistics = true;
    options.ConnectionTrackingOptions = new DbConnectionTrackingOptions
    {
        Enabled = true,
        RecordStackTrace = true
    };
});

// ---- 运行期观测 ----

// 连接统计
var stats = factory.ConnectionManager.GetStatistics();
Console.WriteLine($"总请求 {stats.TotalRequests}，活动连接 {stats.AliveConnections}");

// 泄漏快照巡检
var leaks = DbAccessDiagnostics.GetLeakedConnections();
foreach (var leak in leaks.Where(l => l.IsCritical))
{
    Console.WriteLine($"严重泄漏：{leak.TrackingInfo.ConnectionId}，堆栈：{leak.TrackingInfo.AcquireStackTrace}");
}

// ---- OpenTelemetry 接入（WithMetrics/WithTracing 各一段）----
// .AddMeter(DbAccessMetrics.MeterName)  + .AddSource(DbAccessActivitySource.Name)
```

**完整代码**：可运行的完整示例请参见 [examples/12_Diagnostics/Program.cs](./examples/12_Diagnostics/Program.cs)

**运行示例**：

```bash
cd guides/examples/12_Diagnostics
dotnet run
```

## 相关文档

- [连接配置](01-ConnectionConfiguration.md) - 泄漏阈值、连接追踪等诊断相关连接选项。
- [连接池](02-ConnectionPool.md) - 连接池统计与池化模式下的指标口径。
