# 使用连接作用域

本文档介绍 Xbc.DbAccess 的连接作用域功能，用于在多个数据库操作之间共享同一个连接。

## 目录

1. [为什么使用连接作用域](#1-为什么使用连接作用域)
2. [单个连接作用域](#2-单个连接作用域)
   - [2.1 基本用法](#21-基本用法)
   - [2.2 实际应用场景](#22-实际应用场景)
3. [嵌套连接作用域](#3-嵌套连接作用域)
   - [3.1 基本用法](#31-基本用法)
   - [3.2 嵌套作用域的工作原理](#32-嵌套作用域的工作原理)
   - [3.3 实际应用场景](#33-实际应用场景)
4. [连接作用域与事务](#4-连接作用域与事务)
   - [4.1 先开启连接作用域再开启事务](#41-先开启连接作用域再开启事务)
   - [4.2 先开启事务再开启连接作用域](#42-先开启事务再开启连接作用域)
5. [最佳实践](#5-最佳实践)
   - [5.1 使用 using 语句](#51-使用-using-语句)
   - [5.2 合理控制作用域范围](#52-合理控制作用域范围)
   - [5.3 在多个方法调用中复用连接](#53-在多个方法调用中复用连接)
   - [5.4 与事务配合使用](#54-与事务配合使用)
6. [完整示例](#6-完整示例)

## 1. 为什么使用连接作用域

连接作用域允许在多个数据库操作之间共享同一个数据库连接，避免频繁打开和关闭连接的开销。

### 不使用连接作用域的问题

```csharp
// 每个操作都使用独立的连接
var result1 = await db.ExecuteScalarAsync("SELECT 1");  // 打开连接 -> 执行 -> 关闭连接
var result2 = await db.ExecuteScalarAsync("SELECT 2");  // 打开连接 -> 执行 -> 关闭连接
var result3 = await db.ExecuteScalarAsync("SELECT 3");  // 打开连接 -> 执行 -> 关闭连接
```

### 使用连接作用域的优势

```csharp
// 在同一个作用域内共享连接
using (var scope = await db.BeginConnectionScopeAsync())  // 打开连接
{
    var result1 = await db.ExecuteScalarAsync("SELECT 1");  // 使用已有连接
    var result2 = await db.ExecuteScalarAsync("SELECT 2");  // 使用已有连接
    var result3 = await db.ExecuteScalarAsync("SELECT 3");  // 使用已有连接

    // 作用域结束时自动释放连接
}
```

## 2. 单个连接作用域

Xbc.DbAccess 仅提供异步方法，开启连接作用域统一使用 `BeginConnectionScopeAsync`。

### 2.1 基本用法

```csharp
// 开启连接作用域
using (var scope = await db.BeginConnectionScopeAsync())
{
    // 在作用域内执行多个操作
    await db.ExecuteNonQueryAsync("CREATE TABLE Test (Id INTEGER)");
    await db.ExecuteNonQueryAsync("INSERT INTO Test VALUES (1)");
    await db.ExecuteNonQueryAsync("INSERT INTO Test VALUES (2)");

    var count = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM Test");
    Console.WriteLine($"记录数: {count}");

    // 作用域结束时自动释放连接
}
```

### 2.2 实际应用场景

**场景1：使用临时表时保持连接**

在使用数据库临时表时，必须在同一个连接中创建和使用临时表，连接作用域可以确保这一点：

```csharp
public async Task AnalyzeDataWithTempTable()
{
    using (var scope = await _dbAccess.BeginConnectionScopeAsync())
    {
        // 1. 创建临时表（在当前连接中）
        await _dbAccess.ExecuteNonQueryAsync(
            "CREATE TEMPORARY TABLE TempHighValueCustomers AS ...");

        // 2. 在临时表上执行多个查询（复用同一个连接）
        var topCustomers = await GetTopCustomers();
        var customerDetails = await GetCustomerDetails(topCustomers);

        // 3. 生成分析报告
        GenerateReport(customerDetails);

        // 临时表会在连接关闭时自动删除

        // 如果使用了连接池，连接不会被关闭，而是被放回连接池
        // 这时候需要手动删除临时表
        // 非连接池也推荐这么做，以确保一致的清理行为
        await _dbAccess.ExecuteNonQueryAsync(
            "DROP TEMPORARY TABLE TempHighValueCustomers ...");
    }
}

private async Task<List<int>> GetTopCustomers()
{
    // 复用外部连接作用域，可以访问临时表
    var customers = new List<int>();
    await using (var reader = await _dbAccess.ExecuteReaderAsync(
        "SELECT CustomerId FROM TempHighValueCustomers ..."))
    {
        while (await reader.ReadAsync())
        {
            customers.Add(reader.GetInt32(0));
        }
    }
    return customers;
}

private async Task<List<CustomerDetail>> GetCustomerDetails(List<int> customerIds)
{
    // 复用外部连接作用域，可以访问临时表
    var details = new List<CustomerDetail>();
    foreach (var id in customerIds)
    {
        await using (var reader = await _dbAccess.ExecuteReaderAsync(
            "SELECT c.*, t.TotalAmount FROM Customers c ..."))
        {
            if (await reader.ReadAsync())
            {
                details.Add(new CustomerDetail { ... });
            }
        }
    }
    return details;
}
```

**场景2：批量数据导入**

在数据导入场景中，需要执行大量独立的插入操作，复用连接可以显著提升性能：

```csharp
public async Task ImportUsers(List<User> users)
{
    using (var scope = await _dbAccess.BeginConnectionScopeAsync())
    {
        foreach (var user in users)
        {
            try
            {
                // 插入用户数据
                await _dbAccess.ExecuteNonQueryAsync(
                    "INSERT INTO Users (Name, Email) VALUES (@Name, @Email)",
                    new { user.Name, user.Email });
            }
            catch (Exception ex)
            {
                // 记录失败但不影响其他用户导入
                Console.WriteLine($"导入用户 {user.Name} 失败: {ex.Message}");
            }
        }

        // 导入完成后记录日志
        await _dbAccess.ExecuteNonQueryAsync(
            "INSERT INTO ImportLogs (Count, Time) VALUES (@Count, @Time)",
            new { Count = users.Count, Time = DateTime.Now });
    }
}
```

## 3. 嵌套连接作用域

在已有连接作用域内再次调用 `BeginConnectionScopeAsync()`，会创建嵌套作用域，内部作用域复用外部作用域的连接，不会创建新的连接。

### 3.1 基本用法

```csharp
using (var outerScope = await db.BeginConnectionScopeAsync())
{
    await db.ExecuteNonQueryAsync("INSERT INTO Test VALUES (1)");

    // 内部作用域 - 复用外部作用域的连接
    using (var innerScope = await db.BeginConnectionScopeAsync())
    {
        await db.ExecuteNonQueryAsync("INSERT INTO Test VALUES (2)");
    }

    await db.ExecuteNonQueryAsync("INSERT INTO Test VALUES (3)");
}
```

**提示**：框架内部也会使用嵌套作用域，例如导航属性的分裂查询会在加载导航属性时自动创建嵌套作用域，外部无需关心这些细节。

### 3.2 嵌套作用域的工作原理

```csharp
// 外部作用域
using (var outerScope = await db.BeginConnectionScopeAsync())
{
    Console.WriteLine($"外部作用域 ID: {outerScope.ScopeId}");

    await db.ExecuteNonQueryAsync("CREATE TABLE Test (Id INTEGER)");
    await db.ExecuteNonQueryAsync("INSERT INTO Test VALUES (1)");

    // 内部作用域 - 复用外部作用域的连接
    using (var innerScope = await db.BeginConnectionScopeAsync())
    {
        Console.WriteLine($"内部作用域 ID: {innerScope.ScopeId}");

        // 这些操作使用同一个连接
        await db.ExecuteNonQueryAsync("INSERT INTO Test VALUES (2)");
        var count = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM Test");
        Console.WriteLine($"记录数: {count}");

        // 内部作用域结束时不关闭连接（因为外部作用域还在使用）
    }

    // 继续在外部作用域中操作
    await db.ExecuteNonQueryAsync("INSERT INTO Test VALUES (3)");

    // 外部作用域结束时才真正关闭连接
}
```

### 3.3 实际应用场景

**场景1：分层架构中的连接复用**

在 Repository 模式中，每个 Repository 方法都有自己的连接作用域，但在 Service 层可以统一管理连接：

```csharp
public class UserService
{
    private readonly IUserRepository _userRepo;
    private readonly ILogRepository _logRepo;
    private readonly IDbAccess _dbAccess;

    public async Task UpdateUserWithLog(User user)
    {
        // Service 层统一管理连接
        using (var scope = await _dbAccess.BeginConnectionScopeAsync())
        {
            // Repository 方法各自有作用域，但会复用 Service 层的连接
            await _userRepo.Update(user);
            await _logRepo.AddLog($"用户 {user.Name} 被更新");
        }
    }
}
```

**场景2：数据导入时复用连接（嵌套方法调用）**

在数据导入场景中，需要执行大量独立的插入操作，不需要事务保证（部分失败不影响整体），但复用连接可以显著提升性能：

```csharp
public async Task ImportUsers(List<User> users)
{
    // 外部作用域 - 整个导入过程复用同一个连接
    using (var scope = await _dbAccess.BeginConnectionScopeAsync())
    {
        foreach (var user in users)
        {
            // 每个用户导入使用内部作用域，复用外部连接
            await ImportSingleUser(user);
        }

        // 导入完成后记录日志
        await LogImportResult(users.Count);
    }
}

private async Task ImportSingleUser(User user)
{
    // 内部作用域 - 复用外部连接
    using (var scope = await _dbAccess.BeginConnectionScopeAsync())
    {
        try
        {
            // 插入用户数据
            await _dbAccess.ExecuteNonQueryAsync(...);
        }
        catch (Exception ex)
        {
            // 记录失败但不影响其他用户导入
            Console.WriteLine($"导入用户 {user.Name} 失败: {ex.Message}");
        }
    }
}

private async Task LogImportResult(int count)
{
    // 内部作用域 - 复用外部连接
    using (var scope = await _dbAccess.BeginConnectionScopeAsync())
    {
        await _dbAccess.ExecuteNonQueryAsync(...);
    }
}
```

## 4. 连接作用域与事务

连接作用域和事务可以相互嵌套，无论哪种顺序都能正确复用连接。

### 4.1 先开启连接作用域再开启事务

**适用场景**：需要在事务前后执行一些非事务操作，如记录日志、审计等。

```csharp
// 先开启连接作用域
using (var scope = await db.BeginConnectionScopeAsync())
{
    // 在连接作用域内执行非事务操作（如记录操作日志）
    await db.ExecuteNonQueryAsync("INSERT INTO Logs (Message) VALUES ('开始处理')");

    // 开启事务（复用连接作用域的连接）
    await using (var transaction = await db.BeginTransactionAsync())
    {
        try
        {
            await db.ExecuteNonQueryAsync("INSERT INTO Orders ...");
            await db.ExecuteNonQueryAsync("UPDATE Inventory ...");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // 事务结束后继续使用连接作用域（如记录完成日志）
    await db.ExecuteNonQueryAsync("INSERT INTO Logs (Message) VALUES ('处理完成')");
}
```

### 4.2 先开启事务再开启连接作用域

**适用场景**：在事务中调用其他方法，而这些方法内部开启了连接作用域。

**重要前提**：以下示例假设 `IDbAccess` 注册为 **Scoped** 生命周期（每个请求共享同一个实例）。如果 `IDbAccess` 是 Transient 生命周期，每个服务会获得不同的实例，无法复用连接。

```csharp
// Program.cs 中注册为 Scoped 生命周期
builder.Services.AddDbAccess(options =>
{
    options.ConnectionString = "...";
}, SqliteAccessFactory.CreateSqliteFactory);
builder.Services.AddScoped<IDbAccess>(sp => 
    sp.GetRequiredService<IDbAccessFactory>().Create());

// OrderService 和 InventoryService 都注入同一个 IDbAccess 实例
public class OrderService
{
    private readonly IDbAccess _dbAccess;
    private readonly InventoryService _inventoryService;

    public OrderService(IDbAccess dbAccess, InventoryService inventoryService)
    {
        _dbAccess = dbAccess;
        _inventoryService = inventoryService;
    }

    public async Task CreateOrder(Order order)
    {
        // 先开启事务
        await using (var transaction = await _dbAccess.BeginTransactionAsync())
        {
            // 在主逻辑中执行数据库操作
            await _dbAccess.ExecuteNonQueryAsync(
                "INSERT INTO Orders ...",
                _dbAccess.CreateParameter("...", ...));

            // 调用其他服务方法，该方法内部会开启连接作用域
            // 由于使用同一个 IDbAccess 实例，内部作用域会复用当前事务的连接
            await _inventoryService.ReserveStock(order.Items);

            // 调用另一个服务方法
            await _inventoryService.UpdateInventoryLog(order.Id);

            await transaction.CommitAsync();
        }
    }
}

public class InventoryService
{
    private readonly IDbAccess _dbAccess;

    // 注入同一个 IDbAccess 实例（Scoped 生命周期）
    public InventoryService(IDbAccess dbAccess)
    {
        _dbAccess = dbAccess;
    }

    public async Task ReserveStock(List<OrderItem> items)
    {
        // 该方法内部开启连接作用域
        // 如果外部有事务，会复用事务的连接
        using (var scope = await _dbAccess.BeginConnectionScopeAsync())
        {
            foreach (var item in items)
            {
                await _dbAccess.ExecuteNonQueryAsync(
                    "UPDATE Inventory SET Reserved = Reserved + @Qty WHERE ProductId = @ProductId",
                    new { Qty = item.Quantity, ProductId = item.ProductId });
            }
        }
    }

    public async Task UpdateInventoryLog(int orderId)
    {
        // 另一个内部开启连接作用域的方法
        using (var scope = await _dbAccess.BeginConnectionScopeAsync())
        {
            await _dbAccess.ExecuteNonQueryAsync(
                "INSERT INTO InventoryLog (OrderId, Action) VALUES (@OrderId, 'Reserved')",
                new { OrderId = orderId });
        }
    }
}
```

## 5. 最佳实践

### 5.1 使用 using 语句

```csharp
// 好的做法
using (var scope = await db.BeginConnectionScopeAsync())
{
    // 操作
}

// 避免这样做
var scope = await db.BeginConnectionScopeAsync();
// 操作...
// 可能忘记释放，导致连接泄漏
```

### 5.2 合理控制作用域范围

```csharp
// 好的做法 - 作用域包含相关操作
using (var scope = await db.BeginConnectionScopeAsync())
{
    // 一组相关的数据库操作
    await db.ExecuteNonQueryAsync("INSERT INTO ...");
    await db.ExecuteNonQueryAsync("UPDATE ...");
    var result = await db.ExecuteScalarAsync("SELECT ...");
}

// 避免作用域过大
using (var scope = await db.BeginConnectionScopeAsync())  // 不要这样做
{
    // 长时间操作
    Thread.Sleep(60000);  // 阻塞 1 分钟

    await db.ExecuteNonQueryAsync("INSERT INTO ...");
}
```

### 5.3 在多个方法调用中复用连接

**适用场景**：一个业务流程需要调用多个独立的方法，而这些方法是独立的业务逻辑，每个方法内部都开启了连接作用域，希望它们复用同一个连接提高性能。

**示例：生成月度销售报表**

```csharp
public async Task<MonthlyReport> GenerateMonthlyReport(int year, int month)
{
    // 外部作用域 - 让整个报表生成过程复用同一个连接
    using (var scope = await _dbAccess.BeginConnectionScopeAsync())
    {
        // 调用多个 Repository 方法，它们内部会复用外部连接
        var customerStats = await _customerRepo.GetCustomerStats(year, month);
        var orderStats = await _orderRepo.GetOrderStats(year, month);
        var topProducts = await _productRepo.GetTopSellingProducts(year, month);

        // 组装并返回报表
        return new MonthlyReport { ... };
    }
}

// Repository 方法内部开启连接作用域
public async Task<CustomerStats> GetCustomerStats(int year, int month)
{
    using (var scope = await _dbAccess.BeginConnectionScopeAsync())
    {
        // 如果外部有连接作用域，会复用外部连接
        var newCustomers = await _dbAccess.ExecuteScalarAsync<int>(...);
        var activeCustomers = await _dbAccess.ExecuteScalarAsync<int>(...);
        return new CustomerStats { ... };
    }
}
```

### 5.4 与事务配合使用

**基本原则**：

- 如果只需要事务，不需要显式使用连接作用域，事务会自动管理连接
- 如果需要在事务前后执行非事务操作，先开启连接作用域，再开启事务
- 如果调用了多个方法的内部开启了连接作用域，外部可以直接开启连接作用域或事务（取决于是否需要事务管理）

```csharp
// 场景1：只需要事务
await using (var transaction = await db.BeginTransactionAsync())
{
    // 事务会自动管理连接
    await db.ExecuteNonQueryAsync("...");
    await transaction.CommitAsync();
}

// 场景2：事务前后需要非事务操作
using (var scope = await db.BeginConnectionScopeAsync())
{
    // 非事务操作（如记录日志）
    await db.ExecuteNonQueryAsync("INSERT INTO Logs ...");

    await using (var transaction = await db.BeginTransactionAsync())
    {
        // 事务操作
        await db.ExecuteNonQueryAsync("INSERT INTO Orders ...");
        await transaction.CommitAsync();
    }

    // 非事务操作
    await db.ExecuteNonQueryAsync("INSERT INTO Logs ...");
}

// 场景3：调用内部有连接作用域的方法
await db.ExecuteNonQueryAsync("INSERT INTO Logs ...");  // 非事务操作

await using (var transaction = await db.BeginTransactionAsync())
{
    // 调用其他方法，这些方法内部可能开启连接作用域
    await _orderService.CreateOrder(order);  // 内部使用连接作用域
    await _inventoryService.UpdateStock(items);  // 内部使用连接作用域

    await transaction.CommitAsync();
}
```

**避免过度嵌套**：

虽然连接作用域和事务可以相互嵌套，但应避免过度复杂的嵌套结构，以免造成复杂的管理逻辑和代码混乱：

```csharp
// 不推荐：过度复杂的嵌套
using (var scope1 = await db.BeginConnectionScopeAsync())
{
    await using (var transaction = await db.BeginTransactionAsync())
    {
        using (var scope2 = await db.BeginConnectionScopeAsync())  // 多余的内层作用域
        {
            // 操作...
        }
        await transaction.CommitAsync();
    }
}
```

## 6. 完整示例

以下示例展示了连接作用域的基本用法，包括基本作用域、嵌套作用域以及与事务的配合：

```csharp
// 创建支持嵌套连接作用域的工厂
var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
{
    options.ConnectionString = "Data Source=myapp06.db";
});

using var db = factory.Create();

// 1. 基本连接作用域
using (var scope = await db.BeginConnectionScopeAsync())
{
    // 在作用域内执行多个操作，复用同一个连接
    await db.ExecuteNonQueryAsync("INSERT INTO TestData VALUES (1, 'A', 100)");
    await db.ExecuteNonQueryAsync("INSERT INTO TestData VALUES (2, 'B', 200)");

    var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TestData");
}

// 2. 嵌套连接作用域 - 内部复用外部连接
using (var outerScope = await db.BeginConnectionScopeAsync())
{
    await db.ExecuteNonQueryAsync("INSERT INTO TestData VALUES (10, 'X', 1000)");

    // 内部作用域自动复用外部连接
    using (var innerScope = await db.BeginConnectionScopeAsync())
    {
        await db.ExecuteNonQueryAsync("INSERT INTO TestData VALUES (20, 'Y', 2000)");
    }

    await db.ExecuteNonQueryAsync("INSERT INTO TestData VALUES (30, 'Z', 3000)");
}

// 3. 连接作用域与事务配合使用
using (var scope = await db.BeginConnectionScopeAsync())
{
    await db.ExecuteNonQueryAsync("INSERT INTO TestData VALUES (100, 'Main', 10)");

    await using (var transaction = await db.BeginTransactionAsync())
    {
        await db.ExecuteNonQueryAsync(
            "INSERT INTO TestData VALUES (@Id, @Name, @Value)",
            new { Id = 101, Name = "Sub1", Value = 20 });

        await db.ExecuteNonQueryAsync(
            "INSERT INTO TestData VALUES (@Id, @Name, @Value)",
            new { Id = 102, Name = "Sub2", Value = 30 });

        await transaction.CommitAsync();
    }
}
```

**完整代码**：可运行的完整示例代码请参见 [examples/06_ConnectionScope/Program.cs](./examples/06_ConnectionScope/Program.cs)

**运行示例**：
```bash
cd examples/06_ConnectionScope
dotnet run
```

## 下一步

- [使用存储过程](07-StoredProcedure.md) - 深入了解存储过程的使用方法
