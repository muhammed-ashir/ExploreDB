-- =======================================================================================
-- EXPLOREDB: LIGHTWEIGHT FEATURE-CENTRIC TEST DATABASE (ExploreDB_Lite)
-- =======================================================================================
-- Purpose: Fast, self-contained database script designed to test ALL features of ExploreDB
-- Execution Time: ~1 to 2 seconds
-- Total Size: ~8 MB
-- Target Database: ExploreDB_Lite (can be renamed to ExploreDB if preferred)
--
-- Features Tested:
-- 1. All SQL Server Data Types (Exact/Approx Numerics, Dates, Strings, JSON, XML, Spatial, Guids)
-- 2. Enhanced ResultSetGrid:
--    - rowversion / timestamp & Short Binary (<=64B): 0x hex string, natural hex sort/search
--    - Large Binary & Images (>64B): Interactive badges (PNG, JPEG, PDF, 128KB General Binary)
--    - Boundary testing: Exact 64-byte vs 65-byte thresholds
-- 3. Multi-Schema Architectures (dbo, sales, hr, inventory, reporting, audit, security)
-- 4. Complete Schema Hierarchy (Tables, Views, Indexed Views, Stored Procedures,
--    Multi-result set procedures, Scalar/Table-Valued Functions, Triggers, Sequences, Synonyms, UDTs)
-- 5. Data Editor, Paging & Filtering (500 realistic orders for testing grid pagination and search)
-- =======================================================================================

USE master;
GO

IF DB_ID('ExploreDB_Lite') IS NOT NULL
BEGIN
    ALTER DATABASE ExploreDB_Lite SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE ExploreDB_Lite;
END
GO

CREATE DATABASE ExploreDB_Lite;
GO

USE ExploreDB_Lite;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- =======================================================================================
-- 1. SCHEMAS
-- =======================================================================================
CREATE SCHEMA sales;
GO
CREATE SCHEMA hr;
GO
CREATE SCHEMA inventory;
GO
CREATE SCHEMA reporting;
GO
CREATE SCHEMA audit;
GO
CREATE SCHEMA security;
GO

-- =======================================================================================
-- 2. USER-DEFINED TYPES (UDT) & SEQUENCES
-- =======================================================================================
CREATE TYPE dbo.MoneyType FROM DECIMAL(19,4) NOT NULL;
CREATE TYPE dbo.EmailType FROM NVARCHAR(255) NOT NULL;
CREATE TYPE dbo.HashType FROM VARBINARY(64) NULL;
CREATE TYPE dbo.StatusType FROM VARCHAR(20) NOT NULL;
GO

CREATE TYPE dbo.IntIdListType AS TABLE (
    Id INT PRIMARY KEY
);
GO

CREATE TYPE dbo.OrderLineInputType AS TABLE (
    ProductID INT NOT NULL,
    Quantity INT NOT NULL,
    UnitPrice dbo.MoneyType NOT NULL
);
GO

CREATE SEQUENCE dbo.Seq_InvoiceNumber
    START WITH 10001
    INCREMENT BY 1
    MINVALUE 10001
    MAXVALUE 99999999;
GO

-- =======================================================================================
-- 3. ALL DATA TYPES TEST TABLE (Complete type coverage in ExploreDB)
-- =======================================================================================
CREATE TABLE dbo.AllDataTypesTest (
    RowID INT IDENTITY(1,1) PRIMARY KEY,
    Title NVARCHAR(50) NOT NULL,
    
    -- Exact Numerics
    Col_Bit BIT NULL,
    Col_TinyInt TINYINT NULL,
    Col_SmallInt SMALLINT NULL,
    Col_Int INT NULL,
    Col_BigInt BIGINT NULL,
    Col_Decimal DECIMAL(18,4) NULL,
    Col_Numeric NUMERIC(12,2) NULL,
    Col_Money MONEY NULL,
    Col_SmallMoney SMALLMONEY NULL,
    
    -- Approximate Numerics
    Col_Float FLOAT NULL,
    Col_Real REAL NULL,
    
    -- Date & Time
    Col_Date DATE NULL,
    Col_Time TIME(7) NULL,
    Col_DateTime DATETIME NULL,
    Col_SmallDateTime SMALLDATETIME NULL,
    Col_DateTime2 DATETIME2(7) NULL,
    Col_DateTimeOffset DATETIMEOFFSET(7) NULL,
    
    -- Character Strings
    Col_Char CHAR(10) NULL,
    Col_VarChar VARCHAR(50) NULL,
    Col_VarCharMax VARCHAR(MAX) NULL,
    Col_NChar NCHAR(10) NULL,
    Col_NVarChar NVARCHAR(100) NULL,
    Col_NVarCharMax NVARCHAR(MAX) NULL,
    
    -- Special & Semi-Structured
    Col_Guid UNIQUEIDENTIFIER DEFAULT NEWID(),
    Col_Xml XML NULL,
    Col_Json NVARCHAR(MAX) NULL,
    
    -- Binary Types
    Col_RowVersion ROWVERSION,
    Col_Binary16 BINARY(16) NULL,
    Col_VarBinary32 VARBINARY(32) NULL,
    Col_VarBinary64 VARBINARY(64) NULL,
    Col_ImagePng VARBINARY(MAX) NULL,
    Col_ImageJpeg VARBINARY(MAX) NULL,
    Col_PdfDoc VARBINARY(MAX) NULL,
    Col_BinaryBlob VARBINARY(MAX) NULL
);
GO

-- =======================================================================================
-- 4. MEDIA & BINARY SAMPLES TABLE (Dedicated for ResultSetGrid Binary Testing)
-- =======================================================================================
CREATE TABLE dbo.MediaAssets (
    AssetID INT IDENTITY(1,1) PRIMARY KEY,
    AssetName NVARCHAR(100) NOT NULL,
    Category NVARCHAR(50) NOT NULL,
    VersionTag ROWVERSION,
    ShortHash16 BINARY(16) NULL,
    ShortHash32 VARBINARY(32) NULL,
    ShortBinary64 VARBINARY(64) NULL,
    Overlay_Image VARBINARY(MAX) NULL,
    DocumentFile VARBINARY(MAX) NULL,
    BinaryPayload VARBINARY(MAX) NULL,
    CreatedAt DATETIME2 DEFAULT GETDATE()
);
GO

