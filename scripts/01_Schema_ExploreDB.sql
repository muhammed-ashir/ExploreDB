-- =======================================================================================
-- Create Database (Ultimate Enterprise Edition)
-- =======================================================================================
USE master;
GO

IF DB_ID('ExploreDB') IS NOT NULL
BEGIN
    ALTER DATABASE ExploreDB SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE ExploreDB;
END
GO

CREATE DATABASE ExploreDB;
GO

USE ExploreDB;
GO

-- =======================================================================================
-- Create User-Defined Data Types
-- =======================================================================================
CREATE TYPE dbo.NameType FROM NVARCHAR(100) NOT NULL;
CREATE TYPE dbo.EmailType FROM NVARCHAR(255) NOT NULL;
CREATE TYPE dbo.PhoneType FROM NVARCHAR(50) NULL;
CREATE TYPE dbo.MoneyType FROM DECIMAL(19,4) NOT NULL;
CREATE TYPE dbo.AddressType FROM NVARCHAR(500) NULL;
CREATE TYPE dbo.SKUType FROM NVARCHAR(50) NOT NULL;
CREATE TYPE dbo.ZipCodeType FROM NVARCHAR(20) NOT NULL;
CREATE TYPE dbo.IPAddressType FROM NVARCHAR(45) NULL;
CREATE TYPE dbo.AgeType FROM TINYINT NOT NULL;
CREATE TYPE dbo.TemperatureType FROM FLOAT NULL;
CREATE TYPE dbo.CoordinatesType FROM DECIMAL(9,6) NULL;
CREATE TYPE dbo.JSONDataType FROM NVARCHAR(MAX) NULL;
CREATE TYPE dbo.StatusType FROM VARCHAR(20) NOT NULL;
CREATE TYPE dbo.FlagType FROM BIT NOT NULL;
CREATE TYPE dbo.HashType FROM VARBINARY(64) NULL;
CREATE TYPE dbo.UUIDType FROM UNIQUEIDENTIFIER NOT NULL;
CREATE TYPE dbo.DateOfBirthType FROM DATE NULL;
CREATE TYPE dbo.TimestampType FROM DATETIME2(7) NOT NULL;
GO

-- =======================================================================================
-- Create User-Defined Table Types
-- =======================================================================================
CREATE TYPE dbo.IntListType AS TABLE (
    Id INT PRIMARY KEY
);

CREATE TYPE dbo.StringListType AS TABLE (
    Item NVARCHAR(255)
);

CREATE TYPE dbo.EmployeeRecordType AS TABLE (
    EmployeeId INT PRIMARY KEY,
    FirstName NVARCHAR(50) NOT NULL,
    LastName NVARCHAR(50) NOT NULL,
    HireDate DATE
);
GO

-- =======================================================================================
-- PART 1: SYSTEM & CONFIGURATION
-- =======================================================================================

CREATE TABLE dbo.SystemSettings (
    SettingKey NVARCHAR(100) PRIMARY KEY,
    SettingValue NVARCHAR(MAX) NULL,
    Description NVARCHAR(500) NULL,
    LastUpdated DATETIME2 DEFAULT GETDATE()
);
GO

CREATE TABLE dbo.Languages (
    LanguageID INT IDENTITY(1,1) PRIMARY KEY,
    LanguageCode NVARCHAR(10) NOT NULL UNIQUE, -- e.g., 'en-US'
    LanguageName NVARCHAR(100) NOT NULL,
    IsActive BIT DEFAULT 1
);
GO

CREATE TABLE dbo.Translations (
    TranslationID BIGINT IDENTITY(1,1) PRIMARY KEY,
    LanguageID INT NOT NULL,
    EntityName NVARCHAR(100) NOT NULL,
    EntityKey NVARCHAR(100) NOT NULL,
    TranslatedText NVARCHAR(MAX) NOT NULL,
    CONSTRAINT FK_Trans_Lang FOREIGN KEY (LanguageID) REFERENCES dbo.Languages(LanguageID)
);
GO

CREATE TABLE dbo.Currencies (
    CurrencyID INT IDENTITY(1,1) PRIMARY KEY,
    CurrencyCode NVARCHAR(3) NOT NULL UNIQUE, -- USD, EUR
    CurrencyName NVARCHAR(100) NOT NULL,
    Symbol NVARCHAR(10) NOT NULL
);
GO

CREATE TABLE dbo.ExchangeRates (
    RateID INT IDENTITY(1,1) PRIMARY KEY,
    FromCurrencyID INT NOT NULL,
    ToCurrencyID INT NOT NULL,
    ExchangeRate DECIMAL(18,6) NOT NULL,
    EffectiveDate DATETIME2 DEFAULT GETDATE(),
    CONSTRAINT FK_Exch_From FOREIGN KEY (FromCurrencyID) REFERENCES dbo.Currencies(CurrencyID),
    CONSTRAINT FK_Exch_To FOREIGN KEY (ToCurrencyID) REFERENCES dbo.Currencies(CurrencyID)
);
GO

-- =======================================================================================
-- PART 2: IDENTITY, RBAC & USERS
-- =======================================================================================

CREATE TABLE dbo.Roles (
    RoleID INT IDENTITY(1,1) PRIMARY KEY,
    RoleName NVARCHAR(50) NOT NULL UNIQUE,
    Description NVARCHAR(255) NULL
);
GO

CREATE TABLE dbo.Users (
    UserID INT IDENTITY(1,1) PRIMARY KEY,
    FirstName dbo.NameType,
    LastName dbo.NameType,
    Email dbo.EmailType UNIQUE,
    Phone dbo.PhoneType,
    PasswordHash NVARCHAR(255) NULL,
    PreferredLanguageID INT NULL,
    PreferredCurrencyID INT NULL,
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    LastLogin DATETIME2 NULL,
    IsActive BIT DEFAULT 1,
    ProfilePicture VARBINARY(MAX) NULL,
    CONSTRAINT FK_Users_Lang FOREIGN KEY (PreferredLanguageID) REFERENCES dbo.Languages(LanguageID),
    CONSTRAINT FK_Users_Curr FOREIGN KEY (PreferredCurrencyID) REFERENCES dbo.Currencies(CurrencyID)
);
GO

