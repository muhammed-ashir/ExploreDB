-- =======================================================================================
-- EXPLOREDB: SQL COMPARE TEST DATABASE - SOURCE (CompareDB_Source) [ENTERPRISE SUITE]
-- =======================================================================================
-- Comprehensive enterprise schema with 130+ database objects across 8 domains:
--   dbo, sales, inventory, billing, crm, hr, reporting, audit
-- Features:
--   - Added objects (Source only): New tables, views, procedures, functions, triggers, sequences
--   - Modified objects: Column additions, precision changes, updated logic, new constraints
--   - Identical objects: 100% matching tables, views, procedures, functions across Source & Target
--   - Deprecated objects removed (exist only in Target)
--
-- Target Database: CompareDB_Source
-- =======================================================================================
USE master;
GO

IF DB_ID('CompareDB_Source') IS NOT NULL
BEGIN
    ALTER DATABASE CompareDB_Source SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE CompareDB_Source;
END
GO

CREATE DATABASE CompareDB_Source;
GO

USE CompareDB_Source;
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

CREATE TYPE dbo.KeyValueType AS TABLE (
    [Key] NVARCHAR(100) NOT NULL,
    [Value] NVARCHAR(MAX) NULL
);
GO

CREATE TYPE sales.OrderItemType AS TABLE (
    ProductID INT NOT NULL,
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(18,2) NOT NULL,
    DiscountPct DECIMAL(5,2) NOT NULL
);
GO

CREATE SEQUENCE dbo.Seq_InvoiceNumber
    AS BIGINT
    START WITH 100001
    INCREMENT BY 1
    NO CYCLE;
GO

CREATE SEQUENCE sales.Seq_OrderNumber
    AS BIGINT
    START WITH 500001
    INCREMENT BY 1
    NO CYCLE;
GO

CREATE SEQUENCE billing.Seq_TransactionRef
    AS BIGINT
    START WITH 900001
    INCREMENT BY 1
    NO CYCLE;
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
    Email NVARCHAR(256) NOT NULL,
    PasswordHash VARBINARY(64) NOT NULL,
    PasswordSalt VARBINARY(32) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
    TwoFactorEnabled BIT NOT NULL CONSTRAINT DF_Users_2FA DEFAULT (0),
    LastLoginUtc DATETIME2(3) NULL,
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
    Email NVARCHAR(256) NOT NULL,
    Phone NVARCHAR(30) NULL,
    CountryCode CHAR(2) NOT NULL,
    LoyaltyTier NVARCHAR(20) NOT NULL CONSTRAINT DF_Customers_Tier DEFAULT ('Bronze'),
    IsVerified BIT NOT NULL CONSTRAINT DF_Customers_Verified DEFAULT (0),
    CreditLimit DECIMAL(12,2) NOT NULL CONSTRAINT DF_Customers_Limit DEFAULT (1000.00),
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

CREATE TABLE dbo.NotificationTemplates (
    TemplateID INT IDENTITY(1,1) NOT NULL,
    TemplateCode NVARCHAR(50) NOT NULL,
    SubjectTemplate NVARCHAR(200) NOT NULL,
    BodyTemplate NVARCHAR(MAX) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_NotifTpl_Active DEFAULT (1),
    CONSTRAINT PK_NotificationTemplates PRIMARY KEY CLUSTERED (TemplateID),
    CONSTRAINT UQ_NotificationTemplates_Code UNIQUE (TemplateCode)
);
GO

CREATE TABLE dbo.Notifications (
    NotificationID BIGINT IDENTITY(1,1) NOT NULL,
    UserID INT NOT NULL,
    TemplateID INT NULL,
    Channel NVARCHAR(20) NOT NULL,
    Subject NVARCHAR(200) NOT NULL,
    Body NVARCHAR(MAX) NOT NULL,
    IsRead BIT NOT NULL CONSTRAINT DF_Notifications_IsRead DEFAULT (0),
    SentAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Notifications_Sent DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Notifications PRIMARY KEY CLUSTERED (NotificationID),
    CONSTRAINT FK_Notifications_Users FOREIGN KEY (UserID) REFERENCES dbo.Users(UserID),
    CONSTRAINT FK_Notifications_Templates FOREIGN KEY (TemplateID) REFERENCES dbo.NotificationTemplates(TemplateID)
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
    Barcode NVARCHAR(50) NULL,
    UomCode NVARCHAR(10) NOT NULL,
    UnitPrice DECIMAL(18,4) NOT NULL,
    StandardCost DECIMAL(18,4) NOT NULL,
    ReorderLevel INT NOT NULL CONSTRAINT DF_Products_Reorder DEFAULT (10),
    DiscontinuedDate DATE NULL,
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
    AvailableQuantity AS (OnHandQuantity - ReservedQuantity),
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
    MovementReasonCode NVARCHAR(30) NOT NULL,
    CreatedByUserId INT NOT NULL,
    ApprovedByUserId INT NULL,
    MovementDateUtc DATETIME2(3) NOT NULL CONSTRAINT DF_StockMovements_Date DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_StockMovements PRIMARY KEY CLUSTERED (MovementID),
    CONSTRAINT FK_StockMovements_Products FOREIGN KEY (ProductID) REFERENCES inventory.Products(ProductID),
    CONSTRAINT FK_StockMovements_FromWh FOREIGN KEY (FromWarehouseID) REFERENCES inventory.Warehouses(WarehouseID),
    CONSTRAINT FK_StockMovements_ToWh FOREIGN KEY (ToWarehouseID) REFERENCES inventory.Warehouses(WarehouseID),
    CONSTRAINT FK_StockMovements_Users FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users(UserID)
);
GO

CREATE TABLE inventory.SupplierProducts (
    SupplierID INT NOT NULL,
    ProductID INT NOT NULL,
    SupplierSKU NVARCHAR(50) NOT NULL,
    SupplierPrice DECIMAL(18,4) NOT NULL,
    LeadTimeDays INT NOT NULL CONSTRAINT DF_SupProd_Lead DEFAULT (7),
    CONSTRAINT PK_SupplierProducts PRIMARY KEY CLUSTERED (SupplierID, ProductID),
    CONSTRAINT FK_SupProd_Suppliers FOREIGN KEY (SupplierID) REFERENCES inventory.Suppliers(SupplierID),
    CONSTRAINT FK_SupProd_Products FOREIGN KEY (ProductID) REFERENCES inventory.Products(ProductID)
);
GO

CREATE TABLE inventory.PurchaseOrders (
    PurchaseOrderID INT IDENTITY(1,1) NOT NULL,
    SupplierID INT NOT NULL,
    WarehouseID INT NOT NULL,
    OrderDate DATE NOT NULL,
    ExpectedDeliveryDate DATE NULL,
    Status NVARCHAR(30) NOT NULL CONSTRAINT DF_PO_Status DEFAULT ('Pending'),
    TotalAmount DECIMAL(18,2) NOT NULL,
    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_PO_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_PurchaseOrders PRIMARY KEY CLUSTERED (PurchaseOrderID),
    CONSTRAINT FK_PO_Suppliers FOREIGN KEY (SupplierID) REFERENCES inventory.Suppliers(SupplierID),
    CONSTRAINT FK_PO_Warehouses FOREIGN KEY (WarehouseID) REFERENCES inventory.Warehouses(WarehouseID)
);
GO

