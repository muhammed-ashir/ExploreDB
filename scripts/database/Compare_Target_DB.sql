-- =======================================================================================
-- EXPLOREDB: SQL COMPARE TEST DATABASE - TARGET (CompareDB_Target) [ENTERPRISE SUITE]
-- =======================================================================================
-- Comprehensive enterprise schema with 100+ database objects across 8 domains:
--   dbo, sales, inventory, billing, crm, hr, reporting, audit
-- Features:
--   - Deprecated objects (Target only): Tables, views, procedures, functions to be dropped
--   - Modified objects: Older column definitions, narrower data types, older procedure/view SQL
--   - Identical objects: 100% matching tables, views, procedures, functions across Source & Target
--   - Missing objects (exist only in Source)
--
-- Target Database: CompareDB_Target
-- =======================================================================================
USE master;
GO

IF DB_ID('CompareDB_Target') IS NOT NULL
BEGIN
    ALTER DATABASE CompareDB_Target SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE CompareDB_Target;
END
GO

CREATE DATABASE CompareDB_Target;
GO

USE CompareDB_Target;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE SCHEMA sales;
GO

CREATE SCHEMA inventory;
GO

CREATE SCHEMA billing;
GO

CREATE SCHEMA crm;
GO

CREATE SCHEMA hr;
GO

CREATE SCHEMA reporting;
GO

CREATE SCHEMA audit;
GO

CREATE TYPE dbo.IdListType AS TABLE (
    Id INT NOT NULL PRIMARY KEY
);
GO

CREATE TYPE sales.OrderItemType AS TABLE (
    ProductID INT NOT NULL,
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(18,2) NOT NULL
);
GO

CREATE TABLE dbo.Categories (
    CategoryID INT IDENTITY(1,1) NOT NULL,
    CategoryName NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Categories_IsActive DEFAULT (1),
    CONSTRAINT PK_Categories PRIMARY KEY CLUSTERED (CategoryID)
);
GO

CREATE TABLE dbo.Countries (
    CountryCode CHAR(2) NOT NULL,
    CountryName NVARCHAR(100) NOT NULL,
    Continent NVARCHAR(50) NOT NULL,
    CurrencyCode CHAR(3) NOT NULL,
    CONSTRAINT PK_Countries PRIMARY KEY CLUSTERED (CountryCode)
);
GO

CREATE TABLE dbo.Currencies (
    CurrencyCode CHAR(3) NOT NULL,
    CurrencyName NVARCHAR(50) NOT NULL,
    Symbol NVARCHAR(5) NOT NULL,
    ExchangeRateToUSD DECIMAL(18,6) NOT NULL CONSTRAINT DF_Currencies_Rate DEFAULT (1.0),
    CONSTRAINT PK_Currencies PRIMARY KEY CLUSTERED (CurrencyCode)
);
GO

CREATE TABLE dbo.AppConfiguration (
    ConfigKey NVARCHAR(100) NOT NULL,
    ConfigValue NVARCHAR(MAX) NOT NULL,
    Category NVARCHAR(50) NOT NULL,
    Description NVARCHAR(255) NULL,
    LastModifiedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_AppConfig_LastModified DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_AppConfiguration PRIMARY KEY CLUSTERED (ConfigKey)
);
GO

CREATE TABLE dbo.SystemParameters (
    ParamID INT IDENTITY(1,1) NOT NULL,
    ParamGroup NVARCHAR(50) NOT NULL,
    ParamName NVARCHAR(100) NOT NULL,
    ParamValue NVARCHAR(255) NOT NULL,
    IsEncrypted BIT NOT NULL CONSTRAINT DF_SysParams_Encrypted DEFAULT (0),
    CONSTRAINT PK_SystemParameters PRIMARY KEY CLUSTERED (ParamID)
);
GO

CREATE TABLE dbo.Roles (
    RoleID INT IDENTITY(1,1) NOT NULL,
    RoleName NVARCHAR(50) NOT NULL,
    Description NVARCHAR(200) NULL,
    CONSTRAINT PK_Roles PRIMARY KEY CLUSTERED (RoleID)
);
GO

CREATE TABLE dbo.Users (
    UserID INT IDENTITY(1,1) NOT NULL,
    Username NVARCHAR(50) NOT NULL,
    Email NVARCHAR(100) NOT NULL,
    PasswordHash VARBINARY(64) NOT NULL,
    PasswordSalt VARBINARY(32) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
    LastLogin DATETIME NULL,
    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Users_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Users PRIMARY KEY CLUSTERED (UserID),
    CONSTRAINT UQ_Users_Username UNIQUE (Username),
    CONSTRAINT UQ_Users_Email UNIQUE (Email)
);
GO

CREATE TABLE dbo.UserRoles (
    UserID INT NOT NULL,
    RoleID INT NOT NULL,
    AssignedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_UserRoles_Assigned DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_UserRoles PRIMARY KEY CLUSTERED (UserID, RoleID),
    CONSTRAINT FK_UserRoles_Users FOREIGN KEY (UserID) REFERENCES dbo.Users(UserID) ON DELETE CASCADE,
    CONSTRAINT FK_UserRoles_Roles FOREIGN KEY (RoleID) REFERENCES dbo.Roles(RoleID) ON DELETE CASCADE
);
GO

CREATE TABLE dbo.Customers (
    CustomerID INT IDENTITY(1,1) NOT NULL,
    FirstName NVARCHAR(100) NOT NULL,
    LastName NVARCHAR(100) NOT NULL,
    Email NVARCHAR(100) NOT NULL,
    Phone NVARCHAR(30) NULL,
    CountryCode CHAR(2) NOT NULL,
    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Customers_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Customers PRIMARY KEY CLUSTERED (CustomerID),
    CONSTRAINT FK_Customers_Countries FOREIGN KEY (CountryCode) REFERENCES dbo.Countries(CountryCode)
);
GO