CREATE TABLE dbo.UserRoles (
    UserID INT NOT NULL,
    RoleID INT NOT NULL,
    AssignedAt DATETIME2 DEFAULT GETDATE(),
    PRIMARY KEY (UserID, RoleID),
    CONSTRAINT FK_UserRoles_Users FOREIGN KEY (UserID) REFERENCES dbo.Users(UserID) ON DELETE CASCADE,
    CONSTRAINT FK_UserRoles_Roles FOREIGN KEY (RoleID) REFERENCES dbo.Roles(RoleID) ON DELETE CASCADE
);
GO

CREATE TABLE dbo.UserAddresses (
    AddressID INT IDENTITY(1,1) PRIMARY KEY,
    UserID INT NOT NULL,
    AddressType NVARCHAR(20) DEFAULT 'Shipping',
    AddressLine1 dbo.AddressType,
    AddressLine2 dbo.AddressType,
    City NVARCHAR(100) NOT NULL,
    StateProvince NVARCHAR(100) NOT NULL,
    PostalCode dbo.ZipCodeType,
    Country NVARCHAR(100) NOT NULL,
    IsPrimary BIT DEFAULT 0,
    CONSTRAINT FK_UserAddresses_Users FOREIGN KEY (UserID) REFERENCES dbo.Users(UserID) ON DELETE CASCADE
);
GO

-- =======================================================================================
-- PART 3: HUMAN RESOURCES (HR)
-- =======================================================================================

CREATE TABLE dbo.Departments (
    DepartmentID INT IDENTITY(1,1) PRIMARY KEY,
    DepartmentName NVARCHAR(100) NOT NULL UNIQUE,
    ManagerUserID INT NULL,
    CONSTRAINT FK_Dept_Manager FOREIGN KEY (ManagerUserID) REFERENCES dbo.Users(UserID)
);
GO

CREATE TABLE dbo.Employees (
    EmployeeID INT IDENTITY(1,1) PRIMARY KEY,
    UserID INT NOT NULL UNIQUE,
    DepartmentID INT NOT NULL,
    JobTitle NVARCHAR(100) NOT NULL,
    HireDate DATE NOT NULL,
    TerminationDate DATE NULL,
    BaseSalary dbo.MoneyType NOT NULL,
    CONSTRAINT FK_Emp_Users FOREIGN KEY (UserID) REFERENCES dbo.Users(UserID),
    CONSTRAINT FK_Emp_Dept FOREIGN KEY (DepartmentID) REFERENCES dbo.Departments(DepartmentID)
);
GO

CREATE TABLE dbo.Timesheets (
    TimesheetID BIGINT IDENTITY(1,1) PRIMARY KEY,
    EmployeeID INT NOT NULL,
    WorkDate DATE NOT NULL,
    HoursWorked DECIMAL(4,2) NOT NULL,
    IsOvertime BIT DEFAULT 0,
    CONSTRAINT FK_Timesheet_Emp FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID)
);
GO

CREATE TABLE dbo.LeaveRequests (
    LeaveID INT IDENTITY(1,1) PRIMARY KEY,
    EmployeeID INT NOT NULL,
    LeaveType NVARCHAR(50) NOT NULL, -- Sick, Vacation, Personal
    StartDate DATE NOT NULL,
    EndDate DATE NOT NULL,
    Status NVARCHAR(50) DEFAULT 'Pending', -- Pending, Approved, Rejected
    CONSTRAINT FK_Leave_Emp FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID)
);
GO

-- =======================================================================================
-- PART 4: CATALOG & MANUFACTURING
-- =======================================================================================

CREATE TABLE dbo.Brands (
    BrandID INT IDENTITY(1,1) PRIMARY KEY,
    BrandName NVARCHAR(100) NOT NULL UNIQUE,
    WebsiteURL NVARCHAR(255) NULL
);
GO

CREATE TABLE dbo.Categories (
    CategoryID INT IDENTITY(1,1) PRIMARY KEY,
    ParentCategoryID INT NULL,
    CategoryName NVARCHAR(100) NOT NULL,
    CONSTRAINT FK_Categories_Self FOREIGN KEY (ParentCategoryID) REFERENCES dbo.Categories(CategoryID)
);
GO

CREATE TABLE dbo.Products (
    ProductID INT IDENTITY(1,1) PRIMARY KEY,
    CategoryID INT NOT NULL,
    BrandID INT NOT NULL,
    ProductName NVARCHAR(200) NOT NULL,
    SKU dbo.SKUType UNIQUE,
    BasePrice dbo.MoneyType,
    Weight DECIMAL(10,2) NULL,
    Dimensions NVARCHAR(100) NULL,
    IsPublished BIT DEFAULT 1,
    AttributesJson NVARCHAR(MAX) NULL,
    RowVersion ROWVERSION,
    CONSTRAINT FK_Products_Categories FOREIGN KEY (CategoryID) REFERENCES dbo.Categories(CategoryID),
    CONSTRAINT FK_Products_Brands FOREIGN KEY (BrandID) REFERENCES dbo.Brands(BrandID)
);
GO

CREATE TABLE dbo.RawMaterials (
    MaterialID INT IDENTITY(1,1) PRIMARY KEY,
    MaterialName NVARCHAR(100) NOT NULL UNIQUE,
    UnitOfMeasure NVARCHAR(20) NOT NULL,
    CostPerUnit dbo.MoneyType NOT NULL
);
GO

CREATE TABLE dbo.BillOfMaterials (
    BOM_ID INT IDENTITY(1,1) PRIMARY KEY,
    ProductID INT NOT NULL,
    MaterialID INT NOT NULL,
    QuantityRequired DECIMAL(10,4) NOT NULL,
    CONSTRAINT FK_BOM_Prod FOREIGN KEY (ProductID) REFERENCES dbo.Products(ProductID),
    CONSTRAINT FK_BOM_Mat FOREIGN KEY (MaterialID) REFERENCES dbo.RawMaterials(MaterialID)
);
GO

CREATE TABLE dbo.AssemblyLines (
    AssemblyLineID INT IDENTITY(1,1) PRIMARY KEY,
    LineName NVARCHAR(100) NOT NULL,
    Status NVARCHAR(50) DEFAULT 'Active'
);
GO

