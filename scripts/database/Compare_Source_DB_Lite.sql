-- =======================================================================================
-- EXPLOREDB: SQL COMPARE TEST DATABASE - SOURCE LITE (CompareDB_Source_Lite)
-- =======================================================================================
-- Purpose: Reference "Source" schema (Development/Staging) featuring:
--   - Added objects (Discounts, SecurityLog, Sequences, Triggers, Views, Procedures)
--   - Modified objects (Added columns, modified column data types, new indexes & constraints)
--   - Identical objects (Categories, Warehouses, PaymentMethods, shared procedures)
--   - Missing deprecated objects that exist in Target (Legacy tables & procedures removed)
--   - Dedicated Comparison Option test objects (Whitespace, Comments, Collation, Identity Seed, Permissions, Constraint Names)
--
-- Execution Time: ~1 to 2 seconds
-- Target Database: CompareDB_Source_Lite
-- =======================================================================================

USE master;
GO

IF DB_ID('CompareDB_Source_Lite') IS NOT NULL
BEGIN
    ALTER DATABASE CompareDB_Source_Lite SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE CompareDB_Source_Lite;
END
GO

CREATE DATABASE CompareDB_Source_Lite;
GO

USE CompareDB_Source_Lite;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ---------------------------------------------------------------------------------------
-- Schemas
-- ---------------------------------------------------------------------------------------
CREATE SCHEMA sales;
GO
CREATE SCHEMA inventory;
GO
CREATE SCHEMA audit;
GO
CREATE SCHEMA reporting;
GO

-- ---------------------------------------------------------------------------------------
-- 1. IDENTICAL TABLES (Match Target Exactly)
-- ---------------------------------------------------------------------------------------
CREATE TABLE dbo.Categories (
    CategoryID INT IDENTITY(1,1) NOT NULL,
    CategoryName NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NULL,
    CONSTRAINT PK_Categories PRIMARY KEY CLUSTERED (CategoryID)
);
GO

CREATE TABLE inventory.Warehouses (
    WarehouseID INT IDENTITY(1,1) NOT NULL,
    WarehouseName NVARCHAR(100) NOT NULL,
    LocationCode NVARCHAR(20) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Warehouses_IsActive DEFAULT (1),
    CONSTRAINT PK_Warehouses PRIMARY KEY CLUSTERED (WarehouseID)
);
GO

CREATE TABLE sales.PaymentMethods (
    MethodID INT IDENTITY(1,1) NOT NULL,
    MethodName NVARCHAR(50) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_PaymentMethods_IsActive DEFAULT (1),
    CONSTRAINT PK_PaymentMethods PRIMARY KEY CLUSTERED (MethodID)
);
GO

-- ---------------------------------------------------------------------------------------
-- 2. MODIFIED TABLES (Differ from Target)
-- ---------------------------------------------------------------------------------------

-- Customers: Source has LoyaltyTier, IsVerified, and Email is NVARCHAR(256)
CREATE TABLE dbo.Customers (
    CustomerID INT IDENTITY(1,1) NOT NULL,
    FirstName NVARCHAR(50) NOT NULL,
    LastName NVARCHAR(50) NOT NULL,
    Email NVARCHAR(256) NOT NULL, -- Modified: Length is 256 in Source, 100 in Target
    Phone NVARCHAR(25) NULL,
    LoyaltyTier NVARCHAR(50) NOT NULL CONSTRAINT DF_Customers_LoyaltyTier DEFAULT ('Bronze'), -- Added in Source
    IsVerified BIT NOT NULL CONSTRAINT DF_Customers_IsVerified DEFAULT (0),                   -- Added in Source
    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Customers PRIMARY KEY CLUSTERED (CustomerID),
    CONSTRAINT UQ_Customers_Email UNIQUE NONCLUSTERED (Email)
);
GO