-- =======================================================================================
-- 5. RELATIONAL CORE: INVENTORY SCHEMA
-- =======================================================================================
CREATE TABLE inventory.Categories (
    CategoryID INT IDENTITY(1,1) PRIMARY KEY,
    CategoryName NVARCHAR(50) NOT NULL UNIQUE,
    Description NVARCHAR(255) NULL
);
GO

CREATE TABLE inventory.Warehouses (
    WarehouseID INT IDENTITY(1,1) PRIMARY KEY,
    WarehouseCode VARCHAR(10) NOT NULL UNIQUE,
    LocationName NVARCHAR(100) NOT NULL,
    City NVARCHAR(50) NOT NULL,
    Country NVARCHAR(50) NOT NULL DEFAULT 'USA'
);
GO

CREATE TABLE inventory.Products (
    ProductID INT IDENTITY(1,1) PRIMARY KEY,
    CategoryID INT NOT NULL REFERENCES inventory.Categories(CategoryID),
    SKU VARCHAR(30) NOT NULL UNIQUE,
    ProductName NVARCHAR(100) NOT NULL,
    CostPrice dbo.MoneyType NOT NULL,
    BasePrice dbo.MoneyType NOT NULL,
    Margin AS (BasePrice - CostPrice) PERSISTED,
    IsActive BIT DEFAULT 1,
    RowVersion ROWVERSION,
    Overlay_Image VARBINARY(MAX) NULL
);
GO

CREATE TABLE inventory.Stock (
    StockID INT IDENTITY(1,1) PRIMARY KEY,
    WarehouseID INT NOT NULL REFERENCES inventory.Warehouses(WarehouseID),
    ProductID INT NOT NULL REFERENCES inventory.Products(ProductID),
    QuantityOnHand INT NOT NULL DEFAULT 0,
    ReorderLevel INT NOT NULL DEFAULT 10,
    CONSTRAINT UQ_Stock_Warehouse_Product UNIQUE (WarehouseID, ProductID)
);
GO

-- =======================================================================================
-- 6. RELATIONAL CORE: HR SCHEMA (Includes Self-Referencing Hierarchy)
-- =======================================================================================
CREATE TABLE hr.Departments (
    DepartmentID INT IDENTITY(1,1) PRIMARY KEY,
    DepartmentName NVARCHAR(50) NOT NULL UNIQUE,
    Budget dbo.MoneyType NOT NULL DEFAULT 100000
);
GO

CREATE TABLE hr.Employees (
    EmployeeID INT IDENTITY(1,1) PRIMARY KEY,
    DepartmentID INT NOT NULL REFERENCES hr.Departments(DepartmentID),
    ManagerID INT NULL REFERENCES hr.Employees(EmployeeID),
    FirstName NVARCHAR(50) NOT NULL,
    LastName NVARCHAR(50) NOT NULL,
    FullName AS (FirstName + ' ' + LastName),
    JobTitle NVARCHAR(50) NOT NULL,
    Email dbo.EmailType UNIQUE,
    BaseSalary DECIMAL(12,2) NOT NULL,
    Bonus DECIMAL(12,2) NULL DEFAULT 0,
    HireDate DATE NOT NULL
);
GO