CREATE TABLE dbo.QualityInspections (
    InspectionID BIGINT IDENTITY(1,1) PRIMARY KEY,
    ProductID INT NOT NULL,
    AssemblyLineID INT NOT NULL,
    InspectorEmployeeID INT NOT NULL,
    InspectionDate DATETIME2 DEFAULT GETDATE(),
    Passed BIT NOT NULL,
    DefectReason NVARCHAR(MAX) NULL,
    CONSTRAINT FK_QI_Prod FOREIGN KEY (ProductID) REFERENCES dbo.Products(ProductID),
    CONSTRAINT FK_QI_Line FOREIGN KEY (AssemblyLineID) REFERENCES dbo.AssemblyLines(AssemblyLineID),
    CONSTRAINT FK_QI_Emp FOREIGN KEY (InspectorEmployeeID) REFERENCES dbo.Employees(EmployeeID)
);
GO

-- =======================================================================================
-- PART 5: WAREHOUSING, INVENTORY & B2B SUPPLY CHAIN
-- =======================================================================================

CREATE TABLE dbo.Warehouses (
    WarehouseID INT IDENTITY(1,1) PRIMARY KEY,
    WarehouseCode NVARCHAR(20) UNIQUE NOT NULL,
    LocationName NVARCHAR(100) NOT NULL,
    City NVARCHAR(100),
    Country NVARCHAR(100)
);
GO

CREATE TABLE dbo.WarehouseInventory (
    InventoryID BIGINT IDENTITY(1,1) PRIMARY KEY,
    WarehouseID INT NOT NULL,
    ProductID INT NOT NULL,
    StockQuantity INT NOT NULL DEFAULT 0,
    ReorderLevel INT NOT NULL DEFAULT 10,
    CONSTRAINT FK_Inv_Warehouses FOREIGN KEY (WarehouseID) REFERENCES dbo.Warehouses(WarehouseID),
    CONSTRAINT FK_Inv_Products FOREIGN KEY (ProductID) REFERENCES dbo.Products(ProductID)
);
GO

CREATE TABLE dbo.Suppliers (
    SupplierID INT IDENTITY(1,1) PRIMARY KEY,
    SupplierName NVARCHAR(150) NOT NULL,
    ContactEmail dbo.EmailType,
    Phone dbo.PhoneType
);
GO

CREATE TABLE dbo.PurchaseOrders (
    PO_ID INT IDENTITY(1,1) PRIMARY KEY,
    SupplierID INT NOT NULL,
    WarehouseID INT NOT NULL,
    OrderDate DATETIME2 DEFAULT GETDATE(),
    Status NVARCHAR(50) DEFAULT 'Draft',
    TotalCost dbo.MoneyType DEFAULT 0,
    CONSTRAINT FK_PO_Suppliers FOREIGN KEY (SupplierID) REFERENCES dbo.Suppliers(SupplierID),
    CONSTRAINT FK_PO_Warehouses FOREIGN KEY (WarehouseID) REFERENCES dbo.Warehouses(WarehouseID)
);
GO

CREATE TABLE dbo.PurchaseOrderDetails (
    PODetailID BIGINT IDENTITY(1,1) PRIMARY KEY,
    PO_ID INT NOT NULL,
    ProductID INT NOT NULL,
    QuantityOrdered INT NOT NULL,
    QuantityReceived INT DEFAULT 0,
    UnitCost dbo.MoneyType,
    CONSTRAINT FK_POD_PO FOREIGN KEY (PO_ID) REFERENCES dbo.PurchaseOrders(PO_ID) ON DELETE CASCADE,
    CONSTRAINT FK_POD_Products FOREIGN KEY (ProductID) REFERENCES dbo.Products(ProductID)
);
GO

-- =======================================================================================
-- PART 6: MARKETING, ANALYTICS & AFFILIATES
-- =======================================================================================

CREATE TABLE dbo.Discounts (
    DiscountID INT IDENTITY(1,1) PRIMARY KEY,
    DiscountName NVARCHAR(100) NOT NULL,
    DiscountType NVARCHAR(20) NOT NULL,
    DiscountValue DECIMAL(18,2) NOT NULL,
    IsActive BIT DEFAULT 1
);
GO

CREATE TABLE dbo.CouponCodes (
    CouponID INT IDENTITY(1,1) PRIMARY KEY,
    DiscountID INT NOT NULL,
    Code NVARCHAR(50) UNIQUE NOT NULL,
    TimesUsed INT DEFAULT 0,
    CONSTRAINT FK_Coupons_Discounts FOREIGN KEY (DiscountID) REFERENCES dbo.Discounts(DiscountID)
);
GO

CREATE TABLE dbo.Affiliates (
    AffiliateID INT IDENTITY(1,1) PRIMARY KEY,
    UserID INT NOT NULL UNIQUE,
    AffiliateCode NVARCHAR(50) UNIQUE NOT NULL,
    CommissionRate DECIMAL(5,4) DEFAULT 0.05,
    CONSTRAINT FK_Affil_Users FOREIGN KEY (UserID) REFERENCES dbo.Users(UserID)
);
GO

CREATE TABLE dbo.AdCampaigns (
    CampaignID INT IDENTITY(1,1) PRIMARY KEY,
    CampaignName NVARCHAR(200) NOT NULL,
    Platform NVARCHAR(100) NOT NULL, -- Google, Facebook, etc.
    Budget dbo.MoneyType NOT NULL,
    Spend dbo.MoneyType DEFAULT 0
);
GO

CREATE TABLE dbo.PageViews (
    ViewID BIGINT IDENTITY(1,1) PRIMARY KEY,
    SessionID NVARCHAR(100) NOT NULL,
    UserID INT NULL,
    PageURL NVARCHAR(1000) NOT NULL,
    IPAddress dbo.IPAddressType,
    UserAgent NVARCHAR(500) NULL,
    ViewedAt DATETIME2 DEFAULT GETDATE()
);
GO

-- =======================================================================================
-- PART 7: E-COMMERCE CORE (ORDERS, CART, PAYMENTS)
-- =======================================================================================

CREATE TABLE dbo.ShoppingCart (
    CartID UNIQUEIDENTIFIER DEFAULT NEWID() PRIMARY KEY,
    UserID INT NULL,
    SessionID NVARCHAR(100) NULL,
    CreatedAt DATETIME2 DEFAULT GETDATE()
);
GO

