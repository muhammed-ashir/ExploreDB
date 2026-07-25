-- =======================================================================================
-- Create Database
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
CREATE TYPE dbo.MoneyType FROM DECIMAL(19,4) NOT NULL;
CREATE TYPE dbo.AddressType FROM NVARCHAR(500) NULL;
GO

-- =======================================================================================
-- Create Tables
-- =======================================================================================

-- 1. Users Table
CREATE TABLE dbo.Users (
    UserID INT IDENTITY(1,1) PRIMARY KEY,
    FirstName dbo.NameType,
    LastName dbo.NameType,
    Email dbo.EmailType UNIQUE,
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    IsActive BIT DEFAULT 1,
    ProfilePicture VARBINARY(MAX) NULL
);
GO

-- 2. UserAddresses Table
CREATE TABLE dbo.UserAddresses (
    AddressID INT IDENTITY(1,1) PRIMARY KEY,
    UserID INT NOT NULL,
    AddressLine1 dbo.AddressType,
    AddressLine2 dbo.AddressType,
    City NVARCHAR(100) NOT NULL,
    StateProvince NVARCHAR(100) NOT NULL,
    PostalCode NVARCHAR(20) NOT NULL,
    Country NVARCHAR(100) NOT NULL,
    IsPrimary BIT DEFAULT 0,
    CONSTRAINT FK_UserAddresses_Users FOREIGN KEY (UserID) REFERENCES dbo.Users(UserID) ON DELETE CASCADE
);
GO

-- 3. Categories Table
CREATE TABLE dbo.Categories (
    CategoryID INT IDENTITY(1,1) PRIMARY KEY,
    ParentCategoryID INT NULL,
    CategoryName NVARCHAR(100) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    CONSTRAINT FK_Categories_Self FOREIGN KEY (ParentCategoryID) REFERENCES dbo.Categories(CategoryID)
);
GO

-- 4. Brands Table
CREATE TABLE dbo.Brands (
    BrandID INT IDENTITY(1,1) PRIMARY KEY,
    BrandName NVARCHAR(100) NOT NULL UNIQUE,
    WebsiteURL NVARCHAR(255) NULL
);
GO

-- 5. Products Table
CREATE TABLE dbo.Products (
    ProductID INT IDENTITY(1,1) PRIMARY KEY,
    CategoryID INT NOT NULL,
    BrandID INT NOT NULL,
    ProductName NVARCHAR(200) NOT NULL,
    SKU NVARCHAR(50) NOT NULL UNIQUE,
    Price dbo.MoneyType,
    StockQuantity INT NOT NULL DEFAULT 0,
    IsPublished BIT DEFAULT 1,
    AttributesJson NVARCHAR(MAX) NULL, -- JSON Data
    RowVersion ROWVERSION,
    CONSTRAINT FK_Products_Categories FOREIGN KEY (CategoryID) REFERENCES dbo.Categories(CategoryID),
    CONSTRAINT FK_Products_Brands FOREIGN KEY (BrandID) REFERENCES dbo.Brands(BrandID),
    CONSTRAINT CHK_Price_Positive CHECK (Price >= 0)
);
GO

-- 6. ProductReviews Table
CREATE TABLE dbo.ProductReviews (
    ReviewID BIGINT IDENTITY(1,1) PRIMARY KEY,
    ProductID INT NOT NULL,
    UserID INT NOT NULL,
    Rating TINYINT NOT NULL CHECK (Rating BETWEEN 1 AND 5),
    ReviewText NVARCHAR(1000) NULL,
    ReviewDate DATETIME2 DEFAULT GETDATE(),
    CONSTRAINT FK_ProductReviews_Products FOREIGN KEY (ProductID) REFERENCES dbo.Products(ProductID),
    CONSTRAINT FK_ProductReviews_Users FOREIGN KEY (UserID) REFERENCES dbo.Users(UserID)
);
GO

-- 7. Orders Table
CREATE TABLE dbo.Orders (
    OrderID UNIQUEIDENTIFIER DEFAULT NEWID() PRIMARY KEY,
    UserID INT NOT NULL,
    OrderDate DATETIME2 DEFAULT GETDATE(),
    TotalAmount dbo.MoneyType DEFAULT 0,
    Status VARCHAR(50) DEFAULT 'Pending',
    ShippingAddressID INT NULL,
    CONSTRAINT FK_Orders_Users FOREIGN KEY (UserID) REFERENCES dbo.Users(UserID),
    CONSTRAINT FK_Orders_Addresses FOREIGN KEY (ShippingAddressID) REFERENCES dbo.UserAddresses(AddressID)
);
GO