CREATE TABLE dbo.CustomerAddresses (
    AddressID INT IDENTITY(1,1) NOT NULL,
    CustomerID INT NOT NULL,
    AddressType NVARCHAR(20) NOT NULL,
    AddressLine1 NVARCHAR(150) NOT NULL,
    AddressLine2 NVARCHAR(150) NULL,
    City NVARCHAR(100) NOT NULL,
    StateProvince NVARCHAR(100) NULL,
    PostalCode NVARCHAR(20) NOT NULL,
    CountryCode CHAR(2) NOT NULL,
    IsDefault BIT NOT NULL CONSTRAINT DF_CustAddr_Default DEFAULT (0),
    CONSTRAINT PK_CustomerAddresses PRIMARY KEY CLUSTERED (AddressID),
    CONSTRAINT FK_CustAddr_Customers FOREIGN KEY (CustomerID) REFERENCES dbo.Customers(CustomerID) ON DELETE CASCADE,
    CONSTRAINT FK_CustAddr_Countries FOREIGN KEY (CountryCode) REFERENCES dbo.Countries(CountryCode)
);
GO

CREATE TABLE dbo.LegacyImportQueue (
    QueueID INT IDENTITY(1,1) NOT NULL,
    RawPayload NVARCHAR(MAX) NOT NULL,
    ReceivedAt DATETIME NOT NULL CONSTRAINT DF_LegacyQ_Rec DEFAULT (GETDATE()),
    Processed BIT NOT NULL CONSTRAINT DF_LegacyQ_Proc DEFAULT (0),
    CONSTRAINT PK_LegacyImportQueue PRIMARY KEY CLUSTERED (QueueID)
);
GO

CREATE TABLE dbo.LegacyConfigTable (
    OldKey NVARCHAR(50) NOT NULL,
    OldValue NVARCHAR(255) NULL,
    CONSTRAINT PK_LegacyConfig PRIMARY KEY CLUSTERED (OldKey)
);
GO

CREATE TABLE inventory.Warehouses (
    WarehouseID INT IDENTITY(1,1) NOT NULL,
    WarehouseName NVARCHAR(100) NOT NULL,
    LocationCode NVARCHAR(20) NOT NULL,
    CountryCode CHAR(2) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Warehouses_IsActive DEFAULT (1),
    CONSTRAINT PK_Warehouses PRIMARY KEY CLUSTERED (WarehouseID),
    CONSTRAINT FK_Warehouses_Countries FOREIGN KEY (CountryCode) REFERENCES dbo.Countries(CountryCode)
);
GO

CREATE TABLE inventory.Locations (
    LocationID INT IDENTITY(1,1) NOT NULL,
    WarehouseID INT NOT NULL,
    AisleCode NVARCHAR(10) NOT NULL,
    BinCode NVARCHAR(10) NOT NULL,
    MaxWeightKg DECIMAL(10,2) NOT NULL,
    CONSTRAINT PK_Locations PRIMARY KEY CLUSTERED (LocationID),
    CONSTRAINT FK_Locations_Warehouses FOREIGN KEY (WarehouseID) REFERENCES inventory.Warehouses(WarehouseID)
);
GO

CREATE TABLE inventory.UnitOfMeasures (
    UomCode NVARCHAR(10) NOT NULL,
    UomName NVARCHAR(50) NOT NULL,
    CONSTRAINT PK_UnitOfMeasures PRIMARY KEY CLUSTERED (UomCode)
);
GO

CREATE TABLE inventory.Suppliers (
    SupplierID INT IDENTITY(1,1) NOT NULL,
    SupplierName NVARCHAR(150) NOT NULL,
    ContactEmail NVARCHAR(100) NULL,
    Phone NVARCHAR(30) NULL,
    CountryCode CHAR(2) NOT NULL,
    PaymentTermsDays INT NOT NULL CONSTRAINT DF_Suppliers_Terms DEFAULT (30),
    IsActive BIT NOT NULL CONSTRAINT DF_Suppliers_IsActive DEFAULT (1),
    CONSTRAINT PK_Suppliers PRIMARY KEY CLUSTERED (SupplierID),
    CONSTRAINT FK_Suppliers_Countries FOREIGN KEY (CountryCode) REFERENCES dbo.Countries(CountryCode)
);
GO

CREATE TABLE inventory.Products (
    ProductID INT IDENTITY(1,1) NOT NULL,
    CategoryID INT NOT NULL,
    ProductCode NVARCHAR(50) NOT NULL,
    ProductName NVARCHAR(150) NOT NULL,
    UomCode NVARCHAR(10) NOT NULL,
    UnitPrice DECIMAL(10,2) NOT NULL,
    StandardCost DECIMAL(10,2) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Products_IsActive DEFAULT (1),
    CONSTRAINT PK_Products PRIMARY KEY CLUSTERED (ProductID),
    CONSTRAINT UQ_Products_ProductCode UNIQUE (ProductCode),
    CONSTRAINT FK_Products_Categories FOREIGN KEY (CategoryID) REFERENCES dbo.Categories(CategoryID),
    CONSTRAINT FK_Products_UOM FOREIGN KEY (UomCode) REFERENCES inventory.UnitOfMeasures(UomCode)
);
GO

CREATE TABLE inventory.StockLevels (
    StockID INT IDENTITY(1,1) NOT NULL,
    WarehouseID INT NOT NULL,
    ProductID INT NOT NULL,
    LocationID INT NULL,
    OnHandQuantity INT NOT NULL CONSTRAINT DF_StockLevels_OnHand DEFAULT (0),
    ReservedQuantity INT NOT NULL CONSTRAINT DF_StockLevels_Reserved DEFAULT (0),
    LastAuditUtc DATETIME2(3) NULL,
    CONSTRAINT PK_StockLevels PRIMARY KEY CLUSTERED (StockID),
    CONSTRAINT UQ_StockLevels_Warehouse_Product UNIQUE (WarehouseID, ProductID),
    CONSTRAINT FK_StockLevels_Warehouses FOREIGN KEY (WarehouseID) REFERENCES inventory.Warehouses(WarehouseID),
    CONSTRAINT FK_StockLevels_Products FOREIGN KEY (ProductID) REFERENCES inventory.Products(ProductID),
    CONSTRAINT FK_StockLevels_Locations FOREIGN KEY (LocationID) REFERENCES inventory.Locations(LocationID)
);
GO

