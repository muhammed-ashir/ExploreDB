-- =======================================================================================
-- EXPLOREDB: SQL COMPARE TEST DATABASE - TARGET LITE (CompareDB_Target_Lite)
-- =======================================================================================
-- Purpose: Destination "Target" schema (e.g. Production/Older Environment) featuring:
--   - Missing objects (Discounts, SecurityLog, Sequences, Triggers, Views that are added in Source)
--   - Modified objects (Older column types, missing columns, missing indexes)
--   - Deprecated objects (Legacy tables & procedures to be dropped or cleaned up)
--   - Identical objects (Categories, Warehouses, PaymentMethods, shared procedures)
--   - Dedicated Comparison Option test objects (Whitespace, Comments, Collation, Identity Seed, Permissions, Constraint Names)
--
-- Execution Time: ~1 to 2 seconds
-- Target Database: CompareDB_Target_Lite
-- =======================================================================================

USE master;
GO

IF DB_ID('CompareDB_Target_Lite') IS NOT NULL
BEGIN
    ALTER DATABASE CompareDB_Target_Lite SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE CompareDB_Target_Lite;
END
GO

CREATE DATABASE CompareDB_Target_Lite;
GO

USE CompareDB_Target_Lite;
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
CREATE SCHEMA reporting;
GO

-- ---------------------------------------------------------------------------------------
-- 1. IDENTICAL TABLES (Match Source Exactly)
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
-- 2. MODIFIED TABLES (Older Structure Compared to Source)
-- ---------------------------------------------------------------------------------------

-- Customers: Target has older Email length (100) and lacks LoyaltyTier & IsVerified
CREATE TABLE dbo.Customers (
    CustomerID INT IDENTITY(1,1) NOT NULL,
    FirstName NVARCHAR(50) NOT NULL,
    LastName NVARCHAR(50) NOT NULL,
    Email NVARCHAR(100) NOT NULL, -- Modified: 100 in Target vs 256 in Source
    Phone NVARCHAR(25) NULL,
    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Customers PRIMARY KEY CLUSTERED (CustomerID),
    CONSTRAINT UQ_Customers_Email UNIQUE NONCLUSTERED (Email)
);
GO

-- Products: Target has Price DECIMAL(10,2) and lacks DiscontinuedDate
CREATE TABLE inventory.Products (
    ProductID INT IDENTITY(1,1) NOT NULL,
    CategoryID INT NOT NULL,
    WarehouseID INT NOT NULL,
    ProductName NVARCHAR(150) NOT NULL,
    SKU NVARCHAR(50) NOT NULL,
    Price DECIMAL(10, 2) NOT NULL, -- Modified: 10,2 in Target vs 18,2 in Source
    StockQuantity INT NOT NULL CONSTRAINT DF_Products_Stock DEFAULT (0),
    CONSTRAINT PK_Products PRIMARY KEY CLUSTERED (ProductID),
    CONSTRAINT FK_Products_Categories FOREIGN KEY (CategoryID) REFERENCES dbo.Categories (CategoryID),
    CONSTRAINT FK_Products_Warehouses FOREIGN KEY (WarehouseID) REFERENCES inventory.Warehouses (WarehouseID)
);
GO

-- Orders: Target lacks ShippingMethod, TrackingNumber, Index on OrderDate, and TotalAmount Check
CREATE TABLE sales.Orders (
    OrderID INT IDENTITY(1,1) NOT NULL,
    CustomerID INT NOT NULL,
    OrderDate DATETIME2 NOT NULL CONSTRAINT DF_Orders_OrderDate DEFAULT (SYSUTCDATETIME()),
    TotalAmount DECIMAL(18, 2) NOT NULL,
    Status NVARCHAR(30) NOT NULL CONSTRAINT DF_Orders_Status DEFAULT ('Pending'),
    CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED (OrderID),
    CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerID) REFERENCES dbo.Customers (CustomerID)
);
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
-- 3. DEPRECATED / LEGACY TABLES (Exist in Target, Missing in Source -> Will be DELETED)
-- ---------------------------------------------------------------------------------------

CREATE TABLE dbo.LegacyImportQueue (
    QueueID INT IDENTITY(1,1) NOT NULL,
    RawPayload NVARCHAR(MAX) NULL,
    ImportedAt DATETIME2 NOT NULL CONSTRAINT DF_LegacyImport_Time DEFAULT (SYSUTCDATETIME()),
    IsProcessed BIT NOT NULL CONSTRAINT DF_LegacyImport_Processed DEFAULT (0),
    CONSTRAINT PK_LegacyImportQueue PRIMARY KEY CLUSTERED (QueueID)
);
GO

CREATE TABLE sales.OldOrderArchive (
    ArchiveID INT IDENTITY(1,1) NOT NULL,
    OldOrderID INT NOT NULL,
    LegacyNotes NVARCHAR(200) NULL,
    CONSTRAINT PK_OldOrderArchive PRIMARY KEY CLUSTERED (ArchiveID)
);
GO

-- ---------------------------------------------------------------------------------------
-- 4. VIEWS
-- ---------------------------------------------------------------------------------------

-- Identical View
CREATE VIEW dbo.vw_ActiveCategories
AS
SELECT CategoryID, CategoryName, Description
FROM dbo.Categories;
GO

-- Modified View: Target lacks LoyaltyTier and ShippingMethod
CREATE VIEW sales.vw_CustomerOrders
AS
SELECT 
    o.OrderID,
    o.OrderDate,
    o.TotalAmount,
    o.Status,
    c.CustomerID,
    c.FirstName + ' ' + c.LastName AS CustomerName,
    c.Email