CREATE TABLE dbo.CartItems (
    CartItemID BIGINT IDENTITY(1,1) PRIMARY KEY,
    CartID UNIQUEIDENTIFIER NOT NULL,
    ProductID INT NOT NULL,
    Quantity INT NOT NULL DEFAULT 1,
    CONSTRAINT FK_CartItems_Cart FOREIGN KEY (CartID) REFERENCES dbo.ShoppingCart(CartID) ON DELETE CASCADE,
    CONSTRAINT FK_CartItems_Products FOREIGN KEY (ProductID) REFERENCES dbo.Products(ProductID)
);
GO

CREATE TABLE dbo.Orders (
    OrderID UNIQUEIDENTIFIER DEFAULT NEWID() PRIMARY KEY,
    UserID INT NOT NULL,
    OrderDate DATETIME2 DEFAULT GETDATE(),
    SubTotal dbo.MoneyType DEFAULT 0,
    TotalAmount dbo.MoneyType DEFAULT 0,
    CouponID INT NULL,
    AffiliateID INT NULL,
    Status VARCHAR(50) DEFAULT 'Pending',
    ShippingAddressID INT NULL,
    CONSTRAINT FK_Orders_Users FOREIGN KEY (UserID) REFERENCES dbo.Users(UserID),
    CONSTRAINT FK_Orders_Coupons FOREIGN KEY (CouponID) REFERENCES dbo.CouponCodes(CouponID),
    CONSTRAINT FK_Orders_Affil FOREIGN KEY (AffiliateID) REFERENCES dbo.Affiliates(AffiliateID)
);
GO

CREATE TABLE dbo.OrderDetails (
    OrderDetailID BIGINT IDENTITY(1,1) PRIMARY KEY,
    OrderID UNIQUEIDENTIFIER NOT NULL,
    ProductID INT NOT NULL,
    WarehouseID INT NULL,
    Quantity INT NOT NULL CHECK (Quantity > 0),
    UnitPrice dbo.MoneyType,
    LineTotal AS (Quantity * UnitPrice) PERSISTED,
    CONSTRAINT FK_OrderDetails_Orders FOREIGN KEY (OrderID) REFERENCES dbo.Orders(OrderID) ON DELETE CASCADE,
    CONSTRAINT FK_OrderDetails_Products FOREIGN KEY (ProductID) REFERENCES dbo.Products(ProductID)
);
GO

CREATE TABLE dbo.Invoices (
    InvoiceID INT IDENTITY(1000, 1) PRIMARY KEY,
    OrderID UNIQUEIDENTIFIER NOT NULL,
    InvoiceDate DATETIME2 DEFAULT GETDATE(),
    TotalAmount dbo.MoneyType NOT NULL,
    IsPaid BIT DEFAULT 0,
    CONSTRAINT FK_Invoices_Orders FOREIGN KEY (OrderID) REFERENCES dbo.Orders(OrderID)
);
GO

CREATE TABLE dbo.Payments (
    PaymentID UNIQUEIDENTIFIER DEFAULT NEWID() PRIMARY KEY,
    InvoiceID INT NOT NULL,
    PaymentMethod VARCHAR(50) NOT NULL,
    AmountPaid dbo.MoneyType NOT NULL,
    PaymentDate DATETIME2 DEFAULT GETDATE(),
    CONSTRAINT FK_Payments_Invoices FOREIGN KEY (InvoiceID) REFERENCES dbo.Invoices(InvoiceID)
);
GO

-- =======================================================================================
-- PART 8: LOGISTICS, FLEET & SHIPPING
-- =======================================================================================

CREATE TABLE dbo.ShippingCarriers (
    CarrierID INT IDENTITY(1,1) PRIMARY KEY,
    CarrierName NVARCHAR(100) NOT NULL UNIQUE,
    APIEndpoint NVARCHAR(500) NULL
);
GO

CREATE TABLE dbo.ShippingMethods (
    MethodID INT IDENTITY(1,1) PRIMARY KEY,
    CarrierID INT NOT NULL,
    MethodName NVARCHAR(100) NOT NULL,
    BaseCost dbo.MoneyType NOT NULL,
    EstimatedDays INT NOT NULL,
    CONSTRAINT FK_ShipMethod_Carrier FOREIGN KEY (CarrierID) REFERENCES dbo.ShippingCarriers(CarrierID)
);
GO

CREATE TABLE dbo.VehicleFleet (
    VehicleID INT IDENTITY(1,1) PRIMARY KEY,
    LicensePlate NVARCHAR(20) NOT NULL UNIQUE,
    VehicleType NVARCHAR(50) NOT NULL, -- Truck, Van, Bike
    CapacityWeight DECIMAL(10,2) NULL,
    Status NVARCHAR(50) DEFAULT 'Active'
);
GO

CREATE TABLE dbo.Shipments (
    ShipmentID BIGINT IDENTITY(1,1) PRIMARY KEY,
    OrderID UNIQUEIDENTIFIER NOT NULL,
    MethodID INT NOT NULL,
    VehicleID INT NULL,
    TrackingNumber NVARCHAR(100) NULL,
    DispatchedAt DATETIME2 NULL,
    DeliveredAt DATETIME2 NULL,
    Status NVARCHAR(50) DEFAULT 'Processing',
    CONSTRAINT FK_Ship_Orders FOREIGN KEY (OrderID) REFERENCES dbo.Orders(OrderID),
    CONSTRAINT FK_Ship_Method FOREIGN KEY (MethodID) REFERENCES dbo.ShippingMethods(MethodID),
    CONSTRAINT FK_Ship_Vehicle FOREIGN KEY (VehicleID) REFERENCES dbo.VehicleFleet(VehicleID)
);
GO

-- =======================================================================================
-- PART 9: COMMUNITY & FORUMS
-- =======================================================================================

CREATE TABLE dbo.Forums (
    ForumID INT IDENTITY(1,1) PRIMARY KEY,
    ForumName NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NULL,
    DisplayOrder INT DEFAULT 0
);
GO

CREATE TABLE dbo.ForumTopics (
    TopicID BIGINT IDENTITY(1,1) PRIMARY KEY,
    ForumID INT NOT NULL,
    UserID INT NOT NULL,
    Title NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    IsPinned BIT DEFAULT 0,
    IsLocked BIT DEFAULT 0,
    CONSTRAINT FK_Topic_Forum FOREIGN KEY (ForumID) REFERENCES dbo.Forums(ForumID),
    CONSTRAINT FK_Topic_User FOREIGN KEY (UserID) REFERENCES dbo.Users(UserID)
);
GO

