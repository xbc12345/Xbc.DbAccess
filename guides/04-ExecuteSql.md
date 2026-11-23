# 使用 IDbAccess 执行 SQL

本文档介绍如何使用 IDbAccess 执行各种 SQL 操作。

## 目录

1. [使用参数化查询](#1-使用参数化查询)
   - [1.1 匿名对象（推荐）](#11-匿名对象推荐)
   - [1.2 字典类型](#12-字典类型)
   - [1.3 DbParameterCollection、数组和集合](#13-dbparametercollection数组和集合)
   - [1.4 集合展开（IN 子句）](#14-集合展开in-子句)
2. [使用 DbDataReader 执行查询](#2-使用-dbdatareader-执行查询)
3. [使用 DbDataAdapter 填充 DataSet](#3-使用-dbdataadapter-填充-dataset)
4. [执行非查询 SQL](#4-执行非查询-sql)
5. [执行标量查询](#5-执行标量查询)
   - [5.1 非泛型](#51-非泛型)
   - [5.2 泛型](#52-泛型)
   - [5.3 DbValueConvert](#53-dbvalueconvert)
6. [执行分页查询](#6-执行分页查询)
7. [最佳实践](#7-最佳实践)
   - [7.1 方法对比](#71-方法对比)
   - [7.2 使用参数化查询防止 SQL 注入](#72-使用参数化查询防止-sql-注入)
   - [7.3 始终使用 using 语句](#73-始终使用-using-语句)
   - [7.4 使用异步方法](#74-使用异步方法)
8. [完整示例](#8-完整示例)

## 1. 使用参数化查询

Xbc.DbAccess 支持多种方式传递 SQL 参数，所有方式都能有效防止 SQL 注入攻击。

**注意**：参数名称可以忽略前缀（如 `@`），框架会自动添加。但在 SQL 语句中需要指定完整参数名（如 `@Name`）。

### 1.1 匿名对象（推荐）

最简洁的参数传递方式，适合大多数场景。

```csharp
// 基本用法
var reader = await db.ExecuteReaderAsync(
    "SELECT * FROM Users WHERE Name = @Name AND Age = @Age",
    new { Name = "张三", Age = 25 });

// 插入数据
var count = await db.ExecuteNonQueryAsync(
    "INSERT INTO Users (Name, Email, Age) VALUES (@Name, @Email, @Age)",
    new { Name = "李四", Email = "lisi@example.com", Age = 30 });

// 更新数据
var count = await db.ExecuteNonQueryAsync(
    "UPDATE Users SET Age = @Age WHERE Id = @Id",
    new { Age = 26, Id = 1 });
```

**注意**：

- 匿名参数的属性名称必须与 SQL 语句中的参数名一致。

**优点**：

- 语法最简洁，可读性高
- 编译时类型检查
- 自动处理 null 值

### 1.2 字典类型

适合动态构建参数的场景。

```csharp
// 使用 Dictionary
var dict = new Dictionary<string, object>
{
    ["Name"] = "张三",
    ["Age"] = 25
};
var reader = await db.ExecuteReaderAsync(
    "SELECT * FROM Users WHERE Name = @Name AND Age = @Age",
    dict);

// 动态构建参数
var parameters = new Dictionary<string, object>();
parameters["Name"] = "张三";

var sql = "SELECT * FROM Users WHERE Name = @Name";
if (age.HasValue)
{
    sql += " AND Age = @Age";
    parameters["Age"] = age.Value;
}

var reader = await db.ExecuteReaderAsync(sql, parameters);
```

**适用场景**：

- 条件动态变化的查询
- 参数在运行时确定

### 1.3 DbParameterCollection、数组和集合

需要精确控制参数属性时使用，如指定数据库类型、参数方向等。支持以下类型直接传入：

- `DbParameterCollection` — 无需调用 `ToArray()`
- `DbParameter[]` — 参数数组
- `IEnumerable<DbParameter>` — 如 `List<DbParameter>` 等集合类型
- 单个 `DbParameter` — 自动包装为数组

**方式一：使用 DbParameterCollection（推荐）**

```csharp
// 创建参数集合
var parameters = db.NewParameters()
    .Add("Name", "张三")                                   // 简单参数
    .Add("Age", 25, DbType.Int32)                         // 指定数据库类型
    .Add("Email", "zhangsan@example.com", DbType.String)  // 指定数据库类型
    .AddOutput("NewId", DbType.Int32);                    // 输出参数

// 直接传入，无需调用 ToArray()
var reader = await db.ExecuteReaderAsync(
    "SELECT * FROM Users WHERE Name = @Name AND Age = @Age",
    parameters);

// 执行存储过程
var result = await db.ExecuteNonQueryProcedureAsync("sp_InsertUser", parameters);

// 获取输出参数值
int newId = result.OutputParameters.GetValue<int>("NewId");
```

**注意**：

- `DbParameterCollection` 中的参数名是不区分大小写的，重复添加参数会抛出异常。
- 保持 SQL 和代码中的参数名大小写一致，提高可读性并避免潜在的数据库兼容性问题。

**方式二：使用 DbParameter 数组**

```csharp
// 创建单个参数
var param1 = db.CreateParameter("Name", "张三");
var param2 = db.CreateParameter("Age", 25);
var param3 = db.CreateParameter("BirthDate", DateTime.Now, DbType.DateTime);

// 执行查询（多个参数以数组形式传入）
var reader = await db.ExecuteReaderAsync(
    "SELECT * FROM Users WHERE Name = @Name AND Age = @Age",
    new[] { param1, param2, param3 });

// 或使用数组
var parameters = new[]
{
    db.CreateParameter("Name", "张三"),
    db.CreateParameter("Age", 25),
    db.CreateParameter("BirthDate", DateTime.Now, DbType.DateTime)
};
var reader = await db.ExecuteReaderAsync(sql, parameters);
```

**方式三：使用集合（List 等）**

```csharp
// 使用 List<DbParameter>
var paramList = new List<DbParameter>
{
    db.CreateParameter("Name", "张三"),
    db.CreateParameter("Age", 25)
};
var reader = await db.ExecuteReaderAsync(
    "SELECT * FROM Users WHERE Name = @Name AND Age = @Age",
    paramList);

// 任何 IEnumerable<DbParameter> 类型均可直接传入
IEnumerable<DbParameter> parameters = GetParameters();
var reader = await db.ExecuteReaderAsync(sql, parameters);
```

**适用场景**：

- 存储过程的输出参数
- 需要指定 DbType
- 需要指定参数方向（Input/Output/InputOutput/ReturnValue）

### 1.4 集合展开（IN 子句）

当参数是集合类型（数组、List 等）时，框架会自动展开为多个参数，支持 IN 子句查询。

```csharp
// 集合参数会自动展开
var ids = new[] { 1, 2, 3, 4, 5 };
var reader = await db.ExecuteReaderAsync(
    "SELECT * FROM Users WHERE Id IN (@Ids)",
    new { Ids = ids });

// 实际执行的 SQL 会被替换为：
// SELECT * FROM Users WHERE Id IN (@Ids_0, @Ids_1, @Ids_2, @Ids_3, @Ids_4)

// 支持任何 IEnumerable 类型
var names = new List<string> { "张三", "李四", "王五" };
var reader = await db.ExecuteReaderAsync(
    "SELECT * FROM Users WHERE Name IN (@Names)",
    new { Names = names });
```

**注意**：

- 字符串不会被当作集合处理
- 集合参数名在 SQL 中需要加括号，如 `IN (@Ids)`
- 空集合会抛出 `ArgumentException`（`IN (NULL)` 不会匹配任何行，可能掩盖逻辑错误），请确保集合中至少包含一个元素

**三种方式的对比**：

| 特性        | 匿名对象  | 字典   | DbParameterCollection/数组/集合 |
| --------- | ----- | ---- | ------------------------ |
| 语法        | 最简洁   | 简洁   | 链式调用/较繁琐                 |
| 可读性       | 高     | 高    | 高                        |
| 动态参数      | 不支持   | 支持   | 支持                       |
| 指定 DbType | 不支持   | 不支持  | 支持                       |
| 输出参数      | 不支持   | 不支持  | 支持                       |
| 集合展开      | 支持    | 支持   | 支持                       |
| 适用场景      | 大多数场景 | 动态查询 | 存储过程、精确控制                |

## 2. 使用 DbDataReader 执行查询

使用 `ExecuteReaderAsync` 执行查询并返回 `DbDataReader`，适用于大数据量读取。

```csharp
// 基本用法 - 使用匿名对象
await using (var reader = await db.ExecuteReaderAsync(
    "SELECT * FROM Users WHERE Age > @Age",
    new { Age = 18 }))
{
    while (reader.Read())
    {
        Console.WriteLine(reader["Name"]);
    }
}

// 全异步读取
await using (var reader = await db.ExecuteReaderAsync(
    "SELECT * FROM Users WHERE Age > @Age",
    new { Age = 18 }))
{
    while (await reader.ReadAsync())
    {
        Console.WriteLine(reader["Name"]);
    }
}

// 多结果集
await using (var reader = await db.ExecuteReaderAsync(@"
    SELECT * FROM Users WHERE Age <= 25;
    SELECT * FROM Users WHERE Age > 25"))
{
    // 第一个结果集
    while (reader.Read())
    {
        Console.WriteLine($"年龄 <= 25: {reader["Name"]}");
    }

    // 移动到下一个结果集
    await reader.NextResultAsync();

    // 第二个结果集
    while (reader.Read())
    {
        Console.WriteLine($"年龄 > 25: {reader["Name"]}");
    }
}
```

**注意**：始终使用 `await using` 语句确保资源释放，避免连接泄漏。

## 3. 使用 DbDataAdapter 填充 DataSet

使用 `ExecuteDataSetAsync` 执行查询并填充 `DataSet`，适用于需要离线处理数据的场景。

```csharp
// 单表查询 - 使用匿名对象
var dataSet = await db.ExecuteDataSetAsync(
    "SELECT * FROM Users WHERE Age > @Age",
    new { Age = 18 });

foreach (var row in dataSet.Tables[0].Rows)
{
    Console.WriteLine($"ID: {row["Id"]}, Name: {row["Name"]}");
}

// 多表查询
var multiTableSet = await db.ExecuteDataSetAsync(@"
    SELECT * FROM Users WHERE Age <= 25;
    SELECT * FROM Users WHERE Age > 25");

Console.WriteLine($"表 1 行数: {multiTableSet.Tables[0].Rows.Count}");
Console.WriteLine($"表 2 行数: {multiTableSet.Tables[1].Rows.Count}");
```

## 4. 执行非查询 SQL

使用 `ExecuteNonQueryAsync` 执行 INSERT、UPDATE、DELETE 等非查询 SQL 语句，返回受影响的行数。

```csharp
// 插入数据 - 使用匿名对象
var insertCount = await db.ExecuteNonQueryAsync(
    "INSERT INTO Users (Name, Age) VALUES (@Name, @Age)",
    new { Name = "张三", Age = 25 });
Console.WriteLine($"插入 {insertCount} 行");

// 更新数据
var updateCount = await db.ExecuteNonQueryAsync(
    "UPDATE Users SET Age = @Age WHERE Id = @Id",
    new { Age = 30, Id = 1 });
Console.WriteLine($"更新 {updateCount} 行");

// 删除数据
var deleteCount = await db.ExecuteNonQueryAsync(
    "DELETE FROM Users WHERE Id = @Id",
    new { Id = 1 });
Console.WriteLine($"删除 {deleteCount} 行");
```

## 5. 执行标量查询

使用 `ExecuteScalarAsync` 执行标量查询，返回结果集中第一行第一列的值。

### 5.1 非泛型

返回 `object` 类型，查询结果为空时返回 `DBNull.Value`。

```csharp
// 返回 object 类型，需要手动转换
var count = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM Users");
int userCount = Convert.ToInt32(count);

// 查询结果为空时，返回 DBNull.Value
var age = await db.ExecuteScalarAsync("SELECT Age FROM Users WHERE Id = @Id",
    new { Id = 5 });
```

### 5.2 泛型

查询结果为空时的行为取决于 `T` 是否为可空类型：

- **值类型**（如 `int`）：抛出异常
- **可空值类型**（如 `int?`）：返回 `null`
- **引用类型**（如 `string`）：返回 `null`

查询结果为空字符串时的行为取决于 `T` 是否为可空类型：

- **值类型**（如 `int`）：抛出异常
- **可空值类型**（如 `int?`）：返回 `null`
- **引用类型**（如 `string`）：返回 `string.Empty`

```csharp
// 值类型 - 查询结果为空时抛出异常
int count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users");
int age = await db.ExecuteScalarAsync<int>("SELECT Age FROM Users WHERE Id = 1");

// 可空类型 - 查询结果为空时返回 null
int? age = await db.ExecuteScalarAsync<int?>("SELECT Age FROM Users WHERE Id = 1");
DateTime? createTime = await db.ExecuteScalarAsync<DateTime?>("SELECT CreateTime FROM Users WHERE Id = 1");
Guid? departmentId = await db.ExecuteScalarAsync<Guid?>("SELECT DepartmentId FROM Users WHERE Id = 1");

// 可空类型 - 查询结果为空字符串时返回 null
int? age = await db.ExecuteScalarAsync<int?>("SELECT Age FROM Users WHERE Id = 1");
DateTime? createTime = await db.ExecuteScalarAsync<DateTime?>("SELECT CreateTime FROM Users WHERE Id = 1");
Guid? departmentId = await db.ExecuteScalarAsync<Guid?>("SELECT DepartmentId FROM Users WHERE Id = 1");

// 引用类型 - 查询结果为空时返回 null
string name = await db.ExecuteScalarAsync<string>("SELECT Name FROM Users WHERE Id = 1");

// 引用类型 - 查询结果为空字符串时返回 string.Empty
string name = await db.ExecuteScalarAsync<string>("SELECT Name FROM Users WHERE Id = 1");
```

### 5.3 DbValueConvert

`ExecuteScalarAsync<T>` 内部使用 `DbValueConvert.ChangeType<T>` 进行类型转换，这是一个强化版的类型转换工具类，支持多种特殊场景的类型转换，而且还能够添加自定义的类型转换规则。

```csharp
// 基本类型转换
int age = DbValueConvert.ChangeType<int>(value);
double avg = DbValueConvert.ChangeType<double>(value);

// 可空类型转换 - null、DBNull、空字符串都转为 null
int? age = DbValueConvert.ChangeType<int?>(null);
int? age = DbValueConvert.ChangeType<int?>(DBNull.Value);
int? age = DbValueConvert.ChangeType<int?>("");

// 枚举转换 - 支持字符串（不区分大小写）和数字
public enum Status { Active, Inactive }
var status = DbValueConvert.ChangeType<Status>("active");
var status = DbValueConvert.ChangeType<Status>(1);

// 常用类型转换
DateTime date = DbValueConvert.ChangeType<DateTime>("2024-01-15");
Guid guid = DbValueConvert.ChangeType<Guid>("550e8400-e29b-41d4-a716-446655440000");

// 自定义类型解析器（例如将 JSON 字符串反序列化为对象）
DbValueConvert.AddParser(typeof(Address), value => JsonConvert.DeserializeObject<Address>(value.ToString()));
Address address = DbValueConvert.ChangeType<Address>(jsonString);

// 使用特定区域信息进行转换
var chineseCulture = new System.Globalization.CultureInfo("zh-CN");
decimal chineseDecimal = DbValueConvert.ChangeType<decimal>("1.234,56", chineseCulture);
```

## 6. 执行分页查询

使用分页扩展方法执行分页查询，自动构建分页 SQL。

#### `ExecuteReaderWithPaginationAsync`

```csharp
await using (var reader = await db.ExecuteReaderWithPaginationAsync(
    "SELECT * FROM Users WHERE Status = 'Active'",  // 基础查询 SQL，可以包含条件
    pageIndex: 1,                                   // 页码（从 1 开始）
    pageSize: 10,                                   // 每页记录数
    orderBy: "Id ASC"))                             // 排序条件，必须指定排序字段
{
    while (reader.Read())
    {
        Console.WriteLine($"ID: {reader["Id"]}, Name: {reader["Name"]}");
    }
}
```

#### `ExecuteDataSetWithPaginationAsync`

```csharp
var dataSet = await db.ExecuteDataSetWithPaginationAsync(
    "SELECT * FROM Users WHERE Status = 'Active'",
    pageIndex: 2,
    pageSize: 10,
    orderBy: "Id ASC");

foreach (DataRow row in dataSet.Tables[0].Rows)
{
    Console.WriteLine($"ID: {row["Id"]}, Name: {row["Name"]}");
}
```

#### 分页查询示例

```csharp
int pageIndex = 2;// 当前页码，获取第 2 页数据
int pageSize = 10;// 每页记录数
int totalCount = await db.ExecuteScalarAsync<int>(
    "SELECT COUNT(*) FROM Users WHERE Status = @Status",
    new { Status = Status.Active }); // 获取总记录数
int totalPages = (int)Math.Ceiling((double)totalCount / pageSize);// 总页数

// 注意 SQL 中的 WHERE 条件要和获取总记录数时的 WHERE 条件一致，否则会导致数据不一致
await using (var reader = await db.ExecuteReaderWithPaginationAsync(
    "SELECT * FROM Users WHERE Status = @Status",
    pageIndex,
    pageSize,
    "Id ASC",
    new { Status = Status.Active }))
{
    while (reader.Read())
    {
        Console.WriteLine($"ID: {reader["Id"]}, Name: {reader["Name"]}");
    }
}
```

## 7. 最佳实践

### 7.1 方法对比

| 方法                             | 返回值          | 适用场景                 | 内存占用 |
| ------------------------------ | ------------ | -------------------- | ---- |
| `ExecuteNonQueryAsync`         | int          | INSERT/UPDATE/DELETE | 低    |
| `ExecuteScalarAsync`           | object       | 返回单个值                | 低    |
| `ExecuteScalarAsync<T>`        | T            | 返回单个值（自动类型转换）        | 低    |
| `ExecuteReaderAsync`           | DbDataReader | 大数据量读取，流式处理          | 低    |
| `ExecuteDataSetAsync`          | DataSet      | 离线数据处理，多表结果          | 高    |
| `ExecuteReaderWithPaginationAsync`  | DbDataReader | 分页查询，流式读取            | 低    |
| `ExecuteDataSetWithPaginationAsync` | DataSet      | 分页查询，离线处理            | 中    |

**选择合适的查询方法：**

- **大数据量**：使用 `ExecuteReaderAsync` 避免内存溢出
- **小数据量/离线处理**：使用 `ExecuteDataSetAsync`
- **单个值（非 NULL 值）**：使用 `ExecuteScalarAsync`
- **单个值（NULL）**：使用 `ExecuteScalarAsync<T>`，其中 `T` 应为可空类型（如 `int?`、`DateTime?` 等）
- **分页数据**：使用 `ExecuteReaderWithPaginationAsync` 或 `ExecuteDataSetWithPaginationAsync`

### 7.2 使用参数化查询防止 SQL 注入

```csharp
// 安全的方式 - 使用匿名对象
var reader = await db.ExecuteReaderAsync(
    "SELECT * FROM Users WHERE Name = @Name",
    new { Name = userInput });

// 危险的方式（不要这样做），容易导致 SQL 注入攻击
// 例如：用户输入 "admin' OR '1' = '1" 会导致查询所有用户
var sql = $"SELECT * FROM Users WHERE Name = '{userInput}'";
var reader = await db.ExecuteReaderAsync(sql);
```

### 7.3 始终使用 using 语句

```csharp
// 好的做法
await using (var reader = await db.ExecuteReaderAsync("SELECT * FROM Users"))
{
    // 处理数据
}// 框架会自动释放资源

// 避免这样做
var reader = await db.ExecuteReaderAsync("SELECT * FROM Users");
// 可能忘记释放资源
```

### 7.4 使用异步方法

`IDbAccess` 仅提供异步方法，所有数据库调用都应使用 `await`，避免使用 `.Wait()`/`.Result` 阻塞等待。

```csharp
// 在 Web 应用中使用异步方法
public async Task<IActionResult> GetUserCount()
{
    var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users");
    return Ok( ... );
}

public async Task<IActionResult> GetUsers()
{
    await using (var reader = await db.ExecuteReaderAsync("SELECT * FROM Users"))
    {
        while (await reader.ReadAsync())
        {
            // 处理数据
        }
    }
    return Ok( ... );
}
```

## 8. 完整示例

以下示例展示了如何使用 IDbAccess 执行各种 SQL 操作：

```csharp
using Xbc.DbAccess;
using Xbc.DbAccess.Sqlite;
using Xbc.DbAccess.Common;

// 创建数据库连接
var factory = SqliteAccessFactory.CreateSqliteFactory("Data Source=myapp04.db");
using var db = factory.Create();

// 1. 创建参数 - 使用匿名对象（推荐）
var reader1 = await db.ExecuteReaderAsync(
    "SELECT * FROM Users WHERE Name = @Name AND Age > @Age",
    new { Name = "张三", Age = 25 });

// 1.1 或者使用 DbParameterCollection
var parameters = db.NewParameters()
    .Add("Name", "李四")
    .Add("Age", 30);

// 2. 执行查询 - DbDataReader（使用匿名对象）
await using (var reader = await db.ExecuteReaderAsync("SELECT * FROM Users WHERE Age > @Age",
    new { Age = 25 }))
{
    while (reader.Read())
    {
        Console.WriteLine($"ID: {reader["Id"]}, Name: {reader["Name"]}");
    }
}

// 3. 执行查询 - DataSet
var dataSet = await db.ExecuteDataSetAsync("SELECT * FROM Users");
var rowCount = dataSet.Tables[0].Rows.Count;

// 4. 执行非查询 SQL（使用匿名对象）
var insertCount = await db.ExecuteNonQueryAsync(
    "INSERT INTO Users (Name, Age) VALUES (@Name, @Age)",
    new { Name = "王五", Age = 28 });

// 5. 执行标量查询
var userCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users");
var userName = await db.ExecuteScalarAsync<string>("SELECT Name FROM Users WHERE Id = 1");
var createTime = await db.ExecuteScalarAsync<DateTime?>("SELECT CreateTime FROM Users WHERE Id = 1");

// 6. 分页查询（使用匿名对象）
await using (var reader = await db.ExecuteReaderWithPaginationAsync(
    "SELECT * FROM Users Where IsActive = @IsActive",
    pageIndex: 1,
    pageSize: 10,
    orderBy: "Id ASC",
    new { IsActive = 1 }))
{
    while (reader.Read())
    {
        Console.WriteLine($"ID: {reader["Id"]}, Name: {reader["Name"]}");
    }
}
```

**完整代码**：可运行的完整示例代码请参见 [examples/04\_ExecuteSql/Program.cs](./examples/04_ExecuteSql/Program.cs)

**运行示例**：

```bash
cd examples/04_ExecuteSql
dotnet run
```

## 下一步

- [使用事务](05-Transaction.md) - 在事务中执行 SQL

