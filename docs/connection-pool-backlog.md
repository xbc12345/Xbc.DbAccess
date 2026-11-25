# 连接池审查 Backlog

> 来源：2026 年对 `PoolingDbConnectionManager` / `PoolingDbConnection` / `DbConnectionWrapper` 的代码审查与账目模型梳理。
> 核心并发模型经交错验证为健全（许可证守恒、CAS 状态机、事务双重防护均成立），以下为发现的缺陷、脆弱点与对齐业界实践的改进项。

## P0 缺陷修复

### 1. Dispose 竞态窗口内追踪记录永久泄漏（F1）

- **位置**：`src/Xbc.DbAccess/Connections/PoolingDbConnectionManager.cs#L311-L312`
- **问题**：清理定时器回调正在处理空闲连接 X 时若管理器 Dispose 并发开始，X 的 `ActualDispose` 触发 `LifecycleEnded`，处理器在 `IsDisposed || IsDisposing` 早退分支直接 return，跳过了后面的 `connectionTracker.EndTracking`——X 的追踪记录永久滞留。
- **方案**：早退分支补 `connectionTracker.EndTracking(e.ResourceId)`（对未追踪 id 是无害 no-op）。
- **验证**：现有池测试全量回归 + 新增 Dispose 与清理回调并发竞态测试。

### 2. 预创建 30 秒超时被静默吞掉（F2）

- **位置**：`src/Xbc.DbAccess/Connections/PoolingDbConnectionManager.cs#L100`
- **问题**：`Task.WaitAll(tasks, TimeSpan.FromSeconds(30))` 返回值被忽略。连接串配置长超时且数据库不可达时，构造函数照常返回，预创建任务持续阻塞至驱动超时，无任何日志。
- **方案**：超时时经 `DiagnosticLog.Warn` 记录未完成的预创建数。
- **验证**：现有测试回归 + 日志断言。

## P1 注释加固（当前正确，但正确性悬在隐式链条上）

### 3. 许可证恒等式注释在冷启动期不成立

- **位置**：`src/Xbc.DbAccess/Connections/PoolingDbConnectionManager.cs#L25`
- **问题**：注释写「总数 MaxPoolSize = 空闲连接数 + 可创建余量」，但构造后（MinPoolSize > 0 时）预创建连接不占名额，恒等式只在预热后（所有预创建连接经历过至少一次借还）成立。作者本人梳理时即在此处卡住。
- **方案**：类注释补充账目模型三句话——①证跟借用者走（一次借用领一张证，与连接来源无关）；②预创建是免费库存，首个借用者为其消耗证；③回收时连接入队并归还证，从此该空闲连接的名额以「已归还」形式躺在证池里；恒等式 `MaxPoolSize = 空闲数 + 可创建余量` 仅在预热后成立，真实存活数恒 ≤ MaxPoolSize（证先扣后取 + 借用者先耗队列再创建）。
- **验证**：无代码变更。

### 4. DisposeInternal 的隐式 ReturnToPool 链条

- **位置**：`src/Xbc.DbAccess/Connections/DbConnectionWrapper.cs#L158`
- **问题**：`base.Dispose(true)` → `DbConnection.Dispose(bool)` 虚调用 `Close()` → `PoolingDbConnection.Close()` → `ReturnToPool()`。今天无害仅因 state 已是 Disposed（CAS 落空）或事件已退订（Open 失败路径依赖基类「先退订后 DisposeInternal」的顺序）。状态机未来一改，已销毁连接可能自我入队。
- **方案**：`DisposeInternal` 加注释钉住该链条及其安全性依赖。

### 5. Connection_Recycled 防御分支的可达性说明

- **位置**：`src/Xbc.DbAccess/Connections/PoolingDbConnectionManager.cs#L270-L273`
- **问题**：`IsDisposed → ReleaseLicenseSafely` 分支当前不可达——所有「Active 状态下被 ActualDispose」的路径都被退订或管理器释放分支改道。作为纵深防御保留合理，但许可证守恒的正确性分散在 4 个文件中。
- **方案**：注释说明该分支的预期防御对象（未来新增的 ActualDispose 调用者），及守恒依赖的四条链路。

### 6. UnderlyingConnection 逃逸口的事务防护禁令