-- 8. OrderDetails Table
CREATE TABLE dbo.OrderDetails (
    OrderDetailID BIGINT IDENTITY(1,1) PRIMARY KEY,
    OrderID UNIQUEIDENTIFIER NOT NULL,
    ProductID INT NOT NULL,
    Quantity INT NOT NULL CHECK (Quantity > 0),
    UnitPrice dbo.MoneyType,
    LineTotal AS (Quantity * UnitPrice) PERSISTED, -- Computed Column
    CONSTRAINT FK_OrderDetails_Orders FOREIGN KEY (OrderID) REFERENCES dbo.Orders(OrderID) ON DELETE CASCADE,
    CONSTRAINT FK_OrderDetails_Products FOREIGN KEY (ProductID) REFERENCES dbo.Products(ProductID)
);
GO

-- 9. Invoices Table
CREATE TABLE dbo.Invoices (
    InvoiceID INT IDENTITY(1000, 1) PRIMARY KEY,
    OrderID UNIQUEIDENTIFIER NOT NULL,
    InvoiceDate DATETIME2 DEFAULT GETDATE(),
    DueDate DATETIME2 NOT NULL,
    TaxAmount dbo.MoneyType DEFAULT 0,
    TotalAmount dbo.MoneyType NOT NULL,
    IsPaid BIT DEFAULT 0,
    CONSTRAINT FK_Invoices_Orders FOREIGN KEY (OrderID) REFERENCES dbo.Orders(OrderID)
);
GO

-- 10. Payments Table
CREATE TABLE dbo.Payments (
    PaymentID UNIQUEIDENTIFIER DEFAULT NEWID() PRIMARY KEY,
    InvoiceID INT NOT NULL,
    PaymentMethod VARCHAR(50) NOT NULL,
    AmountPaid dbo.MoneyType NOT NULL,
    PaymentDate DATETIME2 DEFAULT GETDATE(),
    TransactionID NVARCHAR(100) NULL,
    CONSTRAINT FK_Payments_Invoices FOREIGN KEY (InvoiceID) REFERENCES dbo.Invoices(InvoiceID)
);
GO

-- 11. Shipping Table
CREATE TABLE dbo.Shipping (
    ShippingID BIGINT IDENTITY(1,1) PRIMARY KEY,
    OrderID UNIQUEIDENTIFIER NOT NULL,
    TrackingNumber NVARCHAR(100) NULL,
    Carrier NVARCHAR(100) NULL,
    EstimatedDelivery DATETIME2 NULL,
    ActualDelivery DATETIME2 NULL,
    Status NVARCHAR(50) DEFAULT 'Processing',
    CONSTRAINT FK_Shipping_Orders FOREIGN KEY (OrderID) REFERENCES dbo.Orders(OrderID)
);
GO

-- 12. AuditLog Table (For Tracking system events)
CREATE TABLE dbo.AuditLog (
    LogID BIGINT IDENTITY(1,1) PRIMARY KEY,
    TableName NVARCHAR(128) NOT NULL,
    Action NVARCHAR(50) NOT NULL,
    RecordID NVARCHAR(100) NOT NULL,
    OldData NVARCHAR(MAX) NULL,
    NewData NVARCHAR(MAX) NULL,
    ChangedBy NVARCHAR(128) DEFAULT SUSER_SNAME(),
    ChangedAt DATETIME2 DEFAULT GETDATE()
);
GO

-- 13. Wishlists Table
CREATE TABLE dbo.Wishlists (
    WishlistID INT IDENTITY(1,1) PRIMARY KEY,
    UserID INT NOT NULL,
    ListName NVARCHAR(100) NOT NULL,
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    CONSTRAINT FK_Wishlists_Users FOREIGN KEY (UserID) REFERENCES dbo.Users(UserID)
);
GO

-- 14. WishlistItems Table
CREATE TABLE dbo.WishlistItems (
    WishlistItemID BIGINT IDENTITY(1,1) PRIMARY KEY,
    WishlistID INT NOT NULL,
    ProductID INT NOT NULL,
    AddedAt DATETIME2 DEFAULT GETDATE(),
    CONSTRAINT FK_WishlistItems_Wishlists FOREIGN KEY (WishlistID) REFERENCES dbo.Wishlists(WishlistID) ON DELETE CASCADE,
    CONSTRAINT FK_WishlistItems_Products FOREIGN KEY (ProductID) REFERENCES dbo.Products(ProductID)
);
GO

-- =======================================================================================
-- Create Views
-- =======================================================================================