CREATE TABLE inventory.StockMovements (
    MovementID BIGINT IDENTITY(1,1) NOT NULL,
    ProductID INT NOT NULL,
    FromWarehouseID INT NULL,
    ToWarehouseID INT NULL,
    Quantity INT NOT NULL,
    MovementType NVARCHAR(30) NOT NULL,
    CreatedByUserId INT NOT NULL,
    MovementDateUtc DATETIME2(3) NOT NULL CONSTRAINT DF_StockMovements_Date DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_StockMovements PRIMARY KEY CLUSTERED (MovementID),
    CONSTRAINT FK_StockMovements_Products FOREIGN KEY (ProductID) REFERENCES inventory.Products(ProductID),
    CONSTRAINT FK_StockMovements_FromWh FOREIGN KEY (FromWarehouseID) REFERENCES inventory.Warehouses(WarehouseID),
    CONSTRAINT FK_StockMovements_ToWh FOREIGN KEY (ToWarehouseID) REFERENCES inventory.Warehouses(WarehouseID),
    CONSTRAINT FK_StockMovements_Users FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users(UserID)
);
GO

CREATE TABLE inventory.LegacyStockLog (
    LogID INT IDENTITY(1,1) NOT NULL,
    ItemCode NVARCHAR(50) NOT NULL,
    OldQty INT NOT NULL,
    NewQty INT NOT NULL,
    LogTime DATETIME NOT NULL DEFAULT (GETDATE()),
    CONSTRAINT PK_LegacyStockLog PRIMARY KEY CLUSTERED (LogID)
);
GO

CREATE TABLE inventory.OldBinAssignments (
    AssignmentID INT IDENTITY(1,1) NOT NULL,
    BinName NVARCHAR(50) NOT NULL,
    ItemID INT NOT NULL,
    CONSTRAINT PK_OldBinAssign PRIMARY KEY CLUSTERED (AssignmentID)
);
GO

CREATE TABLE sales.PaymentMethods (
    MethodID INT IDENTITY(1,1) NOT NULL,
    MethodName NVARCHAR(50) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_PaymentMethods_IsActive DEFAULT (1),
    CONSTRAINT PK_PaymentMethods PRIMARY KEY CLUSTERED (MethodID)
);
GO

CREATE TABLE sales.ShippingCarriers (
    CarrierID INT IDENTITY(1,1) NOT NULL,
    CarrierName NVARCHAR(100) NOT NULL,
    TrackingUrlTemplate NVARCHAR(255) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Carriers_Active DEFAULT (1),
    CONSTRAINT PK_ShippingCarriers PRIMARY KEY CLUSTERED (CarrierID)
);
GO

CREATE TABLE sales.TaxJurisdictions (
    JurisdictionID INT IDENTITY(1,1) NOT NULL,
    CountryCode CHAR(2) NOT NULL,
    StateProvince NVARCHAR(100) NULL,
    TaxRatePercent DECIMAL(5,3) NOT NULL,
    TaxName NVARCHAR(50) NOT NULL,
    CONSTRAINT PK_TaxJurisdictions PRIMARY KEY CLUSTERED (JurisdictionID),
    CONSTRAINT FK_TaxJuris_Countries FOREIGN KEY (CountryCode) REFERENCES dbo.Countries(CountryCode)
);
GO

CREATE TABLE sales.Orders (
    OrderID INT IDENTITY(1,1) NOT NULL,
    OrderNumber NVARCHAR(30) NOT NULL,
    CustomerID INT NOT NULL,
    OrderDateUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Orders_Date DEFAULT (SYSUTCDATETIME()),
    OrderStatus NVARCHAR(30) NOT NULL CONSTRAINT DF_Orders_Status DEFAULT ('Pending'),
    SubTotal DECIMAL(18,2) NOT NULL CONSTRAINT DF_Orders_SubTotal DEFAULT (0),
    DiscountTotal DECIMAL(18,2) NOT NULL CONSTRAINT DF_Orders_Discount DEFAULT (0),
    TaxTotal DECIMAL(18,2) NOT NULL CONSTRAINT DF_Orders_Tax DEFAULT (0),
    ShippingTotal DECIMAL(18,2) NOT NULL CONSTRAINT DF_Orders_Shipping DEFAULT (0),
    ShippingCarrierID INT NULL,
    CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED (OrderID),
    CONSTRAINT UQ_Orders_OrderNumber UNIQUE (OrderNumber),
    CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerID) REFERENCES dbo.Customers(CustomerID),
    CONSTRAINT FK_Orders_Carriers FOREIGN KEY (ShippingCarrierID) REFERENCES sales.ShippingCarriers(CarrierID)
);
GO

CREATE TABLE sales.OrderItems (
    OrderItemID INT IDENTITY(1,1) NOT NULL,
    OrderID INT NOT NULL,
    ProductID INT NOT NULL,
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(18,2) NOT NULL,
    CONSTRAINT PK_OrderItems PRIMARY KEY CLUSTERED (OrderItemID),
    CONSTRAINT FK_OrderItems_Orders FOREIGN KEY (OrderID) REFERENCES sales.Orders(OrderID) ON DELETE CASCADE,
    CONSTRAINT FK_OrderItems_Products FOREIGN KEY (ProductID) REFERENCES inventory.Products(ProductID)
);
GO