-- Products: Source has Price DECIMAL(18,2) and DiscontinuedDate
CREATE TABLE inventory.Products (
    ProductID INT IDENTITY(1,1) NOT NULL,
    CategoryID INT NOT NULL,
    WarehouseID INT NOT NULL,
    ProductName NVARCHAR(150) NOT NULL,
    SKU NVARCHAR(50) NOT NULL,
    Price DECIMAL(18, 2) NOT NULL,                            -- Modified: 18,2 in Source vs 10,2 in Target
    StockQuantity INT NOT NULL CONSTRAINT DF_Products_Stock DEFAULT (0),
    DiscontinuedDate DATETIME2 NULL,                           -- Added in Source
    CONSTRAINT PK_Products PRIMARY KEY CLUSTERED (ProductID),
    CONSTRAINT FK_Products_Categories FOREIGN KEY (CategoryID) REFERENCES dbo.Categories (CategoryID),
    CONSTRAINT FK_Products_Warehouses FOREIGN KEY (WarehouseID) REFERENCES inventory.Warehouses (WarehouseID)
);
GO

-- Orders: Source has ShippingMethod, TrackingNumber, Index on OrderDate, and TotalAmount Check
CREATE TABLE sales.Orders (
    OrderID INT IDENTITY(1,1) NOT NULL,
    CustomerID INT NOT NULL,
    OrderDate DATETIME2 NOT NULL CONSTRAINT DF_Orders_OrderDate DEFAULT (SYSUTCDATETIME()),
    TotalAmount DECIMAL(18, 2) NOT NULL,
    Status NVARCHAR(30) NOT NULL CONSTRAINT DF_Orders_Status DEFAULT ('Pending'),
    ShippingMethod NVARCHAR(50) NULL,                         -- Added in Source
    TrackingNumber NVARCHAR(100) NULL,                        -- Added in Source
    CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED (OrderID),
    CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerID) REFERENCES dbo.Customers (CustomerID),
    CONSTRAINT CK_Orders_TotalAmount CHECK (TotalAmount >= 0) -- Added in Source
);
GO

CREATE NONCLUSTERED INDEX IX_Orders_OrderDate ON sales.Orders (OrderDate DESC); -- Added in Source
GO

CREATE TABLE sales.OrderItems (
    OrderItemID INT IDENTITY(1,1) NOT NULL,
    OrderID INT NOT NULL,
    ProductID INT NOT NULL,
    Quantity INT NOT NULL CONSTRAINT CK_OrderItems_Quantity CHECK (Quantity > 0),
    UnitPrice DECIMAL(18, 2) NOT NULL,
    CONSTRAINT PK_OrderItems PRIMARY KEY CLUSTERED (OrderItemID),
    CONSTRAINT FK_OrderItems_Orders FOREIGN KEY (OrderID) REFERENCES sales.Orders (OrderID) ON DELETE CASCADE,
    CONSTRAINT FK_OrderItems_Products FOREIGN KEY (ProductID) REFERENCES inventory.Products (ProductID)
);
GO

-- ---------------------------------------------------------------------------------------
-- 3. ADDED TABLES (Exist ONLY in Source)
-- ---------------------------------------------------------------------------------------

-- Discounts: New feature in Source
CREATE TABLE sales.Discounts (
    DiscountID INT IDENTITY(1,1) NOT NULL,
    DiscountCode NVARCHAR(30) NOT NULL,
    DiscountPercentage DECIMAL(5, 2) NOT NULL,
    ValidUntil DATETIME2 NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Discounts_IsActive DEFAULT (1),
    CONSTRAINT PK_Discounts PRIMARY KEY CLUSTERED (DiscountID),
    CONSTRAINT UQ_Discounts_Code UNIQUE NONCLUSTERED (DiscountCode),
    CONSTRAINT CK_Discounts_Percent CHECK (DiscountPercentage > 0 AND DiscountPercentage <= 100)
);
GO

