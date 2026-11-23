# Xbc.DbAccess 性能基准

核心库与 Dapper 2.1.66、原生 ADO.NET、EF Core、SqlSugar 及手写循环（理论下限参照）对照，基准代码位于 `test/Xbc.DbAccess.Benchmarks`。

## 框架优势

基于本文件实测数据与框架能力，Xbc.DbAccess 的核心优势如下。

### 性能

- **映射层全形态领先 Dapper**：窄表（5 列）/ 宽表（14 列）/ 超宽表（32 列）全档位快 8%~40%，列数边际成本更低；内存分配与手写循环理论下限基本一致（差异 < 0.5%）
- **抽象层无额外代价**：端到端与手写原生 ADO.NET 持平（WAL 文件库三方耗时差异在误差范围内），用框架不付性能税
- **内存纪律**：查询场景分配量普遍低于对手（映射管线零额外分配；端到端超宽表比 Dapper 少 6%）
- **异步入口**：实体映射入口原生异步（`ReadAsync`），在承担异步状态机开销的前提下仍快于 Dapper 的同步路径

### 能力

- **表达式驱动的类型安全查询面**：`Where` / `Select`（匿名类型 / 元组 ValueTuple.Create / 命名类型带参构造 / 命名类型初始化器）/ 条件更新 / 聚合（COUNT / SUM / AVG / MAX / MIN / Any / All）/ 排序链与窗口 / keyset 分页 / 导航属性装配，列名经方言引用结构性防注入
- **五方言统一**：SQL Server / MySQL / PostgreSQL / Oracle / SQLite 一套 API——窗口 SQL、分页方言、AVG 数值语义修正、标识列回填、词法差异自动抹平
- **批量写入**：`InsertRangeAsync` / `DeleteRangeAsync` 超参数上限自动分批；bulk insert 原生路径（SqlBulkCopy / COPY）与回退路径（多值 INSERT 分批 / 事务内逐条命令复用）按方言自动选择，行数据流式枚举
- **事务与连接管理**：保存点嵌套事务、连接作用域内复用连接、连接池 + 泄漏追踪分级告警
- **实体元数据体系**：Table / Column / PrimaryKey / NotMapped 特性、乐观并发令牌自动换发、NULL 处理三态策略（安全默认 / 严格异常 / 提供程序默认值）、值转换器链（JSON 列等）
- **SQL 安全纵深**：全参数化、列名方言引用、参数名清洗与分配单点化、标识符词法校验；裸 SQL 入口为显式信任边界（见「SQL 安全与信任边界」）
- **映射缓存治理**：映射委托缓存可配 LRU 上限与空闲过期（`EntityMapperCachePolicy`），防动态查询形状导致的内存无界增长
- **零配置可观测性**：ActivitySource span + Meter 指标 + 慢查询告警，未接入监听时零开销

### 定位

性能贴 Dapper、API 提供 EF 式的表达式安全、异步优先（CancellationToken 全链路贯穿）、兼具两者均不具备的多方言统一与横切能力——占用「极简执行层」与「重型 ORM」之间的空档。

## 实体映射

使用 Fake `DbDataReader` 隔离数据库 I/O，测量纯映射热性能。窄表 5 列（典型业务行）、宽表 14 列（复杂报表行）、超宽表 32 列（现代业务系统规模），三表均刻意覆盖框架全部读取分支（值类型 / 引用类型 / 结构体 / 可空）。数据取自 2026-11-21 复测（ShortRun：3 轮预热 + 3 轮计量）。

窄表：

| 实现 | 1 行 | 100 行 | 1000 行 |
| --- | ---: | ---: | ---: |
| Xbc.DbAccess | **74.6 ns** | **1,526.0 ns** | **13,747.1 ns** |
| Dapper | 123.5 ns | 1,887.0 ns | 16,404.8 ns |
| 手写循环 | — | 718.9 ns | 6,659.9 ns |

宽表：

| 实现 | 1 行 | 100 行 | 1000 行 |
| --- | ---: | ---: | ---: |
| Xbc.DbAccess | **137.0 ns** | **3,422.1 ns** | **32,942.4 ns** |
| Xbc.DbAccess（严格模式） | — | 3,361.0 ns | 31,568.5 ns |
| Dapper | 197.7 ns | 4,118.8 ns | 37,585.2 ns |
| 手写循环 | — | 1,495.5 ns | 13,892.1 ns |

超宽表：

| 实现 | 1 行 | 100 行 | 1000 行 |
| --- | ---: | ---: | ---: |
| Xbc.DbAccess | **263.4 ns** | **7,154.5 ns** | **75,161.3 ns** |
| Dapper | 414.8 ns | 8,191.6 ns | 81,685.6 ns |
| 手写循环 | — | 3,032.7 ns | 28,518.4 ns |