CREATE TABLE sales.Invoices (
    InvoiceID INT IDENTITY(1,1) NOT NULL,
    InvoiceNumber NVARCHAR(50) NOT NULL,
    OrderID INT NOT NULL,
    InvoiceDateUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Invoices_Date DEFAULT (SYSUTCDATETIME()),
    DueDateUtc DATETIME NOT NULL,
    TotalAmount DECIMAL(18,2) NOT NULL,
    Status NVARCHAR(30) NOT NULL CONSTRAINT DF_Invoices_Status DEFAULT ('Unpaid'),
    CONSTRAINT PK_Invoices PRIMARY KEY CLUSTERED (InvoiceID),
    CONSTRAINT UQ_Invoices_Number UNIQUE (InvoiceNumber),
    CONSTRAINT FK_Invoices_Orders FOREIGN KEY (OrderID) REFERENCES sales.Orders(OrderID)
);
GO

CREATE TABLE sales.InvoiceItems (
    InvoiceItemID INT IDENTITY(1,1) NOT NULL,
    InvoiceID INT NOT NULL,
    Description NVARCHAR(250) NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    CONSTRAINT PK_InvoiceItems PRIMARY KEY CLUSTERED (InvoiceItemID),
    CONSTRAINT FK_InvItems_Invoices FOREIGN KEY (InvoiceID) REFERENCES sales.Invoices(InvoiceID) ON DELETE CASCADE
);
GO

CREATE TABLE sales.OldOrderArchive (
    ArchiveID INT IDENTITY(1,1) NOT NULL,
    LegacyOrderNumber NVARCHAR(50) NOT NULL,
    ArchiveDate DATETIME NOT NULL CONSTRAINT DF_Archive_Date DEFAULT (GETDATE()),
    DumpData NVARCHAR(MAX) NULL,
    CONSTRAINT PK_OldOrderArchive PRIMARY KEY CLUSTERED (ArchiveID)
);
GO

CREATE TABLE sales.LegacyDiscounts_2020 (
    DiscCode NVARCHAR(20) NOT NULL,
    Pct DECIMAL(5,2) NOT NULL,
    CONSTRAINT PK_LegacyDisc PRIMARY KEY CLUSTERED (DiscCode)
);
GO

CREATE TABLE billing.PaymentGateways (
    GatewayID INT IDENTITY(1,1) NOT NULL,
    GatewayName NVARCHAR(50) NOT NULL,
    ApiEndpoint NVARCHAR(255) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Gateways_Active DEFAULT (1),
    CONSTRAINT PK_PaymentGateways PRIMARY KEY CLUSTERED (GatewayID)
);
GO

CREATE TABLE billing.BillingPlans (
    PlanID INT IDENTITY(1,1) NOT NULL,
    PlanCode NVARCHAR(50) NOT NULL,
    PlanName NVARCHAR(100) NOT NULL,
    PricePerMonth DECIMAL(18,2) NOT NULL,
    IntervalMonths INT NOT NULL CONSTRAINT DF_Plans_Interval DEFAULT (1),
    IsActive BIT NOT NULL CONSTRAINT DF_Plans_Active DEFAULT (1),
    CONSTRAINT PK_BillingPlans PRIMARY KEY CLUSTERED (PlanID),
    CONSTRAINT UQ_BillingPlans_Code UNIQUE (PlanCode)
);
GO

CREATE TABLE billing.Subscriptions (
    SubscriptionID INT IDENTITY(1,1) NOT NULL,
    CustomerID INT NOT NULL,
    PlanID INT NOT NULL,
    StartDateUtc DATETIME2(3) NOT NULL,
    NextBillingDateUtc DATETIME2(3) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Subs_Active DEFAULT (1),
    CONSTRAINT PK_Subscriptions PRIMARY KEY CLUSTERED (SubscriptionID),
    CONSTRAINT FK_Subs_Customers FOREIGN KEY (CustomerID) REFERENCES dbo.Customers(CustomerID),
    CONSTRAINT FK_Subs_Plans FOREIGN KEY (PlanID) REFERENCES billing.BillingPlans(PlanID)
);
GO

CREATE TABLE billing.SubscriptionInvoices (
    SubInvoiceID INT IDENTITY(1,1) NOT NULL,
    SubscriptionID INT NOT NULL,
    InvoicePeriodStart DATE NOT NULL,
    InvoicePeriodEnd DATE NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    IsPaid BIT NOT NULL CONSTRAINT DF_SubInv_Paid DEFAULT (0),
    PaidAtUtc DATETIME2(3) NULL,
    CONSTRAINT PK_SubscriptionInvoices PRIMARY KEY CLUSTERED (SubInvoiceID),
    CONSTRAINT FK_SubInv_Subscriptions FOREIGN KEY (SubscriptionID) REFERENCES billing.Subscriptions(SubscriptionID)
);
GO

CREATE TABLE billing.LegacyPaymentLog (
    LogID INT IDENTITY(1,1) NOT NULL,
    RawResp NVARCHAR(MAX) NULL,
    LogTime DATETIME NOT NULL DEFAULT (GETDATE()),
    CONSTRAINT PK_LegacyPayLog PRIMARY KEY CLUSTERED (LogID)
);
GO

CREATE TABLE crm.LeadSources (
    SourceID INT IDENTITY(1,1) NOT NULL,
    SourceName NVARCHAR(50) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_LeadSources_Active DEFAULT (1),
    CONSTRAINT PK_LeadSources PRIMARY KEY CLUSTERED (SourceID)
);
GO

CREATE TABLE crm.OpportunityStages (
    StageID INT IDENTITY(1,1) NOT NULL,
    StageName NVARCHAR(50) NOT NULL,
    ProbabilityPercent DECIMAL(5,2) NOT NULL,
    SortOrder INT NOT NULL,
    CONSTRAINT PK_OpportunityStages PRIMARY KEY CLUSTERED (StageID)
);
GO