-- SecurityLog: New audit logging in Source
CREATE TABLE audit.SecurityLog (
    LogID BIGINT IDENTITY(1,1) NOT NULL,
    EventType NVARCHAR(50) NOT NULL,
    UserName NVARCHAR(100) NOT NULL,
    ClientIP NVARCHAR(45) NULL,
    EventTimestamp DATETIME2 NOT NULL CONSTRAINT DF_SecurityLog_Timestamp DEFAULT (SYSUTCDATETIME()),
    Details NVARCHAR(MAX) NULL,
    CONSTRAINT PK_SecurityLog PRIMARY KEY CLUSTERED (LogID)
);
GO

-- ---------------------------------------------------------------------------------------
-- 4. SEQUENCES
-- ---------------------------------------------------------------------------------------

-- Added in Source: Order Invoice Number Sequence
CREATE SEQUENCE dbo.Seq_InvoiceNumber
    AS BIGINT
    START WITH 100001
    INCREMENT BY 1
    NO CACHE;
GO

-- ---------------------------------------------------------------------------------------
-- 5. VIEWS
-- ---------------------------------------------------------------------------------------

-- Identical View
CREATE VIEW dbo.vw_ActiveCategories
AS
SELECT CategoryID, CategoryName, Description
FROM dbo.Categories;
GO

-- Modified View: Includes LoyaltyTier and ShippingMethod in Source
CREATE VIEW sales.vw_CustomerOrders
AS
SELECT 
    o.OrderID,
    o.OrderDate,
    o.TotalAmount,
    o.Status,
    o.ShippingMethod,
    o.TrackingNumber,
    c.CustomerID,
    c.FirstName + ' ' + c.LastName AS CustomerName,
    c.Email,
    c.LoyaltyTier
FROM sales.Orders o
INNER JOIN dbo.Customers c ON o.CustomerID = c.CustomerID;
GO

-- Added View: Exists only in Source
CREATE VIEW reporting.vw_MonthlyRevenueBreakdown
AS
SELECT 
    YEAR(o.OrderDate) AS OrderYear,
    MONTH(o.OrderDate) AS OrderMonth,
    COUNT(o.OrderID) AS TotalOrders,
    SUM(o.TotalAmount) AS TotalRevenue,
    AVG(o.TotalAmount) AS AverageOrderValue
FROM sales.Orders o
WHERE o.Status <> 'Cancelled'
GROUP BY YEAR(o.OrderDate), MONTH(o.OrderDate);
GO

-- ---------------------------------------------------------------------------------------
-- 6. FUNCTIONS
-- ---------------------------------------------------------------------------------------

-- Identical Scalar Function
CREATE FUNCTION dbo.fn_CalculateMargin
(
    @Cost DECIMAL(18, 2),
    @SalePrice DECIMAL(18, 2)
)
RETURNS DECIMAL(18, 2)
AS
BEGIN
    IF @SalePrice = 0 RETURN 0;
    RETURN ((@SalePrice - @Cost) / @SalePrice) * 100.0;
END;
GO

-- Modified Function: Source calculates spend with loyalty tier consideration
CREATE FUNCTION sales.fn_GetCustomerTotalSpend
(
    @CustomerID INT
)
RETURNS DECIMAL(18, 2)
AS
BEGIN
    DECLARE @Total DECIMAL(18, 2);
    SELECT @Total = ISNULL(SUM(TotalAmount), 0)
    FROM sales.Orders
    WHERE CustomerID = @CustomerID AND Status = 'Completed';
    RETURN @Total;
END;
GO

-- Added Function: Exists only in Source
CREATE FUNCTION sales.fn_CalculateDiscountedPrice
(
    @OriginalPrice DECIMAL(18, 2),
    @DiscountPercent DECIMAL(5, 2)
)
RETURNS DECIMAL(18, 2)
AS
BEGIN
    RETURN @OriginalPrice * (1.0 - (@DiscountPercent / 100.0));
END;
GO

