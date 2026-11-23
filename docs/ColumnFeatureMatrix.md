# 特殊列形态支持矩阵（IsJson / IsConcurrencyToken / ConverterType / EnumAsString）

本文档整理四种特殊列形态从元数据构建层到 SQL 翻译/执行层的全链路支持情况，
以及运行时守卫的判据与代码位置。所有场景均以当前代码为准（单测 1557 全绿基线）。

## 一、四种列形态

| 形态 | 特性配置 | 存储形态 | 语义特征 |
|---|---|---|---|
| JSON 列 | `[Column(IsJson = true)]` | 序列化 JSON 文本 | 属性值是**结构化内容**，存储值是文本——比较/排序序与内容序不一致 |
| 并发令牌 | `[Column(IsConcurrencyToken = true)]` | byte[]（GUID 字节） | **框架乐观锁专用**：SET 自动换发、乐观锁条件由框架内部生成；GUID 无序，无增量比较语义 |
| 自定义转换器 | `[Column(ConverterType = ...)]` 或 `DbValueConverterRegistry` 全局注册 | 经 ConvertTo 变换的存储值 | 存储值与属性值不同构（如加密、ticks 数值化），序与函数语义不明 |
| 枚举名称字符串 | `[Column(EnumAsString = true)]` | 枚举名文本 | **映射语义确定**（名称 ↔ 枚举一一对应），等值与序均为确定的名称语义 |

四形态在构建期**互斥**（列级开关至多其一），每列至多属于一种形态。

## 二、两条主线判据

整个运行时守卫体系只有两条判据：

1. **`ColumnMetadata.HasConverterTransform`**（转换器变换：列级转换器 / 全局注册）
   ——「存储值与属性值不同构」，依赖存储值形态的能力（序比较、函数作用于文本）语义不明；
   JSON 列在两个消费场景均由专属检查（排序守卫、WHERE 列操作数守卫）先行拒绝，不经本判据
2. **`ColumnMetadata.IsConcurrencyToken`**（框架专用）
   ——乐观锁管理权归框架，用户面（排序/条件/赋值）全部拒绝

`EnumAsString` **不在任何拦截判据中**：映射语义确定，等值域经 `ConvertToDb` 与存储值同域，行序为名称字母序（可观察、可纠正）。

## 三、构建期（EntityMetadataCache——配置错误直接抛 InvalidOperationException，无运行时意外）

| 校验 | 规则 |
|---|---|
| 列级开关互斥 | IsConcurrencyToken / EnumAsString / IsJson / ConverterType 任一组合同时声明即拒绝（列级开关至多其一） |
| 令牌类型约束 | IsConcurrencyToken 列必须为 byte[]；每实体至多一列 |
| 枚举约束 | EnumAsString 仅用于枚举类型列（可空枚举按基础枚举校验） |
| JSON 约束 | IsJson 属性必须是引用类型，且不可直接映射为数据库列 |
| 转换器约束 | ConverterType 必须提供公共无参构造与 ConvertFrom(object) / ConvertTo(T) 方法 |
| 非基础类型兜底 | 非基础类型属性标记 [Column] 但未指定 ConverterType 且未启用 IsJson 时拒绝 |
| **导航键列** | 键列拒 ConverterType / EnumAsString / IsJson / **IsConcurrencyToken**——键的语义是单值等值匹配，变换列令参数化形态与对方键列存储形态不一致而静默不命中；令牌是乐观锁版本标记，无键语义 |

## 四、查询面

| 场景 | JSON | 令牌 | 转换器（列级/全局） | 枚举名称 | 守卫位置 |
|---|---|---|---|---|---|
| 排序/聚合键 | 拦 | 拦 | 拦 | 放行 | `EntityQueryBase.ResolveMappedColumn`（内联守卫） |
| keyset 分页 | 拦 | 拦 | 拦 | 放行 | 无独立守卫——游标列即排序键列，解析端已保证 |
| SUM / AVG | — | — | — | 放行（「有没有意义」由用户决定，按存储值聚合） | 无预检——仅经 `ResolveMappedColumn`（JSON/令牌/转换器），数值性交数据库 |
| MAX / MIN | — | — | — | 放行（数值序/名称字母序聚合） | 同上 |
| WHERE 等值 / IN | 拦 | 拦 | 放行（参数经 ConvertToDb 与存储值同域） | 放行（名称双射语义自洽） | `WhereClauseTranslator.TryGetColumn`（所有列操作数的必经解析点） |
| WHERE 大小比较 | 拦 | 拦 | 放行（存储值域上的比较自洽） | 放行（按名称字典序比较，序语义由用户把握） | 无枚举守卫 |
| WHERE LIKE / 字符串函数 / 日期函数 | 类型校验拦（非字符串/DateTime） | 类型校验拦 | 拦（HasConverterTransform） | 类型校验拦 | `WhereClauseTranslator.ThrowIfConverterColumn` |