CREATE TABLE inventory.PurchaseOrderItems (
    POItemID INT IDENTITY(1,1) NOT NULL,
    PurchaseOrderID INT NOT NULL,
    ProductID INT NOT NULL,
    OrderedQuantity INT NOT NULL,
    ReceivedQuantity INT NOT NULL CONSTRAINT DF_POI_Received DEFAULT (0),
    UnitCost DECIMAL(18,4) NOT NULL,
    CONSTRAINT PK_PurchaseOrderItems PRIMARY KEY CLUSTERED (POItemID),
    CONSTRAINT FK_POI_PurchaseOrders FOREIGN KEY (PurchaseOrderID) REFERENCES inventory.PurchaseOrders(PurchaseOrderID) ON DELETE CASCADE,
    CONSTRAINT FK_POI_Products FOREIGN KEY (ProductID) REFERENCES inventory.Products(ProductID)
);
GO

CREATE TABLE inventory.StockAudits (
    AuditID INT IDENTITY(1,1) NOT NULL,
    WarehouseID INT NOT NULL,
    AuditDate DATE NOT NULL,
    AuditorUserID INT NOT NULL,
    DiscrepancyCount INT NOT NULL CONSTRAINT DF_Audits_Disc DEFAULT (0),
    Notes NVARCHAR(500) NULL,
    CONSTRAINT PK_StockAudits PRIMARY KEY CLUSTERED (AuditID),
    CONSTRAINT FK_StockAudits_Warehouses FOREIGN KEY (WarehouseID) REFERENCES inventory.Warehouses(WarehouseID),
    CONSTRAINT FK_StockAudits_Users FOREIGN KEY (AuditorUserID) REFERENCES dbo.Users(UserID)
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
    GrandTotal AS (SubTotal - DiscountTotal + TaxTotal + ShippingTotal),
    ShippingCarrierID INT NULL,
    ShippingMethod NVARCHAR(50) NULL,
    TrackingNumber NVARCHAR(100) NULL,
    DeliveredDateUtc DATETIME2(3) NULL,
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
    DiscountAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_OrderItems_Disc DEFAULT (0),
    TaxRatePercent DECIMAL(5,2) NOT NULL CONSTRAINT DF_OrderItems_Tax DEFAULT (0),
    LineTotal AS ((Quantity * UnitPrice) - DiscountAmount),
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
    DueDateUtc DATETIME2(3) NOT NULL,
    PaidDateUtc DATETIME2(3) NULL,
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
    GlAccountCode NVARCHAR(30) NOT NULL CONSTRAINT DF_InvItems_GL DEFAULT ('4000-REV'),
    CONSTRAINT PK_InvoiceItems PRIMARY KEY CLUSTERED (InvoiceItemID),
    CONSTRAINT FK_InvItems_Invoices FOREIGN KEY (InvoiceID) REFERENCES sales.Invoices(InvoiceID) ON DELETE CASCADE
);
GO

CREATE TABLE sales.Discounts (
    DiscountID INT IDENTITY(1,1) NOT NULL,
    DiscountCode NVARCHAR(50) NOT NULL,
    DiscountName NVARCHAR(100) NOT NULL,
    DiscountPercent DECIMAL(5,2) NOT NULL,
    StartDateUtc DATETIME2(3) NOT NULL,
    EndDateUtc DATETIME2(3) NOT NULL,
    MaxUses INT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Discounts_Active DEFAULT (1),
    CONSTRAINT PK_Discounts PRIMARY KEY CLUSTERED (DiscountID),
    CONSTRAINT UQ_Discounts_Code UNIQUE (DiscountCode)
);
GO

CREATE TABLE sales.DiscountCoupons (
    CouponID INT IDENTITY(1,1) NOT NULL,
    DiscountID INT NOT NULL,
    CouponCode NVARCHAR(30) NOT NULL,
    AssignedToCustomerID INT NULL,
    IsRedeemed BIT NOT NULL CONSTRAINT DF_Coupons_Redeemed DEFAULT (0),
    RedeemedDateUtc DATETIME2(3) NULL,
    CONSTRAINT PK_DiscountCoupons PRIMARY KEY CLUSTERED (CouponID),
    CONSTRAINT UQ_DiscountCoupons_Code UNIQUE (CouponCode),
    CONSTRAINT FK_Coupons_Discounts FOREIGN KEY (DiscountID) REFERENCES sales.Discounts(DiscountID) ON DELETE CASCADE,
    CONSTRAINT FK_Coupons_Customers FOREIGN KEY (AssignedToCustomerID) REFERENCES dbo.Customers(CustomerID)
);
GO

CREATE TABLE sales.OrderFulfillments (
    FulfillmentID INT IDENTITY(1,1) NOT NULL,
    OrderID INT NOT NULL,
    WarehouseID INT NOT NULL,
    FulfillmentStatus NVARCHAR(30) NOT NULL,
    ShippedDateUtc DATETIME2(3) NULL,
    PackedByUserId INT NOT NULL,
    CONSTRAINT PK_OrderFulfillments PRIMARY KEY CLUSTERED (FulfillmentID),
    CONSTRAINT FK_Fulfillment_Orders FOREIGN KEY (OrderID) REFERENCES sales.Orders(OrderID),
    CONSTRAINT FK_Fulfillment_Warehouse FOREIGN KEY (WarehouseID) REFERENCES inventory.Warehouses(WarehouseID),
    CONSTRAINT FK_Fulfillment_Users FOREIGN KEY (PackedByUserId) REFERENCES dbo.Users(UserID)
);
GO

CREATE TABLE sales.Returns (
    ReturnID INT IDENTITY(1,1) NOT NULL,
    OrderID INT NOT NULL,
    ReturnReason NVARCHAR(100) NOT NULL,
    RefundAmount DECIMAL(18,2) NOT NULL,
    ReturnStatus NVARCHAR(30) NOT NULL CONSTRAINT DF_Returns_Status DEFAULT ('Pending'),
    RequestedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Returns_Requested DEFAULT (SYSUTCDATETIME()),
    ProcessedAtUtc DATETIME2(3) NULL,
    CONSTRAINT PK_Returns PRIMARY KEY CLUSTERED (ReturnID),
    CONSTRAINT FK_Returns_Orders FOREIGN KEY (OrderID) REFERENCES sales.Orders(OrderID)
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
    TrialDays INT NOT NULL CONSTRAINT DF_Plans_Trial DEFAULT (14),
    BillingInterval NVARCHAR(20) NOT NULL CONSTRAINT DF_Plans_Interval DEFAULT ('Monthly'),
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
    AutoRenew BIT NOT NULL CONSTRAINT DF_Subs_AutoRenew DEFAULT (1),
    CancelledAtUtc DATETIME2(3) NULL,
    Status NVARCHAR(30) NOT NULL CONSTRAINT DF_Subs_Status DEFAULT ('Active'),
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
    GatewayFee DECIMAL(18,2) NOT NULL CONSTRAINT DF_SubInv_Fee DEFAULT (0),
    PaymentRef NVARCHAR(100) NULL,
    IsPaid BIT NOT NULL CONSTRAINT DF_SubInv_Paid DEFAULT (0),
    PaidAtUtc DATETIME2(3) NULL,
    CONSTRAINT PK_SubscriptionInvoices PRIMARY KEY CLUSTERED (SubInvoiceID),
    CONSTRAINT FK_SubInv_Subscriptions FOREIGN KEY (SubscriptionID) REFERENCES billing.Subscriptions(SubscriptionID)
);
GO

