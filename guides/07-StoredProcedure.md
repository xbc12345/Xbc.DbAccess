# 使用存储过程

本文档介绍如何使用 Xbc.DbAccess 执行数据库存储过程。

## 目录

1. [存储过程概述](#1-存储过程概述)
   - [1.1 什么是存储过程](#11-什么是存储过程)
   - [1.2 存储过程的优点与缺点](#12-存储过程的优点与缺点)
2. [执行存储过程](#2-执行存储过程)
   - [2.1 非查询存储过程](#21-非查询存储过程)
   - [2.2 查询存储过程](#22-查询存储过程)
3. [使用参数](#3-使用参数)
   - [3.1 输入参数](#31-输入参数)
   - [3.2 输出参数](#32-输出参数)
   - [3.3 输入输出参数](#33-输入输出参数)
   - [3.4 返回值参数](#34-返回值参数)
4. [最佳实践](#4-最佳实践)
   - [4.1 存储过程命名规范](#41-存储过程命名规范)
   - [4.2 参数验证](#42-参数验证)
   - [4.3 异常处理](#43-异常处理)
5. [完整示例](#5-完整示例)

## 1. 存储过程概述

### 1.1 什么是存储过程

存储过程（Stored Procedure）是预编译并存储在数据库中的一组 SQL 语句。它们可以接受参数、执行复杂逻辑，并返回结果。

**存储过程的基本结构**：

```sql
CREATE PROCEDURE sp_GetUserById
    @UserId INT
AS
BEGIN
    SELECT * FROM Users WHERE Id = @UserId
END
```

### 1.2 存储过程的优点与缺点

**优点**：

| 优点       | 说明             |
| -------- | -------------- |
| **性能优化** | 预编译执行，减少网络传输   |
| **安全性**  | 通过参数化防止 SQL 注入 |
| **代码复用** | 业务逻辑封装在数据库中    |
| **维护性**  | 修改存储过程无需重新部署应用 |

**缺点**：

| 缺点        | 说明                   |
| --------- | -------------------- |
| **数据库依赖** | 不同数据库的存储过程语法差异大，迁移困难 |
| **调试困难**  | 存储过程中的错误难以调试和追踪      |
| **版本控制**  | 存储过程的版本管理与代码版本管理分离   |
| **扩展性**   | 复杂的业务逻辑在存储过程中难以维护和扩展 |
| **团队协作**  | 需要数据库权限，影响开发和部署流程    |

**适用场景建议**：

- ✅ **适合使用**：报表统计、数据清洗、复杂查询、需要高性能的简单操作
- ❌ **避免使用**：复杂的业务逻辑、频繁变更的需求、需要跨数据库兼容的场景

## 2. 执行存储过程

Xbc.DbAccess 仅提供异步方法，提供两种方式执行存储过程：

1. **原始 API 方式**：使用 `ExecuteNonQueryAsync`、`ExecuteDataSetAsync`、`ExecuteReaderAsync` 等方法，传入 `CommandType.StoredProcedure` 参数
2. **扩展方法方式**：使用 `ExecuteNonQueryProcedureAsync`、`ExecuteQueryProcedureAsync` 等扩展方法，返回 `ProcedureResult` 对象

### 2.1 非查询存储过程

执行插入、更新、删除等非查询操作。

**方式一：使用原始 API**

```csharp
// 创建参数 - 使用匿名对象（推荐）
var result = await db.ExecuteNonQueryProcedureAsync("sp_InsertUser", new
{
    UserName = "张三",
    Email = "zhangsan@example.com"
});

// 或者使用 DbParameterCollection
var parameters = db.NewParameters()
    .Add("UserName", "张三")
    .Add("Email", "zhangsan@example.com");

// 执行存储过程
int rowsAffected = await db.ExecuteNonQueryAsync("sp_InsertUser", CommandType.StoredProcedure, parameters);
Console.WriteLine($"影响行数: {rowsAffected}");
```

**方式二：使用扩展方法（推荐）**

```csharp
// 执行存储过程
ProcedureResult result = await db.ExecuteNonQueryProcedureAsync("sp_InsertUser", parameters);

// 获取受影响的行数
Console.WriteLine($"影响行数: {result.RowsAffected}");
```

### 2.2 查询存储过程

执行返回数据集的查询操作。

**方式一：使用原始 API**

```csharp
// 使用 DataSet
DataSet dataSet = await db.ExecuteDataSetAsync("sp_GetUsers", CommandType.StoredProcedure, parameters);
foreach (DataRow row in dataSet.Tables[0].Rows)
{
    Console.WriteLine($"用户: {row["UserName"]}");
}

// 使用 DataReader（适合大数据量）
await using (var reader = await db.ExecuteReaderAsync("sp_GetUsers", CommandType.StoredProcedure, parameters))
{
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"用户: {reader["UserName"]}");
    }
}
```

**方式二：使用扩展方法（推荐）**

```csharp
// 执行查询存储过程
ProcedureResult result = await db.ExecuteQueryProcedureAsync("sp_GetUserById", parameters);

// 获取数据集
DataTable userTable = result.DataSet.Tables[0];
foreach (DataRow row in userTable.Rows)
{
    Console.WriteLine($"用户: {row["UserName"]}");
}
```

**ProcedureResult 属性**：

| 属性                 | 类型                            | 说明                                             |
| ------------------ | ----------------------------- | ---------------------------------------------- |
| `RowsAffected`     | `int`                         | 受影响的行数（非查询存储过程使用）                              |
| `DataSet`          | `DataSet`                     | 数据集（查询存储过程使用）                                  |
| `OutputParameters` | `DbParameterCollection` | 输出参数集合（包含 Output、InputOutput 和 ReturnValue 参数） |

## 3. 使用参数

### 3.1 输入参数

输入参数用于向存储过程传递数据：

**方式一：使用匿名对象（推荐）**

```csharp
// 使用匿名对象传递输入参数
var result = await db.ExecuteNonQueryProcedureAsync("sp_InsertUser", new
{
    UserName = "张三",           // 字符串
    Age = 25,                    // 整数
    BirthDate = DateTime.Now,    // 日期
    IsActive = true              // 布尔值
});
```

**方式二：使用 DbParameterCollection**

```csharp
var parameters = db.NewParameters()
    .Add("UserName", "张三")           // 字符串
    .Add("Age", 25)                    // 整数
    .Add("BirthDate", DateTime.Now)    // 日期
    .Add("IsActive", true);            // 布尔值

var result = await db.ExecuteNonQueryProcedureAsync("sp_InsertUser", parameters);
```

### 3.2 输出参数

输出参数用于从存储过程获取数据：

```csharp
// 创建包含输出参数的参数集合
var parameters = db.NewParameters()
    .Add("UserName", "张三")
    .AddOutput("NewUserId", DbType.Int32)
    .AddOutput("CreatedTime", DbType.DateTime);

var result = await db.ExecuteNonQueryProcedureAsync("sp_InsertUser", parameters);

// 从 ProcedureResult.OutputParameters 中获取输出参数值（推荐）
int newUserId = result.OutputParameters.GetValue<int>("NewUserId");
bool success = result.OutputParameters.TryGetValue("CreatedTime", out DateTime createdTime);

// 或者使用 GetValue 获取 object 类型
object userIdObj = result.OutputParameters.GetValue("NewUserId");
```

`DbParameterCollection` 提供了以下方法获取输出参数值：

| 方法 | 签名 | 返回值 | 参数不存在时 | 适用场景 |
|------|------|--------|-------------|----------|
| `GetValue` | `GetValue(string name)` | `object` | 抛出异常 | 确定参数存在，不需要类型转换 |
| `GetValue<T>` | `GetValue<T>(string name)` | `T` | 抛出异常 | 确定参数存在，需要类型转换 |
| `TryGetValue` | `TryGetValue(string name, out object value)` | `bool` | 返回 `false` | 不确定参数是否存在，不需要类型转换 |
| `TryGetValue<T>` | `TryGetValue<T>(string name, out T value)` | `bool` | 返回 `false` | 不确定参数是否存在，需要类型转换 |

`GetValue<T>` 和 `TryGetValue<T>` 泛型方法内部都使用了 `DbValueConvert` 进行类型转换。

有关 `DbValueConvert` 的使用详见 [执行 SQL 的 5.3 节](04-ExecuteSql.md#53-dbvalueconvert)。

**存储过程定义**：

```sql
CREATE PROCEDURE sp_InsertUser
    @UserName NVARCHAR(100),
    @NewUserId INT OUTPUT,
    @CreatedTime DATETIME OUTPUT
AS
BEGIN
    INSERT INTO Users (UserName, CreatedTime)
    VALUES (@UserName, GETDATE())
    
    SET @NewUserId = SCOPE_IDENTITY()
    SET @CreatedTime = GETDATE()
END
```

### 3.3 输入输出参数

输入输出参数既可以传入值，也可以接收返回值：

```csharp
// 创建输入输出参数
var parameters = db.NewParameters()
    .Add("Counter", 10, DbType.Int32, ParameterDirection.InputOutput);

var result = await db.ExecuteNonQueryProcedureAsync("sp_IncrementCounter", parameters);

// 从 ProcedureResult.OutputParameters 中读取更新后的值
int newCounter = result.OutputParameters.GetValue<int>("Counter");
```

**存储过程定义**：

```sql
CREATE PROCEDURE sp_IncrementCounter
    @Counter INT OUTPUT
AS
BEGIN
    SET @Counter = @Counter + 1
END
```

### 3.4 返回值参数

存储过程可以通过 RETURN 语句返回一个整数值：

```csharp
// 创建包含返回值参数的参数
var parameters = db.NewParameters()
    .Add("UserName", "张三")
    .AddReturnValue("ReturnValue", DbType.Int32);

var result = await db.ExecuteNonQueryProcedureAsync("sp_InsertUser", parameters);

// 从 OutputParameters 中获取返回值
var returnValue = result.OutputParameters.GetValue("ReturnValue");
Console.WriteLine($"存储过程返回值: {returnValue}");
```

**存储过程定义**：

```sql
CREATE PROCEDURE sp_InsertUser
    @UserName NVARCHAR(100)
AS
BEGIN
    IF EXISTS (SELECT 1 FROM Users WHERE UserName = @UserName)
        RETURN -1  -- 用户已存在
    
    INSERT INTO Users (UserName) VALUES (@UserName)
    
    IF @@ROWCOUNT > 0
        RETURN 0   -- 成功
    ELSE
        RETURN -2  -- 插入失败
END
```

## 4. 最佳实践

### 4.1 存储过程命名规范

使用统一的命名规范提高可读性：

| 前缀          | 用途   | 示例                                 |
| ----------- | ---- | ---------------------------------- |
| `sp_Get`    | 查询数据 | `sp_GetUserById`, `sp_GetUsers`    |
| `sp_Insert` | 插入数据 | `sp_InsertUser`, `sp_InsertOrder`  |
| `sp_Update` | 更新数据 | `sp_UpdateUser`, `sp_UpdateStatus` |
| `sp_Delete` | 删除数据 | `sp_DeleteUser`, `sp_DeleteOrder`  |
| `sp_Calc`   | 计算操作 | `sp_CalcTotalAmount`               |

### 4.2 参数验证

始终验证输入参数，防止非法数据：

```csharp
public async Task InsertUser(string userName, string email)
{
    // 参数验证
    if (string.IsNullOrWhiteSpace(userName))
        throw new ArgumentException("用户名不能为空", nameof(userName));
    
    if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
        throw new ArgumentException("邮箱格式不正确", nameof(email));
    
    var result = await db.ExecuteNonQueryProcedureAsync("sp_InsertUser", new
    {
        UserName = userName,
        Email = email
    });
}
```

### 4.3 异常处理

使用 try-catch 块处理存储过程执行中的异常：

```csharp
try
{
    var result = await db.ExecuteNonQueryProcedureAsync("sp_TransferMoney", parameters);
    Console.WriteLine($"转账成功，影响行数: {result.RowsAffected}");
}
catch (DbException ex)
{
    // 处理数据库异常
    Console.WriteLine($"数据库错误: {ex.Message}");
}
catch (Exception ex)
{
    // 处理其他异常
    Console.WriteLine($"操作失败: {ex.Message}");
}
```

## 5. 完整示例

以下是一个完整的存储过程使用示例：

```csharp
using System;
using System.Data;
using Xbc.DbAccess;
using Xbc.DbAccess.SqlServer;

// 创建数据库访问对象
var factory = SqlAccessFactory.CreateSqlServerFactory("...");
using var db = factory.Create();

// 1. 执行非查询存储过程 - 使用匿名对象（推荐）
var insertResult = await db.ExecuteNonQueryProcedureAsync("sp_InsertUser", new
{
    UserName = "张三",
    Email = "zhangsan@example.com"
});
Console.WriteLine($"插入成功，影响行数: {insertResult.RowsAffected}");

// 2. 执行查询存储过程 - 使用匿名对象
var queryResult = await db.ExecuteQueryProcedureAsync("sp_GetUserById", new { UserId = 1 });
foreach (DataRow row in queryResult.DataSet.Tables[0].Rows)
{
    Console.WriteLine($"用户: {row["UserName"]}, 邮箱: {row["Email"]}");
}

// 3. 使用输出参数 - 需要使用 DbParameterCollection
var outputParams = db.NewParameters()
    .Add("UserName", "李四")
    .AddOutput("NewUserId", DbType.Int32);

var outputResult = await db.ExecuteNonQueryProcedureAsync("sp_InsertUser", outputParams);
int newId = outputResult.OutputParameters.GetValue<int>("NewUserId");
Console.WriteLine($"新用户ID: {newId}");
```

**完整代码**：可运行的完整示例代码请参见 [examples/07\_StoredProcedure/Program.cs](./examples/07_StoredProcedure/Program.cs)

**运行示例**：
```bash
cd examples/07_StoredProcedure
dotnet run
```

## 下一步
 
到目前为止，您已经完成了 `IDbAccess` 基础用法的学习，下一步将学习如何通过**实体映射功能**进行数据库操作：

- 如何配置实体映射关系
- 使用 `EntityMapper` 将 `DbDataReader` 映射为实体类
- 使用 `IEntityQuery` 将查询 SQL 语句映射为实体类，比直接使用 `EntityMapper` 更方便（类似 Dapper）
- 使用 `IEntitySet` 进行实体类的数据库操作，如插入、更新、删除等
- 使用 Lambda 表达式构建查询，实现更简洁、更面向对象的数据访问方式
- 适当减少手写 SQL，提高开发效率

敬请期待后续文档！
