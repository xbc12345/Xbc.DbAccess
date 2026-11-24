<div align="center">

<img src="xbc.png" width="160" alt="Xbc.DbAccess" />

# Xbc.DbAccess

**.NET 轻量级数据访问库：Dapper 级性能，EF 式表达式安全查询，五方言统一。**

<!-- TODO: 徽章链接中的 YOUR_GITHUB_USERNAME 为占位符，仓库地址确定后请全局替换 -->
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/YOUR_GITHUB_USERNAME/Xbc.DbAccess/blob/main/LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue)](https://github.com/YOUR_GITHUB_USERNAME/Xbc.DbAccess)
<!-- TODO: 当前为静态占位 NuGet 徽章；包发布到 NuGet 后请替换为官方徽章，如 https://img.shields.io/nuget/v/Xbc.DbAccess.svg -->
[![NuGet](https://img.shields.io/badge/NuGet-1.0.0-blue)](https://www.nuget.org/packages/Xbc.DbAccess)

</div>

---

## 为什么做这个库

现存的 .NET 数据访问方案呈现两极：**Dapper 极简但只管执行**（SQL 与映射，其余全靠手写），**EF Core 全能但重量级**（ChangeTracker、LINQ 翻译管线、模型构建）。Xbc.DbAccess 占据两者之间的空档：

- **性能贴 Dapper**：映射层全形态（窄表 5 列 / 宽表 14 列 / 超宽表 32 列）快 3%~24%，内存分配与手写循环理论下限基本一致（差异 < 0.5%），抽象层不付性能税
- **EF 式表达式安全**：`Where` / `Select` / 排序 / 分页用 lambda 表达式书写，值参数化、列名经方言标识符引用，注入面被结构性封死
- **异步优先**：纯异步 API，`CancellationToken` 全链路贯穿
- **五方言统一**：一套 API 覆盖 SQL Server / MySQL / PostgreSQL / Oracle / SQLite，方言差异（分页语法、标识列回填、词法引用）自动抹平

## 特性

- **表达式驱动的类型安全查询面** —— `Where` / `Select` 投影 / 条件更新 / 聚合（COUNT/SUM/AVG/MAX/MIN）/ 排序链与窗口 / keyset 分页 / `Include` 导航属性装配，列名经方言引用结构性防注入
- **五方言统一** —— SQL Server / MySQL / PostgreSQL / Oracle / SQLite 一套 API，窗口 SQL、分页方言、AVG 数值语义、标识列回填、词法差异自动抹平
- **批量写入** —— `InsertRangeAsync` / `DeleteRangeAsync` 超参数上限自动分批；SqlBulkCopy / COPY 原生路径与回退路径按方言自动选择，行数据流式枚举
- **事务与连接管理** —— 保存点嵌套事务、连接作用域内复用连接、连接池 + 泄漏追踪分级告警
- **实体元数据体系** —— `Table` / `Column` / `PrimaryKey` / `NotMapped` 特性、乐观并发令牌自动换发、NULL 三态策略、值转换器链（JSON 列等）
- **[SQL 安全纵深](#sql-安全与信任边界)** —— 全参数化、参数名清洗与分配单点化、标识符词法校验；插值参数化入口（`FormattableString`）把误用形态在编译期封死
- **映射缓存治理** —— 映射委托缓存可配 LRU 上限与空闲过期（`EntityMapperCachePolicy`），防动态查询形状导致的内存无界增长
- **零配置可观测性** —— ActivitySource span + Meter 指标 + 慢查询告警，未接入监听时零开销

## 安装

核心包必装，方言提供程序包按所用数据库选装其一（版本号 `1.0.0` 为占位，以 NuGet 实际发布版本为准）：

```xml
<ItemGroup>
  <!-- 核心包（支持 netstandard2.0 / net8.0 / net462） -->
  <PackageReference Include="Xbc.DbAccess" Version="1.0.0" />

  <!-- 方言提供程序：按所用数据库选装其一 -->
  <PackageReference Include="Xbc.DbAccess.Sqlite" Version="1.0.0" />
  <!-- 其他可选：Xbc.DbAccess.SqlServer / Xbc.DbAccess.MySql / Xbc.DbAccess.Npgsql / Xbc.DbAccess.Oracle -->
</ItemGroup>
```

## 快速开始

### 1. 创建连接，执行 SQL

```csharp
using Xbc.DbAccess;
using Xbc.DbAccess.Sqlite;

// 创建工厂（五个方言各有对应工厂，生命周期应为单例）
var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
{
    options.ConnectionString = "Data Source=myapp.db";
});

using var db = factory.Create();

// 执行 SQL：值经匿名对象参数化，集合参数自动展开为 IN 子句
int count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users");

var rows = await db.ExecuteNonQueryAsync(
    "INSERT INTO Users (Name, Age) VALUES (@Name, @Age)",
    new { Name = "张三", Age = 25 });
```

### 2. 实体 CRUD

```csharp
using Xbc.DbAccess.Entity;

// 属性名映射同名列，表名默认取类名复数（User → Users），Id 自动识别为主键
public class User
{
    public int Id { get; set; }
    public string UserName { get; set; }
    public string Email { get; set; }
    public int Age { get; set; }
}

var user = new User { UserName = "张三", Email = "zhangsan@example.com", Age = 25 };
await db.Set<User>().InsertAsync(user);
Console.WriteLine($"自增 ID 已回填: {user.Id}");

user = await db.Set<User>().FindAsync(user.Id);   // 按主键查找
user.Age = 26;
await db.Set<User>().UpdateAsync(user);           // 按主键更新（主键/自增列自动排除）
await db.Set<User>().DeleteAsync(user);           // 按主键删除
```

### 3. 表达式查询

```csharp
using Xbc.DbAccess.Entity;

// 单列投影
List<string> names = await db.FromSql<User>("SELECT * FROM Users")
    .Select(u => u.UserName)
    .ToListAsync();

// 谓词查询（列名经方言引用，值参数化）
var adults = await db.Set<User>().Where(u => u.Age >= 18).ToListAsync();

// 排序 + 窗口分页（Skip/Take 要求已设置查询级排序，末级排序键建议用唯一列）
var page = await db.FromSql<User>("SELECT * FROM Users")
    .OrderByDescending(u => u.CreatedAt)
    .Skip(0)      // 第 2 页为 Skip(10)
    .Take(10)
    .ToListAsync();

// 加载导航属性（分裂查询，支持 ThenInclude 多级）
var usersWithOrders = await db.FromSql<User>("SELECT * FROM Users")
    .Include(u => u.Orders)
    .ToListAsync();
```

## 性能基准

核心库与 Dapper 2.1.66、原生 ADO.NET、EF Core、SqlSugar 及手写循环（理论下限参照）对照，基准代码位于 `test/Xbc.DbAccess.Benchmarks`。

**环境**：主表为 Windows 11 本机复测（.NET 8.0.5 RyuJIT AVX2，2026-11-24）；跨平台对照为 Ubuntu 24.04 云端（Intel Xeon Platinum 8582C，.NET 8.0.34，2026-11-23）。两侧均为 ShortRun 作业（3 预热 + 3 迭代）快速冒烟基准，误差区间较大，精确结论以 `BenchmarkDotNet.Artifacts\results\` 完整报告为准。

### 实体映射微基准（Fake `DbDataReader` 隔离 I/O，单位 ns，Windows 本机）

| 数据形状 | Xbc.DbAccess | Dapper | 手写循环（下限） |
| --- | ---: | ---: | ---: |
| 窄表 5 列 × 1 | **103.8** | 123.4 | — |
| 窄表 5 列 × 100 | **1,636.6** | 1,918.0 | 815.8 |
| 窄表 5 列 × 1000 | **14,840.2** | 17,938.3 | 6,902.6 |
| 宽表 14 列 × 1 | **167.7** | 210.0 | — |
| 宽表 14 列 × 100 | **3,700.3** | 4,082.6 | 1,604.9 |
| 宽表 14 列 × 1000 | **34,686.9** | 40,306.1 | 15,487.1 |
| 宽表 14 列（严格模式）× 1000 | **35,335.9** | — | — |
| 超宽表 32 列 × 1 | **293.7** | 379.4 | — |
| 超宽表 32 列 × 100 | **7,527.5** | 7,764.5 | 3,017.0 |
| 超宽表 32 列 × 1000 | **77,163.5** | 84,019.4 | 30,601.0 |

窄表快 15%~17%，宽表快 9%~20%，超宽表快 3%~23%；严格模式零额外成本；内存分配与手写循环理论下限基本一致（差异 < 2%）。跨平台方向一致：Ubuntu 云端同档位 Xbc 领先 Dapper 2%~20%（其超宽表 1000 行档误差区间过大，不作结论）。

### 端到端（真实 SQLite 文件库 / WAL，单位 ms，Windows 本机）

| 操作 | Xbc.DbAccess | Dapper | 原生 ADO.NET |
| --- | ---: | ---: | ---: |
| 查询 100 行 | 1.420（33.73 KB） | 1.357（36.84 KB） | **1.378**（26.22 KB） |
| 插入单行 | 11.878（10.73 KB） | 11.743（10.52 KB） | **11.038**（10.40 KB） |

查询较原生 +3%、分配比 Dapper 少 8%，插入与 Dapper 持平；Linux 云端同口径为查询较原生 +12%、插入反快于 Dapper 19%——真实 DB 往返主导，薄封装层开销不构成瓶颈。

### ORM 同台对比（100 行查询，单位 μs / 内存 KB，Windows 本机）

| 数据形状 | Xbc.DbAccess | Dapper | EF Core | SqlSugar |
| --- | ---: | ---: | ---: | ---: |
| 窄表 5 列 | 66.9 / 31.9 | **65.6** / 31.2 | 111.5 / 42.7 | 164.0 / 30.7 |
| 宽表 14 列 | **168.2** / 81.1 | 178.0 / 85.7 | 293.0 / 80.7 | 505.1 / 93.3 |
| 超宽表 32 列 | 341.9 / 172.5 | **301.3** / 184.1 | 515.8 / 142.7 | 1,041.6 / 195.5 |

窄表与 Dapper 持平（±2%），SqlSugar、EF 差距明显；宽 / 超宽档 Windows 上互有胜负、Linux 上落后 Dapper 15%~28%——宽表端到端小幅落后的现象跨平台存在（而映射基准 Xbc 更快），指向执行管道逐列读取开销，列为后续优化方向。EF 内存分配最低（无逐列委托开销），Xbc 介于中间。

### 参数构建与诊断开销（双平台一致结论）

- **参数构建**（匿名对象 → DbParameter）：Xbc x50 = 5,333 ns / 20.9 KB（Windows；Linux 同档 10,577 ns），约为手写的 10~16 倍耗时、5 倍分配。**逐参数构建是当前已知热点**，高频小写入场景建议复用参数化 SQL 或等待后续版本优化
- **诊断开关**（执行管道含连接管理）：Metrics 与关闭持平（零开销），Activity / Full +43%~60%（分配 800 B → 1,656 B）——诊断默认关闭，按需开启

完整报告（映射 / 端到端 / ORM 对比 / 参数构建 / 诊断开销五组，Windows 与 Linux 两套环境）见 `test/Xbc.DbAccess.Benchmarks/BenchmarkDotNet.Artifacts/results/`。

## SQL 安全与信任边界

- **强类型查询面结构性防注入**：值一律参数化（永不内联进 SQL 文本）、列名/表名经方言标识符引用（内嵌包裹符双写转义）、聚合方法白名单匹配、LIKE 通配符全转义（含转义符自身）
- **裸 SQL 入口是设计上的信任边界**：`ExecuteNonQueryAsync` / `ExecuteScalarAsync` / `ExecuteReaderAsync` / `ExecuteDataSetAsync` 与 `FromSql<T>(sql)` 的 SQL 文本原样透传、视为可信输入，**禁止拼接用户输入**；分页扩展的 `orderBy` 参数直接拼入 SQL，必须使用常量或白名单列名
- **插值参数化入口**（`ExecuteNonQueryInterpolatedAsync` 等，EF `FromSqlInterpolated` 同款语义）：调用点内联 `$"..."` 书写，插值孔一律转为数据库参数、值永不进入 SQL 文本；参数类型为 `FormattableString`，先物化字符串再传入会**编译失败**
- ⚠️ **插值字符串错觉**：对裸 SQL 入口写 `$"... WHERE Name = {userName}"` 不构成任何防护——参数类型是 `string`，编译器在调用点即完成拼接，与手动 `+` 拼接完全等价

## 文档

| 指南 | 内容 |
| --- | --- |
| [01 连接配置选项](guides/01-ConnectionConfiguration.md) | `DbConnectionOptions` 连接配置 |
| [02 连接池](guides/02-ConnectionPool.md) | 连接池配置与行为 |
| [03 创建 IDbAccess 实例](guides/03-CreateIDbAccess.md) | 工厂模式与依赖注入集成（含多数据库） |
| [04 执行 SQL](guides/04-ExecuteSql.md) | 参数化查询、插值参数化、Reader / DataSet / 标量 / 分页 |
| [05 事务](guides/05-Transaction.md) | 事务与保存点嵌套 |
| [06 连接作用域](guides/06-ConnectionScope.md) | 连接作用域内复用连接 |
| [07 存储过程](guides/07-StoredProcedure.md) | 存储过程执行与输出参数 |
| [08 实体类的定义](guides/08-EntityDefinition.md) | Table / Column / 主键 / 导航特性、内置列能力开关 |
| [09 实体映射与缓存治理](guides/09-EntityMapping.md) | 实体映射与缓存治理 |
| [10 IEntityQuery](guides/10-EntityQuery.md) | 实体查询：谓词表达式、投影、聚合、GroupBy、窗口与 keyset 分页、Include |
| [11 IEntitySet](guides/11-EntitySet.md) | 实体增删改查、Upsert、条件写入、乐观并发与批量操作 |
| [12 可观测性诊断](guides/12-Diagnostics.md) | 日志、慢查询告警、指标、链路追踪与连接泄漏追踪 |

可运行示例代码位于 `guides/examples/`（12 个工程随构建验证）。

## License

本项目基于 [MIT License](LICENSE) 开源发布。