CREATE TABLE crm.Leads (
    LeadID INT IDENTITY(1,1) NOT NULL,
    FirstName NVARCHAR(50) NOT NULL,
    LastName NVARCHAR(50) NOT NULL,
    Email NVARCHAR(100) NOT NULL,
    Phone NVARCHAR(30) NULL,
    CompanyName NVARCHAR(100) NULL,
    SourceID INT NOT NULL,
    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Leads_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Leads PRIMARY KEY CLUSTERED (LeadID),
    CONSTRAINT FK_Leads_Sources FOREIGN KEY (SourceID) REFERENCES crm.LeadSources(SourceID)
);
GO

CREATE TABLE crm.Opportunities (
    OpportunityID INT IDENTITY(1,1) NOT NULL,
    OpportunityName NVARCHAR(150) NOT NULL,
    CustomerID INT NULL,
    LeadID INT NULL,
    StageID INT NOT NULL,
    EstValue DECIMAL(18,2) NOT NULL,
    CloseDate DATE NOT NULL,
    AssignedToUserId INT NOT NULL,
    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Opps_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Opportunities PRIMARY KEY CLUSTERED (OpportunityID),
    CONSTRAINT FK_Opps_Customers FOREIGN KEY (CustomerID) REFERENCES dbo.Customers(CustomerID),
    CONSTRAINT FK_Opps_Leads FOREIGN KEY (LeadID) REFERENCES crm.Leads(LeadID),
    CONSTRAINT FK_Opps_Stages FOREIGN KEY (StageID) REFERENCES crm.OpportunityStages(StageID),
    CONSTRAINT FK_Opps_Users FOREIGN KEY (AssignedToUserId) REFERENCES dbo.Users(UserID)
);
GO

CREATE TABLE crm.Activities (
    ActivityID BIGINT IDENTITY(1,1) NOT NULL,
    ActivityType NVARCHAR(30) NOT NULL,
    Subject NVARCHAR(200) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    LeadID INT NULL,
    OpportunityID INT NULL,
    PerformedByUserId INT NOT NULL,
    OccurredAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Activities_Occurred DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Activities PRIMARY KEY CLUSTERED (ActivityID),
    CONSTRAINT FK_Activities_Leads FOREIGN KEY (LeadID) REFERENCES crm.Leads(LeadID),
    CONSTRAINT FK_Activities_Opps FOREIGN KEY (OpportunityID) REFERENCES crm.Opportunities(OpportunityID),
    CONSTRAINT FK_Activities_Users FOREIGN KEY (PerformedByUserId) REFERENCES dbo.Users(UserID)
);
GO

CREATE TABLE crm.LegacyLeadArchive (
    OldLeadID INT NOT NULL,
    ArchivedAt DATETIME NOT NULL DEFAULT (GETDATE()),
    CONSTRAINT PK_LegacyLeadArch PRIMARY KEY CLUSTERED (OldLeadID)
);
GO

CREATE TABLE hr.Departments (
    DepartmentID INT IDENTITY(1,1) NOT NULL,
    DepartmentName NVARCHAR(100) NOT NULL,
    CostCenterCode NVARCHAR(20) NOT NULL,
    CONSTRAINT PK_Departments PRIMARY KEY CLUSTERED (DepartmentID)
);
GO

CREATE TABLE hr.JobTitles (
    JobTitleID INT IDENTITY(1,1) NOT NULL,
    TitleName NVARCHAR(100) NOT NULL,
    MinSalary DECIMAL(18,2) NOT NULL,
    MaxSalary DECIMAL(18,2) NOT NULL,
    CONSTRAINT PK_JobTitles PRIMARY KEY CLUSTERED (JobTitleID)
);
GO

CREATE TABLE hr.Employees (
    EmployeeID INT IDENTITY(1,1) NOT NULL,
    DepartmentID INT NOT NULL,
    JobTitleID INT NOT NULL,
    FirstName NVARCHAR(50) NOT NULL,
    LastName NVARCHAR(50) NOT NULL,
    Email NVARCHAR(100) NOT NULL,
    HireDate DATE NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Employees_Active DEFAULT (1),
    CONSTRAINT PK_Employees PRIMARY KEY CLUSTERED (EmployeeID),
    CONSTRAINT FK_Employees_Departments FOREIGN KEY (DepartmentID) REFERENCES hr.Departments(DepartmentID),
    CONSTRAINT FK_Employees_JobTitles FOREIGN KEY (JobTitleID) REFERENCES hr.JobTitles(JobTitleID)
);
GO

CREATE TABLE hr.CompensationHistory (
    CompID INT IDENTITY(1,1) NOT NULL,
    EmployeeID INT NOT NULL,
    BaseSalary DECIMAL(18,2) NOT NULL,
    EffectiveDate DATE NOT NULL,
    ApprovedByUserId INT NOT NULL,
    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Comp_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_CompensationHistory PRIMARY KEY CLUSTERED (CompID),
    CONSTRAINT FK_Comp_Employees FOREIGN KEY (EmployeeID) REFERENCES hr.Employees(EmployeeID),
    CONSTRAINT FK_Comp_Users FOREIGN KEY (ApprovedByUserId) REFERENCES dbo.Users(UserID)
);
GO

CREATE TABLE hr.LegacyStaffLog (
    StaffLogID INT IDENTITY(1,1) NOT NULL,
    Notes NVARCHAR(200) NULL,
    CONSTRAINT PK_LegacyStaffLog PRIMARY KEY CLUSTERED (StaffLogID)
);
GO

CREATE TABLE audit.LegacyAccessLog (
    AccessID INT IDENTITY(1,1) NOT NULL,
    UserName NVARCHAR(50) NOT NULL,
    LoginTime DATETIME NOT NULL DEFAULT (GETDATE()),
    CONSTRAINT PK_LegacyAccessLog PRIMARY KEY CLUSTERED (AccessID)
);
GO