-- View: Active Users List
CREATE VIEW dbo.vw_ActiveUsers AS
SELECT UserID, FirstName + ' ' + LastName AS FullName, Email, CreatedAt
FROM dbo.Users WHERE IsActive = 1;
GO

-- View: Order Summary
CREATE VIEW dbo.vw_OrderSummary AS
SELECT o.OrderID, u.FirstName + ' ' + u.LastName AS CustomerName, o.OrderDate, o.Status,
       COUNT(od.ProductID) AS TotalItems, SUM(od.LineTotal) AS CalculatedTotal
FROM dbo.Orders o
JOIN dbo.Users u ON o.UserID = u.UserID
JOIN dbo.OrderDetails od ON o.OrderID = od.OrderID
GROUP BY o.OrderID, u.FirstName, u.LastName, o.OrderDate, o.Status;
GO

-- View: Product Details with Brand and Category
CREATE VIEW dbo.vw_ProductDetails AS
SELECT p.ProductID, p.SKU, p.ProductName, b.BrandName, c.CategoryName, p.Price, p.StockQuantity, p.IsPublished
FROM dbo.Products p
JOIN dbo.Brands b ON p.BrandID = b.BrandID
JOIN dbo.Categories c ON p.CategoryID = c.CategoryID;
GO

-- View: Unpaid Invoices
CREATE VIEW dbo.vw_UnpaidInvoices AS
SELECT i.InvoiceID, o.OrderID, u.FirstName + ' ' + u.LastName AS CustomerName, 
       i.InvoiceDate, i.DueDate, i.TotalAmount, DATEDIFF(DAY, i.DueDate, GETDATE()) AS DaysOverdue
FROM dbo.Invoices i
JOIN dbo.Orders o ON i.OrderID = o.OrderID
JOIN dbo.Users u ON o.UserID = u.UserID
WHERE i.IsPaid = 0;
GO

-- View: High Value Customers
CREATE VIEW dbo.vw_HighValueCustomers AS
SELECT u.UserID, u.FirstName + ' ' + u.LastName AS CustomerName, u.Email, SUM(o.TotalAmount) AS TotalSpent
FROM dbo.Users u
JOIN dbo.Orders o ON u.UserID = o.UserID
WHERE o.Status = 'Completed'
GROUP BY u.UserID, u.FirstName, u.LastName, u.Email
HAVING SUM(o.TotalAmount) > 1000; -- Threshold for high value
GO

-- View: Recent Reviews
CREATE VIEW dbo.vw_RecentReviews AS
SELECT TOP 100 r.ReviewID, p.ProductName, u.FirstName, r.Rating, r.ReviewText, r.ReviewDate
FROM dbo.ProductReviews r
JOIN dbo.Products p ON r.ProductID = p.ProductID
JOIN dbo.Users u ON r.UserID = u.UserID
ORDER BY r.ReviewDate DESC;
GO

-- =======================================================================================
-- Create Functions
-- =======================================================================================

-- Scalar: Calculate Tax
CREATE FUNCTION dbo.fn_CalculateTax (@Amount dbo.MoneyType, @TaxRate DECIMAL(5,4))
RETURNS dbo.MoneyType
AS
BEGIN
    RETURN @Amount * @TaxRate;
END
GO

-- Scalar: Get Average Product Rating
CREATE FUNCTION dbo.fn_GetProductRating (@ProductID INT)
RETURNS DECIMAL(3,2)
AS
BEGIN
    DECLARE @AvgRating DECIMAL(3,2);
    SELECT @AvgRating = AVG(CAST(Rating AS DECIMAL(3,2)))
    FROM dbo.ProductReviews WHERE ProductID = @ProductID;
    RETURN ISNULL(@AvgRating, 0);
END
GO

-- TVF: Get Orders By Date Range
CREATE FUNCTION dbo.fn_GetOrdersByDateRange (@StartDate DATETIME2, @EndDate DATETIME2)
RETURNS TABLE
AS
RETURN (
    SELECT OrderID, UserID, OrderDate, TotalAmount, Status
    FROM dbo.Orders
    WHERE OrderDate BETWEEN @StartDate AND @EndDate
);
GO

-- TVF: Get User Wishlist details
CREATE FUNCTION dbo.fn_GetUserWishlist (@UserID INT)
RETURNS TABLE
AS
RETURN (
    SELECT w.ListName, p.ProductName, p.Price, wi.AddedAt
    FROM dbo.Wishlists w
    JOIN dbo.WishlistItems wi ON w.WishlistID = wi.WishlistID
    JOIN dbo.Products p ON wi.ProductID = p.ProductID
    WHERE w.UserID = @UserID
);
GO

