using System;
using System.Collections.Generic;
using System.Text.Json;
using Xbc.DbAccess;
using Xbc.DbAccess.Entity;
using Xbc.DbAccess.Sqlite;

namespace EntityDefinitionExample
{
    /// <summary>
    /// 08 实体类定义示例
    /// 演示如何定义实体类以支持 ORM 映射
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== 08 实体类定义示例 ===\n");

            var dbFileName = $"myapp08_{Guid.NewGuid()}.db";
            try
            {
                var factory = SqliteAccessFactory.CreateSqliteFactory(options =>
                {
                    options.ConnectionString = $"Data Source={dbFileName}";
                });

                using var db = factory.Create();

                // 初始化数据库表
                await InitializeDatabaseAsync(db);

                // 示例 1: 基本实体映射
                Console.WriteLine("1. 基本实体映射");
                await DemonstrateBasicEntityMappingAsync(db);

                // 示例 2: 特性配置映射
                Console.WriteLine("\n2. 特性配置映射");
                await DemonstrateAttributeMappingAsync(db);

                // 示例 3: 主键和自增列
                Console.WriteLine("\n3. 主键和自增列");
                await DemonstratePrimaryKeyAndIdentityAsync(db);

                // 示例 4: 忽略列
                Console.WriteLine("\n4. 忽略列");
                await DemonstrateIgnoredColumnsAsync(db);

                // 示例 5: DBNull 处理
                Console.WriteLine("\n5. DBNull 处理");
                await DemonstrateDbNullHandlingAsync(db);

                // 示例 6: 自定义类型转换器
                Console.WriteLine("\n6. 自定义类型转换器（JSON）");
                await DemonstrateCustomConverterAsync(db);

                Console.WriteLine("\n示例运行完成！");
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
        /// 初始化数据库表
        /// </summary>
        static async Task InitializeDatabaseAsync(IDbAccess db)
        {
            // 创建 Users 表
            await db.ExecuteNonQueryAsync(@"
                CREATE TABLE Users (
                    user_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_name TEXT NOT NULL,
                    email TEXT,
                    age INTEGER DEFAULT 0,
                    created_at TEXT,
                    updated_at TEXT
                )");

            // 创建 Products 表
            await db.ExecuteNonQueryAsync(@"
                CREATE TABLE Products (
                    product_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    product_name TEXT NOT NULL,
                    price REAL DEFAULT 0
                )");

        }

        /// <summary>
        /// 示例 1: 基本实体映射
        /// </summary>
        static async Task DemonstrateBasicEntityMappingAsync(IDbAccess db)
        {
            // 插入测试数据
            await db.ExecuteNonQueryAsync(@"
                INSERT INTO Users (user_name, email, age, created_at) 
                VALUES (@UserName, @Email, @Age, @CreatedAt)",
                new
                {
                    UserName = "张三",
                    Email = "zhangsan@example.com",
                    Age = 25,
                    CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });

            // 使用 Query<T> 查询实体列表
            var users = await db.FromSql<BasicUser>("SELECT user_id, user_name, email, age, created_at FROM Users").ToListAsync();

            foreach (var user in users)
            {
                Console.WriteLine($"   用户: {user.UserName}, 邮箱: {user.Email}, 年龄: {user.Age}");
            }
        }

        /// <summary>
        /// 示例 2: 特性配置映射
        /// </summary>
        static async Task DemonstrateAttributeMappingAsync(IDbAccess db)
        {
            // 插入测试数据
            await db.ExecuteNonQueryAsync(@"
                INSERT INTO Users (user_name, email, age, created_at) 
                VALUES (@UserName, @Email, @Age, @CreatedAt)",
                new
                {
                    UserName = "李四",
                    Email = "lisi@example.com",
                    Age = 30,
                    CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });

            // 使用带特性的实体查询
            var users = await db.FromSql<AttributedUser>(
                "SELECT user_id, user_name, email, age, created_at FROM Users WHERE user_name = @UserName",
                new { UserName = "李四" }).ToListAsync();

            foreach (var user in users)
            {
                Console.WriteLine($"   用户ID: {user.Id}, 用户名: {user.UserName}, 邮箱: {user.Email}");
            }
        }

        /// <summary>
        /// 示例 3: 主键和自增列
        /// </summary>
        static async Task DemonstratePrimaryKeyAndIdentityAsync(IDbAccess db)
        {
            // 插入数据并获取自增ID
            await db.ExecuteNonQueryAsync(@"
                INSERT INTO Products (product_name, price) VALUES (@ProductName, @Price)",
                new { ProductName = "笔记本电脑", Price = 5999.99 });

            // 查询刚插入的产品
            var products = await db.FromSql<Product>(
                "SELECT product_id, product_name, price FROM Products WHERE product_name = @ProductName",
                new { ProductName = "笔记本电脑" }).ToListAsync();

            foreach (var product in products)
            {
                Console.WriteLine($"   产品ID: {product.ProductId}, 名称: {product.ProductName}, 价格: {product.Price:C}");
            }
        }

        /// <summary>
        /// 示例 4: 忽略列
        /// </summary>
        static async Task DemonstrateIgnoredColumnsAsync(IDbAccess db)
        {
            // 插入测试数据
            await db.ExecuteNonQueryAsync(@"
                INSERT INTO Users (user_name, email, age) 
                VALUES (@UserName, @Email, @Age)",
                new { UserName = "王五", Email = "wangwu@example.com", Age = 35 });

            // 查询数据
            var users = await db.FromSql<UserWithIgnoredColumns>(
                "SELECT user_id, user_name, email, age FROM Users WHERE user_name = @UserName",
                new { UserName = "王五" }).ToListAsync();

            foreach (var user in users)
            {
                Console.WriteLine($"   用户: {user.UserName}, 邮箱: {user.Email}");
                Console.WriteLine($"   计算属性 - 是否成年: {user.IsAdult}, 显示名称: {user.DisplayName}");
            }
        }

        /// <summary>
        /// 示例 5: DBNull 处理
        /// </summary>
        static async Task DemonstrateDbNullHandlingAsync(IDbAccess db)
        {
            // 插入包含 NULL 的数据
            await db.ExecuteNonQueryAsync(@"
                INSERT INTO Users (user_name, email, age, updated_at) 
                VALUES (@UserName, @Email, @Age, NULL)",
                new { UserName = "赵六", Email = "zhaoliu@example.com", Age = 28 });

            // 使用不同 DBNull 处理策略的实体查询
            var users = await db.FromSql<UserWithDbNullHandling>(
                "SELECT user_id, user_name, email, age, updated_at FROM Users WHERE user_name = @UserName",
                new { UserName = "赵六" }).ToListAsync();

            foreach (var user in users)
            {
                Console.WriteLine($"   用户: {user.UserName}");
                Console.WriteLine($"   更新时间: {(user.UpdatedAt.HasValue ? user.UpdatedAt.Value.ToString() : "NULL（使用默认值）")}");
            }
        }

        /// <summary>
        /// 示例 6: 自定义类型转换器（JSON 转换）
        /// </summary>
        static async Task DemonstrateCustomConverterAsync(IDbAccess db)
        {
            // 创建包含 JSON 列的表
            await db.ExecuteNonQueryAsync(@"
                ALTER TABLE Users ADD COLUMN address_json TEXT");

            // 插入包含 JSON 数据
            var addressJson = """{"Street":"中山路100号","City":"北京市","ZipCode":"100000"}""";
            await db.ExecuteNonQueryAsync(@"
                UPDATE Users SET address_json = @AddressJson WHERE user_name = @UserName",
                new { AddressJson = addressJson, UserName = "张三" });

            // 使用自定义转换器查询
            var users = await db.FromSql<UserWithConverter>(
                "SELECT user_id, user_name, email, address_json FROM Users WHERE user_name = @UserName",
                new { UserName = "张三" }).ToListAsync();

            foreach (var user in users)
            {
                Console.WriteLine($"   用户: {user.UserName}");
                Console.WriteLine($"   地址: {user.Address.City} {user.Address.Street} ({user.Address.ZipCode})");
            }
        }

    }

    #region 实体类定义

    /// <summary>
    /// 基本用户实体 - 自动映射
    /// </summary>
    public class BasicUser
    {
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int Age { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
    }

    /// <summary>
    /// 带特性配置的用户实体
    /// </summary>
    [Table("Users")]
    public class AttributedUser
    {
        [PrimaryKey("user_id")]
        public int Id { get; set; }

        [Column("user_name")]
        public string UserName { get; set; } = string.Empty;

        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Column("age")]
        public int Age { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 产品实体 - 演示主键和自增列
    /// </summary>
    [Table("Products")]
    public class Product
    {
        [PrimaryKey("product_id", IsIdentity = true)]
        public int ProductId { get; set; }

        [Column("product_name")]
        public string ProductName { get; set; } = string.Empty;

        [Column("price")]
        public decimal Price { get; set; }
    }

    /// <summary>
    /// 带忽略列的用户实体
    /// </summary>
    [Table("Users")]
    public class UserWithIgnoredColumns
    {
        [PrimaryKey("user_id")]
        public int Id { get; set; }

        [Column("user_name")]
        public string UserName { get; set; } = string.Empty;

        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Column("age")]
        public int Age { get; set; }

        // 忽略列 - 不映射到数据库
        [NotMapped]
        public bool IsAdult => Age >= 18;

        // 忽略列 - 不映射到数据库
        [NotMapped]
        public string DisplayName => $"{UserName} ({Email})";
    }

    /// <summary>
    /// 带 DBNull 处理的用户实体 - 演示 SkipAssignment（默认行为）
    /// </summary>
    [Table("Users")]
    public class UserWithDbNullHandling
    {
        [PrimaryKey("user_id")]
        public int Id { get; set; }

        [Column("user_name")]
        public string UserName { get; set; } = string.Empty;

        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Column("age")]
        public int Age { get; set; }

        // 可空类型，DBNull 自动转为 null（SkipAssignment 默认行为）
        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// 带 DBNull 处理的用户实体 - 演示 UseDefaultValue
    /// </summary>
    [Table("Users")]
    public class UserWithDefaultValue
    {
        [PrimaryKey("user_id")]
        public int Id { get; set; }

        [Column("user_name")]
        public string UserName { get; set; } = string.Empty;

        [Column("email")]
        public string Email { get; set; } = string.Empty;

        // 非可空类型，DBNull 时使用默认值 0
        [Column("age", DbNullHandling = DbNullHandling.UseDefaultValue)]
        public int Age { get; set; }

        // 可空类型，DBNull 时保持为 null（SkipAssignment 默认行为）
        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// 地址类 - 用于 JSON 转换器示例
    /// </summary>
    public class Address
    {
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
    }

    /// <summary>
    /// 带自定义类型转换器的用户实体
    /// </summary>
    [Table("Users")]
    public class UserWithConverter
    {
        [PrimaryKey("user_id")]
        public int Id { get; set; }

        [Column("user_name")]
        public string UserName { get; set; } = string.Empty;

        [Column("email")]
        public string Email { get; set; } = string.Empty;

        // 使用自定义转换器将 JSON 字符串转换为 Address 对象
        [Column("address_json", ConverterType = typeof(JsonConverter<Address>))]
        public Address Address { get; set; } = new Address();
    }

    /// <summary>
    /// JSON 转换器 - 演示自定义类型转换
    /// </summary>
    public class JsonConverter<T> : IDbValueConverter<T> where T : class, new()
    {
        public T ConvertFrom(object dbValue)
        {
            if (dbValue == null || dbValue == DBNull.Value || string.IsNullOrEmpty(dbValue.ToString()))
                return null;

            return JsonSerializer.Deserialize<T>(dbValue.ToString());
        }

        public object ConvertTo(T value)
        {
            if (value == null)
                return null;

            return JsonSerializer.Serialize(value);
        }
    }

    #endregion
}