CREATE VIEW dbo.vw_ActiveCategories
AS
SELECT 
    CategoryID,
    CategoryName,
    Description
FROM dbo.Categories
WHERE IsActive = 1;
GO

CREATE VIEW inventory.vw_WarehouseStock
AS
SELECT 
    w.WarehouseID,
    w.WarehouseName,
    p.ProductID,
    p.ProductName,
    p.ProductCode,
    s.OnHandQuantity,
    s.ReservedQuantity,
    s.OnHandQuantity - s.ReservedQuantity AS AvailableQuantity
FROM inventory.StockLevels s
INNER JOIN inventory.Warehouses w ON s.WarehouseID = w.WarehouseID
INNER JOIN inventory.Products p ON s.ProductID = p.ProductID;
GO

CREATE VIEW hr.vw_DepartmentHeadcount
AS
SELECT 
    d.DepartmentID,
    d.DepartmentName,
    COUNT(e.EmployeeID) AS TotalEmployees
FROM hr.Departments d
LEFT JOIN hr.Employees e ON d.DepartmentID = e.DepartmentID AND e.IsActive = 1
GROUP BY d.DepartmentID, d.DepartmentName;
GO

CREATE VIEW dbo.vw_ActiveUsers
AS
SELECT 
    u.UserID,
    u.Username,
    u.Email,
    u.LastLogin
FROM dbo.Users u
WHERE u.IsActive = 1;
GO

CREATE VIEW dbo.vw_CustomerSummary
AS
SELECT 
    c.CustomerID,
    c.FirstName + ' ' + c.LastName AS FullName,
    c.Email,
    COUNT(o.OrderID) AS TotalOrdersPlaced
FROM dbo.Customers c
LEFT JOIN sales.Orders o ON c.CustomerID = o.CustomerID
GROUP BY c.CustomerID, c.FirstName, c.LastName, c.Email;
GO

CREATE VIEW sales.vw_CustomerOrders
AS
SELECT 
    o.OrderID,
    o.OrderNumber,
    o.OrderDateUtc,
    o.OrderStatus,
    c.CustomerID,
    c.FirstName + ' ' + c.LastName AS CustomerName,
    c.Email AS CustomerEmail,
    o.SubTotal,
    o.DiscountTotal,
    o.TaxTotal,
    o.ShippingTotal
FROM sales.Orders o
INNER JOIN dbo.Customers c ON o.CustomerID = c.CustomerID;
GO

CREATE VIEW sales.vw_OpenInvoices
AS
SELECT 
    i.InvoiceID,
    i.InvoiceNumber,
    i.OrderID,
    i.InvoiceDateUtc,
    i.DueDateUtc,
    i.TotalAmount,
    i.Status
FROM sales.Invoices i
WHERE i.Status = 'Unpaid';
GO

CREATE VIEW inventory.vw_LowStockAlerts
AS
SELECT 
    p.ProductID,
    p.ProductCode,
    p.ProductName,
    ISNULL(SUM(s.OnHandQuantity - s.ReservedQuantity), 0) AS TotalAvailable
FROM inventory.Products p
LEFT JOIN inventory.StockLevels s ON p.ProductID = s.ProductID
WHERE p.IsActive = 1
GROUP BY p.ProductID, p.ProductCode, p.ProductName;
GO

CREATE VIEW inventory.vw_ProductCatalog
AS
SELECT 
    p.ProductID,
    p.ProductCode,
    p.ProductName,
    c.CategoryName,
    p.UnitPrice,
    p.StandardCost,
    p.IsActive
FROM inventory.Products p
INNER JOIN dbo.Categories c ON p.CategoryID = c.CategoryID;
GO

CREATE VIEW billing.vw_ActiveSubscriptions
AS
SELECT 
    s.SubscriptionID,
    s.CustomerID,
    c.FirstName + ' ' + c.LastName AS CustomerName,
    p.PlanName,
    p.PricePerMonth,
    s.StartDateUtc,
    s.NextBillingDateUtc,
    s.IsActive
FROM billing.Subscriptions s
INNER JOIN dbo.Customers c ON s.CustomerID = c.CustomerID
INNER JOIN billing.BillingPlans p ON s.PlanID = p.PlanID
WHERE s.IsActive = 1;
GO

CREATE VIEW crm.vw_PipelineSummary
AS
SELECT 
    st.StageID,
    st.StageName,
    COUNT(o.OpportunityID) AS OpportunityCount,
    ISNULL(SUM(o.EstValue), 0) AS TotalPipelineValue
FROM crm.OpportunityStages st
LEFT JOIN crm.Opportunities o ON st.StageID = o.StageID
GROUP BY st.StageID, st.StageName;
GO

CREATE VIEW hr.vw_EmployeeDirectory
AS
SELECT 
    e.EmployeeID,
    e.FirstName + ' ' + e.LastName AS FullName,
    e.Email AS WorkEmail,
    d.DepartmentName,
    jt.TitleName AS JobTitle,
    e.HireDate
FROM hr.Employees e
INNER JOIN hr.Departments d ON e.DepartmentID = d.DepartmentID
INNER JOIN hr.JobTitles jt ON e.JobTitleID = jt.JobTitleID
WHERE e.IsActive = 1;
GO

CREATE VIEW reporting.vw_ProductMarginRanking
AS
SELECT 
    p.ProductID,
    p.ProductName,
    c.CategoryName,
    p.UnitPrice,
    p.StandardCost,
    (p.UnitPrice - p.StandardCost) AS MarginAmount
FROM inventory.Products p
INNER JOIN dbo.Categories c ON p.CategoryID = c.CategoryID
WHERE p.IsActive = 1;
GO

CREATE VIEW dbo.vw_LegacyImportStatus
AS
SELECT QueueID, Processed, ReceivedAt FROM dbo.LegacyImportQueue;
GO

