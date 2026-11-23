using System.Data;
using Xbc.DbAccess;
using Xbc.DbAccess.Sqlite;

namespace Examples
{
    /// <summary>
    /// 示例 05: 使用事务
    /// 演示事务的基本用法、嵌套事务和事务隔离级别
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== 示例 05: 使用事务 ===\n");

            var dbFileName = $"myapp05_{Guid.NewGuid()}.db";
            try
            {
                var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    options.ConnectionString = $"Data Source={dbFileName}";
                });
                using var db = factory.Create();

                // 创建测试表
                await CreateTestTables(db);

                // 1. 普通事务
                await DemoBasicTransaction(db);

                // 2. 嵌套事务
                await DemoNestedTransaction(db);

                // 3. 事务隔离级别
                await DemoIsolationLevel(db);

                Console.WriteLine("示例运行完成！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n运行出错: {ex}");
            }
            finally
            {
                try { File.Delete(dbFileName); } catch { }
            }

            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }

        /// <summary>
        /// 创建测试表
        /// </summary>
        private static async Task CreateTestTables(IDbAccess db)
        {
            await db.ExecuteNonQueryAsync(@"
                CREATE TABLE Accounts (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Balance DECIMAL(18,2) NOT NULL DEFAULT 0
                )");

            await db.ExecuteNonQueryAsync(@"
                CREATE TABLE Orders (
                    Id INTEGER PRIMARY KEY,
                    CustomerId INTEGER,
                    TotalAmount DECIMAL(18,2),
                    Status TEXT
                )");

            await db.ExecuteNonQueryAsync(@"
                CREATE TABLE OrderItems (
                    Id INTEGER PRIMARY KEY,
                    OrderId INTEGER,
                    ProductId INTEGER,
                    Quantity INTEGER
                )");

            // 插入测试数据
            await db.ExecuteNonQueryAsync("INSERT INTO Accounts (Id, Name, Balance) VALUES (1, '账户A', 1000)");
            await db.ExecuteNonQueryAsync("INSERT INTO Accounts (Id, Name, Balance) VALUES (2, '账户B', 500)");

            Console.WriteLine("测试表创建完成\n");
        }

        /// <summary>
        /// 1. 普通事务
        /// </summary>
        private static async Task DemoBasicTransaction(IDbAccess db)
        {
            Console.WriteLine("【1. 普通事务】\n");

            // 显示初始余额
            Console.WriteLine("1.1 初始余额:");
            await ShowBalances(db);

            // 执行转账
            Console.WriteLine("\n1.2 执行转账（使用事务）:");
            await Transfer(db, 1, 2, 200);

            // 显示转账后余额
            Console.WriteLine("\n1.3 转账后余额:");
            await ShowBalances(db);

            // 演示回滚
            Console.WriteLine("\n1.4 演示回滚（余额不足）:");
            try
            {
                await Transfer(db, 2, 1, 1000);  // 账户B只有700，不够1000
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"   预期异常: {ex.Message}");
            }

            Console.WriteLine("\n1.5 回滚后余额（未改变）:");
            await ShowBalances(db);
            Console.WriteLine();
        }

        /// <summary>
        /// 转账操作
        /// </summary>
        private static async Task Transfer(IDbAccess db, int fromId, int toId, decimal amount)
        {
            await using (var transaction = await db.BeginTransactionAsync())
            {
                try
                {
                    // 检查余额
                    decimal balance = await db.ExecuteScalarAsync<decimal>(
                        "SELECT Balance FROM Accounts WHERE Id = @Id",
                        new { Id = fromId });

                    if (balance < amount)
                    {
                        throw new InvalidOperationException("余额不足");
                    }

                    // 扣款
                    await db.ExecuteNonQueryAsync(
                        "UPDATE Accounts SET Balance = Balance - @Amount WHERE Id = @Id",
                        new { Amount = amount, Id = fromId });

                    // 入账
                    await db.ExecuteNonQueryAsync(
                        "UPDATE Accounts SET Balance = Balance + @Amount WHERE Id = @Id",
                        new { Amount = amount, Id = toId });

                    await transaction.CommitAsync();
                    Console.WriteLine($"   转账成功: {fromId} -> {toId}, 金额: {amount}");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Console.WriteLine($"   转账失败: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// 显示余额
        /// </summary>
        private static async Task ShowBalances(IDbAccess db)
        {
            await using (var reader = await db.ExecuteReaderAsync("SELECT * FROM Accounts"))
            {
                while (await reader.ReadAsync())
                {
                    Console.WriteLine($"   {reader["Name"]}: ￥{reader["Balance"]}");
                }
            }
        }

        /// <summary>
        /// 2. 嵌套事务
        /// </summary>
        private static async Task DemoNestedTransaction(IDbAccess db)
        {
            Console.WriteLine("【2. 嵌套事务】\n");

            // 清理数据
            await db.ExecuteNonQueryAsync("DELETE FROM Orders");
            await db.ExecuteNonQueryAsync("DELETE FROM OrderItems");

            await using (var transaction = await db.BeginTransactionAsync())
            {
                try
                {
                    // 1. 创建订单（根事务）
                    await db.ExecuteNonQueryAsync(
                        "INSERT INTO Orders (Id, CustomerId, TotalAmount, Status) VALUES (@Id, @CustomerId, @Amount, @Status)",
                        new { Id = 1, CustomerId = 100, Amount = 500, Status = "Processing" });
                    Console.WriteLine("   创建订单成功");

                    // 2. 处理订单项1（嵌套事务）
                    await ProcessOrderItem(db, 1, 101, 2);
                    Console.WriteLine("   处理订单项1成功");

                    // 3. 处理订单项2（嵌套事务）- 模拟失败
                    try
                    {
                        await ProcessOrderItemWithError(db, 1, 102, 5);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   订单项2处理失败（已回滚）: {ex.Message}");
                    }

                    // 4. 处理订单项3（嵌套事务）
                    await ProcessOrderItem(db, 1, 103, 1);
                    Console.WriteLine("   处理订单项3成功");

                    await transaction.CommitAsync();
                    Console.WriteLine("   根事务提交成功");
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }

            // 显示结果
            var orderCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Orders");
            var itemCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM OrderItems");
            Console.WriteLine($"\n   订单数: {orderCount}, 订单项数: {itemCount}（订单项2的回滚不影响其他）\n");
        }

        /// <summary>
        /// 处理订单项（嵌套事务）
        /// </summary>
        private static async Task ProcessOrderItem(IDbAccess db, int orderId, int productId, int quantity)
        {
            await using (var nestedTransaction = await db.BeginTransactionAsync())
            {
                try
                {
                    await db.ExecuteNonQueryAsync(
                        "INSERT INTO OrderItems (OrderId, ProductId, Quantity) VALUES (@OrderId, @ProductId, @Quantity)",
                        new { OrderId = orderId, ProductId = productId, Quantity = quantity });

                    await nestedTransaction.CommitAsync();
                }
                catch
                {
                    await nestedTransaction.RollbackAsync();
                    throw;
                }
            }
        }

        /// <summary>
        /// 处理订单项（模拟错误）
        /// </summary>
        private static async Task ProcessOrderItemWithError(IDbAccess db, int orderId, int productId, int quantity)
        {
            await using (var nestedTransaction = await db.BeginTransactionAsync())
            {
                try
                {
                    await db.ExecuteNonQueryAsync(
                        "INSERT INTO OrderItems (OrderId, ProductId, Quantity) VALUES (@OrderId, @ProductId, @Quantity)",
                        new { OrderId = orderId, ProductId = productId, Quantity = quantity });

                    // 模拟业务错误
                    throw new InvalidOperationException("库存不足");
                }
                catch
                {
                    await nestedTransaction.RollbackAsync();
                    throw;
                }
            }
        }

        /// <summary>
        /// 3. 事务隔离级别
        /// </summary>
        private static async Task DemoIsolationLevel(IDbAccess db)
        {
            Console.WriteLine("【3. 事务隔离级别】\n");

            // SQLite 支持的隔离级别: ReadCommitted 和 Serializable

            // 3.1 ReadCommitted (默认)
            Console.WriteLine("3.1 ReadCommitted (默认)");
            await using (var transaction = await db.BeginTransactionAsync(IsolationLevel.ReadCommitted))
            {
                var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Accounts");
                Console.WriteLine($"   查询结果: {count} 条记录");
                await transaction.CommitAsync();
            }

            // 3.2 Serializable
            Console.WriteLine("\n3.2 Serializable");
            await using (var transaction = await db.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Accounts");
                Console.WriteLine($"   查询结果: {count} 条记录");
                await transaction.CommitAsync();
            }

            Console.WriteLine();
        }
    }
}