CREATE TABLE dbo.ForumPosts (
    PostID BIGINT IDENTITY(1,1) PRIMARY KEY,
    TopicID BIGINT NOT NULL,
    UserID INT NOT NULL,
    PostBody NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    CONSTRAINT FK_Post_Topic FOREIGN KEY (TopicID) REFERENCES dbo.ForumTopics(TopicID),
    CONSTRAINT FK_Post_User FOREIGN KEY (UserID) REFERENCES dbo.Users(UserID)
);
GO

CREATE TABLE dbo.Badges (
    BadgeID INT IDENTITY(1,1) PRIMARY KEY,
    BadgeName NVARCHAR(100) NOT NULL,
    IconURL NVARCHAR(500) NULL
);
GO

CREATE TABLE dbo.UserBadges (
    UserBadgeID BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserID INT NOT NULL,
    BadgeID INT NOT NULL,
    AwardedAt DATETIME2 DEFAULT GETDATE(),
    CONSTRAINT FK_UB_Users FOREIGN KEY (UserID) REFERENCES dbo.Users(UserID),
    CONSTRAINT FK_UB_Badges FOREIGN KEY (BadgeID) REFERENCES dbo.Badges(BadgeID)
);
GO

-- =======================================================================================
-- VIEWS (Complex Analytics)
-- =======================================================================================

CREATE VIEW dbo.vw_EmployeeTurnover AS
SELECT d.DepartmentName, 
       COUNT(e.EmployeeID) AS TotalEmployees,
       SUM(CASE WHEN e.TerminationDate IS NOT NULL THEN 1 ELSE 0 END) AS TerminatedEmployees
FROM dbo.Employees e
JOIN dbo.Departments d ON e.DepartmentID = d.DepartmentID
GROUP BY d.DepartmentName;
GO

-- This view references ANOTHER view (acting as a child view)
CREATE VIEW dbo.vw_HighTurnoverDepartments AS
SELECT 
    DepartmentName, 
    TotalEmployees, 
    TerminatedEmployees,
    (CAST(TerminatedEmployees AS FLOAT) / NULLIF(TotalEmployees, 0)) * 100 AS TurnoverRate
FROM dbo.vw_EmployeeTurnover
WHERE (CAST(TerminatedEmployees AS FLOAT) / NULLIF(TotalEmployees, 0)) > 0.10;
GO

CREATE VIEW dbo.vw_ManufacturingDefects AS
SELECT p.ProductName, al.LineName,
       COUNT(qi.InspectionID) AS TotalInspections,
       SUM(CASE WHEN qi.Passed = 0 THEN 1 ELSE 0 END) AS FailedInspections,
       CAST(SUM(CASE WHEN qi.Passed = 0 THEN 1 ELSE 0 END) AS FLOAT) / COUNT(qi.InspectionID) AS DefectRate
FROM dbo.QualityInspections qi
JOIN dbo.Products p ON qi.ProductID = p.ProductID
JOIN dbo.AssemblyLines al ON qi.AssemblyLineID = al.AssemblyLineID
GROUP BY p.ProductName, al.LineName;
GO

CREATE VIEW dbo.vw_TrafficSources AS
SELECT UserAgent, COUNT(ViewID) AS TotalHits, 
       COUNT(DISTINCT SessionID) AS UniqueSessions
FROM dbo.PageViews
GROUP BY UserAgent;
GO

-- =======================================================================================
-- STORED PROCEDURES & FUNCTIONS
-- =======================================================================================

CREATE FUNCTION dbo.fn_ConvertCurrency (@Amount dbo.MoneyType, @FromCode NVARCHAR(3), @ToCode NVARCHAR(3))
RETURNS dbo.MoneyType
AS
BEGIN
    IF @FromCode = @ToCode RETURN @Amount;
    
    DECLARE @Rate DECIMAL(18,6);
    SELECT TOP 1 @Rate = ExchangeRate
    FROM dbo.ExchangeRates er
    JOIN dbo.Currencies c1 ON er.FromCurrencyID = c1.CurrencyID
    JOIN dbo.Currencies c2 ON er.ToCurrencyID = c2.CurrencyID
    WHERE c1.CurrencyCode = @FromCode AND c2.CurrencyCode = @ToCode
    ORDER BY EffectiveDate DESC;
    
    RETURN ISNULL(@Amount * @Rate, @Amount);
END
GO

-- 1. Scalar Function returning INT from DATETIME2
CREATE FUNCTION dbo.fn_CalculateAge (@DOB DATE)
RETURNS INT
AS
BEGIN
    IF @DOB IS NULL RETURN NULL;
    RETURN DATEDIFF(YEAR, @DOB, GETDATE()) - 
           CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, @DOB, GETDATE()), @DOB) > GETDATE() THEN 1 ELSE 0 END;
END
GO

-- 2. Multi-Statement Table Valued Function (MSTVF)
CREATE FUNCTION dbo.fn_GetTopEmployeesBySalary (@Limit INT)
RETURNS @TopEmployees TABLE
(
    EmployeeID INT,
    FullName NVARCHAR(200),
    Salary dbo.MoneyType
)
AS
BEGIN
    INSERT INTO @TopEmployees (EmployeeID, FullName, Salary)
    SELECT TOP (@Limit)
        e.EmployeeID,
        u.FirstName + ' ' + u.LastName,
        e.BaseSalary
    FROM dbo.Employees e
    JOIN dbo.Users u ON e.UserID = u.UserID
    ORDER BY e.BaseSalary DESC;

    RETURN;
END
GO

-- 3. String Formatting Scalar Function
CREATE FUNCTION dbo.fn_FormatPhone (@Phone dbo.PhoneType)
RETURNS dbo.PhoneType
AS
BEGIN
    IF LEN(@Phone) = 10
        RETURN '(' + SUBSTRING(@Phone, 1, 3) + ') ' + SUBSTRING(@Phone, 4, 3) + '-' + SUBSTRING(@Phone, 7, 4);
    RETURN @Phone;
END
GO

