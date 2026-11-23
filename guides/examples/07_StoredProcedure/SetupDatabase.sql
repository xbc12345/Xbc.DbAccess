-- =============================================
-- 示例 07: 存储过程测试数据库初始化脚本
-- 数据库: DbAccessTest
-- 身份认证: Windows 身份认证
-- =============================================

USE [DbAccessTest];
GO

-- =============================================
-- 1. 创建测试表
-- =============================================
IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL
    DROP TABLE dbo.Users;
GO

CREATE TABLE dbo.Users
(
    Id INT IDENTITY(1,1) PRIMARY KEY,
    UserName NVARCHAR(100) NOT NULL,
    Email NVARCHAR(200) NULL,
    Age INT NULL,
    Status NVARCHAR(50) DEFAULT 'Active',
    CreatedTime DATETIME2 DEFAULT GETDATE()
);
GO

-- =============================================
-- 2. 创建非查询存储过程: 插入用户
-- =============================================
IF OBJECT_ID('dbo.sp_InsertUser', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_InsertUser;
GO

CREATE PROCEDURE dbo.sp_InsertUser
    @UserName NVARCHAR(100),
    @Email NVARCHAR(200) = NULL,
    @Age INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    INSERT INTO dbo.Users (UserName, Email, Age)
    VALUES (@UserName, @Email, @Age);
    
    RETURN @@ROWCOUNT;
END;
GO

-- =============================================
-- 3. 创建带输出参数的存储过程: 插入用户并返回新ID
-- =============================================
IF OBJECT_ID('dbo.sp_InsertUserWithOutput', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_InsertUserWithOutput;
GO

CREATE PROCEDURE dbo.sp_InsertUserWithOutput
    @UserName NVARCHAR(100),
    @NewUserId INT OUTPUT,
    @CreatedTime DATETIME2 OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    
    INSERT INTO dbo.Users (UserName)
    VALUES (@UserName);
    
    SET @NewUserId = SCOPE_IDENTITY();
    SET @CreatedTime = GETDATE();
    
    RETURN @@ROWCOUNT;
END;
GO

-- =============================================
-- 4. 创建带返回值的存储过程
-- =============================================
IF OBJECT_ID('dbo.sp_InsertUserWithReturnValue', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_InsertUserWithReturnValue;
GO

CREATE PROCEDURE dbo.sp_InsertUserWithReturnValue
    @UserName NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    
    -- 检查用户名是否已存在
    IF EXISTS (SELECT 1 FROM dbo.Users WHERE UserName = @UserName)
    BEGIN
        RETURN -1; -- 用户名已存在
    END
    
    INSERT INTO dbo.Users (UserName)
    VALUES (@UserName);
    
    RETURN SCOPE_IDENTITY(); -- 返回新插入的ID
END;
GO

-- =============================================
-- 5. 创建查询存储过程: 根据ID获取用户
-- =============================================
IF OBJECT_ID('dbo.sp_GetUserById', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_GetUserById;
GO

CREATE PROCEDURE dbo.sp_GetUserById
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT Id, UserName, Email, Age, Status, CreatedTime
    FROM dbo.Users
    WHERE Id = @UserId;
END;
GO

-- =============================================
-- 6. 创建查询存储过程: 根据状态获取用户列表
-- =============================================
IF OBJECT_ID('dbo.sp_GetUsersByStatus', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_GetUsersByStatus;
GO

CREATE PROCEDURE dbo.sp_GetUsersByStatus
    @Status NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT Id, UserName, Email, Age, Status, CreatedTime
    FROM dbo.Users
    WHERE Status = @Status
    ORDER BY Id;
END;
GO

-- =============================================
-- 7. 创建非查询存储过程: 更新用户状态
-- =============================================
IF OBJECT_ID('dbo.sp_UpdateUserStatus', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UpdateUserStatus;
GO

CREATE PROCEDURE dbo.sp_UpdateUserStatus
    @UserId INT,
    @NewStatus NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE dbo.Users
    SET Status = @NewStatus
    WHERE Id = @UserId;
    
    RETURN @@ROWCOUNT;
END;
GO

-- =============================================
-- 8. 创建查询存储过程: 获取所有用户
-- =============================================
IF OBJECT_ID('dbo.sp_GetAllUsers', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_GetAllUsers;
GO

CREATE PROCEDURE dbo.sp_GetAllUsers
    @MinId INT = 0
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT Id, UserName, Email, Age, Status, CreatedTime
    FROM dbo.Users
    WHERE Id >= @MinId
    ORDER BY Id;
END;
GO

-- =============================================
-- 9. 插入测试数据
-- =============================================
INSERT INTO dbo.Users (UserName, Email, Age, Status)
VALUES 
    ('张三', 'zhangsan@example.com', 25, 'Active'),
    ('李四', 'lisi@example.com', 30, 'Active'),
    ('王五', 'wangwu@example.com', 28, 'Inactive');
GO

PRINT '数据库初始化完成！';
PRINT '已创建表: Users';
PRINT '已创建存储过程: sp_InsertUser, sp_InsertUserWithOutput, sp_InsertUserWithReturnValue';
PRINT '已创建存储过程: sp_GetUserById, sp_GetUsersByStatus, sp_UpdateUserStatus, sp_GetAllUsers';
GO
