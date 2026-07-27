USE ExploreDB;
GO

-- =======================================================================================
-- THE ULTIMATE MASSIVE DATA GENERATOR (100k+ Records)
-- =======================================================================================
CREATE PROCEDURE dbo.sp_GenerateUltimateDummyData
AS
BEGIN
    SET NOCOUNT ON;
    PRINT 'Initiating Ultimate Bulk Data Generation (This will generate 100k+ records)...';

    -- Base config
    INSERT INTO dbo.Currencies (CurrencyCode, CurrencyName, Symbol) VALUES ('USD', 'US Dollar', '$'), ('EUR', 'Euro', '€'), ('GBP', 'British Pound', '£');
    INSERT INTO dbo.Languages (LanguageCode, LanguageName) VALUES ('en-US', 'English'), ('es-ES', 'Spanish'), ('fr-FR', 'French');
    INSERT INTO dbo.Roles (RoleName) VALUES ('Admin'), ('Customer'), ('Support'), ('Employee'), ('Affiliate');

    -- Departments & Assembly Lines
    INSERT INTO dbo.Departments (DepartmentName) VALUES ('HR'), ('Engineering'), ('Sales'), ('Marketing'), ('Logistics'), ('Manufacturing');
    INSERT INTO dbo.AssemblyLines (LineName) VALUES ('Alpha Line'), ('Beta Line'), ('Gamma Line');
    INSERT INTO dbo.ShippingCarriers (CarrierName) VALUES ('FedEx'), ('UPS'), ('USPS'), ('DHL');
    INSERT INTO dbo.ShippingMethods (CarrierID, MethodName, BaseCost, EstimatedDays) 
    SELECT CarrierID, 'Standard Ground', 5.99, 5 FROM dbo.ShippingCarriers UNION ALL
    SELECT CarrierID, '2-Day Express', 15.99, 2 FROM dbo.ShippingCarriers UNION ALL
    SELECT CarrierID, 'Overnight Priority', 29.99, 1 FROM dbo.ShippingCarriers;

    -- Warehouses
    DECLARE @w INT = 1; WHILE @w <= 10 BEGIN INSERT INTO dbo.Warehouses (WarehouseCode, LocationName, City, Country) VALUES ('WH' + CAST(@w AS VARCHAR), 'Mega Warehouse ' + CAST(@w AS VARCHAR), 'Metropolis', 'USA'); SET @w = @w + 1; END

    -- Vehicles
    DECLARE @v INT = 1; WHILE @v <= 100 BEGIN INSERT INTO dbo.VehicleFleet (LicensePlate, VehicleType) VALUES ('TRK-' + LEFT(CAST(NEWID() AS VARCHAR(36)), 8), CASE WHEN @v%2=0 THEN 'Truck' ELSE 'Van' END); SET @v = @v + 1; END

    -- Brands & Categories
    DECLARE @b INT = 1; WHILE @b <= 200 BEGIN INSERT INTO dbo.Brands (BrandName) VALUES ('Global Brand ' + CAST(NEWID() AS VARCHAR(36))); INSERT INTO dbo.Categories (CategoryName) VALUES ('Global Category ' + CAST(NEWID() AS VARCHAR(36))); SET @b = @b + 1; END

    -- Products (20,000)
    PRINT 'Generating 20,000 Products & Inventory...';
    DECLARE @p INT = 1; WHILE @p <= 20000 BEGIN 
        INSERT INTO dbo.Products (CategoryID, BrandID, ProductName, SKU, BasePrice) VALUES ((CAST(RAND() * 199 AS INT) + 1), (CAST(RAND() * 199 AS INT) + 1), 'Ultimate Item ' + CAST(@p AS VARCHAR), 'SKU-' + CAST(NEWID() AS VARCHAR(36)), ROUND(RAND() * 1000, 2) + 10);
        SET @p = @p + 1; 
    END

    -- Inventory Distribution (100,000 records)
    PRINT 'Distributing Inventory across Warehouses (100,000 records)...';
    INSERT INTO dbo.WarehouseInventory (WarehouseID, ProductID, StockQuantity) SELECT (ProductID % 10) + 1, ProductID, CAST(RAND(ProductID) * 5000 AS INT) + 100 FROM dbo.Products;
    INSERT INTO dbo.WarehouseInventory (WarehouseID, ProductID, StockQuantity) SELECT ((ProductID+1) % 10) + 1, ProductID, CAST(RAND(ProductID) * 2000 AS INT) + 50 FROM dbo.Products;

    -- Raw Materials & BOM
    DECLARE @rm INT = 1; WHILE @rm <= 500 BEGIN INSERT INTO dbo.RawMaterials (MaterialName, UnitOfMeasure, CostPerUnit) VALUES ('Material ' + CAST(@rm AS VARCHAR), 'kg', RAND() * 10); SET @rm = @rm + 1; END
    INSERT INTO dbo.BillOfMaterials (ProductID, MaterialID, QuantityRequired) SELECT TOP 10000 ProductID, (ProductID % 500) + 1, RAND() * 5 FROM dbo.Products;

    -- Users (20,000)
    PRINT 'Generating 20,000 Users...';
    DECLARE @u INT = 1; WHILE @u <= 20000 BEGIN INSERT INTO dbo.Users (FirstName, LastName, Email, PreferredLanguageID, PreferredCurrencyID) VALUES ('MegaUserFn_' + CAST(@u AS VARCHAR), 'MegaUserLn_' + CAST(@u AS VARCHAR), 'user' + CAST(@u AS VARCHAR) + '@mega.com', (CAST(RAND() * 2 AS INT) + 1), (CAST(RAND() * 2 AS INT) + 1)); SET @u = @u + 1; END

    -- Employees (1,000)
    INSERT INTO dbo.Employees (UserID, DepartmentID, JobTitle, HireDate, BaseSalary) SELECT TOP 1000 UserID, (UserID % 6) + 1, 'Enterprise Staff', DATEADD(DAY, -CAST(RAND()*1000 AS INT), GETDATE()), 50000 + (RAND()*50000) FROM dbo.Users ORDER BY NEWID();

    -- Affiliates (500)
    INSERT INTO dbo.Affiliates (UserID, AffiliateCode) SELECT TOP 500 UserID, 'AFF-' + CAST(UserID AS VARCHAR) FROM dbo.Users WHERE UserID NOT IN (SELECT UserID FROM dbo.Employees);

    -- Quality Inspections (10,000)
    INSERT INTO dbo.QualityInspections (ProductID, AssemblyLineID, InspectorEmployeeID, Passed, DefectReason) SELECT TOP 10000 ProductID, (ProductID % 3) + 1, (SELECT TOP 1 EmployeeID FROM dbo.Employees), CASE WHEN (ProductID % 15) = 0 THEN 0 ELSE 1 END, CASE WHEN (ProductID % 15) = 0 THEN 'Failed Tolerance Test' ELSE NULL END FROM dbo.Products;

    -- Orders (50,000)
    PRINT 'Generating 50,000 Orders (This may take a moment)...';
    DECLARE @o INT = 1, @OrderID UNIQUEIDENTIFIER, @UserID INT;
    WHILE @o <= 50000 BEGIN
        SET @OrderID = NEWID(); SET @UserID = CAST(RAND() * 19999 AS INT) + 1;
        INSERT INTO dbo.Orders (OrderID, UserID, OrderDate, Status) VALUES (@OrderID, @UserID, DATEADD(DAY, -CAST(RAND() * 1000 AS INT), GETDATE()), CASE WHEN @o % 10 = 0 THEN 'Pending' ELSE 'Completed' END);
        -- Order details (1-3 items)
        DECLARE @i INT = 1, @items INT = CAST(RAND() * 3 AS INT) + 1;
        WHILE @i <= @items BEGIN
            DECLARE @ProdID INT = CAST(RAND() * 19999 AS INT) + 1;
            INSERT INTO dbo.OrderDetails (OrderID, ProductID, Quantity, UnitPrice) VALUES (@OrderID, @ProdID, CAST(RAND() * 4 AS INT) + 1, (SELECT BasePrice FROM dbo.Products WHERE ProductID = @ProdID));
            SET @i = @i + 1;
        END
        UPDATE dbo.Orders SET TotalAmount = (SELECT ISNULL(SUM(LineTotal),0) FROM dbo.OrderDetails WHERE OrderID = @OrderID) WHERE OrderID = @OrderID;
        SET @o = @o + 1;
    END

    -- Page Views (100,000 analytics hits)
    PRINT 'Generating 100,000 PageViews (Analytics)...';
    DECLARE @pv INT = 1; WHILE @pv <= 100000 BEGIN INSERT INTO dbo.PageViews (SessionID, PageURL, UserAgent) VALUES ('SESSION_' + CAST(CAST(RAND()*1000 AS INT) AS VARCHAR), '/products/item-' + CAST(CAST(RAND()*20000 AS INT) AS VARCHAR), CASE WHEN @pv%3=0 THEN 'Mozilla/5.0 (iPhone)' ELSE 'Mozilla/5.0 (Windows NT 10.0)' END); SET @pv = @pv + 1; END

    PRINT 'Ultimate Data Generation Complete! DB is now heavily populated.';
END
GO

-- =======================================================================================
-- Execute the Ultimate Generation SP
-- =======================================================================================
    -- Execution of the core dummy data
    EXEC dbo.sp_GenerateUltimateDummyData;
    
    -- =======================================================================================
    -- OPTIONAL STRESS TESTS (Comment out if you do not want to run them)
    -- =======================================================================================
    
    -- Generates exactly 5,000,000 rows in a single table for performance testing
    EXEC dbo.sp_GenerateMassiveDataVolume;
GO