CREATE TABLE billing.PaymentTransactions (
    TransactionID BIGINT IDENTITY(1,1) NOT NULL,
    CustomerID INT NOT NULL,
    GatewayID INT NOT NULL,
    TransactionRef NVARCHAR(100) NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    CurrencyCode CHAR(3) NOT NULL,
    TransactionStatus NVARCHAR(30) NOT NULL,
    GatewayResponse NVARCHAR(MAX) NULL,
    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_PayTx_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_PaymentTransactions PRIMARY KEY CLUSTERED (TransactionID),
    CONSTRAINT UQ_PaymentTransactions_Ref UNIQUE (TransactionRef),
    CONSTRAINT FK_PayTx_Customers FOREIGN KEY (CustomerID) REFERENCES dbo.Customers(CustomerID),
    CONSTRAINT FK_PayTx_Gateways FOREIGN KEY (GatewayID) REFERENCES billing.PaymentGateways(GatewayID),
    CONSTRAINT FK_PayTx_Currencies FOREIGN KEY (CurrencyCode) REFERENCES dbo.Currencies(CurrencyCode)
);
GO

CREATE TABLE billing.Refunds (
    RefundID INT IDENTITY(1,1) NOT NULL,
    TransactionID BIGINT NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    Reason NVARCHAR(200) NOT NULL,
    ProcessedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Refunds_Processed DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Refunds PRIMARY KEY CLUSTERED (RefundID),
    CONSTRAINT FK_Refunds_Transactions FOREIGN KEY (TransactionID) REFERENCES billing.PaymentTransactions(TransactionID)
);
GO

CREATE TABLE billing.Disputes (
    DisputeID INT IDENTITY(1,1) NOT NULL,
    TransactionID BIGINT NOT NULL,
    DisputeReason NVARCHAR(100) NOT NULL,
    DisputeStatus NVARCHAR(30) NOT NULL CONSTRAINT DF_Disputes_Status DEFAULT ('Open'),
    EvidenceDueDate DATE NOT NULL,
    ResolvedDateUtc DATETIME2(3) NULL,
    CONSTRAINT PK_Disputes PRIMARY KEY CLUSTERED (DisputeID),
    CONSTRAINT FK_Disputes_Transactions FOREIGN KEY (TransactionID) REFERENCES billing.PaymentTransactions(TransactionID)
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
    Score INT NOT NULL CONSTRAINT DF_Leads_Score DEFAULT (0),
    AssignedToUserId INT NULL,
    LifecycleStage NVARCHAR(50) NOT NULL CONSTRAINT DF_Leads_Stage DEFAULT ('Prospect'),
    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Leads_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Leads PRIMARY KEY CLUSTERED (LeadID),
    CONSTRAINT FK_Leads_Sources FOREIGN KEY (SourceID) REFERENCES crm.LeadSources(SourceID),
    CONSTRAINT FK_Leads_Users FOREIGN KEY (AssignedToUserId) REFERENCES dbo.Users(UserID)
);
GO

CREATE TABLE crm.Opportunities (
    OpportunityID INT IDENTITY(1,1) NOT NULL,
    OpportunityName NVARCHAR(150) NOT NULL,
    CustomerID INT NULL,
    LeadID INT NULL,
    StageID INT NOT NULL,
    EstValue DECIMAL(18,2) NOT NULL,
    Probability DECIMAL(5,2) NOT NULL CONSTRAINT DF_Opps_Prob DEFAULT (20.0),
    WeightedValue AS (EstValue * (Probability / 100.0)),
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
    DurationMinutes INT NOT NULL CONSTRAINT DF_Activities_Duration DEFAULT (15),
    Outcome NVARCHAR(100) NULL,
    OccurredAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Activities_Occurred DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Activities PRIMARY KEY CLUSTERED (ActivityID),
    CONSTRAINT FK_Activities_Leads FOREIGN KEY (LeadID) REFERENCES crm.Leads(LeadID),
    CONSTRAINT FK_Activities_Opps FOREIGN KEY (OpportunityID) REFERENCES crm.Opportunities(OpportunityID),
    CONSTRAINT FK_Activities_Users FOREIGN KEY (PerformedByUserId) REFERENCES dbo.Users(UserID)
);
GO

CREATE TABLE crm.Campaigns (
    CampaignID INT IDENTITY(1,1) NOT NULL,
    CampaignName NVARCHAR(150) NOT NULL,
    CampaignType NVARCHAR(50) NOT NULL,
    Budget DECIMAL(18,2) NOT NULL CONSTRAINT DF_Campaigns_Budget DEFAULT (0),
    ActualCost DECIMAL(18,2) NOT NULL CONSTRAINT DF_Campaigns_Cost DEFAULT (0),
    StartDate DATE NOT NULL,
    EndDate DATE NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Campaigns_Active DEFAULT (1),
    CONSTRAINT PK_Campaigns PRIMARY KEY CLUSTERED (CampaignID)
);
GO

CREATE TABLE crm.CampaignMembers (
    CampaignMemberID INT IDENTITY(1,1) NOT NULL,
    CampaignID INT NOT NULL,
    LeadID INT NULL,
    CustomerID INT NULL,
    Status NVARCHAR(30) NOT NULL CONSTRAINT DF_CampMem_Status DEFAULT ('Targeted'),
    RespondedAtUtc DATETIME2(3) NULL,
    CONSTRAINT PK_CampaignMembers PRIMARY KEY CLUSTERED (CampaignMemberID),
    CONSTRAINT FK_CampMem_Campaigns FOREIGN KEY (CampaignID) REFERENCES crm.Campaigns(CampaignID) ON DELETE CASCADE,
    CONSTRAINT FK_CampMem_Leads FOREIGN KEY (LeadID) REFERENCES crm.Leads(LeadID),
    CONSTRAINT FK_CampMem_Customers FOREIGN KEY (CustomerID) REFERENCES dbo.Customers(CustomerID)
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
    ManagerID INT NULL,
    FirstName NVARCHAR(50) NOT NULL,
    LastName NVARCHAR(50) NOT NULL,
    WorkEmail NVARCHAR(256) NOT NULL,
    HireDate DATE NOT NULL,
    EmploymentType NVARCHAR(30) NOT NULL CONSTRAINT DF_Employees_Type DEFAULT ('FullTime'),
    IsActive BIT NOT NULL CONSTRAINT DF_Employees_Active DEFAULT (1),
    CONSTRAINT PK_Employees PRIMARY KEY CLUSTERED (EmployeeID),
    CONSTRAINT FK_Employees_Departments FOREIGN KEY (DepartmentID) REFERENCES hr.Departments(DepartmentID),
    CONSTRAINT FK_Employees_JobTitles FOREIGN KEY (JobTitleID) REFERENCES hr.JobTitles(JobTitleID),
    CONSTRAINT FK_Employees_Manager FOREIGN KEY (ManagerID) REFERENCES hr.Employees(EmployeeID)
);
GO