-- =======================================================================================
-- Create Stored Procedures
-- =======================================================================================

-- SP: Add Product Review
CREATE PROCEDURE dbo.sp_AddProductReview
    @ProductID INT, @UserID INT, @Rating TINYINT, @ReviewText NVARCHAR(1000)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.ProductReviews (ProductID, UserID, Rating, ReviewText)
    VALUES (@ProductID, @UserID, @Rating, @ReviewText);
END
GO

-- SP: Generate Invoice
CREATE PROCEDURE dbo.sp_GenerateInvoice
    @OrderID UNIQUEIDENTIFIER, @TaxRate DECIMAL(5,4) = 0.08
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @TotalAmount dbo.MoneyType, @TaxAmount dbo.MoneyType;
    
    SELECT @TotalAmount = SUM(LineTotal) FROM dbo.OrderDetails WHERE OrderID = @OrderID;
    SET @TaxAmount = dbo.fn_CalculateTax(@TotalAmount, @TaxRate);
    
    INSERT INTO dbo.Invoices (OrderID, DueDate, TaxAmount, TotalAmount, IsPaid)
    VALUES (@OrderID, DATEADD(DAY, 30, GETDATE()), @TaxAmount, @TotalAmount + @TaxAmount, 0);
END
GO

-- SP: Process Payment
CREATE PROCEDURE dbo.sp_ProcessPayment
    @InvoiceID INT, @PaymentMethod VARCHAR(50), @AmountPaid dbo.MoneyType, @TransactionID NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        
        INSERT INTO dbo.Payments (InvoiceID, PaymentMethod, AmountPaid, TransactionID)
        VALUES (@InvoiceID, @PaymentMethod, @AmountPaid, @TransactionID);
        
        DECLARE @TotalPaid dbo.MoneyType, @InvoiceTotal dbo.MoneyType;
        SELECT @TotalPaid = SUM(AmountPaid) FROM dbo.Payments WHERE InvoiceID = @InvoiceID;
        SELECT @InvoiceTotal = TotalAmount FROM dbo.Invoices WHERE InvoiceID = @InvoiceID;
        
        IF @TotalPaid >= @InvoiceTotal
        BEGIN
            UPDATE dbo.Invoices SET IsPaid = 1 WHERE InvoiceID = @InvoiceID;
        END
        
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

-- SP: Bulk Insert Products
CREATE PROCEDURE dbo.sp_BulkInsertProducts
    @BatchSize INT = 1000
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @i INT = 1;
    WHILE @i <= @BatchSize
    BEGIN
        INSERT INTO dbo.Products (CategoryID, BrandID, ProductName, SKU, Price, StockQuantity)
        VALUES (
            (SELECT TOP 1 CategoryID FROM dbo.Categories ORDER BY NEWID()),
            (SELECT TOP 1 BrandID FROM dbo.Brands ORDER BY NEWID()),
            'Product_' + CAST(NEWID() AS VARCHAR(36)),
            'SKU_' + CAST(NEWID() AS VARCHAR(36)),
            ROUND(RAND() * 1000, 2) + 1,
            CAST(RAND() * 500 AS INT)
        );
        SET @i = @i + 1;
    END
END
GO

-- =======================================================================================
-- Bulk Data Generation (Populate Tables)
-- =======================================================================================
SET NOCOUNT ON;

-- 1. Insert 50 Brands
DECLARE @b INT = 1;
WHILE @b <= 50
BEGIN
    INSERT INTO dbo.Brands (BrandName, WebsiteURL)
    VALUES ('Brand_' + CAST(@b AS VARCHAR(10)), 'https://www.brand' + CAST(@b AS VARCHAR(10)) + '.com');
    SET @b = @b + 1;
END

-- 2. Insert 50 Categories
DECLARE @c INT = 1;
WHILE @c <= 50
BEGIN
    INSERT INTO dbo.Categories (CategoryName, Description)
    VALUES ('Category_' + CAST(@c AS VARCHAR(10)), 'Description for category ' + CAST(@c AS VARCHAR(10)));
    SET @c = @c + 1;
END

-- 3. Insert 2,000 Products using our new SP
EXEC dbo.sp_BulkInsertProducts @BatchSize = 2000;