-- 4. Simple Table Valued Function (Inline TVF)
CREATE FUNCTION dbo.fn_GetActiveUsers ()
RETURNS TABLE
AS
RETURN (
    SELECT UserID, Email, CreatedAt
    FROM dbo.Users
    WHERE IsActive = 1
);
GO

CREATE PROCEDURE dbo.sp_LogPageView
    @SessionID NVARCHAR(100), @UserID INT = NULL, @PageURL NVARCHAR(1000), @IPAddress dbo.IPAddressType = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.PageViews (SessionID, UserID, PageURL, IPAddress)
    VALUES (@SessionID, @UserID, @PageURL, @IPAddress);
END
GO

CREATE PROCEDURE dbo.sp_RegisterUserAndLogActivity
    @FirstName dbo.NameType,
    @LastName dbo.NameType,
    @Email dbo.EmailType,
    @SessionID NVARCHAR(100),
    @IPAddress dbo.IPAddressType
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @NewUserID INT;
    
    -- Insert the new user
    INSERT INTO dbo.Users (FirstName, LastName, Email, CreatedAt, IsActive)
    VALUES (@FirstName, @LastName, @Email, GETDATE(), 1);
    
    SET @NewUserID = SCOPE_IDENTITY();
    
    -- CALLING ANOTHER STORED PROCEDURE HERE
    EXEC dbo.sp_LogPageView 
        @SessionID = @SessionID, 
        @UserID = @NewUserID, 
        @PageURL = '/auth/register-success', 
        @IPAddress = @IPAddress;
        
    SELECT @NewUserID AS UserID;
END
GO

CREATE PROCEDURE dbo.sp_GetComprehensiveEmployeeReport
    @DepartmentName NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    
    -- This query pulls data by joining actual tables with a pre-aggregated view
    SELECT 
        e.EmployeeID,
        u.FirstName + ' ' + u.LastName AS EmployeeName,
        e.JobTitle,
        e.BaseSalary,
        et.DepartmentName,
        et.TotalEmployees,
        et.TerminatedEmployees,
        (CAST(et.TerminatedEmployees AS FLOAT) / NULLIF(et.TotalEmployees, 0)) * 100 AS TurnoverPercentage
    FROM dbo.Employees e
    JOIN dbo.Users u ON e.UserID = u.UserID
    JOIN dbo.Departments d ON e.DepartmentID = d.DepartmentID
    JOIN dbo.vw_EmployeeTurnover et ON d.DepartmentName = et.DepartmentName
    WHERE d.DepartmentName = @DepartmentName;
END
GO

CREATE PROCEDURE dbo.sp_AnalyzeAndLogDepartmentTurnover
    @DepartmentName NVARCHAR(100),
    @ExecutedByUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @TurnoverRate FLOAT;
    DECLARE @LogMessage NVARCHAR(1000);
    DECLARE @SessionID NVARCHAR(100) = 'ANALYTICS_JOB_' + CAST(NEWID() AS NVARCHAR(36));
    
    -- 1 & 2. Referencing a VIEW (vw_EmployeeTurnover) and a TABLE (Departments)
    SELECT 
        @TurnoverRate = (CAST(et.TerminatedEmployees AS FLOAT) / NULLIF(et.TotalEmployees, 0)) * 100
    FROM dbo.vw_EmployeeTurnover et
    JOIN dbo.Departments d ON et.DepartmentName = d.DepartmentName
    WHERE d.DepartmentName = @DepartmentName;
    
    SET @LogMessage = '/reports/turnover?dept=' + @DepartmentName + '&rate=' + ISNULL(CAST(@TurnoverRate AS NVARCHAR(50)), '0');
    
    -- 3. Calling another stored procedure
    EXEC dbo.sp_LogPageView 
        @SessionID = @SessionID, 
        @UserID = @ExecutedByUserID, 
        @PageURL = @LogMessage, 
        @IPAddress = '127.0.0.1';
        
    -- Return the final analysis dataset (combining View and Table again)
    SELECT 
        d.DepartmentName,
        d.ManagerUserID,
        et.TotalEmployees,
        et.TerminatedEmployees,
        @TurnoverRate AS TurnoverPercentage
    FROM dbo.vw_EmployeeTurnover et
    JOIN dbo.Departments d ON et.DepartmentName = d.DepartmentName
    WHERE d.DepartmentName = @DepartmentName;
END
GO

-- =======================================================================================
-- NO DEPENDENCY STORED PROCEDURE
-- =======================================================================================
CREATE PROCEDURE dbo.sp_CalculateCompoundInterest
    @PrincipalAmount DECIMAL(18,4),
    @AnnualInterestRate DECIMAL(5,4),
    @Years INT,
    @CompoundingFrequencyPerYear INT
AS
BEGIN
    SET NOCOUNT ON;
    
    -- This Stored Procedure has ZERO dependencies on any tables or views!
    -- It purely performs mathematical calculations based on the input parameters.
    -- Formula: A = P * (1 + r/n)^(nt)
    
    DECLARE @FinalAmount DECIMAL(18,4);
    
    SET @FinalAmount = @PrincipalAmount * POWER((1 + (@AnnualInterestRate / @CompoundingFrequencyPerYear)), (@CompoundingFrequencyPerYear * @Years));
    
    SELECT 
        @PrincipalAmount AS Principal,
        @AnnualInterestRate AS InterestRate,
        @Years AS Years,
        @FinalAmount AS FinalAmount,
        (@FinalAmount - @PrincipalAmount) AS TotalInterestEarned;
END
GO