CREATE TABLE hr.CompensationHistory (
    CompID INT IDENTITY(1,1) NOT NULL,
    EmployeeID INT NOT NULL,
    BaseSalary DECIMAL(18,2) NOT NULL,
    BonusPercentage DECIMAL(5,2) NOT NULL CONSTRAINT DF_Comp_Bonus DEFAULT (0),
    EffectiveDate DATE NOT NULL,
    ApprovedByUserId INT NOT NULL,
    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Comp_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_CompensationHistory PRIMARY KEY CLUSTERED (CompID),
    CONSTRAINT FK_Comp_Employees FOREIGN KEY (EmployeeID) REFERENCES hr.Employees(EmployeeID),
    CONSTRAINT FK_Comp_Users FOREIGN KEY (ApprovedByUserId) REFERENCES dbo.Users(UserID)
);
GO

CREATE TABLE hr.LeaveTypes (
    LeaveTypeID INT IDENTITY(1,1) NOT NULL,
    LeaveTypeName NVARCHAR(50) NOT NULL,
    AnnualDaysAllowed INT NOT NULL,
    IsPaid BIT NOT NULL CONSTRAINT DF_LeaveTypes_Paid DEFAULT (1),
    CONSTRAINT PK_LeaveTypes PRIMARY KEY CLUSTERED (LeaveTypeID)
);
GO

CREATE TABLE hr.Leaves (
    LeaveID INT IDENTITY(1,1) NOT NULL,
    EmployeeID INT NOT NULL,
    LeaveTypeID INT NOT NULL,
    StartDate DATE NOT NULL,
    EndDate DATE NOT NULL,
    DaysTaken INT NOT NULL,
    Status NVARCHAR(30) NOT NULL CONSTRAINT DF_Leaves_Status DEFAULT ('Pending'),
    ApprovedByEmployeeID INT NULL,
    CONSTRAINT PK_Leaves PRIMARY KEY CLUSTERED (LeaveID),
    CONSTRAINT FK_Leaves_Employees FOREIGN KEY (EmployeeID) REFERENCES hr.Employees(EmployeeID),
    CONSTRAINT FK_Leaves_Types FOREIGN KEY (LeaveTypeID) REFERENCES hr.LeaveTypes(LeaveTypeID),
    CONSTRAINT FK_Leaves_Approver FOREIGN KEY (ApprovedByEmployeeID) REFERENCES hr.Employees(EmployeeID)
);
GO

CREATE TABLE audit.SecurityLog (
    LogID BIGINT IDENTITY(1,1) NOT NULL,
    EventType NVARCHAR(50) NOT NULL,
    Severity NVARCHAR(20) NOT NULL,
    UserID INT NULL,
    IpAddress NVARCHAR(50) NULL,
    UserAgent NVARCHAR(255) NULL,
    Details NVARCHAR(MAX) NULL,
    TimestampUtc DATETIME2(3) NOT NULL CONSTRAINT DF_SecLog_Time DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_SecurityLog PRIMARY KEY CLUSTERED (LogID)
);
GO

CREATE TABLE audit.SchemaChangeLog (
    ChangeID INT IDENTITY(1,1) NOT NULL,
    DatabaseName NVARCHAR(100) NOT NULL,
    ObjectType NVARCHAR(50) NOT NULL,
    ObjectName NVARCHAR(150) NOT NULL,
    CommandText NVARCHAR(MAX) NOT NULL,
    ExecutedBy NVARCHAR(100) NOT NULL,
    ExecutedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_SchemaLog_Time DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_SchemaChangeLog PRIMARY KEY CLUSTERED (ChangeID)
);
GO