CREATE VIEW sales.vw_LegacyArchive_v1
AS
SELECT ArchiveID, LegacyOrderNumber, ArchiveDate FROM sales.OldOrderArchive;
GO

CREATE VIEW inventory.vw_DeprecatedBinStats
AS
SELECT BinName, COUNT(*) AS TotalAssigned FROM inventory.OldBinAssignments GROUP BY BinName;
GO

CREATE VIEW billing.vw_OldGatewayBatches
AS
SELECT LogID, LogTime FROM billing.LegacyPaymentLog;
GO

CREATE VIEW reporting.vw_DeprecatedSalesStats
AS
SELECT CAST(OrderDateUtc AS DATE) AS OrderDate, COUNT(*) AS TotalOrders
FROM sales.Orders
GROUP BY CAST(OrderDateUtc AS DATE);
GO

CREATE VIEW reporting.vw_OldQuarterlyReport
AS
SELECT YEAR(OrderDateUtc) AS RptYear, COUNT(*) AS Cnt
FROM sales.Orders
GROUP BY YEAR(OrderDateUtc);
GO

CREATE PROCEDURE dbo.sp_GetCategories
    @IsActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CategoryID, CategoryName, Description, IsActive
    FROM dbo.Categories
    WHERE (@IsActiveOnly = 0 OR IsActive = 1)
    ORDER BY CategoryName;
END;
GO

CREATE PROCEDURE inventory.sp_GetProductStock
    @ProductID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        w.WarehouseName,
        sl.OnHandQuantity,
        sl.ReservedQuantity,
        sl.OnHandQuantity - sl.ReservedQuantity AS AvailableQuantity
    FROM inventory.StockLevels sl
    INNER JOIN inventory.Warehouses w ON sl.WarehouseID = w.WarehouseID
    WHERE sl.ProductID = @ProductID;
END;
GO

CREATE PROCEDURE crm.sp_LogActivity
    @ActivityType NVARCHAR(30),
    @Subject NVARCHAR(200),
    @Description NVARCHAR(MAX),
    @LeadID INT = NULL,
    @OpportunityID INT = NULL,
    @PerformedByUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO crm.Activities (ActivityType, Subject, Description, LeadID, OpportunityID, PerformedByUserId, OccurredAtUtc)
    VALUES (@ActivityType, @Subject, @Description, @LeadID, @OpportunityID, @PerformedByUserId, SYSUTCDATETIME());
END;
GO

CREATE PROCEDURE hr.sp_GetDepartmentStaff
    @DepartmentID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        e.EmployeeID,
        e.FirstName,
        e.LastName,
        e.Email AS WorkEmail,
        jt.TitleName AS JobTitle
    FROM hr.Employees e
    INNER JOIN hr.JobTitles jt ON e.JobTitleID = jt.JobTitleID
    WHERE e.DepartmentID = @DepartmentID AND e.IsActive = 1
    ORDER BY e.LastName, e.FirstName;
END;
GO

CREATE PROCEDURE dbo.sp_GetUserProfile
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        u.UserID,
        u.Username,
        u.Email,
        u.LastLogin
    FROM dbo.Users u
    WHERE u.UserID = @UserID;
END;
GO

CREATE PROCEDURE dbo.sp_CreateCustomer
    @FirstName NVARCHAR(100),
    @LastName NVARCHAR(100),
    @Email NVARCHAR(100),
    @Phone NVARCHAR(30) = NULL,
    @CountryCode CHAR(2),
    @NewCustomerID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Customers (FirstName, LastName, Email, Phone, CountryCode, CreatedAtUtc)
    VALUES (@FirstName, @LastName, @Email, @Phone, @CountryCode, SYSUTCDATETIME());
    
    SET @NewCustomerID = SCOPE_IDENTITY();
END;
GO

CREATE PROCEDURE sales.sp_CreateOrder
    @CustomerID INT,
    @Items sales.OrderItemType READONLY,
    @NewOrderID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @OrderNum NVARCHAR(30) = 'ORD-OLD-' + CAST(NEWID() AS NVARCHAR(36));
        
        DECLARE @SubTotal DECIMAL(18,2);
        SELECT @SubTotal = SUM(Quantity * UnitPrice)
        FROM @Items;

        INSERT INTO sales.Orders (OrderNumber, CustomerID, OrderStatus, SubTotal, DiscountTotal, TaxTotal, ShippingTotal)
        VALUES (@OrderNum, @CustomerID, 'Pending', @SubTotal, 0, 0, 10.00);

        SET @NewOrderID = SCOPE_IDENTITY();

        INSERT INTO sales.OrderItems (OrderID, ProductID, Quantity, UnitPrice)
        SELECT 
            @NewOrderID,
            ProductID,
            Quantity,
            UnitPrice
        FROM @Items;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

CREATE PROCEDURE sales.sp_GenerateInvoice
    @OrderID INT,
    @DueDateUtc DATETIME,
    @NewInvoiceID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Total DECIMAL(18,2);
    SELECT @Total = SubTotal + ShippingTotal FROM sales.Orders WHERE OrderID = @OrderID;

    DECLARE @InvNum NVARCHAR(50) = 'INV-OLD-' + CAST(NEWID() AS NVARCHAR(36));

    INSERT INTO sales.Invoices (InvoiceNumber, OrderID, DueDateUtc, TotalAmount, Status)
    VALUES (@InvNum, @OrderID, @DueDateUtc, @Total, 'Unpaid');

    SET @NewInvoiceID = SCOPE_IDENTITY();
END;
GO

CREATE PROCEDURE inventory.sp_AdjustStock
    @ProductID INT,
    @WarehouseID INT,
    @QuantityAdjustment INT,
    @MovementType NVARCHAR(30),
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE inventory.StockLevels
    SET OnHandQuantity = OnHandQuantity + @QuantityAdjustment
    WHERE ProductID = @ProductID AND WarehouseID = @WarehouseID;

    INSERT INTO inventory.StockMovements (ProductID, FromWarehouseID, ToWarehouseID, Quantity, MovementType, CreatedByUserId)
    VALUES (@ProductID, @WarehouseID, @WarehouseID, @QuantityAdjustment, @MovementType, @UserID);