-- =======================================================================================
-- OPTIONAL STRESS TEST: GENERATE 5 MILLION ROWS
-- =======================================================================================
CREATE PROCEDURE dbo.sp_GenerateMassiveDataVolume
AS
BEGIN
    SET NOCOUNT ON;
    PRINT 'Creating dbo.MassiveDataTest table...';

    IF OBJECT_ID('dbo.MassiveDataTest', 'U') IS NOT NULL 
        DROP TABLE dbo.MassiveDataTest;

    CREATE TABLE dbo.MassiveDataTest (
        RowID BIGINT IDENTITY(1,1) PRIMARY KEY CLUSTERED,
        Category NVARCHAR(50),
        RandomValue DECIMAL(18,4),
        DateLogged DATETIME2,
        StatusFlag BIT,
        LargeTextField NVARCHAR(MAX)
    );

    PRINT 'Generating 5 Million Rows...';
    
    WITH 
      L0   AS (SELECT c FROM (VALUES(1),(1)) AS D(c)),
      L1   AS (SELECT 1 AS c FROM L0 AS A CROSS JOIN L0 AS B),
      L2   AS (SELECT 1 AS c FROM L1 AS A CROSS JOIN L1 AS B),
      L3   AS (SELECT 1 AS c FROM L2 AS A CROSS JOIN L2 AS B),
      L4   AS (SELECT 1 AS c FROM L3 AS A CROSS JOIN L3 AS B),
      L5   AS (SELECT 1 AS c FROM L4 AS A CROSS JOIN L4 AS B),
      Nums AS (SELECT ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS N FROM L5)

    INSERT INTO dbo.MassiveDataTest WITH (TABLOCK) 
    (Category, RandomValue, DateLogged, StatusFlag, LargeTextField)
    SELECT TOP (5000000)
        CHOOSE((N % 5) + 1, 'Analytics', 'System', 'Audit', 'Error', 'Transaction'),
        CAST((N * 3.14159) % 1000 AS DECIMAL(18,4)),
        DATEADD(SECOND, -(N % 31536000), GETDATE()),
        (N % 2),
        REPLICATE('Data_Volume_Test_Block_', (N % 5) + 1)
    FROM Nums;
    
    PRINT 'Data generation complete! Run: SELECT COUNT(*) FROM dbo.MassiveDataTest; to verify.';
END
GO


