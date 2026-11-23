# 使用事务

本文档介绍 Xbc.DbAccess 的事务管理功能，包括普通事务和嵌套事务的使用。

## 目录

1. [为什么使用事务](#1-为什么使用事务)
2. [普通事务](#2-普通事务)
   - [2.1 基本用法](#21-基本用法)
   - [2.2 带查询的事务](#22-带查询的事务)
3. [嵌套事务](#3-嵌套事务)
   - [3.1 嵌套事务始终启用](#31-嵌套事务始终启用)
   - [3.2 嵌套事务的工作原理](#32-嵌套事务的工作原理)
   - [3.3 实际应用场景](#33-实际应用场景)
4. [事务隔离级别](#4-事务隔离级别)
   - [4.1 设置隔离级别](#41-设置隔离级别)
   - [4.2 隔离级别说明](#42-隔离级别说明)
   - [4.3 隔离级别选择指南](#43-隔离级别选择指南)
5. [最佳实践](#5-最佳实践)
   - [5.1 始终使用 using 语句](#51-始终使用-using-语句)
   - [5.2 保持事务简短](#52-保持事务简短)
   - [5.3 正确处理异常](#53-正确处理异常)
   - [5.4 避免死锁](#54-避免死锁)
6. [完整示例](#6-完整示例)

## 1. 为什么使用事务

事务确保数据库操作的原子性、一致性、隔离性和持久性（ACID）。当多个数据库操作需要作为一个整体成功或失败时，必须使用事务。

```csharp
// 没有事务的问题：如果第二个操作失败，数据将不一致
await db.ExecuteNonQueryAsync("UPDATE Accounts SET Balance = Balance - 100 WHERE Id = 1");
// 如果这里发生异常，账户1的钱扣了，但账户2没有收到
await db.ExecuteNonQueryAsync("UPDATE Accounts SET Balance = Balance + 100 WHERE Id = 2");

// 使用事务保证数据一致性
await using (var transaction = await db.BeginTransactionAsync())
{
    try
    {
        await db.ExecuteNonQueryAsync("UPDATE Accounts SET Balance = Balance - 100 WHERE Id = 1");
        await db.ExecuteNonQueryAsync("UPDATE Accounts SET Balance = Balance + 100 WHERE Id = 2");
        await transaction.CommitAsync();  // 提交所有更改
    }
    catch
    {
        await transaction.RollbackAsync();  // 发生错误时回滚所有更改
        throw;
    }
}
```

## 2. 普通事务

### 2.1 基本用法

```csharp
// 开始事务
await using (var transaction = await db.BeginTransactionAsync())
{
    try
    {
        // 执行数据库操作
        await db.ExecuteNonQueryAsync(
            "INSERT INTO Orders (CustomerId, OrderDate) VALUES (@CustomerId, @OrderDate)",
            new { CustomerId = 1, OrderDate = DateTime.Now });

        await db.ExecuteNonQueryAsync(
            "UPDATE Inventory SET Quantity = Quantity - 1 WHERE ProductId = @ProductId",
            new { ProductId = 100 });

        // 提交事务
        await transaction.CommitAsync();
        Console.WriteLine("事务提交成功");
    }
    catch (Exception ex)
    {
        // 回滚事务
        await transaction.RollbackAsync();
        Console.WriteLine($"事务回滚: {ex.Message}");
        throw;
    }
}
```

### 2.2 带查询的事务

```csharp
await using (var transaction = await db.BeginTransactionAsync())
{
    try
    {
        // 检查账户余额
        decimal balance = await db.ExecuteScalarAsync<decimal>(
            "SELECT Balance FROM Accounts WHERE Id = @Id",
            new { Id = 1 });

        decimal transferAmount = 200;

        if (balance < transferAmount)
        {
            throw new InvalidOperationException("余额不足");
        }

        // 执行转账
        await db.ExecuteNonQueryAsync(
            "UPDATE Accounts SET Balance = Balance - @Amount WHERE Id = @Id",
            new { Amount = transferAmount, Id = 1 });

        await db.ExecuteNonQueryAsync(
            "UPDATE Accounts SET Balance = Balance + @Amount WHERE Id = @Id",
            new { Amount = transferAmount, Id = 2 });

        await transaction.CommitAsync();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}
```

## 3. 嵌套事务

Xbc.DbAccess 支持嵌套事务，内部事务使用数据库保存点（Savepoint）实现。

### 3.1 嵌套事务始终启用

框架始终启用嵌套事务，无需任何配置。早期版本通过 `AllowNestedTransaction` 选项控制该行为，该选项已废弃：框架内部固定使用嵌套事务管理器。

在已有事务内再次调用 `await db.BeginTransactionAsync()` 时，框架会自动创建基于保存点（Savepoint）的子事务，不会抛出异常：

```csharp
var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
{
    options.ConnectionString = "...";
});

using (var db = factory.Create())
{
    await using (var transaction1 = await db.BeginTransactionAsync())
    {
        // 在已有事务内再次开启事务 - 自动创建保存点子事务
        await using (var transaction2 = await db.BeginTransactionAsync())  // ✅ 保存点子事务
        {
            // ...
        }
    }
}
```

### 3.2 嵌套事务的工作原理

#### 什么是保存点（Savepoint）

保存点是数据库事务中的一个标记，允许你在事务内部设置一个"回滚点"。当回滚到保存点时，保存点之后的所有操作都会被撤销，但保存点之前的操作仍然保留。

**保存点的工作流程**：

```
开始根事务
    │
    v
执行操作 A                    ──► ✅ 数据已写入（未提交）
    │
    v
创建保存点 SP1  ◄──────────── 设置回滚标记
    │
    v
执行操作 B                    ──► ✅ 数据已写入（未提交）
    │
    v
创建保存点 SP2  ◄──────────── 设置新的回滚标记
    │
    v
执行操作 C                    ──► ❌ 执行失败！
    │
    v
回滚到 SP2  ◄──────────────── 撤销操作 C
    │                          • 操作 C 的数据被撤销
    │                          • 操作 B 的数据保留（在 SP2 之前）
    │                          • 操作 A 的数据保留
    │                          • 事务继续执行（未结束）
    v
可以继续执行其他操作...         ──► 例如：执行操作 D
    │
    v
提交根事务  ◄──────────────── 操作 A、B 和 D 生效
```

**关键特性**：

- 保存点只在当前事务内有效，事务提交后所有保存点自动释放
- 回滚到保存点不会结束事务，事务仍然可以继续执行
- 子事务通过保存点实现，提交子事务时会释放保存点，回滚子事务是回滚到保存点
- 一旦子事务提交，其保存点即被释放，无法再回滚到该保存点

#### 不同数据库的保存点实现

不同数据库对保存点的 SQL 语法支持有所差异：

| 数据库            | 保存点支持 | 创建保存点                   | 回滚到保存点                       | 释放保存点                      | 说明                 |
| -------------- | ----- | ----------------------- | ---------------------------- | -------------------------- | ------------------ |
| **SQLite**     | ✅ 支持  | `SAVEPOINT name`        | `ROLLBACK TO SAVEPOINT name` | ✅ `RELEASE SAVEPOINT name` | 支持完整的保存点操作         |
| **SQL Server** | ✅ 支持  | `SAVE TRANSACTION name` | `ROLLBACK TRANSACTION name`  | ❌ 不支持                      | 使用 TRANSACTION 关键字 |
| **MySQL**      | ✅ 支持  | `SAVEPOINT name`        | `ROLLBACK TO SAVEPOINT name` | ❌ 不支持                      | 与 SQLite 语法相同      |
| **PostgreSQL** | ✅ 支持  | `SAVEPOINT name`        | `ROLLBACK TO SAVEPOINT name` | ✅ `RELEASE SAVEPOINT name` | 支持显式释放保存点          |
| **Oracle**     | ✅ 支持  | `SAVEPOINT name`        | `ROLLBACK TO SAVEPOINT name` | ❌ 不支持                      | 事务提交时自动释放          |

**框架的统一处理**：

Xbc.DbAccess 框架自动处理不同数据库的差异：

- **自动适配 SQL 语法**：根据数据库类型选择正确的保存点语法
- **智能处理保存点释放**：对于不支持显式释放的数据库（如 SQL Server），自动跳过释放操作
- **统一的使用体验**：无论使用何种数据库，嵌套事务 API 的行为完全一致

```csharp
// 框架自动处理不同数据库的保存点语法
await using (var rootTransaction = await db.BeginTransactionAsync())  // 开始实际事务
{
    // 框架自动调用正确的 SQL 来创建保存点
    await using (var nestedTransaction = await db.BeginTransactionAsync())
    {
        // 执行操作...

        await nestedTransaction.CommitAsync();  // 自动释放保存点（如果支持）
        // 或 await nestedTransaction.RollbackAsync();  // 自动回滚到保存点
    }
}
```

#### 多层嵌套示例

```csharp
// 根事务
await using (var rootTransaction = await db.BeginTransactionAsync())
{
    // 执行操作 1
    await db.ExecuteNonQueryAsync("INSERT INTO Table1 ...");

    // 嵌套事务 1 - 创建保存点 SAVEPOINT_1
    await using (var nestedTransaction1 = await db.BeginTransactionAsync())
    {
        await db.ExecuteNonQueryAsync("INSERT INTO Table2 ...");

        // 嵌套事务 2 - 创建保存点 SAVEPOINT_2
        await using (var nestedTransaction2 = await db.BeginTransactionAsync())
        {
            await db.ExecuteNonQueryAsync("INSERT INTO Table3 ...");

            // 提交嵌套事务 2 - 释放保存点 SAVEPOINT_2
            await nestedTransaction2.CommitAsync();
        }

        // 回滚嵌套事务 1 - 回滚到保存点 SAVEPOINT_1
        // 注意：回滚到 SAVEPOINT_1 会撤销 SAVEPOINT_1 之后的所有操作
        // 因此 Table2 和 Table3 的插入都会被撤销
        await nestedTransaction1.RollbackAsync();
    }

    // 提交根事务 - 提交实际的数据库事务
    // 只有 Table1 的插入会生效
    await rootTransaction.CommitAsync();
}
```

**平行子事务示例**

```csharp
// 根事务
await using (var rootTransaction = await db.BeginTransactionAsync())
{
    // 创建订单主表（根事务）
    await db.ExecuteNonQueryAsync("INSERT INTO Orders (Id, TotalAmount) VALUES (1, 1000)");

    // 子事务 1 - 创建保存点 SAVEPOINT_1
    await using (var childTransaction1 = await db.BeginTransactionAsync())
    {
        await db.ExecuteNonQueryAsync("INSERT INTO OrderItems (OrderId, ProductId) VALUES (1, 101)");
        await childTransaction1.CommitAsync();  // 释放 SAVEPOINT_1
    }

    // 子事务 2 - 创建保存点 SAVEPOINT_2（与 SAVEPOINT_1 平行）
    await using (var childTransaction2 = await db.BeginTransactionAsync())
    {
        await db.ExecuteNonQueryAsync("INSERT INTO OrderItems (OrderId, ProductId) VALUES (1, 102)");
        await childTransaction2.CommitAsync();  // 释放 SAVEPOINT_2
    }

    // 子事务 3 - 模拟失败
    await using (var childTransaction3 = await db.BeginTransactionAsync())
    {
        await db.ExecuteNonQueryAsync("INSERT INTO OrderItems (OrderId, ProductId) VALUES (1, 103)");
        // 回滚子事务 3 - 只撤销 ProductId = 103 的插入
        await childTransaction3.RollbackAsync();
    }

    // 提交根事务
    // 结果：Orders 表有 1 条记录，OrderItems 表有 2 条记录（101 和 102）
    await rootTransaction.CommitAsync();
}
```

### 3.3 实际应用场景

**场景一：批量数据导入**

在批量导入数据时，希望部分数据导入失败不影响其他数据的导入，同时需要保证每个成功导入的数据都是完整一致的。

```csharp
public async Task<ImportResult> ImportUsers(List<UserImportDto> users)
{
    // 根事务 - 记录导入批次信息
    await using (var rootTransaction = await _dbAccess.BeginTransactionAsync())
    {
        // 创建导入批次记录
        int batchId = await _dbAccess.ExecuteScalarAsync<int>(...);

        // 逐个导入用户（每个用户使用嵌套事务）
        foreach (var user in users)
        {
            try
            {
                // 使用嵌套事务导入单个用户
                await ImportSingleUser(user, batchId);
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                // 记录失败信息，继续处理下一个用户
                result.FailedItems.Add(new FailedItem { ... });
                result.FailedCount++;
            }
        }

        // 更新批次状态
        await _dbAccess.ExecuteNonQueryAsync("UPDATE ImportBatches SET ...", ...);

        await rootTransaction.CommitAsync();
        return result;
    }
}

private async Task ImportSingleUser(UserImportDto user, int batchId)
{
    // 嵌套事务 - 单个用户的导入要么全部成功，要么全部失败
    await using (var nestedTransaction = await _dbAccess.BeginTransactionAsync())
    {
        try
        {
            // 1. 验证数据
            ValidateUserData(user);

            // 2. 插入用户主表
            int userId = await _dbAccess.ExecuteScalarAsync<int>(...);

            // 3. 插入用户扩展信息
            await _dbAccess.ExecuteNonQueryAsync(...);

            // 4. 插入用户角色关联
            foreach (var roleId in user.RoleIds)
            {
                await _dbAccess.ExecuteNonQueryAsync(...);
            }

            await nestedTransaction.CommitAsync();
        }
        catch
        {
            await nestedTransaction.RollbackAsync();
            throw;
        }
    }
}
```

**场景二：订单处理中的部分失败处理**

在电商系统中，创建订单时需要扣减库存、生成订单、记录日志等多个操作。如果库存扣减失败，整个订单应该回滚；但如果只是日志记录失败，订单应该继续创建。

```csharp
public async Task<OrderResult> CreateOrder(OrderRequest request)
{
    // 根事务 - 保证订单核心数据的一致性
    await using (var rootTransaction = await _dbAccess.BeginTransactionAsync())
    {
        // 1. 扣减库存（核心操作）
        foreach (var item in request.Items)
        {
            var affected = await _dbAccess.ExecuteNonQueryAsync(
                "UPDATE Products SET Stock = Stock - @Qty WHERE Id = @Id",
                ...);

            if (affected == 0)
                throw new InvalidOperationException("库存不足");
        }

        // 2. 创建订单（核心操作）
        int orderId = await _dbAccess.ExecuteScalarAsync<int>(...);

        // 3. 插入订单明细（核心操作）
        foreach (var item in request.Items)
        {
            await _dbAccess.ExecuteNonQueryAsync(...);
        }

        // 4. 记录操作日志（非核心操作，使用嵌套事务）
        try
        {
            await using (var logTransaction = await _dbAccess.BeginTransactionAsync())
            {
                await _dbAccess.ExecuteNonQueryAsync(...);
                await logTransaction.CommitAsync();
            }
        }
        catch
        {
            // 日志记录失败不影响订单创建
            result.Warnings.Add("日志记录失败");
        }

        // 5. 发送通知（非核心操作，使用嵌套事务）
        try
        {
            await using (var notifyTransaction = await _dbAccess.BeginTransactionAsync())
            {
                await _dbAccess.ExecuteNonQueryAsync(...);
                await notifyTransaction.CommitAsync();
            }
        }
        catch
        {
            // 通知记录失败不影响订单创建
            result.Warnings.Add("通知记录失败");
        }

        await rootTransaction.CommitAsync();
        return result;
    }
}
```

**场景三：工作流步骤处理**

在工作流系统中，一个流程包含多个步骤，每个步骤可能涉及多个数据库操作。如果某个步骤失败，应该回滚该步骤的操作，但不应该影响其他已成功的步骤。

```csharp
public async Task<WorkflowResult> ExecuteWorkflow(int workflowId, Dictionary<string, object> context)
{
    var steps = GetWorkflowSteps(workflowId);

    // 根事务 - 记录工作流执行状态
    await using (var rootTransaction = await _dbAccess.BeginTransactionAsync())
    {
        // 创建工作流执行记录
        int executionId = await _dbAccess.ExecuteScalarAsync<int>(...);

        // 逐个执行步骤
        foreach (var step in steps)
        {
            var stepResult = await ExecuteStep(executionId, step, context);
            result.StepResults.Add(stepResult);

            if (!stepResult.Success)
            {
                // 步骤失败，更新工作流状态为部分成功
                await _dbAccess.ExecuteNonQueryAsync(
                    "UPDATE WorkflowExecutions SET Status = 'PartialSuccess' ...");

                await rootTransaction.CommitAsync();
                return result;
            }
        }

        // 所有步骤成功
        await _dbAccess.ExecuteNonQueryAsync(
            "UPDATE WorkflowExecutions SET Status = 'Success' ...");

        await rootTransaction.CommitAsync();
        return result;
    }
}

private async Task<StepResult> ExecuteStep(int executionId, WorkflowStep step, Dictionary<string, object> context)
{
    // 嵌套事务 - 单个步骤的操作
    await using (var stepTransaction = await _dbAccess.BeginTransactionAsync())
    {
        try
        {
            // 记录步骤开始
            await _dbAccess.ExecuteNonQueryAsync(...);

            // 执行步骤操作
            await ExecuteStepAction(step, context);

            // 记录步骤成功
            await _dbAccess.ExecuteNonQueryAsync(...);

            await stepTransaction.CommitAsync();
            return new StepResult { Success = true };
        }
        catch (Exception ex)
        {
            await stepTransaction.RollbackAsync();

            // 记录步骤失败
            await _dbAccess.ExecuteNonQueryAsync(...);

            return new StepResult { Success = false, ErrorMessage = ex.Message };
        }
    }
}
```

## 4. 事务隔离级别

### 4.1 设置隔离级别

```csharp
// 使用默认隔离级别（Unspecified，由数据库决定）
await using (var transaction = await db.BeginTransactionAsync())
{
    // 事务操作
}

// 指定隔离级别
await using (var transaction = await db.BeginTransactionAsync(IsolationLevel.ReadCommitted))
{
    // 事务操作
}
```

### 4.2 隔离级别说明

#### 隔离级别对比表

| 隔离级别                | 脏读    | 不可重复读 | 幻读    | 并发性能     | 适用场景          |
| ------------------- | ----- | ----- | ----- | -------- | ------------- |
| **ReadUncommitted** | ✓ 允许  | ✓ 允许  | ✓ 允许  | ⭐⭐⭐⭐⭐ 最高 | 报表查询、数据分析     |
| **ReadCommitted**   | ✗ 不允许 | ✓ 允许  | ✓ 允许  | ⭐⭐⭐⭐ 高   | 一般业务操作（默认）    |
| **RepeatableRead**  | ✗ 不允许 | ✗ 不允许 | ✓ 允许  | ⭐⭐⭐ 中等   | 需要重复读取同一数据    |
| **Serializable**    | ✗ 不允许 | ✗ 不允许 | ✗ 不允许 | ⭐ 最低     | 关键业务、数据一致性要求高 |

**术语说明**：

- **脏读（Dirty Read）**：读取到其他事务未提交的数据
- **不可重复读（Non-repeatable Read）**：同一事务内两次读取同一数据，结果不同（数据被其他事务修改）
- **幻读（Phantom Read）**：同一事务内两次查询，结果集数量不同（数据被其他事务插入或删除）

#### 各隔离级别示例

**ReadUncommitted - 最低隔离级别**

```csharp
// 可能读取到其他事务未提交的数据（脏读）
await using (var transaction = await db.BeginTransactionAsync(IsolationLevel.ReadUncommitted))
{
    var data = await db.ExecuteScalarAsync("SELECT Balance FROM Accounts WHERE Id = 1");
    // 可能读取到未提交的余额（其他事务正在修改但未提交）
}
```

**ReadCommitted - 只能读取已提交的数据**

```csharp
// 大多数数据库的默认级别
await using (var transaction = await db.BeginTransactionAsync(IsolationLevel.ReadCommitted))
{
    var data = await db.ExecuteScalarAsync("SELECT Balance FROM Accounts WHERE Id = 1");
    // 只能读取到已提交的余额
}
```

**RepeatableRead - 可重复读**

```csharp
// 在事务期间，读取的数据不会被其他事务修改
await using (var transaction = await db.BeginTransactionAsync(IsolationLevel.RepeatableRead))
{
    var data1 = await db.ExecuteScalarAsync<decimal>("SELECT Balance FROM Accounts WHERE Id = 1");
    // 其他事务不能修改 Id = 1 的余额
    var data2 = await db.ExecuteScalarAsync<decimal>("SELECT Balance FROM Accounts WHERE Id = 1");
    // data1 和 data2 一定相同
}
```

**Serializable - 最高隔离级别**

```csharp
// 完全串行化执行
await using (var transaction = await db.BeginTransactionAsync(IsolationLevel.Serializable))
{
    var count1 = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Accounts");
    // 其他事务不能插入或删除账户
    var count2 = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Accounts");
    // count1 和 count2 一定相同
}
```

### 4.3 隔离级别选择指南

```csharp
public class TransactionGuide
{
    // 报表查询 - 允许脏读，提高并发性能
    public async Task GenerateReport()
    {
        await using var transaction = await db.BeginTransactionAsync(IsolationLevel.ReadUncommitted);
        var data = await db.ExecuteDataSetAsync("SELECT * FROM LargeTable");
        // 处理报表数据
    }

    // 一般业务操作 - 默认级别
    public async Task NormalOperation()
    {
        await using var transaction = await db.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        // 业务操作
        await transaction.CommitAsync();
    }

    // 需要一致性的操作 - 可重复读
    public async Task ConsistencyRequiredOperation()
    {
        await using var transaction = await db.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        // 需要多次读取同一数据且保持一致
        await transaction.CommitAsync();
    }

    // 关键业务操作 - 串行化
    public async Task CriticalOperation()
    {
        await using var transaction = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        // 关键业务，需要最高一致性
        await transaction.CommitAsync();
    }
}
```

## 5. 最佳实践

### 5.1 始终使用 using 语句

```csharp
// 好的做法 - 自动释放资源
await using (var transaction = await db.BeginTransactionAsync())
{
    // 事务操作
    await transaction.CommitAsync();
}

// 避免这样做 - 可能忘记释放资源
var transaction = await db.BeginTransactionAsync();
try
{
    // 事务操作
    await transaction.CommitAsync();
}
finally
{
    transaction.Dispose();  // 容易忘记
}
```

### 5.2 保持事务简短

```csharp
// 好的做法 - 事务只包含必要的数据库操作
await using (var transaction = await db.BeginTransactionAsync())
{
    await db.ExecuteNonQueryAsync("UPDATE ...");
    await db.ExecuteNonQueryAsync("INSERT ...");
    await transaction.CommitAsync();
}

// 避免在事务中执行耗时操作
await using (var transaction = await db.BeginTransactionAsync())
{
    await db.ExecuteNonQueryAsync("UPDATE ...");

    // 避免：在事务中调用外部 API
    await CallExternalApiAsync();  // 不要这样做

    // 避免：在事务中进行复杂计算
    var data = PerformComplexCalculation();  // 不要这样做

    await transaction.CommitAsync();
}
```

### 5.3 正确处理异常

```csharp
public async Task SafeTransactionOperation()
{
    await using (var transaction = await db.BeginTransactionAsync())
    {
        try
        {
            BusinessOperation(transaction);
            await transaction.CommitAsync();
        }
        catch (DbException ex)
        {
            // 数据库异常，回滚
            await transaction.RollbackAsync();
            logger.LogError(ex, "数据库操作失败");
            throw;
        }
        catch (BusinessException ex)
        {
            // 业务异常，回滚
            await transaction.RollbackAsync();
            logger.LogWarning(ex, "业务验证失败");
            throw;
        }
    }
}
```

### 5.4 避免死锁

**常见死锁场景与解决方案**：

| 场景         | 问题描述                        | 解决方案             |
| ---------- | --------------------------- | ---------------- |
| **顺序访问死锁** | 事务A：更新资源1→资源2；事务B：更新资源2→资源1 | 按固定顺序（如ID排序）访问资源 |
| **读写冲突死锁** | 事务A先读后写，事务B同时写同一数据          | 减少事务持有时间，或使用乐观锁  |
| **长时间事务**  | 事务中执行耗时操作（调用API、复杂计算）       | 尽快提交，耗时操作放在事务外   |

**场景一：顺序访问导致的死锁**

```
死锁场景示例（不要这样做）：

事务A：扣减商品1库存 → 扣减商品2库存
事务B：扣减商品2库存 → 扣减商品1库存

当两个事务同时执行时：
- 事务A获取了商品1的锁，等待商品2的锁
- 事务B获取了商品2的锁，等待商品1的锁
- 结果：死锁！
```

**解决方案：按固定顺序访问**

```csharp
public async Task CreateOrderAvoidDeadlock(List<OrderItem> items)
{
    // 按商品ID排序，确保所有事务按相同顺序访问
    var sortedItems = items.OrderBy(i => i.ProductId).ToList();

    await using (var transaction = await _dbAccess.BeginTransactionAsync())
    {
        // 按固定顺序扣减库存
        foreach (var item in sortedItems)
        {
            await _dbAccess.ExecuteNonQueryAsync(
                "UPDATE Products SET Stock = Stock - @Qty WHERE Id = @Id",
                ...);
        }

        // 创建订单...

        await transaction.CommitAsync();
    }
}
```

**场景二：读写冲突导致的死锁**

```
死锁场景示例（不要这样做）：

事务A（使用 RepeatableRead 或 Serializable）：
  1. 读取账户余额（获取读锁）
  2. 执行复杂计算（长时间持有读锁）
  3. 更新账户余额（尝试获取写锁）

事务B：
  1. 更新同一账户余额（等待事务A释放读锁）

结果：事务A等待事务B释放写锁，事务B等待事务A释放读锁，死锁！
```

**解决方案：减少事务持有时间 + 使用乐观锁**

```csharp
// 方案1：减少事务持有时间
// 好的做法：事务中只包含必要的数据库操作
await using (var transaction = await db.BeginTransactionAsync())
{
    await db.ExecuteNonQueryAsync("UPDATE Inventory ...");
    await db.ExecuteNonQueryAsync("INSERT INTO Orders ...");
    await transaction.CommitAsync();  // 尽快提交
}

// 避免：在事务中执行耗时操作
await using (var transaction = await db.BeginTransactionAsync())
{
    await db.ExecuteNonQueryAsync("UPDATE Inventory ...");

    // 错误：在事务中调用外部 API
    await CallExternalApiAsync();  // 网络请求可能很慢

    await transaction.CommitAsync();
}
```

````csharp
// 方案2：使用乐观锁（适用于高并发更新场景）

```csharp
public async Task<bool> UpdateWithVersion(int id, decimal amount)
{
    await using (var transaction = await _dbAccess.BeginTransactionAsync())
    {
        // 读取当前版本号
        var version = await _dbAccess.ExecuteScalarAsync<int>(...);
        
        // 使用版本号更新（乐观锁）
        var affected = await _dbAccess.ExecuteNonQueryAsync(
            "UPDATE Accounts SET Balance = @Balance, Version = Version + 1
             WHERE Id = @Id AND Version = @Version",
            ...);
        
        if (affected == 0)
        {
            // 版本号不匹配，需要重试
            await transaction.RollbackAsync();
            return false;
        }
        
        await transaction.CommitAsync();
        return true;
    }
}
````

**死锁避免原则**：

1. **按固定顺序访问资源**：对所有事务都按相同的顺序访问多个资源
2. **减少事务持有时间**：尽快提交事务，避免在事务中执行耗时操作
3. **使用较低的隔离级别**：对于只读操作，使用 `ReadUncommitted` 减少锁竞争
4. **使用乐观锁**：对于高并发更新场景，使用版本号机制替代悲观锁

## 6. 完整示例

以下示例展示了事务的基本用法，包括普通事务、嵌套事务和事务隔离级别：

```csharp
// 创建数据库连接
var factory = SqliteAccessFactory.CreateSqliteFactory("Data Source=myapp05.db");
using var db = factory.Create();

// 1. 普通事务 - 转账操作
await using (var transaction = await db.BeginTransactionAsync())
{
    try
    {
        // 检查余额
        decimal balance = await db.ExecuteScalarAsync<decimal>(
            "SELECT Balance FROM Accounts WHERE Id = @Id",
            new { Id = fromId });

        if (balance < amount)
            throw new InvalidOperationException("余额不足");

        // 扣款
        await db.ExecuteNonQueryAsync(
            "UPDATE Accounts SET Balance = Balance - @Amount WHERE Id = @Id",
            new { Amount = amount, Id = fromId });

        // 入账
        await db.ExecuteNonQueryAsync(
            "UPDATE Accounts SET Balance = Balance + @Amount WHERE Id = @Id",
            new { Amount = amount, Id = toId });

        await transaction.CommitAsync();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}

// 2. 嵌套事务 - 内部使用保存点
await using (var transaction = await db.BeginTransactionAsync())  // 根事务
{
    // 创建订单
    await db.ExecuteNonQueryAsync("INSERT INTO Orders ...");
    
    // 处理订单项（内部嵌套事务）
    await using (var nested = await db.BeginTransactionAsync())  // 实际使用保存点
    {
        await db.ExecuteNonQueryAsync("INSERT INTO OrderItems ...");
        await nested.CommitAsync();  // 提交保存点
    }
    
    await transaction.CommitAsync();  // 提交根事务
}

// 3. 指定隔离级别（SQLite支持ReadCommitted和Serializable）
await using (var transaction = await db.BeginTransactionAsync(IsolationLevel.Serializable))
{
    await using (var data = await db.ExecuteReaderAsync("SELECT ..."))
    {
        // 在事务内多次读取，保证数据一致性
    }
    await transaction.CommitAsync();
}
```

**完整代码**：可运行的完整示例代码请参见 [examples/05_Transaction/Program.cs](./examples/05_Transaction/Program.cs)

**运行示例**：
```bash
cd examples/05_Transaction
dotnet run
```

## 下一步

- [使用连接作用域](06-ConnectionScope.md) - 管理连接生命周期的高级用法