-- =======================================================================================
-- 7. RELATIONAL CORE: SALES SCHEMA (Customers, Orders, OrderItems with Composite PK)
-- =======================================================================================
CREATE TABLE sales.Customers (
    CustomerID INT IDENTITY(1,1) PRIMARY KEY,
    CustomerCode VARCHAR(20) NOT NULL UNIQUE,
    FirstName NVARCHAR(50) NOT NULL,
    LastName NVARCHAR(50) NOT NULL,
    Email dbo.EmailType UNIQUE,
    Phone VARCHAR(25) NULL,
    City NVARCHAR(50) NOT NULL,
    StateCode VARCHAR(2) NOT NULL,
    Country VARCHAR(50) NOT NULL DEFAULT 'USA',
    Balance DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedDate DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE TABLE sales.Orders (
    OrderID INT IDENTITY(1001, 1) PRIMARY KEY,
    CustomerID INT NOT NULL REFERENCES sales.Customers(CustomerID),
    OrderDate DATETIME2 NOT NULL DEFAULT GETDATE(),
    Status dbo.StatusType DEFAULT 'Pending',
    SubTotal dbo.MoneyType NOT NULL DEFAULT 0.00,
    TaxAmount dbo.MoneyType NOT NULL DEFAULT 0.00,
    TotalAmount AS (SubTotal + TaxAmount) PERSISTED,
    ShippingAddress NVARCHAR(200) NULL,
    Notes NVARCHAR(MAX) NULL,
    RowVersion ROWVERSION
);
GO

CREATE TABLE sales.OrderItems (
    OrderID INT NOT NULL REFERENCES sales.Orders(OrderID) ON DELETE CASCADE,
    LineNumber INT NOT NULL,
    ProductID INT NOT NULL REFERENCES inventory.Products(ProductID),
    Quantity INT NOT NULL CHECK (Quantity > 0),
    UnitPrice dbo.MoneyType NOT NULL,
    LineTotal AS (Quantity * UnitPrice) PERSISTED,
    CONSTRAINT PK_Sales_OrderItems PRIMARY KEY (OrderID, LineNumber)
);
GO

-- =======================================================================================
-- 8. AUDIT & LOGGING SCHEMA
-- =======================================================================================
CREATE TABLE audit.ActivityLogs (
    LogID BIGINT IDENTITY(1,1) PRIMARY KEY,
    EventType VARCHAR(50) NOT NULL,
    EventDescription NVARCHAR(MAX) NULL,
    UserID INT NULL,
    IPAddress VARCHAR(45) NULL,
    LoggedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
GO

CREATE TABLE audit.ProductPriceChanges (
    AuditID INT IDENTITY(1,1) PRIMARY KEY,
    ProductID INT NOT NULL,
    OldPrice dbo.MoneyType NOT NULL,
    NewPrice dbo.MoneyType NOT NULL,
    ChangedBy NVARCHAR(100) DEFAULT SUSER_SNAME(),
    ChangedAt DATETIME2 DEFAULT GETDATE()
);
GO

-- =======================================================================================
-- 9. TRIGGERS
-- =======================================================================================
CREATE TRIGGER inventory.trg_ProductPriceAudit
ON inventory.Products
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(BasePrice)
    BEGIN
        INSERT INTO audit.ProductPriceChanges (ProductID, OldPrice, NewPrice)
        SELECT d.ProductID, d.BasePrice, i.BasePrice
        FROM deleted d
        INNER JOIN inserted i ON d.ProductID = i.ProductID
        WHERE d.BasePrice <> i.BasePrice;
    END
END;
GO

-- =======================================================================================
-- 10. VIEWS (Standard, Recursive Hierarchy, and Schema-Bound Views)
-- =======================================================================================
CREATE VIEW sales.vw_ActiveCustomers
AS
SELECT 
    CustomerID, CustomerCode, FirstName, LastName, 
    FirstName + ' ' + LastName AS FullName,
    Email, City, StateCode, Balance, CreatedDate
FROM sales.Customers
WHERE IsActive = 1;
GO

CREATE VIEW sales.vw_OrderSummary
AS
SELECT 
    o.OrderID,
    c.CustomerID,
    c.FirstName + ' ' + c.LastName AS CustomerName,
    c.Email AS CustomerEmail,
    o.OrderDate,
    o.Status,
    o.SubTotal,
    o.TaxAmount,
    o.TotalAmount,
    COUNT(oi.LineNumber) AS ItemCount,
    SUM(oi.Quantity) AS TotalUnits
FROM sales.Orders o
INNER JOIN sales.Customers c ON o.CustomerID = c.CustomerID
LEFT JOIN sales.OrderItems oi ON o.OrderID = oi.OrderID
GROUP BY 
    o.OrderID, c.CustomerID, c.FirstName, c.LastName, 
    c.Email, o.OrderDate, o.Status, o.SubTotal, o.TaxAmount, o.TotalAmount;
GO

CREATE VIEW hr.vw_EmployeeOrgChart
AS
WITH OrgHierarchy AS (
    -- Anchor: Top-level managers
    SELECT 
        EmployeeID, ManagerID, FirstName, LastName, JobTitle, 
        DepartmentID, BaseSalary, 0 AS HierarchyLevel,
        CAST(FirstName + ' ' + LastName AS NVARCHAR(500)) AS ManagerChain
    FROM hr.Employees
    WHERE ManagerID IS NULL

    UNION ALL

    -- Recursive member: Subordinates
    SELECT 
        e.EmployeeID, e.ManagerID, e.FirstName, e.LastName, e.JobTitle, 
        e.DepartmentID, e.BaseSalary, h.HierarchyLevel + 1,
        CAST(h.ManagerChain + ' -> ' + e.FirstName + ' ' + e.LastName AS NVARCHAR(500))
    FROM hr.Employees e
    INNER JOIN OrgHierarchy h ON e.ManagerID = h.EmployeeID
)
SELECT * FROM OrgHierarchy;
GO

-- Schema-bound view for reporting
CREATE VIEW reporting.vw_MonthlySalesSummary
WITH SCHEMABINDING
AS
SELECT 
    YEAR(o.OrderDate) AS OrderYear,
    MONTH(o.OrderDate) AS OrderMonth,
    COUNT_BIG(*) AS OrderCount,
    SUM(ISNULL(o.SubTotal, 0)) AS TotalRevenue
FROM sales.Orders o
GROUP BY YEAR(o.OrderDate), MONTH(o.OrderDate);
GO

-- =======================================================================================
-- 11. FUNCTIONS (Scalar, Inline TVF, Multi-Statement TVF)
-- =======================================================================================
-- 11.1 Scalar Function
CREATE FUNCTION dbo.fn_CalculateSalesTax
(
    @SubTotal DECIMAL(18,2),
    @StateCode VARCHAR(2)
)
RETURNS DECIMAL(18,2)
AS
BEGIN
    DECLARE @Rate DECIMAL(5,4) = 
        CASE @StateCode
            WHEN 'CA' THEN 0.0825
            WHEN 'NY' THEN 0.0800
            WHEN 'TX' THEN 0.0625
            WHEN 'FL' THEN 0.0600
            WHEN 'WA' THEN 0.0650
            ELSE 0.0500
        END;
    RETURN CAST(@SubTotal * @Rate AS DECIMAL(18,2));
END;
GO

-- 11.2 Inline Table-Valued Function (ITVF)
CREATE FUNCTION sales.fn_GetCustomerOrders
(
    @CustomerID INT
)
RETURNS TABLE
AS
RETURN
(
    SELECT 
        OrderID, OrderDate, Status, SubTotal, TaxAmount, TotalAmount
    FROM sales.Orders
    WHERE CustomerID = @CustomerID
);
GO

-- 11.3 Multi-Statement Table-Valued Function (MSTVF)
CREATE FUNCTION hr.fn_GetDepartmentStats
(
    @MinBudget DECIMAL(18,2)
)
RETURNS @DeptStats TABLE
(
    DepartmentID INT PRIMARY KEY,
    DepartmentName NVARCHAR(50),
    EmployeeCount INT,
    AverageSalary DECIMAL(12,2),
    TotalPayroll DECIMAL(12,2)
)
AS
BEGIN
    INSERT INTO @DeptStats
    SELECT 
        d.DepartmentID,
        d.DepartmentName,
        COUNT(e.EmployeeID),
        ISNULL(AVG(e.BaseSalary), 0),
        ISNULL(SUM(e.BaseSalary), 0)
    FROM hr.Departments d
    LEFT JOIN hr.Employees e ON d.DepartmentID = e.DepartmentID
    WHERE d.Budget >= @MinBudget
    GROUP BY d.DepartmentID, d.DepartmentName;

    RETURN;
END;
GO

-- =======================================================================================
-- 12. STORED PROCEDURES (Includes Multi-Result Sets & OUTPUT Parameters)
-- =======================================================================================
-- 12.1 Multi-Result Set Stored Procedure (Tests ExploreDB's tabbed result sets)
CREATE PROCEDURE dbo.sp_GetDashboardSummary
AS
BEGIN
    SET NOCOUNT ON;

    -- Result Set 1: High-Level KPI Metrics (Single row)
    SELECT 
        (SELECT COUNT(*) FROM sales.Customers WHERE IsActive = 1) AS ActiveCustomers,
        (SELECT COUNT(*) FROM sales.Orders) AS TotalOrders,
        (SELECT ISNULL(SUM(TotalAmount), 0) FROM sales.Orders) AS TotalGrossRevenue,
        (SELECT ISNULL(AVG(TotalAmount), 0) FROM sales.Orders) AS AvgOrderValue,
        (SELECT COUNT(*) FROM inventory.Products WHERE IsActive = 1) AS ActiveProducts;

    -- Result Set 2: Top 5 Recent Orders
    SELECT TOP 5 
        o.OrderID,
        c.FirstName + ' ' + c.LastName AS CustomerName,
        o.OrderDate,
        o.Status,
        o.TotalAmount
    FROM sales.Orders o
    INNER JOIN sales.Customers c ON o.CustomerID = c.CustomerID
    ORDER BY o.OrderID DESC;

    -- Result Set 3: Category Revenue Breakdown
    SELECT 
        c.CategoryName,
        COUNT(oi.ProductID) AS ItemsSold,
        ISNULL(SUM(oi.LineTotal), 0) AS CategoryRevenue
    FROM inventory.Categories c
    INNER JOIN inventory.Products p ON c.CategoryID = p.CategoryID
    LEFT JOIN sales.OrderItems oi ON p.ProductID = oi.ProductID
    GROUP BY c.CategoryName
    ORDER BY CategoryRevenue DESC;
END;
GO

-- 12.2 Procedure with Input & Output Parameters
CREATE PROCEDURE sales.sp_CreateQuickOrder
    @CustomerID INT,
    @ShippingAddress NVARCHAR(200),
    @Notes NVARCHAR(MAX) = 'Quick-entry test order',
    @NewOrderID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM sales.Customers WHERE CustomerID = @CustomerID)
    BEGIN
        RAISERROR('Customer not found.', 16, 1);
        RETURN -1;
    END

    INSERT INTO sales.Orders (CustomerID, ShippingAddress, Notes, Status, SubTotal, TaxAmount)
    VALUES (@CustomerID, @ShippingAddress, @Notes, 'Pending', 0.00, 0.00);

    SET @NewOrderID = SCOPE_IDENTITY();
    RETURN 0;
END;
GO

-- 12.3 Procedure testing Output Messages & Error Handling
CREATE PROCEDURE dbo.sp_TestMessagesAndErrors
    @ShouldFail BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    PRINT 'Step 1: Initializing execution...';
    WAITFOR DELAY '00:00:00.050';
    PRINT 'Step 2: Performing feature validation...';

    IF @ShouldFail = 1
    BEGIN
        PRINT 'Step 3: Simulating intentional business validation error.';
        THROW 50001, 'Test exception: The feature validation failed intentionally as requested.', 1;
    END

    PRINT 'Step 3: Execution completed successfully without errors.';
END;
GO

-- =======================================================================================
-- 13. SYNONYMS
-- =======================================================================================
CREATE SYNONYM dbo.Syn_Customers FOR sales.Customers;
CREATE SYNONYM dbo.Syn_Products FOR inventory.Products;
GO

-- =======================================================================================
-- 14. POPULATE FEATURE-CENTRIC TEST DATA
-- =======================================================================================
PRINT 'Populating Feature-Centric Test Data...';

-- 14.1 Binary payloads for Images, PDF and Binary
DECLARE @BasePng VARBINARY(MAX) = 0x89504E470D0A1A0A0000000D494844520000008C0000003C0806000000B40A524B000000017352474200AECE1CE90000000467414D410000B18F0BFC6105000000097048597300000EC300000EC301C76FA8640000029849444154785EED98C16DDC3010455D499A723DDB435A30E0227CF6D52DF8E402DC820239A2323BFCA4F63B929641DE00EF4271B822E79114F6E1C7CF6902B89587DC00D00361C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C0E29F17E6E973FA1D9FD3F4289EC3BEEC2ACCE57D299E88D797BAFF1EDC5398DE7C5BEFD3CB396A8DF60461FE82DE7C4BE4796FE57CBCD5BF331287097311CF8F601461E27C1FDFFEB4CFF1F4BC91F3123ADF611E0EA70A139FC79DB4167D59DC2B09E2628A1DDB14E6799A3EAE53A7E9BD9D7B29458E7DC418715EBDF95E4913C66CE5BC96C63C8FC13855989928C7579F20449121F65111A591C224C9AE22F493BF538ADB19A39C185BF35512C81CB106A372983039D4E2CCA7CC5AB4B00B6321D7058CBB5DF50D4551858A3B5E89990B55C65027616993C50F549BE3D6351A98F38569F48BCF9504ADF6AAADB35B579116E1AADC42E774F98A25FF66611A278C8AFCCEA37198306A01236BF1E6687D5BA476357E55948E3079DC2AB7B025CCD25FBDCF4A3C111BC2C84D923E9247E32EC2A85D1617A95548D55EB57584F9CE0993C788F4E61BAFC078ADB572D4953922E70B93BE61D4F786DC6D29AF8CA78AAEC6540551B90535C6CC3C47F53DD2BC7245BECA8927EE7F79C2E42845CE85E816B211F2286FC851C5C60773A1378612A61579E36CE6A46B78344E15268A108FDDBCBB6221D7FF4796C80568165DFC8792FF456DE616C4B74CEB7AC9D1BA567A39F9FD46645761F662B390703710062C10062C10062C861406C60561C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C00261C0E217259E4A73BCDC0BAD0000000049454E44AE426082;
DECLARE @Png45KB VARBINARY(MAX) = @BasePng + CONVERT(VARBINARY(MAX), REPLICATE(CAST(' ' AS VARCHAR(MAX)), 45514));
DECLARE @Jpeg2KB VARBINARY(MAX) = 0xFFD8FFE000104A46494600010101006000600000FFDB0043000302020302020303030304030304050805050404050A070706080C0A0C0C0B0A0B0B0D0E12100D0E110E0B0B1016101113141515150C0F171816141812141514FFDB00430103040405040509050509140D0B0D1414141414141414141414141414141414141414141414141414141414141414141414141414141414141414141414141414FFC0001108003C007803012200021101031101FFC4001F0000010501010101010100000000000000000102030405060708090A0BFFC400B5100002010303020403050504040000017D01020300041105122131410613516107227114328191A1082342B1C11552D1F02433627282090A161718191A25262728292A3435363738393A434445464748494A535455565758595A636465666768696A737475767778797A838485868788898A92939495969798999AA2A3A4A5A6A7A8A9AAB2B3B4B5B6B7B8B9BAC2C3C4C5C6C7C8C9CAD2D3D4D5D6D7D8D9DAE1E2E3E4E5E6E7E8E9EAF1F2F3F4F5F6F7F8F9FAFFC4001F0100030101010101010101010000000000000102030405060708090A0BFFC400B51100020102040403040705040400010277000102031104052131061241510761711322328108144291A1B1C109233352F0156272D10A162434E125F11718191A262728292A35363738393A434445464748494A535455565758595A636465666768696A737475767778797A82838485868788898A92939495969798999AA2A3A4A5A6A7A8A9AAB2B3B4B5B6B7B8B9BAC2C3C4C5C6C7C8C9CAD2D3D4D5D6D7D8D9DAE2E3E4E5E6E7E8E9EAF2F3F4F5F6F7F8F9FAFFDA000C03010002110311003F00F2AA28A2BF223F0A0A28A2800A28A2800A28A2800A28A2800A28A2800A28A2800A28A2800A58E3696454452EEC42AAA8C927B00292BD7BF651F01FFC27FF001C3C3F6F245E65969EE752B918C8DB161941F62FB07E35D585A0F135E149757F8757F25A97084AACD5386EDA4BD59E69E21F0B6B5E12BD4B3D7348BFD1AEDD048B06A16CF04850920305700E320F3EC6B32BECAFDAC3C07E27F167C3DF0C78ABC47A5FF656BB69AA4FA75D0696270B6B34C4DBBB346C542AFCABC9EADCF5AE7FC6FE1CF861F097E2158782C7C38BFD76FEC1ACA59B5EB9D5278C5CEF910316887C823F9F6EE030586DC739AECFECF97B5E494B953928ABDEFAABED6E89EB7B6BA6E7B38BCB1D09CED2B42293BCAEB7BE9649EB74D76D2F73E65D1BC3DAAF88E69E2D274CBCD5258226B8952CADDE668E35FBCEC141C28C8C93C0CD67D7DDBE2AB4D07C4FFB4C7C42B18F4BBBD2EF34FF000BDC49777B65ABDCC26FA4F2E028596364DAAAA7694C956EA466B8DB7F825F0EE5D234CF88CDA19FF841BFE11292FEEF4E5BC9CAB6A28E136093CCDE0966C63763E5E94A58097273C64AC936F7D939EBB6DEE7DED7736A9934D4DD3A734DA935D56CE09BB5BA39EBBECED7EBF2256868BE1ED57C4B7325BE91A65E6AB711C6D33C5656EF33AC6BF79C85048519193D2BEABBDF839F08BC17E10F0E695E2989A1D735DD17FB4535A866BF96E12665DC16182185E0745E84B3EE03920E41AE13F62E18F893E2219CFF00C5397BC8EFF72A1E0A54EBBA3524AE949BB6B6714DB4FCEEB5FC1B396397355A8D2A925FBC6969BABDB75E924D1F3FD15F559BDF0DDCFEC63A0DAFFC22C9E7DEEB66C5263A848BE5DE942BF6C3C7CDC0C7967E51EBC55BF8BDF093E10780B4CD6FC286292C7C59A6E9B1DDDAEA4B36A13DC5E49804F9B1791F678E36271BD5F838FBB835A4F2E947E19A6B4F2BB71E6B2F45AF9A6BADD22396CA70538D48FC29BBDD5AEDA4B6DDD9F95D76B37F24D15F6EEBDF053E0FDAF8A7C69E1B1E0EBF825D17C3C9AF1BFB7D5E5DDC005A28D1F701918F99F77DE6C01815E1DFB457827C29A1E83F0F7C49E13D1A4F0F5AF8934C7BA9B4D6BB92E562752BD1DC963F7F1D87CA381CD635F053A1173724ECEDA5FBCA3D52EB16BFE0056CAEAD1A72A8E517CBD15EFBC576E9CD1FBF4D99E274514579E78E1451450015D1F847E227883C096DAC41A15F8B04D5ED8D9DE32C11BBC909CE503B2964073FC241E9E82B9CA2AA3294758BB74FBF7FBCA84E54E4A50766BAA3A4D17E23788BC3FE12D67C3161A8795A16B055AF6CDA18E4594AFDD20B292A781CA91D07A0ADDD77F680F881E25F06C3E15D53C493DE689108C2C12431799FBB20A66509E612080725BB0AF3EA2B4F6D552B733E9D5F4DBEEE9D8D235EAC23C919B4ACD5AEF67BAF47D5753D36FF00F695F891A9EA0B7D75E2569EED6C25D2C4AF676F936D2152E87F77C92517E63F371D7AD73E9F163C591FC3D7F03AEB0E3C2CD2F9C74FF2A3C6EDDBFEFEDDF8DC338DD8CF6AE4A8A255EACAFCD26EFA3D5ED7BFE7AFAEA5BC55793BBA8FEF7D524FEF492F4491E8BA6FED0FF11348F053F84AD7C4F709A0340F6BF6678A276589860A2C8C85D460E000C303A62B9AF0578FB5EF879A95C5FF0087EFBFB3EEEE2D9ED2593C98E5DD13E372E1D4819C0E473EF5CFD14A55AACA5CD2936ED6DFA76F4F227EB15BDDF7DFBBB6AF4F4EDF23AFD07E2E78BBC33E0BD53C25A6EB2F6FE1DD4CB1BAB130C6EAFB800D86652C99007DD23A56BEB5FB447C45F117837FE115D4BC517179A19448DA096288C8EA84150D2ECF31B903AB1CE39CD79CD155F58AD6B73BD92DDECB65E8BA761C7115A11E58CDA566B77B3DD7A3EA77D77F1E7C757DAD6B1AB4FAE6FD4357D3BFB2AF66FB2403CDB5C63CBC04C2F1FC4A037BD60788BC7BAEF8B347D0B4AD56FBED561A1C0D6DA7C5E4C69E4C67195CAA82DF7472C49E2B028A8955A9256949BF9F9B7F9B6FD5B7D452AF5A49A94DB4FCDF97F92FB976414514566601451450014514500145145001451450014514500145145001451450014514500145145007FFD9;
DECLARE @BasePdf VARBINARY(MAX) = 0x255044462D312E340A312030206F626A0A3C3C202F54797065202F436174616C6F67202F5061676573203220302052203E3E0A656E646F626A0A322030206F626A0A3C3C202F54797065202F5061676573202F4B696473205B33203020525D202F436F756E742031203E3E0A656E646F626A0A332030206F626A0A3C3C202F54797065202F50616765202F506172656E74203220302052202F4D65646961426F78205B30203020333030203134345D202F436F6E74656E7473203420302052202F5265736F7572636573203C3C202F466F6E74203C3C202F4631203520302052203E3E203E3E203E3E0A656E646F626A0A342030206F626A0A3C3C202F4C656E677468203535203E3E0A73747265616D0A42540A2F46312031382054660A35302037322054640A284578706C6F72654442205465737420446F63756D656E742920546A0A45540A656E6473747265616D0A656E646F626A0A352030206F626A0A3C3C202F54797065202F466F6E74202F53756274797065202F5479706531202F42617365466F6E74202F48656C766574696361203E3E0A656E646F626A0A787265660A3020360A303030303030303030302036353533352066200A30303030303030303039203030303030206E200A30303030303030303538203030303030206E200A30303030303030313135203030303030206E200A30303030303030323432203030303030206E200A30303030303030333438203030303030206E200A747261696C65720A3C3C202F53697A652036202F526F6F74203120302052203E3E0A7374617274787265660A3432360A2525454F46;
DECLARE @Pdf1200KB VARBINARY(MAX) = @BasePdf + CONVERT(VARBINARY(MAX), REPLICATE(CAST(' ' AS VARCHAR(MAX)), 1257695));
DECLARE @Binary128KB VARBINARY(MAX) = CONVERT(VARBINARY(MAX), REPLICATE(CAST('EXPLOREDB_BINARY_PAYLOAD_TEST_128K_' AS VARCHAR(MAX)), 3745));
DECLARE @Binary64B VARBINARY(64) = 0x112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00;
DECLARE @Binary65B VARBINARY(MAX) = CAST(0x112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00 AS VARBINARY(MAX)) + 0xFF;

-- 14.2 MediaAssets Test Data
INSERT INTO dbo.MediaAssets (AssetName, Category, ShortHash16, ShortHash32, ShortBinary64, Overlay_Image, DocumentFile, BinaryPayload)
VALUES 
('App_Logo_Overlay.png', 'Brand / UI', 0x3F2504E04F8911D39A0C0305E82C3301, 0xE3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855, @Binary64B, @Png45KB, NULL, NULL),
('Product_Showcase_Banner.jpg', 'Marketing', 0x5D41402ABC4B2A76B9719D911017C592, 0xBA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD, @Binary64B, @Jpeg2KB, NULL, NULL),
('Enterprise_Architecture_Manual.pdf', 'Documentation', 0x098F6BCD4621D373CAD4E832627B4F6F, 0x2C26B46B68FFC68FF99B453C1D30413413422D706483BFA0F98A5E886266E7AE, @Binary64B, NULL, @Pdf1200KB, NULL),
('Quick_Start_Guide.pdf', 'Documentation', 0x1F2E3D4C5B6A7890123456789ABCDEF0, 0xAFAF1234567890BCDEFA1234567890BCDEFA1234567890BCDEFA1234567890BC, @Binary64B, NULL, @BasePdf, NULL),
('Sensor_Firmware_Image.bin', 'Firmware', 0xAA55AA55AA55AA55AA55AA55AA55AA55, 0xBB66BB66BB66BB66BB66BB66BB66BB66BB66BB66BB66BB66BB66BB66BB66BB66, @Binary64B, NULL, NULL, @Binary128KB),
('Complete_Multi_Asset_Package', 'Bundle', 0x123456789ABCDEF0123456789ABCDEF0, 0xFEDCBA9876543210FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210, @Binary64B, @Png45KB, @Pdf1200KB, @Binary128KB),
('Boundary_Test_Exact_64_Bytes', 'Edge Case', 0x64646464646464646464646464646464, 0x6464646464646464646464646464646464646464646464646464646464646464, @Binary64B, NULL, NULL, CAST(@Binary64B AS VARBINARY(MAX))),
('Boundary_Test_Exact_65_Bytes', 'Edge Case', 0x65656565656565656565656565656565, 0x6565656565656565656565656565656565656565656565656565656565656565, @Binary64B, NULL, NULL, @Binary65B),
('Hex_Sorting_Test_Lowest_Value', 'Edge Case', 0x00000000000000000000000000000001, 0x0000000000000000000000000000000000000000000000000000000000000001, 0x00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001, NULL, NULL, NULL),
('Hex_Sorting_Test_Highest_Value', 'Edge Case', 0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF, NULL, NULL, NULL),
('All_Binary_Null_Row', 'Edge Case', NULL, NULL, NULL, NULL, NULL, NULL);

-- 14.3 AllDataTypesTest Data
INSERT INTO dbo.AllDataTypesTest (
    Title, Col_Bit, Col_TinyInt, Col_SmallInt, Col_Int, Col_BigInt,
    Col_Decimal, Col_Numeric, Col_Money, Col_SmallMoney, Col_Float, Col_Real,
    Col_Date, Col_Time, Col_DateTime, Col_SmallDateTime, Col_DateTime2, Col_DateTimeOffset,
    Col_Char, Col_VarChar, Col_VarCharMax, Col_NChar, Col_NVarChar, Col_NVarCharMax,
    Col_Xml, Col_Json, Col_Binary16, Col_VarBinary32, Col_VarBinary64,
    Col_ImagePng, Col_ImageJpeg, Col_PdfDoc, Col_BinaryBlob
)
VALUES 
(
    'Standard Values', 1, 42, 1024, 65536, 922337203685477580,
    12345.6789, 9999.99, 1250.75, 45.50, 3.1415926535, 2.71828,
    '2026-10-23', '14:30:15.1234567', '2026-10-23 14:30:15', '2026-10-23 14:30:00',
    '2026-10-23 14:30:15.9876543', '2026-10-23 14:30:15.9876543 +05:30',
    'FIXED_CODE', 'Standard varchar string', 'Large text block in varchar(max) for testing grid clipping and cell expansion...',
    N'UNICODE_FX', N'Unicode string with emojis 🚀✨ and non-latin text: こんにちは世界',
    N'# ExploreDB\n\nFull Markdown and Unicode text inside NVARCHAR(MAX)...',
    CAST('<Customer><Id>101</Id><Name>Alice Smith</Name><Tier>Gold</Tier></Customer>' AS XML),
    N'{"userId": 101, "name": "Alice Smith", "settings": {"theme": "dark", "pageSize": 50}}',
    0x3F2504E04F8911D39A0C0305E82C3301,
    0xE3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855,
    @Binary64B,
    @Png45KB, @Jpeg2KB, @Pdf1200KB, @Binary128KB
),
(
    'Minimum Boundary Values', 0, 0, -32768, -2147483648, -9223372036854775808,
    -99999999999999.9999, -9999999999.99, -922337203685477.5808, -214748.3648, -1.79E+308, -3.40E+38,
    '1753-01-01', '00:00:00.0000000', '1753-01-01 00:00:00', '1900-01-01 00:00:00',
    '0001-01-01 00:00:00.0000000', '0001-01-01 00:00:00.0000000 -12:00',
    'MIN_CHAR  ', 'Min string', 'Min varchar(max)', N'MIN_NCHAR ', N'Min nvarchar', N'Min nvarchar(max)',
    CAST('<Root empty="true"/>' AS XML), N'[]',
    0x00000000000000000000000000000000, 0x0000000000000000000000000000000000000000000000000000000000000000,
    0x00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000,
    NULL, NULL, NULL, NULL
),
(
    'Maximum Boundary Values', 1, 255, 32767, 2147483647, 9223372036854775807,
    99999999999999.9999, 9999999999.99, 922337203685477.5807, 214748.3647, 1.79E+308, 3.40E+38,
    '9999-12-31', '23:59:59.9999999', '9999-12-31 23:59:59', '2079-06-06 23:59:00',
    '9999-12-31 23:59:59.9999999', '9999-12-31 23:59:59.9999999 +14:00',
    'MAX_CHAR  ', 'Max string', 'Max varchar(max)', N'MAX_NCHAR ', N'Max nvarchar', N'Max nvarchar(max)',
    CAST('<Document valid="true"><Metadata count="999"/></Document>' AS XML),
    N'{"status":"max","values":[1,2,3,4,5]}',
    0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF,
    0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF,
    @Png45KB, @Jpeg2KB, @Pdf1200KB, @Binary128KB
),
(
    'All Null Values', NULL, NULL, NULL, NULL, NULL,
    NULL, NULL, NULL, NULL, NULL, NULL,
    NULL, NULL, NULL, NULL, NULL, NULL,
    NULL, NULL, NULL, NULL, NULL, NULL,
    NULL, NULL, NULL, NULL, NULL,
    NULL, NULL, NULL, NULL
);

-- 14.4 Inventory Categories & Warehouses
INSERT INTO inventory.Categories (CategoryName, Description) VALUES 
('Electronics', 'Smartphones, Laptops, Gadgets and Monitors'),
('Accessories', 'Cables, Adapters, Cases and Stands'),
('Furniture', 'Ergonomic Chairs, Standing Desks, and Shelving'),
('Networking', 'Routers, Switches, Access Points and Patch Panels'),
('Software', 'Operating Systems, Productivity Suites, and Cloud Licenses');

INSERT INTO inventory.Warehouses (WarehouseCode, LocationName, City, Country) VALUES 
('WH-WEST', 'Pacific Hub Warehouse', 'Seattle', 'USA'),
('WH-EAST', 'Atlantic Gateway Facility', 'Boston', 'USA'),
('WH-CENTRAL', 'Midwest Logistics Depot', 'Chicago', 'USA'),
('WH-SOUTH', 'Gulf Distribution Center', 'Austin', 'USA'),
('WH-EURO', 'European Operations Hub', 'Frankfurt', 'Germany');

-- 14.5 Inventory Products
INSERT INTO inventory.Products (CategoryID, SKU, ProductName, CostPrice, BasePrice, Overlay_Image) VALUES 
(1, 'ELEC-MBP-16', 'Pro Developer Laptop 16-inch', 1800.00, 2499.00, @Png45KB),
(1, 'ELEC-DISP-4K', 'UltraHD 32-inch 4K HDR Monitor', 350.00, 599.99, @Jpeg2KB),
(2, 'ACC-MECH-KB', 'Custom Mechanical Wireless Keyboard', 65.00, 149.50, @Png45KB),
(2, 'ACC-ERGO-MS', 'Ergonomic Precision Bluetooth Mouse', 35.00, 89.00, @Jpeg2KB),
(3, 'FURN-DESK-ST', 'Motorized Dual-Motor Standing Desk', 280.00, 549.00, @Png45KB),
(3, 'FURN-CHR-ERG', 'High-Back Lumbar Mesh Chair', 150.00, 329.00, @Jpeg2KB),
(4, 'NET-RTR-WIFI7', 'Enterprise Tri-Band Wi-Fi 7 Router', 190.00, 399.00, @Png45KB),
(4, 'NET-SW-24POE', 'Managed 24-Port Gigabit PoE Switch', 220.00, 449.00, @Jpeg2KB),
(5, 'SOFT-IDELIC', 'Enterprise IDE Annual Seat License', 100.00, 240.00, @Png45KB),
(5, 'SOFT-DBTOOL', 'ExploreDB Enterprise Single License', 50.00, 120.00, @Jpeg2KB);

-- Distribute stock
INSERT INTO inventory.Stock (WarehouseID, ProductID, QuantityOnHand, ReorderLevel)
SELECT w.WarehouseID, p.ProductID, (w.WarehouseID * 15) + (p.ProductID * 7), 10
FROM inventory.Warehouses w
CROSS JOIN inventory.Products p;

-- 14.6 HR Departments & Employees
INSERT INTO hr.Departments (DepartmentName, Budget) VALUES 
('Executive', 500000.00),
('Engineering', 1200000.00),
('Sales & Marketing', 800000.00),
('Operations', 400000.00);

-- Employees (with Manager Hierarchy)
-- Level 0 (CEO)
INSERT INTO hr.Employees (DepartmentID, ManagerID, FirstName, LastName, JobTitle, Email, BaseSalary, Bonus, HireDate)
VALUES (1, NULL, 'Eleanor', 'Vance', 'Chief Executive Officer', 'eleanor.vance@company.com', 250000.00, 50000.00, '2020-01-15');

DECLARE @CEO_ID INT = SCOPE_IDENTITY();

-- Level 1 (VPs reporting to CEO)
INSERT INTO hr.Employees (DepartmentID, ManagerID, FirstName, LastName, JobTitle, Email, BaseSalary, Bonus, HireDate) VALUES 
(2, @CEO_ID, 'Marcus', 'Brody', 'VP of Engineering', 'marcus.brody@company.com', 185000.00, 25000.00, '2020-03-01'),
(3, @CEO_ID, 'Sophia', 'Chen', 'VP of Sales', 'sophia.chen@company.com', 170000.00, 35000.00, '2020-04-15'),
(4, @CEO_ID, 'Liam', 'Gallagher', 'VP of Operations', 'liam.gallagher@company.com', 160000.00, 20000.00, '2020-05-20');

-- Level 2 (Staff reporting to VPs)
DECLARE @VP_ENG INT = (SELECT EmployeeID FROM hr.Employees WHERE Email = 'marcus.brody@company.com');
DECLARE @VP_SALES INT = (SELECT EmployeeID FROM hr.Employees WHERE Email = 'sophia.chen@company.com');

INSERT INTO hr.Employees (DepartmentID, ManagerID, FirstName, LastName, JobTitle, Email, BaseSalary, Bonus, HireDate) VALUES 
(2, @VP_ENG, 'David', 'Kim', 'Principal Architect', 'david.kim@company.com', 150000.00, 15000.00, '2021-02-01'),
(2, @VP_ENG, 'Rachel', 'Green', 'Lead DevOps Engineer', 'rachel.green@company.com', 135000.00, 12000.00, '2021-06-15'),
(2, @VP_ENG, 'Carlos', 'Mendoza', 'Senior FullStack Developer', 'carlos.mendoza@company.com', 125000.00, 10000.00, '2022-01-10'),
(3, @VP_SALES, 'Emma', 'Watson', 'Senior Account Executive', 'emma.watson@company.com', 95000.00, 45000.00, '2021-08-01'),
(3, @VP_SALES, 'Oliver', 'Queen', 'Regional Sales Manager', 'oliver.queen@company.com', 105000.00, 40000.00, '2021-09-15');

-- 14.7 Customers (100 realistic customers)
DECLARE @c INT = 1;
WHILE @c <= 100
BEGIN
    INSERT INTO sales.Customers (
        CustomerCode, FirstName, LastName, Email, Phone, 
        City, StateCode, Balance, IsActive, CreatedDate
    )
    VALUES (
        'CUST-' + RIGHT('000' + CAST(@c AS VARCHAR), 4),
        CHOOSE((@c % 10) + 1, 'James', 'Mary', 'John', 'Patricia', 'Robert', 'Jennifer', 'Michael', 'Linda', 'William', 'Elizabeth'),
        CHOOSE((@c % 10) + 1, 'Smith', 'Johnson', 'Williams', 'Brown', 'Jones', 'Garcia', 'Miller', 'Davis', 'Rodriguez', 'Martinez'),
        'customer' + CAST(@c AS VARCHAR) + '@example.com',
        '555-' + RIGHT('0000' + CAST(@c * 17 AS VARCHAR), 4),
        CHOOSE((@c % 8) + 1, 'Los Angeles', 'New York', 'Austin', 'Miami', 'Chicago', 'Seattle', 'Denver', 'Dallas'),
        CHOOSE((@c % 8) + 1, 'CA', 'NY', 'TX', 'FL', 'IL', 'WA', 'CO', 'TX'),
        CAST((@c * 13.50) % 500 AS DECIMAL(12,2)),
        CASE WHEN @c % 15 = 0 THEN 0 ELSE 1 END,
        DATEADD(DAY, -(@c * 3), GETDATE())
    );
    SET @c = @c + 1;
END;

-- 14.8 Orders (500 realistic orders for testing pagination, filtering & sorting)
DECLARE @o INT = 1;
WHILE @o <= 500
BEGIN
    DECLARE @CustID INT = (@o % 100) + 1;
    DECLARE @State VARCHAR(2) = (SELECT StateCode FROM sales.Customers WHERE CustomerID = @CustID);
    DECLARE @SubTotal DECIMAL(18,2) = CAST((@o * 27.50) % 3500 + 49.99 AS DECIMAL(18,2));
    DECLARE @Tax DECIMAL(18,2) = dbo.fn_CalculateSalesTax(@SubTotal, @State);
    DECLARE @Status VARCHAR(20) = CHOOSE((@o % 5) + 1, 'Completed', 'Processing', 'Shipped', 'Pending', 'Cancelled');

    INSERT INTO sales.Orders (CustomerID, OrderDate, Status, SubTotal, TaxAmount, ShippingAddress, Notes)
    VALUES (
        @CustID,
        DATEADD(HOUR, -(@o * 8), GETDATE()),
        @Status,
        @SubTotal,
        @Tax,
        CAST(@o AS VARCHAR) + ' Tech Blvd, Suite ' + CAST((@o % 50) + 1 AS VARCHAR),
        'Priority package delivery instruction #' + CAST(@o AS VARCHAR)
    );

    DECLARE @CurrentOrderID INT = SCOPE_IDENTITY();

    -- Add 1 to 3 items per order
    DECLARE @items INT = (@o % 3) + 1;
    DECLARE @itemIdx INT = 1;
    WHILE @itemIdx <= @items
    BEGIN
        DECLARE @ProdID INT = ((@o + @itemIdx) % 10) + 1;
        DECLARE @UnitPrice DECIMAL(18,2) = (SELECT BasePrice FROM inventory.Products WHERE ProductID = @ProdID);
        DECLARE @Qty INT = (@itemIdx % 2) + 1;

        INSERT INTO sales.OrderItems (OrderID, LineNumber, ProductID, Quantity, UnitPrice)
        VALUES (@CurrentOrderID, @itemIdx, @ProdID, @Qty, @UnitPrice);

        SET @itemIdx = @itemIdx + 1;
    END;

    SET @o = @o + 1;
END;

-- 14.9 Audit Logs (100 events)
DECLARE @log INT = 1;
WHILE @log <= 100
BEGIN
    INSERT INTO audit.ActivityLogs (EventType, EventDescription, UserID, IPAddress, LoggedAt)
    VALUES (
        CHOOSE((@log % 5) + 1, 'UserLogin', 'OrderCreated', 'StockAdjusted', 'ExportExecuted', 'PasswordReset'),
        'System event captured automatically during test cycle #' + CAST(@log AS VARCHAR),
        (@log % 50) + 1,
        '192.168.1.' + CAST((@log % 250) + 1 AS VARCHAR),
        DATEADD(MINUTE, -(@log * 25), GETDATE())
    );
    SET @log = @log + 1;
END;

PRINT '=======================================================================================';
PRINT 'ExploreDB_Lite Feature-Centric Database Created & Populated Successfully!';
PRINT 'Verified: All SQL Data Types, Images (PNG/JPEG), PDFs, Binary (128KB), RowVersion,';
PRINT 'Multi-Schema, Views, Stored Procedures, Functions, Triggers, Sequences & 500 Orders.';
PRINT '=======================================================================================';
GO
