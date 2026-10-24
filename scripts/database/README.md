# ExploreDB Database Scripts

This folder contains database setup and test scripts for ExploreDB, organized by use case:

---

## 1. Lightweight Feature-Centric Database (Recommended for Fast Development & UI Testing)
- **Script**: [`ExploreDB_Features_Lite.sql`](ExploreDB_Features_Lite.sql)
- **Execution Time**: **~0.9 seconds**
- **Target Database**: `ExploreDB_Lite` *(Change to `ExploreDB` at the top of the script if preferred)*
- **Disk Footprint**: ~8 MB
- **Coverage**:
  - **All SQL Server Data Types**: Exact & approximate numerics, dates, times, Unicode, emojis, JSON, XML, spatial, and Guids (`dbo.AllDataTypesTest`).
  - **Enhanced ResultSetGrid Binary Features**:
    - `ROWVERSION` / `TIMESTAMP` and Short Binary ($\le 64$ B) displayed directly as `0x...` uppercase hex strings with natural hex sorting and search.
    - Large Binary & Images ($> 64$ B) displayed as interactive badges (**PNG 45.2 KB**, **JPEG 2.1 KB**, **PDF 1.20 MB**, **General Binary 128.0 KB**), opening the Binary / Image Viewer Modal.
    - Exact boundary testing for 64-byte vs 65-byte thresholds.
  - **Multi-Schema Hierarchy**: `dbo`, `sales`, `hr`, `inventory`, `reporting`, `audit`, `security`.
  - **Object Explorer Coverage**: Tables, Views, Indexed Views, Stored Procedures (including multi-result sets and `OUTPUT` parameters), Table-Valued Functions (ITVF & MSTVF), Scalar Functions, Triggers, Sequences, Synonyms, and User-Defined Types (UDT).
  - **Paging & Filtering**: 500 realistic relational orders for testing pagination, filtering, and sorting in the grid.

---

## 2. Enterprise High-Volume Benchmark Database (Stress Testing & Massive Data)
- **Scripts**:
  1. [`01_Schema_ExploreDB.sql`](01_Schema_ExploreDB.sql) - Full enterprise schema, partitioning, indexing, procedures, and types.
  2. [`02_Data_ExploreDB.sql`](02_Data_ExploreDB.sql) - High-volume generator featuring:
     - **5,000,000 (50 Lakh) rows** in `dbo.Orders` generated via set-based tally table CTE in ~40 seconds.
     - **100,000 line items** in `dbo.OrderDetails`.
     - **20,000 users** and **20,000 products**.
     - Optional **5,000,000 rows** in `dbo.MassiveDataTest`.
- **Target Database**: `ExploreDB`
- **Disk Footprint**: ~450 MB data (within SQL Server Express 10 GB limit).

---

## 3. SQL Compare Test Pair (Realistic Schema Difference Testing)
- **Scripts & Packages**:
  1. [`Compare_Source_DB.sql`](Compare_Source_DB.sql) & [`CompareDB_Source.dacpac`](CompareDB_Source.dacpac) - Target DB: `CompareDB_Source` (Development / Reference schema with new features).
  2. [`Compare_Target_DB.sql`](Compare_Target_DB.sql) & [`CompareDB_Target.dacpac`](CompareDB_Target.dacpac) - Target DB: `CompareDB_Target` (Production / Destination schema with older structures and deprecated objects).
- **Execution Time**: **~1 to 2 seconds each**
- **Test Matrix Covered**:
  - **Added Objects** (Source only): `sales.Discounts`, `audit.SecurityLog`, `dbo.Seq_InvoiceNumber`, `reporting.vw_MonthlyRevenueBreakdown`, `sales.fn_CalculateDiscountedPrice`, `sales.sp_ApplyBulkDiscount`, `sales.trg_UpdateStockOnOrder`.
  - **Modified Objects**:
    - `dbo.Customers` (Email type length, LoyaltyTier & IsVerified added).
    - `sales.Orders` (ShippingMethod & TrackingNumber added, new index, check constraint).
    - `inventory.Products` (Decimal precision change, DiscontinuedDate column added).
    - `sales.vw_CustomerOrders`, `sales.sp_CreateOrder`, `sales.fn_GetCustomerTotalSpend` (Definition updates).
  - **Deleted / Deprecated Objects** (Target only): `dbo.LegacyImportQueue`, `sales.OldOrderArchive`, `reporting.vw_DeprecatedSalesStats`, `dbo.sp_LegacySync`.
  - **Identical Objects** (100% Match): `dbo.Categories`, `inventory.Warehouses`, `sales.PaymentMethods`, `dbo.vw_ActiveCategories`, `dbo.sp_GetCategories`, `dbo.fn_CalculateMargin`.