## 五、写入面

| 场景 | JSON | 令牌 | 转换器 | 枚举名称 | 守卫位置 |
|---|---|---|---|---|---|
| SET 显式赋值 | 放行 | **拦** | 放行 | 放行 | `SetClauseTranslator`（令牌列检查） |
| Update(entity) 全列 | 写入 | **排除**（乐观锁自动换发回填） | 写入 | 写入 | `EntitySet.Update` 全列清单排除主键/标识/令牌 |
| Upsert（实体形态） | 放行 | **框架换发**（入口总是生成新令牌：插入路径成为新行初始令牌，更新路径使旧令牌失效，防过时快照静默覆盖） | 放行 | 放行 | `EntitySet.Upsert` 入口换发 + 列清单自然写入 |
| INSERT / BulkInsert | 放行 | 放行（首版令牌值写入） | 放行 | 放行 | 值统一经 ConvertToDb，无守卫 |

## 六、读取面（零守卫——转换层吸收一切形态差异）

| 场景 | 行为 |
|---|---|
| 投影 Select | 四形态全放行（JSON 返回反序列化对象、令牌读 byte[]、转换器 ConvertFrom、枚举名称解析） |
| 实体装配 | 同上，ConvertFromDb 转换链统一处理 |
| 导航填充 | 键列形态已由构建期排除 |

## 七、框架内部（不经用户面守卫）

乐观锁条件（`WHERE Id = @id AND 令牌 = @old`）由 `EntitySet.Delete` / `EntitySet.Update`
的并发令牌分支以字符串拼接生成——不经过 `WhereClauseTranslator`，
是并发令牌唯一出现的 SQL 场景（GUID 无序，用户查询面无任何合法用法）。

## 八、守卫代码点清单

各守卫点自包含（不跨场景合并）。

| # | 位置 | 职责 |
|---|---|---|
| 1 | `EntityMetadataCache` 构建期（互斥 + 类型约束 + 非基础类型兜底） | 配置合法性 |
| 2 | `EntityMetadataCache.ThrowIfInvalidNavigationKey` | 导航键形态资格（单端校验模板，两端各调一次） |
| 3 | `EntityQueryBase.ResolveMappedColumn`（内联守卫） | 排序/聚合键资格（JSON/令牌/转换器拒绝，枚举放行）——唯一调用方持有 |
| 4 | `WhereClauseTranslator.TryGetColumn` | WHERE 列操作数资格（JSON/令牌拒绝） |
| 5 | `WhereClauseTranslator.ThrowIfConverterColumn` | SQL 函数/文本匹配的转换器变换拒绝 |
| 6 | `SetClauseTranslator` 令牌检查 | SET 显式赋值（与主键/标识并列的赋值合法性组） |

守卫消息术语：资格守卫统一「属性 '{X}' 是{资格}，{说明}」句式（与构建期对齐）；
表达式解析失败类消息用「成员 '{X}' 不是实体 '{T}' 的映射列」句式（`ResolveColumn` / `TryGetColumn` / `SetClauseTranslator` 共用形态）。

## 九、心智模型

- **配得上就配出来**：形态组合错误构建期全拦，运行时零意外
- **合法就不拒绝**：有没有意义由用户决定（枚举 SUM/AVG、非数值聚合、枚举名称大小比较均放行交数据库/按存储值执行），
  框架只拦「数据库不会报错但结果静默错误」（JSON 比较文本、转换器序不一致）
  与「框架管理权」（令牌）两类
- **枚举随便用**：映射语义确定，全场景放行
- **转换器列 = 值域列**：等值/比较/SET/INSERT/投影全放行，唯独「序」（排序/聚合/keyset）
  与「函数作用于存储文本」（LIKE 等）不行——这两类能力依赖存储值形态
- **JSON 列 = 内容列**：比较/序/函数全拦（比较的是文本不是内容），投影/SET 放行
- **令牌 = 框架专用列**：用户面（排序/条件/赋值）全拦，框架内部专用（乐观锁条件）
- **新增场景先挂既有守卫**：先问该挂在哪个判据（HasConverterTransform / IsConcurrencyToken）与哪个守卫点上，不新增散点

## 附录、四个开关的代码出现位置地图

各开关从特性定义到转换管线的全库引用点（按层组织，行号为整理时的代码状态）。
无关项说明：Xbc.Alerting / Xbc.UnitOfWork 中的 `IsConcurrencyToken()` 为 EF Core fluent API，
Xbc.ImportExport 的 `ConverterType` 为其自有的表格列转换器特性——均与本矩阵的开关无关。

### ConverterType（列级转换器）