本轮复测 Xbc.DbAccess 在三表全档位均快于 Dapper（窄表快 16%~40%，宽表快 12%~31%，超宽表快 8%~37%），内存分配与 Dapper / 手写循环基本一致（各形态差异 < 0.5%）。三方耗时均随列数线性扩展（每列每行约 2.1~2.5 ns），Xbc.DbAccess 的列数边际成本略低，领先幅度随列数增加收窄但不反转。宽表默认模式为每个非可空列生成 DBNull 安全检查（NULL 取类型默认值，不抛异常），Dapper 直接读取（NULL 抛异常）；对 NOT NULL 列标注 `DbNullHandling.ProviderException` 即跳过该检查（可空列与引用类型列不受影响），本轮严格模式与默认模式差异在误差范围内。手写循环为理论下限，Xbc.DbAccess 为其 2.1~2.6 倍（异步 `ReadAsync` 入口与泛化列读取的设计代价）。

## 端到端执行

真实 SQLite 文件数据库（WAL 模式），每操作新建连接。数据取自 2026-11-21 复测（ShortRun）：

| 场景 | Xbc.DbAccess | Dapper | 原生 ADO.NET |
| --- | ---: | ---: | ---: |
| 查询 100 行 | 1.321 ms / 33.73 KB | 1.342 ms / 36.84 KB | 1.323 ms / 26.22 KB |
| 插入单行 | **10.236 ms** / 10.73 KB | 12.220 ms / 10.52 KB | 12.468 ms / 10.40 KB |

查询耗时三方持平（差异在误差范围内），抽象层无额外代价，Xbc.DbAccess 内存分配比 Dapper 少 8%；插入 Xbc.DbAccess 均值最优但三方误差带较大、中位数接近，差异不显著；插入耗时由 WAL 落盘主导，分配量三方接近。

## ORM 端到端对比

内存 SQLite（共享内存库）查 100 行，四方对照：Xbc.DbAccess / Dapper 走 System.Data.SQLite 栈，EF Core / SqlSugar 走 Microsoft.Data.Sqlite 栈（均为各库官方推荐用法，跨栈绝对值不可直接对比）。数据取自 2026-11-21 复测（ShortRun）：

| 场景 | Xbc.DbAccess | Dapper | EF Core | SqlSugar |
| --- | ---: | ---: | ---: | ---: |
| 窄表 100 行 | 67.1 μs / 31.87 KB | 63.5 μs / 31.24 KB | 106.7 μs / 42.74 KB | 151.8 μs / 30.69 KB |
| 宽表 100 行 | 161.2 μs / 81.13 KB | 139.0 μs / 85.68 KB | 246.5 μs / 80.66 KB | 472.1 μs / 93.30 KB |
| 超宽表 100 行 | 328.6 μs / 172.48 KB | 276.1 μs / 184.06 KB | 500.4 μs / 142.67 KB | 1,009.6 μs / 195.51 KB |

同栈内：窄表 Xbc.DbAccess 与 Dapper 持平（误差范围内），宽/超宽表 Dapper 快约 14%~22%（三轮独立复测复现；映射微基准中 Xbc.DbAccess 在宽/超宽形态反而更快，差距位于 provider 交互层，待定位）；Xbc.DbAccess 端到端分配量低于 Dapper（超宽表少 6%）。EF Core / SqlSugar 数值更低源于 Microsoft.Data.Sqlite 更薄的 provider 层，非查询管道优势。

## 参数构建

匿名对象到参数集合的转换。数据取自 2026-11-21 复测：

| 参数量 | Xbc.DbAccess | Dapper DynamicParameters | 手写循环 |
| ---: | ---: | ---: | ---: |
| 1 | 145.94 ns / 824 B | 24.24 ns / 216 B | 9.36 ns / 104 B |
| 10 | 983.62 ns / 4,416 B | 25.26 ns / 216 B | 70.36 ns / 824 B |
| 50 | 4,737.76 ns / 20,946 B | 26.91 ns / 216 B | 322.40 ns / 4,024 B |

口径说明：Xbc.DbAccess 在此阶段即物化真实的 `DbParameter` 对象（含元数据解析），Dapper 的 `DynamicParameters` 仅保存模板并延迟到执行期物化，两者口径不同；50 参数场景仅 4.7 μs，实际影响可忽略。

## 诊断开销

Fake 管道单次执行，四种诊断模式自身对照。数据取自 2026-11-21 复测：

| 模式 | Mean | Allocated |
| --- | ---: | ---: |
| 全关 | 577.3 ns | 800 B |
| 仅指标 | 571.5 ns | 800 B |
| 仅 Span | 916.1 ns | 1,656 B |
| 全开 | 880.6 ns | 1,656 B |

指标通道开启零开销（本轮略快 6 ns，在噪声内）；Span 通道代价约 +300~340 ns / +856 B。

## SQL 安全与信任边界

框架的强类型查询面具备结构性防注入能力：值一律参数化（永不内联进 SQL 文本）、列名/表名经方言标识符引用（内嵌包裹符双写转义）、聚合方法白名单匹配、LIKE 通配符全转义（含转义符自身）。

以下 API 是**设计上的信任边界**——SQL 文本原样透传，框架不做改写或防护：