-- 4. Insert 1,000 Users
DECLARE @u INT = 1;
WHILE @u <= 1000
BEGIN
    INSERT INTO dbo.Users (FirstName, LastName, Email, IsActive)
    VALUES (
        'FirstName_' + CAST(@u AS VARCHAR(10)), 
        'LastName_' + CAST(@u AS VARCHAR(10)), 
        'user' + CAST(@u AS VARCHAR(10)) + '@example.com', 
        CASE WHEN @u % 10 = 0 THEN 0 ELSE 1 END -- 10% inactive
    );
    SET @u = @u + 1;
END

-- 5. Insert Addresses for Users (1 address per user)
INSERT INTO dbo.UserAddresses (UserID, AddressLine1, City, StateProvince, PostalCode, Country, IsPrimary)
SELECT UserID, '123 Main St Apt ' + CAST(UserID AS VARCHAR(10)), 'City_' + CAST(UserID % 50 AS VARCHAR(10)), 
       'State_' + CAST(UserID % 10 AS VARCHAR(10)), '100' + CAST(UserID % 99 AS VARCHAR(2)), 'CountryName', 1
FROM dbo.Users;

-- 6. Insert 5,000 Orders
DECLARE @o INT = 1;
DECLARE @RandomUserID INT, @NewOrderID UNIQUEIDENTIFIER;
WHILE @o <= 5000
BEGIN
    SET @NewOrderID = NEWID();
    SET @RandomUserID = (CAST(RAND() * 999 AS INT) + 1); -- Random user 1 to 1000
    
    INSERT INTO dbo.Orders (OrderID, UserID, OrderDate, Status, ShippingAddressID)
    VALUES (
        @NewOrderID, 
        @RandomUserID, 
        DATEADD(DAY, -CAST(RAND() * 365 AS INT), GETDATE()), -- Random date in past year
        CASE WHEN @o % 5 = 0 THEN 'Pending' ELSE 'Completed' END,
        (SELECT TOP 1 AddressID FROM dbo.UserAddresses WHERE UserID = @RandomUserID)
    );
    
    -- Insert 1 to 5 OrderDetails for each order
    DECLARE @NumItems INT = CAST(RAND() * 5 AS INT) + 1;
    DECLARE @od INT = 1;
    WHILE @od <= @NumItems
    BEGIN
        DECLARE @RandomProductID INT = CAST(RAND() * 1999 AS INT) + 1;
        DECLARE @ProductPrice dbo.MoneyType = (SELECT Price FROM dbo.Products WHERE ProductID = @RandomProductID);
        
        INSERT INTO dbo.OrderDetails (OrderID, ProductID, Quantity, UnitPrice)
        VALUES (@NewOrderID, @RandomProductID, CAST(RAND() * 5 AS INT) + 1, @ProductPrice);
        
        SET @od = @od + 1;
    END

    -- Update Order TotalAmount
    UPDATE dbo.Orders
    SET TotalAmount = (SELECT ISNULL(SUM(LineTotal), 0) FROM dbo.OrderDetails WHERE OrderID = @NewOrderID)
    WHERE OrderID = @NewOrderID;

    -- If completed, Generate Invoice
    IF (CASE WHEN @o % 5 = 0 THEN 'Pending' ELSE 'Completed' END) = 'Completed'
    BEGIN
        EXEC dbo.sp_GenerateInvoice @OrderID = @NewOrderID;
    END

    SET @o = @o + 1;
END

-- 7. Insert 2,000 Product Reviews
DECLARE @pr INT = 1;
WHILE @pr <= 2000
BEGIN
    INSERT INTO dbo.ProductReviews (ProductID, UserID, Rating, ReviewText, ReviewDate)
    VALUES (
        CAST(RAND() * 1999 AS INT) + 1, -- Random Product
        CAST(RAND() * 999 AS INT) + 1,  -- Random User
        CAST(RAND() * 4 AS TINYINT) + 1, -- Rating 1 to 5
        'This is a generated review text for testing bulk scenarios. Item ' + CAST(@pr AS VARCHAR(10)),
        DATEADD(DAY, -CAST(RAND() * 365 AS INT), GETDATE())
    );
    SET @pr = @pr + 1;
END

-- 8. Add some payments to invoices
INSERT INTO dbo.Payments (InvoiceID, PaymentMethod, AmountPaid, PaymentDate, TransactionID)
SELECT TOP 2000 InvoiceID, 'Credit Card', TotalAmount, GETDATE(), 'TXN_' + CAST(NEWID() AS VARCHAR(36))
FROM dbo.Invoices
WHERE IsPaid = 0;

-- Update Invoices that were paid
UPDATE i
SET i.IsPaid = 1
FROM dbo.Invoices i
JOIN dbo.Payments p ON i.InvoiceID = p.InvoiceID;

PRINT 'Bulk Data Generation Complete!';
GO