FROM sales.Orders o
INNER JOIN dbo.Customers c ON o.CustomerID = c.CustomerID;
GO

-- Deprecated View (Will be DELETED)
CREATE VIEW reporting.vw_DeprecatedSalesStats
AS
SELECT 
    COUNT(OrderID) AS OldTotalOrders,
    SUM(TotalAmount) AS OldTotalRevenue
FROM sales.Orders;
GO

-- ---------------------------------------------------------------------------------------
-- 5. FUNCTIONS
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

-- Modified Function: Older simpler calculation in Target
CREATE FUNCTION sales.fn_GetCustomerTotalSpend
(
    @CustomerID INT
)
RETURNS DECIMAL(18, 2)
AS
BEGIN
    RETURN (SELECT ISNULL(SUM(TotalAmount), 0) FROM sales.Orders WHERE CustomerID = @CustomerID);
END;
GO

-- ---------------------------------------------------------------------------------------
-- 6. STORED PROCEDURES
-- ---------------------------------------------------------------------------------------

-- Identical Procedure
CREATE PROCEDURE dbo.sp_GetCategories
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CategoryID, CategoryName, Description FROM dbo.Categories ORDER BY CategoryName;
END;
GO

-- Modified Procedure: Target lacks @ShippingMethod and lacks audit log
CREATE PROCEDURE sales.sp_CreateOrder
    @CustomerID INT,
    @TotalAmount DECIMAL(18, 2),
    @NewOrderID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO sales.Orders (CustomerID, TotalAmount, Status)
    VALUES (@CustomerID, @TotalAmount, 'Pending');

    SET @NewOrderID = SCOPE_IDENTITY();
END;
GO

-- Deprecated Procedure (Will be DELETED)
CREATE PROCEDURE dbo.sp_LegacySync
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.LegacyImportQueue SET IsProcessed = 1 WHERE IsProcessed = 0;
END;
GO

-- ---------------------------------------------------------------------------------------
-- 7. COMPARISON OPTIONS TEST OBJECTS
-- ---------------------------------------------------------------------------------------
-- Paired counterparts to Source for validating Comparison Options:
--   1. Ignore Whitespace: dbo.vw_WhitespaceTest (single-line compact format)
--   2. Ignore Comments: dbo.sp_CommentsTest (legacy short comment)
--   3. Ignore Collation Differences: dbo.CollationTest (SQL_Latin1_General_CP1_CI_AS)
--   4. Ignore Identity (Seed & Increment): dbo.IdentityTest (IDENTITY(1000,10))
--   5. Ignore Permissions & Roles: (Role & Grant omitted in Target)
--   6. Ignore System Constraint Names: dbo.SystemConstraintTest (unnamed default constraint)
-- ---------------------------------------------------------------------------------------

CREATE TABLE dbo.IdentityTest (
    ID INT IDENTITY(1000,10) NOT NULL,
    Name NVARCHAR(50) NOT NULL,
    CONSTRAINT PK_IdentityTest PRIMARY KEY CLUSTERED (ID)
);
GO

CREATE TABLE dbo.CollationTest (
    ID INT NOT NULL,
    Code NVARCHAR(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
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

CREATE VIEW dbo.vw_WhitespaceTest AS SELECT CategoryID, CategoryName, Description FROM dbo.Categories;
GO

CREATE PROCEDURE dbo.sp_CommentsTest
AS
BEGIN
    -- Version 1.0: Legacy category fetch
    SET NOCOUNT ON;
    SELECT CategoryID, CategoryName FROM dbo.Categories WHERE CategoryID > 0;
END;
GO

-- ---------------------------------------------------------------------------------------
-- 8. SEED SAMPLE DATA
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

INSERT INTO dbo.Customers (FirstName, LastName, Email, Phone) VALUES
('Alice', 'Smith', 'alice.smith@example.com', '555-0101'),
('Bob', 'Jones', 'bob.jones@example.com', '555-0102');

INSERT INTO inventory.Products (CategoryID, WarehouseID, ProductName, SKU, Price, StockQuantity) VALUES
(1, 1, 'Pro Ultrabook 15', 'TECH-NB-001', 1299.99, 45),
(1, 1, '4K UltraSharp Monitor 27"', 'TECH-MON-002', 399.99, 80),
(2, 2, 'Ergonomic Mesh Chair', 'FURN-CHR-001', 249.50, 30),
(3, 1, 'Wireless Mechanical Keyboard', 'ACC-KB-001', 89.99, 150);

INSERT INTO sales.Orders (CustomerID, TotalAmount, Status) VALUES
(1, 1299.99, 'Completed'),
(2, 489.98, 'Pending');

INSERT INTO sales.OrderItems (OrderID, ProductID, Quantity, UnitPrice) VALUES
(1, 1, 1, 1299.99),
(2, 2, 1, 399.99),
(2, 4, 1, 89.99);

INSERT INTO dbo.LegacyImportQueue (RawPayload, IsProcessed) VALUES
('{"legacy_batch": 1001, "status": "archive"}', 1);

INSERT INTO dbo.IdentityTest (Name) VALUES ('Seed Row 1'), ('Seed Row 2');
INSERT INTO dbo.CollationTest (ID, Code, Description) VALUES (1, 'ALPHA', 'Collation sample record');
INSERT INTO dbo.SystemConstraintTest (StatusCode) VALUES (1);

PRINT '>>> CompareDB_Target_Lite created and populated successfully.';
GO