END;
GO

CREATE PROCEDURE billing.sp_RenewSubscription
    @SubscriptionID INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE billing.Subscriptions
    SET NextBillingDateUtc = DATEADD(month, 1, NextBillingDateUtc)
    WHERE SubscriptionID = @SubscriptionID;
END;
GO

CREATE PROCEDURE crm.sp_ConvertLead
    @LeadID INT,
    @NewCustomerID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @FirstName NVARCHAR(50), @LastName NVARCHAR(50), @Email NVARCHAR(100);
    SELECT @FirstName = FirstName, @LastName = LastName, @Email = Email
    FROM crm.Leads WHERE LeadID = @LeadID;

    INSERT INTO dbo.Customers (FirstName, LastName, Email, CountryCode)
    VALUES (@FirstName, @LastName, @Email, 'US');
    SET @NewCustomerID = SCOPE_IDENTITY();
END;
GO

CREATE PROCEDURE hr.sp_PromoteEmployee
    @EmployeeID INT,
    @NewJobTitleID INT,
    @NewBaseSalary DECIMAL(18,2),
    @ApprovedByUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE hr.Employees
    SET JobTitleID = @NewJobTitleID
    WHERE EmployeeID = @EmployeeID;

    INSERT INTO hr.CompensationHistory (EmployeeID, BaseSalary, EffectiveDate, ApprovedByUserId)
    VALUES (@EmployeeID, @NewBaseSalary, CAST(SYSUTCDATETIME() AS DATE), @ApprovedByUserId);
END;
GO

CREATE PROCEDURE reporting.sp_GenerateMonthlySummary
    @Year INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        COUNT(OrderID) AS OrderCount,
        SUM(SubTotal) AS TotalRevenue
    FROM sales.Orders
    WHERE YEAR(OrderDateUtc) = @Year;
END;
GO

CREATE PROCEDURE dbo.sp_LegacySync
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.LegacyImportQueue SET Processed = 1 WHERE Processed = 0;
END;
GO

CREATE PROCEDURE sales.sp_ArchiveOldOrders_v1
    @CutoffDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO sales.OldOrderArchive (LegacyOrderNumber, ArchiveDate, DumpData)
    SELECT OrderNumber, GETDATE(), 'Archived'
    FROM sales.Orders
    WHERE OrderDateUtc < @CutoffDate;
END;
GO

CREATE PROCEDURE inventory.sp_LegacyBinTransfer
    @ItemID INT,
    @NewBin NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE inventory.OldBinAssignments SET BinName = @NewBin WHERE ItemID = @ItemID;
END;
GO

CREATE PROCEDURE billing.sp_PurgeOldTransactions
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM billing.LegacyPaymentLog WHERE LogTime < DATEADD(year, -2, GETDATE());
END;
GO

CREATE PROCEDURE reporting.sp_LegacyCubeRefresh
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 1 AS Status;
END;
GO

CREATE FUNCTION dbo.fn_CalculateMargin
(
    @SellingPrice DECIMAL(18,2),
    @CostPrice DECIMAL(18,2)
)
RETURNS DECIMAL(18,2)
AS
BEGIN
    IF @SellingPrice IS NULL OR @CostPrice IS NULL RETURN 0.0;
    RETURN @SellingPrice - @CostPrice;
END;
GO

CREATE FUNCTION dbo.fn_FormatAddress
(
    @AddressLine1 NVARCHAR(150),
    @City NVARCHAR(100),
    @State NVARCHAR(100),
    @PostalCode NVARCHAR(20)
)
RETURNS NVARCHAR(500)
AS
BEGIN
    RETURN CONCAT_WS(', ', NULLIF(@AddressLine1, ''), NULLIF(@City, ''), NULLIF(@State, ''), NULLIF(@PostalCode, ''));
END;
GO

CREATE FUNCTION sales.fn_GetCustomerTotalSpend
(
    @CustomerID INT
)
RETURNS DECIMAL(18,2)
AS
BEGIN
    DECLARE @Total DECIMAL(18,2);
    SELECT @Total = ISNULL(SUM(SubTotal), 0.0)
    FROM sales.Orders
    WHERE CustomerID = @CustomerID;
    RETURN @Total;
END;
GO

CREATE FUNCTION inventory.fn_GetAvailableStock
(
    @ProductID INT
)
RETURNS INT
AS
BEGIN
    DECLARE @Avail INT;
    SELECT @Avail = ISNULL(SUM(OnHandQuantity), 0)
    FROM inventory.StockLevels
    WHERE ProductID = @ProductID;
    RETURN @Avail;
END;
GO

CREATE FUNCTION crm.fn_GetLeadScore
(
    @LeadID INT
)
RETURNS INT
AS
BEGIN
    RETURN 10;
END;
GO

CREATE FUNCTION dbo.fn_LegacyDecrypt
(
    @EncryptedVal NVARCHAR(255)
)
RETURNS NVARCHAR(255)
AS
BEGIN
    RETURN @EncryptedVal;
END;
GO

CREATE FUNCTION sales.fn_GetLegacySalesTotal
()
RETURNS DECIMAL(18,2)
AS
BEGIN
    RETURN 0.0;
END;
GO

CREATE FUNCTION inventory.fn_GetLegacyItemBin
(
    @ItemID INT
)
RETURNS NVARCHAR(50)
AS
BEGIN
    DECLARE @Bin NVARCHAR(50);
    SELECT @Bin = BinName FROM inventory.OldBinAssignments WHERE ItemID = @ItemID;
    RETURN @Bin;
END;
GO