CREATE TABLE audit.DataChangeLog (
    DataChangeID BIGINT IDENTITY(1,1) NOT NULL,
    TableName NVARCHAR(150) NOT NULL,
    RecordID NVARCHAR(100) NOT NULL,
    Operation NVARCHAR(20) NOT NULL,
    OldValuesJson NVARCHAR(MAX) NULL,
    NewValuesJson NVARCHAR(MAX) NULL,
    ChangedBy NVARCHAR(100) NOT NULL,
    ChangedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_DataChangeLog_Time DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_DataChangeLog PRIMARY KEY CLUSTERED (DataChangeID)
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
    s.AvailableQuantity
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
    u.TwoFactorEnabled,
    u.LastLoginUtc,
    STRING_AGG(r.RoleName, ', ') WITHIN GROUP (ORDER BY r.RoleName) AS Roles
FROM dbo.Users u
LEFT JOIN dbo.UserRoles ur ON u.UserID = ur.UserID
LEFT JOIN dbo.Roles r ON ur.RoleID = r.RoleID
WHERE u.IsActive = 1
GROUP BY u.UserID, u.Username, u.Email, u.TwoFactorEnabled, u.LastLoginUtc;
GO

CREATE VIEW dbo.vw_CustomerSummary
AS
SELECT 
    c.CustomerID,
    c.FirstName + ' ' + c.LastName AS FullName,
    c.Email,
    c.LoyaltyTier,
    c.CreditLimit,
    ISNULL(SUM(o.GrandTotal), 0) AS TotalOrderValue,
    COUNT(o.OrderID) AS TotalOrdersPlaced
FROM dbo.Customers c
LEFT JOIN sales.Orders o ON c.CustomerID = o.CustomerID
GROUP BY c.CustomerID, c.FirstName, c.LastName, c.Email, c.LoyaltyTier, c.CreditLimit;
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
    c.LoyaltyTier,
    o.SubTotal,
    o.DiscountTotal,
    o.TaxTotal,
    o.ShippingTotal,
    o.GrandTotal,
    o.TrackingNumber
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
    i.Status,
    DATEDIFF(day, i.DueDateUtc, SYSUTCDATETIME()) AS DaysOverdue,
    CASE 
        WHEN DATEDIFF(day, i.DueDateUtc, SYSUTCDATETIME()) <= 0 THEN 'Current'
        WHEN DATEDIFF(day, i.DueDateUtc, SYSUTCDATETIME()) BETWEEN 1 AND 30 THEN '1-30 Days'
        WHEN DATEDIFF(day, i.DueDateUtc, SYSUTCDATETIME()) BETWEEN 31 AND 60 THEN '31-60 Days'
        ELSE '60+ Days'
    END AS AgingBucket
FROM sales.Invoices i
WHERE i.Status IN ('Unpaid', 'PartiallyPaid');
GO

CREATE VIEW inventory.vw_LowStockAlerts
AS
SELECT 
    p.ProductID,
    p.ProductCode,
    p.ProductName,
    p.ReorderLevel,
    ISNULL(SUM(s.AvailableQuantity), 0) AS TotalAvailable,
    CASE 
        WHEN ISNULL(SUM(s.AvailableQuantity), 0) <= 0 THEN 'Critical'
        WHEN ISNULL(SUM(s.AvailableQuantity), 0) <= p.ReorderLevel THEN 'Reorder Required'
        ELSE 'Normal'
    END AS StockStatus
FROM inventory.Products p
LEFT JOIN inventory.StockLevels s ON p.ProductID = s.ProductID
WHERE p.IsActive = 1
GROUP BY p.ProductID, p.ProductCode, p.ProductName, p.ReorderLevel
HAVING ISNULL(SUM(s.AvailableQuantity), 0) <= p.ReorderLevel;
GO

CREATE VIEW inventory.vw_ProductCatalog
AS
SELECT 
    p.ProductID,
    p.ProductCode,
    p.ProductName,
    p.Barcode,
    c.CategoryName,
    p.UnitPrice,
    p.StandardCost,
    p.UnitPrice - p.StandardCost AS UnitProfitMargin,
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
    s.AutoRenew,
    s.Status
FROM billing.Subscriptions s
INNER JOIN dbo.Customers c ON s.CustomerID = c.CustomerID
INNER JOIN billing.BillingPlans p ON s.PlanID = p.PlanID
WHERE s.Status = 'Active';
GO

CREATE VIEW crm.vw_PipelineSummary
AS
SELECT 
    st.StageID,
    st.StageName,
    st.ProbabilityPercent,
    COUNT(o.OpportunityID) AS OpportunityCount,
    ISNULL(SUM(o.EstValue), 0) AS TotalPipelineValue,
    ISNULL(SUM(o.WeightedValue), 0) AS WeightedForecastValue
FROM crm.OpportunityStages st
LEFT JOIN crm.Opportunities o ON st.StageID = o.StageID
GROUP BY st.StageID, st.StageName, st.ProbabilityPercent;
GO

CREATE VIEW hr.vw_EmployeeDirectory
AS
SELECT 
    e.EmployeeID,
    e.FirstName + ' ' + e.LastName AS FullName,
    e.WorkEmail,
    d.DepartmentName,
    jt.TitleName AS JobTitle,
    m.FirstName + ' ' + m.LastName AS ManagerName,
    e.HireDate,
    e.EmploymentType
FROM hr.Employees e
INNER JOIN hr.Departments d ON e.DepartmentID = d.DepartmentID
INNER JOIN hr.JobTitles jt ON e.JobTitleID = jt.JobTitleID
LEFT JOIN hr.Employees m ON e.ManagerID = m.EmployeeID
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
    (p.UnitPrice - p.StandardCost) AS MarginAmount,
    CAST(((p.UnitPrice - p.StandardCost) / NULLIF(p.UnitPrice, 0)) * 100 AS DECIMAL(5,2)) AS MarginPercent,
    DENSE_RANK() OVER (PARTITION BY p.CategoryID ORDER BY (p.UnitPrice - p.StandardCost) DESC) AS CategoryRank
FROM inventory.Products p
INNER JOIN dbo.Categories c ON p.CategoryID = c.CategoryID
WHERE p.IsActive = 1;
GO

CREATE VIEW dbo.vw_SystemHealth
AS
SELECT 
    (SELECT COUNT(*) FROM dbo.Users WHERE IsActive = 1) AS ActiveUsers,
    (SELECT COUNT(*) FROM sales.Orders WHERE OrderStatus = 'Pending') AS PendingOrders,
    (SELECT COUNT(*) FROM inventory.Products WHERE IsActive = 1) AS TotalProducts,
    (SELECT COUNT(*) FROM billing.Subscriptions WHERE Status = 'Active') AS ActiveSubscriptions;
GO

CREATE VIEW sales.vw_DailySalesSummary
AS
SELECT 
    CAST(OrderDateUtc AS DATE) AS SalesDate,
    COUNT(OrderID) AS OrderCount,
    SUM(SubTotal) AS GrossSales,
    SUM(DiscountTotal) AS TotalDiscounts,
    SUM(TaxTotal) AS TotalTax,
    SUM(GrandTotal) AS NetSales
FROM sales.Orders
GROUP BY CAST(OrderDateUtc AS DATE);
GO

CREATE VIEW sales.vw_ReturnRates
AS
SELECT 
    p.ProductID,
    p.ProductName,
    COUNT(DISTINCT oi.OrderID) AS TotalOrdersWithItem,
    COUNT(r.ReturnID) AS TotalReturns,
    CAST((COUNT(r.ReturnID) * 100.0) / NULLIF(COUNT(DISTINCT oi.OrderID), 0) AS DECIMAL(5,2)) AS ReturnRatePct
FROM inventory.Products p
INNER JOIN sales.OrderItems oi ON p.ProductID = oi.ProductID
LEFT JOIN sales.Returns r ON oi.OrderID = r.OrderID
GROUP BY p.ProductID, p.ProductName;
GO

CREATE VIEW inventory.vw_PendingPurchaseOrders
AS
SELECT 
    po.PurchaseOrderID,
    s.SupplierName,
    w.WarehouseName,
    po.OrderDate,
    po.ExpectedDeliveryDate,
    po.TotalAmount,
    COUNT(poi.POItemID) AS TotalLineItems
FROM inventory.PurchaseOrders po
INNER JOIN inventory.Suppliers s ON po.SupplierID = s.SupplierID
INNER JOIN inventory.Warehouses w ON po.WarehouseID = w.WarehouseID
LEFT JOIN inventory.PurchaseOrderItems poi ON po.PurchaseOrderID = poi.PurchaseOrderID
WHERE po.Status = 'Pending'
GROUP BY po.PurchaseOrderID, s.SupplierName, w.WarehouseName, po.OrderDate, po.ExpectedDeliveryDate, po.TotalAmount;
GO

CREATE VIEW billing.vw_MonthlyRecurringRevenue
AS
SELECT 
    p.PlanName,
    COUNT(s.SubscriptionID) AS ActiveSubscribers,
    SUM(p.PricePerMonth) AS TotalMRR
FROM billing.Subscriptions s
INNER JOIN billing.BillingPlans p ON s.PlanID = p.PlanID
WHERE s.Status = 'Active'
GROUP BY p.PlanName;
GO

CREATE VIEW billing.vw_FailedPayments
AS
SELECT 
    t.TransactionID,
    t.TransactionRef,
    c.CustomerID,
    c.FirstName + ' ' + c.LastName AS CustomerName,
    t.Amount,
    t.CreatedAtUtc,
    t.GatewayResponse
FROM billing.PaymentTransactions t
INNER JOIN dbo.Customers c ON t.CustomerID = c.CustomerID
WHERE t.TransactionStatus = 'Failed';
GO

CREATE VIEW crm.vw_CampaignPerformance
AS
SELECT 
    c.CampaignID,
    c.CampaignName,
    c.CampaignType,
    c.Budget,
    c.ActualCost,
    COUNT(cm.CampaignMemberID) AS TotalMembers,
    SUM(CASE WHEN cm.Status = 'Responded' THEN 1 ELSE 0 END) AS TotalResponded
FROM crm.Campaigns c
LEFT JOIN crm.CampaignMembers cm ON c.CampaignID = cm.CampaignID
GROUP BY c.CampaignID, c.CampaignName, c.CampaignType, c.Budget, c.ActualCost;
GO

CREATE VIEW crm.vw_StaleLeads
AS
SELECT 
    l.LeadID,
    l.FirstName + ' ' + l.LastName AS LeadName,
    l.Email,
    l.LifecycleStage,
    DATEDIFF(day, l.CreatedAtUtc, SYSUTCDATETIME()) AS DaysSinceCreated
FROM crm.Leads l
WHERE l.LifecycleStage = 'Prospect'
  AND DATEDIFF(day, l.CreatedAtUtc, SYSUTCDATETIME()) > 90;
GO

CREATE VIEW hr.vw_LeaveBalance
AS
SELECT 
    e.EmployeeID,
    e.FirstName + ' ' + e.LastName AS EmployeeName,
    lt.LeaveTypeName,
    lt.AnnualDaysAllowed,
    ISNULL(SUM(l.DaysTaken), 0) AS TotalDaysTaken,
    lt.AnnualDaysAllowed - ISNULL(SUM(l.DaysTaken), 0) AS RemainingBalance
FROM hr.Employees e
CROSS JOIN hr.LeaveTypes lt
LEFT JOIN hr.Leaves l ON e.EmployeeID = l.EmployeeID AND lt.LeaveTypeID = l.LeaveTypeID AND l.Status = 'Approved'
WHERE e.IsActive = 1
GROUP BY e.EmployeeID, e.FirstName, e.LastName, lt.LeaveTypeName, lt.AnnualDaysAllowed;
GO

CREATE VIEW reporting.vw_DailyExecutiveKPI
AS
SELECT 
    CAST(SYSUTCDATETIME() AS DATE) AS ReportDate,
    (SELECT COUNT(*) FROM sales.Orders WHERE CAST(OrderDateUtc AS DATE) = CAST(SYSUTCDATETIME() AS DATE)) AS OrdersToday,
    (SELECT ISNULL(SUM(GrandTotal), 0) FROM sales.Orders WHERE CAST(OrderDateUtc AS DATE) = CAST(SYSUTCDATETIME() AS DATE)) AS RevenueToday,
    (SELECT COUNT(*) FROM crm.Leads WHERE CAST(CreatedAtUtc AS DATE) = CAST(SYSUTCDATETIME() AS DATE)) AS NewLeadsToday;
GO

CREATE VIEW reporting.vw_MonthlyRevenueBreakdown
AS
SELECT 
    YEAR(OrderDateUtc) AS OrderYear,
    MONTH(OrderDateUtc) AS OrderMonth,
    COUNT(OrderID) AS TotalOrders,
    SUM(SubTotal) AS GrossSales,
    SUM(DiscountTotal) AS TotalDiscounts,
    SUM(GrandTotal) AS NetRevenue
FROM sales.Orders
GROUP BY YEAR(OrderDateUtc), MONTH(OrderDateUtc);
GO

CREATE VIEW reporting.vw_CustomerLifetimeValue
AS
SELECT 
    c.CustomerID,
    c.FirstName + ' ' + c.LastName AS CustomerName,
    c.LoyaltyTier,
    COUNT(o.OrderID) AS LifetimeOrders,
    ISNULL(SUM(o.GrandTotal), 0) AS LifetimeSpend,
    DATEDIFF(day, MIN(o.OrderDateUtc), MAX(o.OrderDateUtc)) AS CustomerTenureDays
FROM dbo.Customers c
LEFT JOIN sales.Orders o ON c.CustomerID = o.CustomerID
GROUP BY c.CustomerID, c.FirstName, c.LastName, c.LoyaltyTier;
GO

CREATE VIEW audit.vw_RecentSecurityEvents
AS
SELECT TOP 100
    LogID,
    EventType,
    Severity,
    UserID,
    IpAddress,
    TimestampUtc
FROM audit.SecurityLog
ORDER BY LogID DESC;
GO

CREATE VIEW audit.vw_SuspiciousLogins
AS
SELECT 
    IpAddress,
    COUNT(*) AS FailedAttemptsCount,
    MIN(TimestampUtc) AS FirstAttemptUtc,
    MAX(TimestampUtc) AS LastAttemptUtc
FROM audit.SecurityLog
WHERE EventType = 'LoginFailed'
GROUP BY IpAddress
HAVING COUNT(*) >= 5;
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
        sl.AvailableQuantity
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
        e.WorkEmail,
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
        u.TwoFactorEnabled,
        u.LastLoginUtc,
        u.CreatedAtUtc,
        r.RoleName
    FROM dbo.Users u
    LEFT JOIN dbo.UserRoles ur ON u.UserID = ur.UserID
    LEFT JOIN dbo.Roles r ON ur.RoleID = r.RoleID
    WHERE u.UserID = @UserID;
END;
GO

CREATE PROCEDURE dbo.sp_CreateCustomer
    @FirstName NVARCHAR(100),
    @LastName NVARCHAR(100),
    @Email NVARCHAR(256),
    @Phone NVARCHAR(30) = NULL,
    @CountryCode CHAR(2),
    @LoyaltyTier NVARCHAR(20) = 'Bronze',
    @CreditLimit DECIMAL(12,2) = 1000.00,
    @NewCustomerID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Customers (FirstName, LastName, Email, Phone, CountryCode, LoyaltyTier, IsVerified, CreditLimit, CreatedAtUtc)
    VALUES (@FirstName, @LastName, @Email, @Phone, @CountryCode, @LoyaltyTier, 0, @CreditLimit, SYSUTCDATETIME());
    
    SET @NewCustomerID = SCOPE_IDENTITY();
END;
GO

CREATE PROCEDURE sales.sp_CreateOrder
    @CustomerID INT,
    @Items sales.OrderItemType READONLY,
    @ShippingCarrierID INT = NULL,
    @NewOrderID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @OrderNum NVARCHAR(30) = 'ORD-' + CAST(NEXT VALUE FOR sales.Seq_OrderNumber AS NVARCHAR(20));
        
        DECLARE @SubTotal DECIMAL(18,2);
        SELECT @SubTotal = SUM(Quantity * UnitPrice * (1.0 - (DiscountPct / 100.0)))
        FROM @Items;

        INSERT INTO sales.Orders (OrderNumber, CustomerID, OrderStatus, SubTotal, DiscountTotal, TaxTotal, ShippingTotal, ShippingCarrierID)
        VALUES (@OrderNum, @CustomerID, 'Pending', @SubTotal, 0, @SubTotal * 0.08, 15.00, @ShippingCarrierID);

        SET @NewOrderID = SCOPE_IDENTITY();

        INSERT INTO sales.OrderItems (OrderID, ProductID, Quantity, UnitPrice, DiscountAmount, TaxRatePercent)
        SELECT 
            @NewOrderID,
            ProductID,
            Quantity,
            UnitPrice,
            (Quantity * UnitPrice * (DiscountPct / 100.0)),
            8.00
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
    @DueDateUtc DATETIME2(3),
    @NewInvoiceID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Total DECIMAL(18,2);
    SELECT @Total = GrandTotal FROM sales.Orders WHERE OrderID = @OrderID;

    DECLARE @InvNum NVARCHAR(50) = 'INV-' + CAST(NEXT VALUE FOR dbo.Seq_InvoiceNumber AS NVARCHAR(20));

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
    @ReasonCode NVARCHAR(30),
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        UPDATE inventory.StockLevels
        SET OnHandQuantity = OnHandQuantity + @QuantityAdjustment
        WHERE ProductID = @ProductID AND WarehouseID = @WarehouseID;

        INSERT INTO inventory.StockMovements (ProductID, FromWarehouseID, ToWarehouseID, Quantity, MovementType, MovementReasonCode, CreatedByUserId)
        VALUES (@ProductID, @WarehouseID, @WarehouseID, @QuantityAdjustment, @MovementType, @ReasonCode, @UserID);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

CREATE PROCEDURE billing.sp_RenewSubscription
    @SubscriptionID INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE billing.Subscriptions
    SET NextBillingDateUtc = DATEADD(month, 1, NextBillingDateUtc)
    WHERE SubscriptionID = @SubscriptionID AND Status = 'Active';
END;
GO

CREATE PROCEDURE crm.sp_ConvertLead
    @LeadID INT,
    @CreatedByUserId INT,
    @NewCustomerID INT OUTPUT,
    @NewOpportunityID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @FirstName NVARCHAR(50), @LastName NVARCHAR(50), @Email NVARCHAR(100), @Company NVARCHAR(100);
        SELECT @FirstName = FirstName, @LastName = LastName, @Email = Email, @Company = CompanyName
        FROM crm.Leads WHERE LeadID = @LeadID;

        INSERT INTO dbo.Customers (FirstName, LastName, Email, CountryCode)
        VALUES (@FirstName, @LastName, @Email, 'US');
        SET @NewCustomerID = SCOPE_IDENTITY();

        INSERT INTO crm.Opportunities (OpportunityName, CustomerID, LeadID, StageID, EstValue, CloseDate, AssignedToUserId)
        VALUES (@Company + ' - Initial Sale', @NewCustomerID, @LeadID, 1, 5000.00, DATEADD(day, 30, CAST(SYSUTCDATETIME() AS DATE)), @CreatedByUserId);
        SET @NewOpportunityID = SCOPE_IDENTITY();

        UPDATE crm.Leads SET LifecycleStage = 'Customer' WHERE LeadID = @LeadID;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

CREATE PROCEDURE hr.sp_PromoteEmployee
    @EmployeeID INT,
    @NewJobTitleID INT,
    @NewBaseSalary DECIMAL(18,2),
    @NewBonusPct DECIMAL(5,2),
    @ApprovedByUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        UPDATE hr.Employees
        SET JobTitleID = @NewJobTitleID
        WHERE EmployeeID = @EmployeeID;

        INSERT INTO hr.CompensationHistory (EmployeeID, BaseSalary, BonusPercentage, EffectiveDate, ApprovedByUserId)
        VALUES (@EmployeeID, @NewBaseSalary, @NewBonusPct, CAST(SYSUTCDATETIME() AS DATE), @ApprovedByUserId);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

CREATE PROCEDURE reporting.sp_GenerateMonthlySummary
    @Year INT,
    @Month INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        COUNT(OrderID) AS OrderCount,
        SUM(GrandTotal) AS TotalRevenue,
        AVG(GrandTotal) AS AverageOrderValue
    FROM sales.Orders
    WHERE YEAR(OrderDateUtc) = @Year AND MONTH(OrderDateUtc) = @Month;
END;
GO

CREATE PROCEDURE dbo.sp_SendNotification
    @UserID INT,
    @TemplateCode NVARCHAR(50),
    @Subject NVARCHAR(200),
    @Body NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @TplID INT = (SELECT TemplateID FROM dbo.NotificationTemplates WHERE TemplateCode = @TemplateCode);
    INSERT INTO dbo.Notifications (UserID, TemplateID, Channel, Subject, Body)
    VALUES (@UserID, @TplID, 'InApp', @Subject, @Body);
END;
GO

CREATE PROCEDURE sales.sp_ApplyBulkDiscount
    @DiscountID INT,
    @CategoryIDs dbo.IdListType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.ProductID, p.ProductName, p.UnitPrice, d.DiscountPercent
    FROM inventory.Products p
    CROSS JOIN sales.Discounts d
    WHERE d.DiscountID = @DiscountID
      AND p.CategoryID IN (SELECT Id FROM @CategoryIDs);
END;
GO

CREATE PROCEDURE sales.sp_ProcessReturn
    @OrderID INT,
    @ReturnReason NVARCHAR(100),
    @RefundAmount DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO sales.Returns (OrderID, ReturnReason, RefundAmount, ReturnStatus)
    VALUES (@OrderID, @ReturnReason, @RefundAmount, 'Approved');
END;
GO

CREATE PROCEDURE inventory.sp_ReceivePurchaseOrder
    @PurchaseOrderID INT,
    @ReceivedByUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE inventory.PurchaseOrders
    SET Status = 'Received'
    WHERE PurchaseOrderID = @PurchaseOrderID;
END;
GO

CREATE PROCEDURE inventory.sp_PerformStockTake
    @WarehouseID INT,
    @AuditorUserID INT,
    @Notes NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO inventory.StockAudits (WarehouseID, AuditDate, AuditorUserID, DiscrepancyCount, Notes)
    VALUES (@WarehouseID, CAST(SYSUTCDATETIME() AS DATE), @AuditorUserID, 0, @Notes);
END;
GO

CREATE PROCEDURE billing.sp_ProcessRefund
    @TransactionID BIGINT,
    @Amount DECIMAL(18,2),
    @Reason NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO billing.Refunds (TransactionID, Amount, Reason)
    VALUES (@TransactionID, @Amount, @Reason);
END;
GO

CREATE PROCEDURE billing.sp_ReconcileTransactions
    @Date DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        GatewayID,
        TransactionStatus,
        COUNT(*) AS TransactionCount,
        SUM(Amount) AS TotalSettled
    FROM billing.PaymentTransactions
    WHERE CAST(CreatedAtUtc AS DATE) = @Date
    GROUP BY GatewayID, TransactionStatus;
END;
GO

CREATE PROCEDURE crm.sp_AssignLeads
    @UserId INT,
    @MaxLeads INT = 10
AS
BEGIN
    SET NOCOUNT ON;
    WITH Unassigned AS (
        SELECT TOP (@MaxLeads) LeadID
        FROM crm.Leads
        WHERE AssignedToUserId IS NULL
        ORDER BY Score DESC, CreatedAtUtc ASC
    )
    UPDATE crm.Leads
    SET AssignedToUserId = @UserId
    WHERE LeadID IN (SELECT LeadID FROM Unassigned);
END;
GO

CREATE PROCEDURE hr.sp_RequestLeave
    @EmployeeID INT,
    @LeaveTypeID INT,
    @StartDate DATE,
    @EndDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Days INT = DATEDIFF(day, @StartDate, @EndDate) + 1;
    INSERT INTO hr.Leaves (EmployeeID, LeaveTypeID, StartDate, EndDate, DaysTaken, Status)
    VALUES (@EmployeeID, @LeaveTypeID, @StartDate, @EndDate, @Days, 'Pending');
END;
GO

CREATE PROCEDURE reporting.sp_ExportAuditMetrics
    @StartDate DATE,
    @EndDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT EventType, COUNT(*) AS EventCount
    FROM audit.SecurityLog
    WHERE CAST(TimestampUtc AS DATE) BETWEEN @StartDate AND @EndDate
    GROUP BY EventType;
END;
GO

CREATE PROCEDURE audit.sp_LogSecurityEvent
    @EventType NVARCHAR(50),
    @Severity NVARCHAR(20),
    @UserID INT = NULL,
    @IpAddress NVARCHAR(50) = NULL,
    @Details NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO audit.SecurityLog (EventType, Severity, UserID, IpAddress, Details)
    VALUES (@EventType, @Severity, @UserID, @IpAddress, @Details);
END;
GO

CREATE PROCEDURE audit.sp_PurgeAuditLogs
    @RetentionDays INT = 90
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM audit.SecurityLog
    WHERE TimestampUtc < DATEADD(day, -@RetentionDays, SYSUTCDATETIME());
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
    @PostalCode NVARCHAR(20),
    @CountryCode CHAR(2)
)
RETURNS NVARCHAR(500)
AS
BEGIN
    RETURN CONCAT_WS(', ', NULLIF(@AddressLine1, ''), NULLIF(@City, ''), NULLIF(@State, ''), NULLIF(@PostalCode, ''), NULLIF(@CountryCode, ''));