-- ---------------------------------------------------------------------------------------
-- 7. STORED PROCEDURES
-- ---------------------------------------------------------------------------------------

-- Identical Procedure
CREATE PROCEDURE dbo.sp_GetCategories
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CategoryID, CategoryName, Description FROM dbo.Categories ORDER BY CategoryName;
END;
GO

-- Modified Procedure: Source accepts @ShippingMethod and updates loyalty points
CREATE PROCEDURE sales.sp_CreateOrder
    @CustomerID INT,
    @TotalAmount DECIMAL(18, 2),
    @ShippingMethod NVARCHAR(50) = 'Standard', -- Added parameter in Source
    @NewOrderID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;

    INSERT INTO sales.Orders (CustomerID, TotalAmount, Status, ShippingMethod)
    VALUES (@CustomerID, @TotalAmount, 'Pending', @ShippingMethod);

    SET @NewOrderID = SCOPE_IDENTITY();

    -- Log creation in audit
    INSERT INTO audit.SecurityLog (EventType, UserName, Details)
    VALUES ('ORDER_CREATED', SUSER_SNAME(), CONCAT('Order #', @NewOrderID, ' created for Customer #', @CustomerID));

    COMMIT TRANSACTION;
END;
GO

-- Added Procedure: Exists only in Source
CREATE PROCEDURE sales.sp_ApplyBulkDiscount
    @DiscountCode NVARCHAR(30),
    @MinOrderAmount DECIMAL(18, 2)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT d.DiscountCode, d.DiscountPercentage, d.ValidUntil
    FROM sales.Discounts d
    WHERE d.DiscountCode = @DiscountCode AND d.IsActive = 1 AND d.ValidUntil > SYSUTCDATETIME();
END;
GO

-- ---------------------------------------------------------------------------------------
-- 8. TRIGGERS
-- ---------------------------------------------------------------------------------------

-- Added Trigger in Source: Decrement stock when order item is added
CREATE TRIGGER sales.trg_UpdateStockOnOrder
ON sales.OrderItems
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE p
    SET p.StockQuantity = p.StockQuantity - i.Quantity
    FROM inventory.Products p
    INNER JOIN inserted i ON p.ProductID = i.ProductID;
END;
GO

-- ---------------------------------------------------------------------------------------
-- 9. COMPARISON OPTIONS TEST OBJECTS
-- ---------------------------------------------------------------------------------------
-- These objects allow verification of each toggle in the Comparison Options dialog:
--   1. Ignore Whitespace: dbo.vw_WhitespaceTest (multi-line indented vs single-line compact)
--   2. Ignore Comments: dbo.sp_CommentsTest (inline doc comments vs legacy comments)
--   3. Ignore Collation Differences: dbo.CollationTest (Latin1_General_CI_AS vs SQL_Latin1_General_CP1_CI_AS)
--   4. Ignore Identity (Seed & Increment): dbo.IdentityTest (IDENTITY(1,1) vs IDENTITY(1000,10))
--   5. Ignore Permissions & Roles: AppReadOnlyRole & SELECT grant on sales.PaymentMethods
--   6. Ignore System Constraint Names: dbo.SystemConstraintTest (unnamed default constraint)
-- ---------------------------------------------------------------------------------------

CREATE TABLE dbo.IdentityTest (
    ID INT IDENTITY(1,1) NOT NULL,
    Name NVARCHAR(50) NOT NULL,
    CONSTRAINT PK_IdentityTest PRIMARY KEY CLUSTERED (ID)
);
GO

CREATE TABLE dbo.CollationTest (
    ID INT NOT NULL,
    Code NVARCHAR(50) COLLATE Latin1_General_CI_AS NOT NULL,
    Description NVARCHAR(100) NULL,
    CONSTRAINT PK_CollationTest PRIMARY KEY CLUSTERED (ID)
);
GO