| 层 | 位置 | 用途 |
|---|---|---|
| 特性定义 | `ColumnAttribute.cs` L41 | 配置入口 |
| 元数据 | `ColumnMetadata.cs` L79（属性）、L114（HasConverterTransform 判据） | 判据数据源 |
| 构建期 | `EntityMetadataCache.cs` L559（非基础类型兜底）、L570（触发 ValidateConverter）、L650（互斥收集）、L702-725（ValidateConverter：无参构造 / ConvertFrom / ConvertTo 签名 / 返回可赋值）、L931（导航键拒绝） | 配置合法性 |
| 运行时守卫 | `EntityQueryBase.cs` L346（HasConverterTransform 转换器守卫段，排序/聚合）、`WhereClauseTranslator.cs` L1083（LIKE/函数守卫 ThrowIfConverterColumn） | 能力限制 |
| 转换管线 | `ColumnMetadata.ConvertToDb` L268（写入优先级 1）、`EntityMapperCore.cs` L511（读取 ConvertFrom） | 值变换 |

全局转换器（`DbValueConverterRegistry`）独立于 ConverterType 配置，但与列级转换器共用
HasConverterTransform 判据（第二项）与全部运行时守卫。

### EnumAsString（枚举名称字符串）

| 层 | 位置 | 用途 |
|---|---|---|
| 特性定义 | `ColumnAttribute.cs` L59 | 配置入口 |
| 元数据 | `ColumnMetadata.cs` L99（属性；不在任何拦截判据中，仅 HasConverterTransform doc 说明豁免） | 数据源 |
| 构建期 | `EntityMetadataCache.cs` L644（互斥收集）、L668-672（必须枚举类型）、L934（导航键拒绝） | 配置合法性 |
| 运行时守卫 | 无（大小比较等全场景放行，按存储值执行） | 能力限制 |
| 转换管线 | `ColumnMetadata.ConvertToDb` L254-262（名称字符串）、`EntitySet.BulkInsert.cs` L71（CLR 类型归一 string，非守卫） | 值变换 |

### IsJson（JSON 存储）

| 层 | 位置 | 用途 |
|---|---|---|
| 特性定义 | `ColumnAttribute.cs` L67 | 配置入口 |
| 元数据 | `ColumnMetadata.cs` L104（属性；不在任何拦截判据中——JSON 在消费场景由专属检查先行拒绝） | 数据源 |
| 构建期 | `EntityMetadataCache.cs` L559（非基础类型兜底的另一分支）、L647（互斥收集）、L674-684（必须引用类型 + 拒 string/byte[] 误标）、L937（导航键拒绝） | 配置合法性 |
| 运行时守卫 | `EntityQueryBase.cs` L338（排序/聚合）、`WhereClauseTranslator.cs` L833（WHERE 条件，TryGetColumn）；LIKE 守卫处 JSON 由类型校验先行拦截，到不了此处 | 能力限制 |
| 转换管线 | `ColumnMetadata.ConvertToDb` L264（写入序列化）、`EntitySet.BulkInsert.cs` L71（CLR 归一 string）、`EntityMapperCore.cs` L515（读取反序列化） | 值变换 |

### IsConcurrencyToken（并发令牌）——运行时守卫最多

| 层 | 位置 | 用途 |
|---|---|---|
| 特性定义 | `ColumnAttribute.cs` L29 | 配置入口 |
| 元数据 | `ColumnMetadata.cs` L57（属性） | 数据源 |
| 构建期 | `EntityMetadataCache.cs` L385（每实体至多一列）、L638（互斥收集）、L657-661（必须 byte[]）、L940（导航键拒绝） | 配置合法性 |
| 运行时守卫 | `EntityQueryBase.cs` L342（排序/聚合）、`WhereClauseTranslator.cs` L837（WHERE 条件）、`SetClauseTranslator.cs` L108（SET 赋值）、`EntitySet.Update.cs` L198（全列更新排除→换发） | 写管理权 |
| 框架内部 | `EntitySet.Concurrency.cs`（GUID 生成/校验）、Update/Delete 乐观锁条件拼接、`EntitySet.Upsert` 入口换发 | 乐观锁核心 |

### 图谱揭示的结构性事实

- **守卫密度排序**：令牌（4 个运行时守卫）> JSON（2 直接）> 转换器（2，全部经
  HasConverterTransform）> 枚举（0，全场景放行）——与「框架专用 → 内容列 → 值域列 → 语义确定列」的管控强度同构
- **构建期落点完全同构**：互斥收集 + 类型/适用性校验 + 导航键拒绝——新增开关照此三件套即可
- **运行时守卫无一遗漏**：每个「拦」都有对应代码行，每个「放行」确无隐藏检查
  （BulkInsert L71 已核实为类型归一非守卫）