END;
GO

CREATE FUNCTION sales.fn_GetCustomerTotalSpend
(
    @CustomerID INT,
    @StartDate DATETIME2(3) = NULL
)
RETURNS DECIMAL(18,2)
AS
BEGIN
    DECLARE @Total DECIMAL(18,2);
    SELECT @Total = ISNULL(SUM(GrandTotal), 0.0)
    FROM sales.Orders
    WHERE CustomerID = @CustomerID
      AND (@StartDate IS NULL OR OrderDateUtc >= @StartDate);
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
    SELECT @Avail = ISNULL(SUM(AvailableQuantity), 0)
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
    DECLARE @Score INT;
    SELECT @Score = Score FROM crm.Leads WHERE LeadID = @LeadID;
    RETURN ISNULL(@Score, 0);
END;
GO

CREATE FUNCTION dbo.fn_GetSystemSetting
(
    @ConfigKey NVARCHAR(100)
)
RETURNS NVARCHAR(MAX)
AS
BEGIN
    DECLARE @Val NVARCHAR(MAX);
    SELECT @Val = ConfigValue FROM dbo.AppConfiguration WHERE ConfigKey = @ConfigKey;
    RETURN @Val;
END;
GO

CREATE FUNCTION sales.fn_CalculateDiscountedPrice
(
    @OriginalPrice DECIMAL(18,2),
    @DiscountPercent DECIMAL(5,2)
)
RETURNS DECIMAL(18,2)
AS
BEGIN
    RETURN @OriginalPrice * (1.0 - (@DiscountPercent / 100.0));