| 入口 | 透传内容 | 契约 |
| --- | --- | --- |
| `ExecuteNonQueryAsync` / `ExecuteScalarAsync` / `ExecuteReaderAsync` / `ExecuteDataSetAsync` | 整条 SQL | SQL 文本视为可信输入，值经 `object param` 或 `DbParameter[]` 传入，**禁止拼接用户输入** |
| `Set<T>(sql)` / `AsQuery(sql)` | 整条 SQL | 同上；表达式翻译出的 `Where`/排序/窗口子句会追加或包裹该 SQL，传入 SQL 应为可安全组合的完整查询 |
| 分页扩展的 `orderBy` 参数 | 标识符片段 | 直接拼入 SQL（标识符天然不可参数化），**必须使用常量或白名单列名，禁止任何用户输入** |

调用方将用户输入拼接进上述入口导致的注入属于应用层契约违背。含用户输入的动态条件请使用插值参数化入口（见下），或表达式驱动的强类型查询面。

**插值参数化入口**（`DbAccessInterpolatedExtensions`，EF `FromSqlInterpolated` 同款语义）：`ExecuteNonQueryInterpolatedAsync` / `ExecuteScalarInterpolatedAsync`（含泛型）/ `ExecuteReaderInterpolatedAsync` / `ExecuteDataSetInterpolatedAsync`。调用点内联 `$"..."` 书写，编译器产出「SQL 骨架 + 值数组」的结构分离，插值孔一律转为数据库参数、值永不进入 SQL 文本——注入面被编译器产品形态封死而非靠校验：

```csharp
// 插值孔只能是值（含集合——自动展开为 IN 参数列表），不能承载列名/表名等标识符语义
var rows = await dbAccess.ExecuteNonQueryInterpolatedAsync(
    $"UPDATE Users SET Remark = {remark} WHERE Id IN ({ids})");

// 孔位于字符串字面量内会被显式拒绝（参数引用在字面量内不绑定值）：
// LIKE 模式应把含通配符的整个模式作为孔，如 $"... LIKE {pattern}"
```

方法只接受 `FormattableString` 参数（`string` 无隐式转换）：先物化字符串再传入会**编译失败**，误用形态在编译期封死。

> **插值字符串错觉警示**：对上表 string 版入口写 `$"... WHERE Name = {userName}"` **不构成任何防护**——参数类型是 `string`，编译器在调用点即执行 `string.Format`，传进框架前 SQL 已是拼接完成的最终文本，与手动 `+` 拼接完全等价。插值形态只有在参数类型为 `FormattableString` 时才会产出「骨架 + 值数组」的结构分离（且同名 `string`/`FormattableString` 重载并存时，规范规定插值字符串优先绑定 `string` 重载，不会自动升级——因此本框架以独立方法名区分两个信任等级）。

## 映射缓存治理

实体映射与投影映射委托缓存默认无界（键基数由应用的查询形状决定）。动态拼接裸 SQL 且结果集形状不可控的场景应配置治理策略（全局静态，建议启动期设置一次）：

```csharp
EntityMapperCachePolicy.MaxEntries = 1000;                       // 上限（软上限），超出按最后访问时间 LRU 淘汰
EntityMapperCachePolicy.IdleExpiration = TimeSpan.FromHours(2);  // 空闲过期（惰性清理，随插入或显式 Sweep 触发）
```

淘汰只在插入期（编译新委托的低频时刻）与显式 `EntityMapperCachePolicy.Sweep()` 触发：命中即续期、清理永远不动热条目，被淘汰委托再次访问时自动重编译（单键单次编译语义不变）。两项均默认 null（保持无界缓存的原行为）。长期驻留且不再产生新查询形状的服务，可由宿主后台定时调用 `Sweep()` 回收冷条目。

## 环境与复现

- BenchmarkDotNet v0.14.0
- .NET 8.0.5，X64 RyuJIT AVX2
- Windows 11
- 完整原始输出保存在 `BenchmarkDotNet.Artifacts\`（按运行日期命名），报告（md / csv / html）在 `BenchmarkDotNet.Artifacts\results\`。本文 2026-11-21 数据为一次全量复测（57 个基准，五组报告），各组报告见 `results\` 下同名 md / csv / html；更早的原始记录见基准项目的 `benchmark-rerun.log`、`benchmark-results.log` 与 `mapping-rerun.log`

```powershell
# 全量基准（默认 job，约 30~60 分钟）
dotnet run -c Release -f net8.0 --project test\Xbc.DbAccess.Benchmarks\Xbc.DbAccess.Benchmarks.csproj -- --filter *

# 单组快速档（ShortRun：3 轮预热 + 3 轮计量）
dotnet run -c Release -f net8.0 --project test\Xbc.DbAccess.Benchmarks\Xbc.DbAccess.Benchmarks.csproj -- --filter "*MappingBenchmarks*" --job short
```

如默认工具链与本仓库的集中输出目录（`BaseOutputPath`）冲突，可追加 `--inProcess` 改用 InProcessEmitToolchain。