CREATE TABLE dbo.SystemConstraintTest (
    ConstraintID INT IDENTITY(1,1) NOT NULL,
    StatusCode INT DEFAULT (1),
    CONSTRAINT PK_SystemConstraintTest PRIMARY KEY CLUSTERED (ConstraintID)
);
GO

CREATE VIEW dbo.vw_WhitespaceTest
AS
SELECT 
    CategoryID,
    CategoryName,
    Description
FROM 
    dbo.Categories;
GO

CREATE PROCEDURE dbo.sp_CommentsTest
AS
BEGIN
    -- Version 2.0: Optimized category fetch with active status filter
    /* 
       Documentation Header:
       Returns all active categories for UI dropdown binding.
    */
    SET NOCOUNT ON;
    SELECT CategoryID, CategoryName FROM dbo.Categories WHERE CategoryID > 0;
END;
GO

CREATE ROLE AppReadOnlyRole;
GO
GRANT SELECT ON sales.PaymentMethods TO AppReadOnlyRole;
GO

-- ---------------------------------------------------------------------------------------
-- 10. SEED SAMPLE DATA
-- ---------------------------------------------------------------------------------------
INSERT INTO dbo.Categories (CategoryName, Description) VALUES
('Electronics', 'Smartphones, laptops, and smart devices'),
('Furniture', 'Office and home ergonomic furniture'),
('Accessories', 'Cables, chargers, keyboards, and mice');

INSERT INTO inventory.Warehouses (WarehouseName, LocationCode) VALUES
('East Coast Fulfillment', 'US-EAST-1'),
('West Coast Hub', 'US-WEST-2');

INSERT INTO sales.PaymentMethods (MethodName) VALUES
('Credit Card'),
('PayPal'),
('Bank Wire');

INSERT INTO dbo.Customers (FirstName, LastName, Email, Phone, LoyaltyTier, IsVerified) VALUES
('Alice', 'Smith', 'alice.smith@example.com', '555-0101', 'Gold', 1),
('Bob', 'Jones', 'bob.jones@example.com', '555-0102', 'Silver', 1),
('Charlie', 'Brown', 'charlie.brown@example.com', '555-0103', 'Bronze', 0);

INSERT INTO inventory.Products (CategoryID, WarehouseID, ProductName, SKU, Price, StockQuantity) VALUES
(1, 1, 'Pro Ultrabook 15', 'TECH-NB-001', 1299.99, 45),
(1, 1, '4K UltraSharp Monitor 27"', 'TECH-MON-002', 399.99, 80),
(2, 2, 'Ergonomic Mesh Chair', 'FURN-CHR-001', 249.50, 30),
(3, 1, 'Wireless Mechanical Keyboard', 'ACC-KB-001', 89.99, 150);

INSERT INTO sales.Orders (CustomerID, TotalAmount, Status, ShippingMethod, TrackingNumber) VALUES
(1, 1299.99, 'Completed', 'NextDay', 'TRK-987654321'),
(2, 489.98, 'Pending', 'Standard', 'TRK-123456789');

INSERT INTO sales.OrderItems (OrderID, ProductID, Quantity, UnitPrice) VALUES
(1, 1, 1, 1299.99),
(2, 2, 1, 399.99),
(2, 4, 1, 89.99);

INSERT INTO sales.Discounts (DiscountCode, DiscountPercentage, ValidUntil, IsActive) VALUES
('SUMMER2026', 15.00, DATEADD(MONTH, 3, SYSUTCDATETIME()), 1),
('WELCOME10', 10.00, DATEADD(YEAR, 1, SYSUTCDATETIME()), 1);

INSERT INTO dbo.IdentityTest (Name) VALUES ('Seed Row 1'), ('Seed Row 2');
INSERT INTO dbo.CollationTest (ID, Code, Description) VALUES (1, 'ALPHA', 'Collation sample record');
INSERT INTO dbo.SystemConstraintTest (StatusCode) VALUES (1);

PRINT '>>> CompareDB_Source_Lite created and populated successfully.';
GO