END;
GO

CREATE FUNCTION sales.fn_CalculateOrderTax
(
    @SubTotal DECIMAL(18,2),
    @TaxPercent DECIMAL(5,2)
)
RETURNS DECIMAL(18,2)
AS
BEGIN
    RETURN @SubTotal * (@TaxPercent / 100.0);
END;
GO

CREATE FUNCTION inventory.fn_CalculateInventoryValue
(
    @WarehouseID INT = NULL
)
RETURNS DECIMAL(18,2)
AS
BEGIN
    DECLARE @TotalVal DECIMAL(18,2);
    SELECT @TotalVal = ISNULL(SUM(s.OnHandQuantity * p.StandardCost), 0)
    FROM inventory.StockLevels s
    INNER JOIN inventory.Products p ON s.ProductID = p.ProductID
    WHERE (@WarehouseID IS NULL OR s.WarehouseID = @WarehouseID);
    RETURN @TotalVal;
END;
GO

CREATE FUNCTION billing.fn_CalculateSubscriptionProration
(
    @PricePerMonth DECIMAL(18,2),
    @DaysUsed INT,
    @DaysInMonth INT
)
RETURNS DECIMAL(18,2)
AS
BEGIN
    IF @DaysInMonth <= 0 RETURN 0;
    RETURN CAST((@PricePerMonth * @DaysUsed) / @DaysInMonth AS DECIMAL(18,2));