-- =======================================================================================
-- OPTIONAL STRESS TEST: GENERATE MASSIVE SCHEMA (1,500 Tables, 5,000 SPs)
-- =======================================================================================
CREATE PROCEDURE dbo.sp_GenerateStressTestSchema
AS
BEGIN
    SET NOCOUNT ON;
    PRINT 'Initializing Mass Schema Generation... This will take a few minutes.';

    IF OBJECT_ID('tempdb..#Entities') IS NOT NULL DROP TABLE #Entities;
    IF OBJECT_ID('tempdb..#Suffixes') IS NOT NULL DROP TABLE #Suffixes;
    IF OBJECT_ID('tempdb..#GeneratedTables') IS NOT NULL DROP TABLE #GeneratedTables;
    IF OBJECT_ID('tempdb..#GeneratedSPs') IS NOT NULL DROP TABLE #GeneratedSPs;

    CREATE TABLE #Entities (Name NVARCHAR(50));
    INSERT INTO #Entities (Name) VALUES 
    ('User'), ('Account'), ('Order'), ('Product'), ('Invoice'), ('Payment'), ('Shipment'), ('Department'), ('Employee'), ('Customer'), 
    ('Vendor'), ('Supplier'), ('Contract'), ('Ticket'), ('Note'), ('Attachment'), ('Tag'), ('Category'), ('Profile'), ('Setting'), 
    ('Log'), ('Event'), ('Device'), ('Session'), ('Token'), ('Campaign'), ('Lead'), ('Opportunity'), ('Quote'), ('Cart'), 
    ('Item'), ('Review'), ('Rating'), ('Subscription'), ('Plan'), ('Address'), ('Route'), ('Vehicle'), ('Warehouse'), ('Location'), 
    ('Bin'), ('Zone'), ('Rule'), ('Policy'), ('Discount'), ('Tax'), ('Rate'), ('Currency'), ('Language'), ('Module');

    CREATE TABLE #Suffixes (Name NVARCHAR(50));
    INSERT INTO #Suffixes (Name) VALUES 
    (''), ('History'), ('Log'), ('Mapping'), ('Stats'), ('Metrics'), ('Archive'), ('Rules'), ('Cache'), ('Queue'), 
    ('Details'), ('Info'), ('Data'), ('Config'), ('Audit'), ('Base'), ('Core'), ('Ext'), ('Link'), ('Ref');

    CREATE TABLE #GeneratedTables (TableID INT IDENTITY(1,1), TableName NVARCHAR(128));

    INSERT INTO #GeneratedTables (TableName)
    SELECT TOP 1500 e1.Name + e2.Name + s.Name
    FROM #Entities e1
    CROSS JOIN #Entities e2
    CROSS JOIN #Suffixes s
    ORDER BY NEWID();

    PRINT 'Creating 1,500 Tables with Custom Types...';
    DECLARE @i INT = 1;
    DECLARE @TableName NVARCHAR(128);
    DECLARE @sql NVARCHAR(MAX);
    DECLARE @RandomType NVARCHAR(50);

    WHILE @i <= 1500
    BEGIN
        SELECT @TableName = TableName FROM #GeneratedTables WHERE TableID = @i;
        SELECT @RandomType = CHOOSE(CAST(RAND()*10 AS INT)+1, 'dbo.NameType', 'dbo.MoneyType', 'dbo.PhoneType', 'dbo.IPAddressType', 'dbo.DateOfBirthType', 'dbo.AgeType', 'dbo.StatusType', 'dbo.FlagType', 'dbo.HashType', 'dbo.UUIDType');
        
        SET @sql = 'CREATE TABLE dbo.' + @TableName + ' (
            ID INT IDENTITY(1,1) PRIMARY KEY,
            DynamicColumn ' + ISNULL(@RandomType, 'NVARCHAR(100)') + ' NULL,
            GenericData NVARCHAR(500) NULL,
            CreatedAt DATETIME2 DEFAULT GETDATE(),
            IsActive BIT DEFAULT 1
        );';
        EXEC sp_executesql @sql;
        SET @i = @i + 1;
    END

    PRINT 'Weaving Foreign Key Graph (Randomly linking tables together for Routing App)...';
    SET @i = 2;
    WHILE @i <= 1500
    BEGIN
        SELECT @TableName = TableName FROM #GeneratedTables WHERE TableID = @i;
        
        DECLARE @ParentTable NVARCHAR(128);
        DECLARE @ParentID INT = CAST(RAND() * (@i - 1) AS INT) + 1;
        SELECT @ParentTable = TableName FROM #GeneratedTables WHERE TableID = @ParentID;
        
        SET @sql = 'ALTER TABLE dbo.' + @TableName + ' ADD Parent' + @ParentTable + 'ID INT NULL; 
                    ALTER TABLE dbo.' + @TableName + ' ADD CONSTRAINT FK_' + @TableName + '_' + @ParentTable + ' FOREIGN KEY (Parent' + @ParentTable + 'ID) REFERENCES dbo.' + @ParentTable + '(ID);';
        EXEC sp_executesql @sql;
        
        IF RAND() > 0.6 AND @i > 5
        BEGIN
            DECLARE @ParentID2 INT = CAST(RAND() * (@i - 1) AS INT) + 1;
            IF @ParentID2 <> @ParentID
            BEGIN
                DECLARE @ParentTable2 NVARCHAR(128);
                SELECT @ParentTable2 = TableName FROM #GeneratedTables WHERE TableID = @ParentID2;
                
                SET @sql = 'ALTER TABLE dbo.' + @TableName + ' ADD Linked' + @ParentTable2 + 'ID INT NULL; 
                            ALTER TABLE dbo.' + @TableName + ' ADD CONSTRAINT FK2_' + @TableName + '_' + @ParentTable2 + ' FOREIGN KEY (Linked' + @ParentTable2 + 'ID) REFERENCES dbo.' + @ParentTable2 + '(ID);';
                EXEC sp_executesql @sql;
            END
        END
        
        SET @i = @i + 1;
    END

    PRINT 'Creating 50 Complex Views...';
    SET @i = 1;
    WHILE @i <= 50
    BEGIN
        DECLARE @T1 NVARCHAR(128), @T2 NVARCHAR(128), @T3 NVARCHAR(128);
        SELECT TOP 1 @T1 = TableName FROM #GeneratedTables ORDER BY NEWID();
        SELECT TOP 1 @T2 = TableName FROM #GeneratedTables ORDER BY NEWID();
        SELECT TOP 1 @T3 = TableName FROM #GeneratedTables ORDER BY NEWID();

        SET @sql = 'CREATE VIEW dbo.vw_StressAnalysis_' + CAST(@i AS NVARCHAR) + ' AS 
                    SELECT t1.ID AS T1_ID, t2.ID AS T2_ID, t3.ID AS T3_ID, t1.CreatedAt
                    FROM dbo.' + @T1 + ' t1 
                    CROSS JOIN dbo.' + @T2 + ' t2 
                    CROSS JOIN dbo.' + @T3 + ' t3;';
        EXEC sp_executesql @sql;
        SET @i = @i + 1;
    END

    PRINT 'Creating 50 Functions...';
    SET @i = 1;
    WHILE @i <= 50
    BEGIN
        DECLARE @FuncType INT = CAST(RAND() * 3 AS INT);
        SELECT TOP 1 @T1 = TableName FROM #GeneratedTables ORDER BY NEWID();

        IF @FuncType = 0
            SET @sql = 'CREATE FUNCTION dbo.fn_StressScalar_' + CAST(@i AS NVARCHAR) + ' (@Input dbo.MoneyType) RETURNS dbo.MoneyType AS BEGIN RETURN @Input * 1.05; END;';
        ELSE IF @FuncType = 1
            SET @sql = 'CREATE FUNCTION dbo.fn_StressInlineTVF_' + CAST(@i AS NVARCHAR) + ' (@ID INT) RETURNS TABLE AS RETURN (SELECT TOP 10 * FROM dbo.' + @T1 + ' WHERE ID > @ID);';
        ELSE
            SET @sql = 'CREATE FUNCTION dbo.fn_StressMSTVF_' + CAST(@i AS NVARCHAR) + ' (@Filter BIT) RETURNS @Ret TABLE (ID INT, Name NVARCHAR(100)) AS BEGIN INSERT INTO @Ret (ID, Name) SELECT TOP 5 ID, GenericData FROM dbo.' + @T1 + '; RETURN; END;';

        EXEC sp_executesql @sql;
        SET @i = @i + 1;
    END

    PRINT 'Generating 5,000 Stored Procedures with Custom Data Types...';
    CREATE TABLE #GeneratedSPs (SpID INT IDENTITY(1,1), SpName NVARCHAR(128));

    INSERT INTO #GeneratedSPs (SpName)
    SELECT TOP 5000 v.Verb + t.TableName
    FROM (VALUES ('sp_Get'),('sp_Insert'),('sp_Update'),('sp_Delete'),('sp_Process'),('sp_Sync'),('sp_Archive')) v(Verb)
    CROSS JOIN #GeneratedTables t
    ORDER BY NEWID();

    SET @i = 1;
    WHILE @i <= 5000
    BEGIN
        DECLARE @SpName NVARCHAR(128);
        SELECT @SpName = SpName FROM #GeneratedSPs WHERE SpID = @i;
        
        DECLARE @RandomParam1 NVARCHAR(50), @RandomParam2 NVARCHAR(50);
        SELECT @RandomParam1 = CHOOSE(CAST(RAND()*8 AS INT)+1, 'dbo.NameType', 'dbo.MoneyType', 'dbo.PhoneType', 'INT', 'NVARCHAR(255)', 'dbo.IPAddressType', 'UNIQUEIDENTIFIER', 'dbo.StatusType');
        SELECT @RandomParam2 = CHOOSE(CAST(RAND()*8 AS INT)+1, 'dbo.AgeType', 'dbo.DateOfBirthType', 'BIT', 'FLOAT', 'dbo.HashType', 'DATETIME2', 'dbo.FlagType', 'dbo.TimestampType');
        
        SET @sql = 'CREATE PROCEDURE dbo.' + @SpName + ' 
            @Param1 ' + ISNULL(@RandomParam1, 'INT') + ',
            @Param2 ' + ISNULL(@RandomParam2, 'BIT') + ' = NULL OUTPUT
        AS 
        BEGIN
            SET NOCOUNT ON;
            SELECT @Param1 AS InParam, @Param2 AS OutParam;
        END';
        
        EXEC sp_executesql @sql;
        SET @i = @i + 1;
    END

    PRINT '==================================================';
    PRINT 'MASS SCHEMA GENERATION COMPLETE!';
    PRINT 'Total Tables: 1,500';
    PRINT 'Total Stored Procedures: 5,000';
    PRINT '==================================================';
END
GO


-- =======================================================================================
-- EXECUTE OPTIONAL STRESS TEST SCHEMA GENERATION (Comment out if not needed)
-- =======================================================================================
EXEC dbo.sp_GenerateStressTestSchema;
GO