- **位置**：`src/Xbc.DbAccess/Connections/DbConnectionWrapper.cs#L28`、`src/Xbc.DbAccess/Connections/DbConnectionPoolOptions.cs#L5-L6`
- **问题**：直接 `UnderlyingConnection.BeginTransaction()` 会绕过 `pendingTransaction` 记录，回池的挂事务防护（`HasPendingTransaction`）失效——带活事务的连接被回收给下一个借用者，其命令静默加入旧事务。现有 bulk 路径安全（先经框架事务包装器），但这是内部 API 纪律问题，选项文件的已知限制清单未覆盖。
- **方案**：`UnderlyingConnection` 属性注释禁止直接 BeginTransaction/BeginAsyncTransaction，要求一律经框架事务包装器；`DbConnectionPoolOptions` 已知限制清单补该条目。
- **验证**：无代码变更。

## P2 低风险行为改进

### 7. 借用者取消与连接失败的统计区分（F3）

- **位置**：`src/Xbc.DbAccess/Connections/PoolingDbConnectionManager.cs#L227-L230`
- **问题**：激活阶段的任何异常（含借用者自身的 `OperationCanceledException`）被吞并视为连接死亡——一次取消销毁一个健康连接；取消期间创建失败计入 `TotalCreateFailed`，排查时误导。
- **方案**：`OperationCanceledException` 单独计数（如 `TotalCancelledOnActivate`），行为保持销毁（保守安全），仅统计区分。
- **验证**：新增取消场景统计断言测试。

### 8. 连接最大寿命（maxLifetime）

- **位置**：`DbConnectionPoolOptions` 新增选项 + `PoolingDbConnection` 记录出生时间戳 + 回收/清理时检查
- **问题**：现有 `MaxIdleTime` 只清理空闲超时的连接；被持续借用的连接永不换新。NAT/LB/防火墙静默断链场景下，长寿命连接成为僵尸（借出时才失败），对应 HikariCP `maxLifetime` 教科书特性。
- **方案**：①新增 `MaxLifetime`（默认如 30 分钟，0 表示不限）；②`PoolingDbConnection` 记录创建时间；③回收入队时超龄则销毁重建而非入队；④清理定时器顺带清理空闲超龄连接；⑤激活时发现超龄同样销毁（防御借用途中到达寿命）。
- **验证**：专项测试（超龄连接回收即销毁、激活超龄销毁、清理路径）+ 五方言真库回归。

### 9. 预创建懒加载化，消除构造函数阻塞

- **位置**：`src/Xbc.DbAccess/Connections/PoolingDbConnectionManager.cs#L58-L59, L73-L101`
- **问题**：构造函数内 `Task.WaitAll(30s)` 同步预创建违反框架设计指南「构造函数不长时间阻塞」；数据库不可达时最坏阻塞 30 秒才返回。
- **方案**：预创建改为首次 `GetOrCreateAsync` 触发的懒初始化（或后台异步预热不阻塞构造）。需保持语义：预创建失败的连接静默跳过、后续按需创建（现行为），统计口径不变。
- **验证**：现有池测试全量回归 + 预创建失败场景测试。

## P3 功能差距对齐（对标 ADO.NET 内置池）

### 10. 会话状态重置（对标 sp_reset_connection）✅ 已完成（2026-11-25）

- **位置**：`PoolingDbConnection.ResetSessionState`（回池时执行）+ 各方言 `SessionStateResetCommand`
- **实现**：连接归还连接池时按方言同步执行复位命令（在归还线程执行，保持借用快速路径语义不变），任一命令失败连接销毁换新、不入池：
  SQL Server `sp_reset_connection`；PostgreSQL `DISCARD ALL`；MySQL `ROLLBACK` + `SET SESSION sql_mode/time_zone = DEFAULT`（无原生等价机制）；
  Oracle（临时表为全局临时表对象、事务隔离为事务级设置）与 SQLite（无会话级可变状态）默认空序列。
  经 `DbConnectionPoolOptions.EnableSessionStateReset`（默认开启）关闭。
- **验证**：单元 2015/2015（含回池执行/失败销毁/开关关闭 ×3 与五方言命令契约 ×5）；
  真库集成 PG16 + MySQL 8.0.46 通过（SET datestyle/sql_mode → 回池复位 → 复借恢复默认，池容量 1 保证同连接可观测）；
  SQL Server 用例（临时表复位）待环境重建后点亮。

## 审查结论存档（已验证不改动）

以下不变量经交错推演验证成立，是后续改动必须保持的底线：

- 许可证守恒：`sem.Count = MaxPoolSize − 活动数`，所有归还链路（回收/生命周期结束/失败路径）无双重归还
- 回收「先入队后归还」顺序、登记/记账不入 catch、ActualDispose 先进终态幂等
- 挂起事务双重防护（回池拒收 + 激活拒复用）、`ClearPendingTransaction` 防乱序误清
- 清理循环 maxCheckCount 硬上限兜底；健康检查按方言生成（Oracle `FROM DUAL`）