END;
GO

CREATE FUNCTION billing.fn_GetCustomerMRR
(
    @CustomerID INT
)
RETURNS DECIMAL(18,2)
AS
BEGIN
    DECLARE @MRR DECIMAL(18,2);
    SELECT @MRR = ISNULL(SUM(p.PricePerMonth), 0)
    FROM billing.Subscriptions s
    INNER JOIN billing.BillingPlans p ON s.PlanID = p.PlanID
    WHERE s.CustomerID = @CustomerID AND s.Status = 'Active';
    RETURN @MRR;
END;
GO

CREATE FUNCTION crm.fn_CalculatePipelineVelocity
(
    @TotalWonDeals INT,
    @AvgDealSize DECIMAL(18,2),
    @WinRatePct DECIMAL(5,2),
    @SalesCycleDays INT
)
RETURNS DECIMAL(18,2)
AS
BEGIN
    IF @SalesCycleDays <= 0 RETURN 0;
    RETURN CAST((@TotalWonDeals * @AvgDealSize * (@WinRatePct / 100.0)) / @SalesCycleDays AS DECIMAL(18,2));
END;
GO

CREATE TRIGGER sales.trg_UpdateStockOnOrder
ON sales.OrderItems
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE s
    SET s.ReservedQuantity = s.ReservedQuantity + i.Quantity
    FROM inventory.StockLevels s
    INNER JOIN inserted i ON s.ProductID = i.ProductID;
END;
GO

CREATE TRIGGER sales.trg_Orders_AuditStatus
ON sales.Orders
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(OrderStatus)
    BEGIN
        INSERT INTO audit.SecurityLog (EventType, Severity, Details)
        SELECT 'OrderStatusChanged', 'Info', 
               CONCAT('Order ', i.OrderNumber, ' status changed from ', d.OrderStatus, ' to ', i.OrderStatus)
        FROM inserted i
        INNER JOIN deleted d ON i.OrderID = d.OrderID;
    END
END;
GO

