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
  - **Circular Foreign Key Loops**: Built-in 2-table cycle (`dbo.Circular_Department` <-> `dbo.Circular_Employee`) and 3-table cyclic loop (`dbo.Exam_Body` -> `dbo.Exam_Product` -> `dbo.Exam_Session` -> `dbo.Exam_Body`) for testing the **Data Cleanup Tool** cycle detection and automatic constraint-disabling cleanup script generation.

---

## 2. Circular Foreign Key Test Tables (Data Cleanup Tool Validation)
- **Script**: [`Test_Circular_Foreign_Keys.sql`](Test_Circular_Foreign_Keys.sql)
- **Execution Time**: **~0.1 seconds**
- **Target Database**: Current database context (auto-selects `ExploreDB_Lite` or `ExploreDB`)
- **Purpose**:
  - Creates 2-table and 3-table circular foreign key dependency loops populated with sample data.
  - Allows reproduction and verification of ExploreDB's **Data Cleanup Tool** yellow warning callout (*"Circular Foreign Key Loop Detected: One or more tables reference each other circularly..."*).
  - Verifies the automatic Kahn topological sorting and the generated cleanup script containing `ALTER TABLE ... NOCHECK CONSTRAINT ALL;` before deletion and `WITH CHECK CHECK CONSTRAINT ALL;` after deletion.

---

## 3. Enterprise High-Volume Benchmark Database (Stress Testing & Massive Data)
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

## 4. SQL Compare Test Pair (Realistic Schema Difference Testing)

### A. High-Volume Enterprise Test Pair (Recommended for Deep Testing & Report Validation)
- **Scripts & Packages**:
  1. [`Compare_Source_DB.sql`](Compare_Source_DB.sql) & [`CompareDB_Source.dacpac`](CompareDB_Source.dacpac) - Target DB: `CompareDB_Source` (Development / Reference schema with new enterprise modules).
  2. [`Compare_Target_DB.sql`](Compare_Target_DB.sql) & [`CompareDB_Target.dacpac`](CompareDB_Target.dacpac) - Target DB: `CompareDB_Target` (Production / Destination schema with older structures and legacy tables).
- **Execution Time**: **~2 to 3 seconds each**
- **Scale**: **125+ schema differences** across 8 domains (`dbo`, `sales`, `inventory`, `billing`, `crm`, `hr`, `reporting`, `audit`).
- **Test Matrix Covered**:
  - **Added Objects (57)**: New tables (`sales.Discounts`, `billing.PaymentTransactions`, `crm.Campaigns`, `hr.Leaves`, `audit.SecurityLog`, etc.), Views (`reporting.vw_DailyExecutiveKPI`, `sales.vw_DailySalesSummary`, `billing.vw_MonthlyRecurringRevenue`, etc.), Stored Procedures, Functions, Sequences, and Triggers.
  - **Modified Objects (44)**: Deep column additions, data type expansions (`NVARCHAR(100)` -> `NVARCHAR(256)`), precision modifications (`DECIMAL(10,2)` -> `DECIMAL(18,4)`), computed columns, updated views, and enhanced procedure & function logic.
  - **Deleted / Deprecated Objects (24)**: Legacy tables (`dbo.LegacyImportQueue`, `sales.OldOrderArchive`, `billing.LegacyPaymentLog`, etc.), obsolete views, and retired stored procedures.
  - **Identical Objects (36+)**: 100% identical lookup tables, catalog views, and utility procedures.

### B. Lightweight Test Pair (Fast Validation & Comparison Options Testing)
- **Scripts & Packages**:
  1. [`Compare_Source_DB_Lite.sql`](Compare_Source_DB_Lite.sql) & [`CompareDB_Source_Lite.dacpac`](CompareDB_Source_Lite.dacpac) - Target DB: `CompareDB_Source_Lite`
  2. [`Compare_Target_DB_Lite.sql`](Compare_Target_DB_Lite.sql) & [`CompareDB_Target_Lite.dacpac`](CompareDB_Target_Lite.dacpac) - Target DB: `CompareDB_Target_Lite`
- **Execution Time**: **~0.5 seconds**
- **Scale**: ~25 schema differences for quick spot checks and rapid UI feedback.
- **Dedicated Comparison Options Test Matrix**:
  The Lite suite includes specific paired objects designed to test each checkbox in the **Comparison Options** modal:
  | Option Checkbox | Test Object | Source Definition | Target Definition | Unchecked Result | Checked Result |
  |---|---|---|---|---|---|
  | **Ignore Identity (Seed & Increment)** | `dbo.IdentityTest` | `IDENTITY(1, 1)` | `IDENTITY(1000, 10)` | **Modified** | **Identical** (diff ignored) |
  | **Ignore Collation Differences** | `dbo.CollationTest` | `Latin1_General_CI_AS` | `SQL_Latin1_General_CP1_CI_AS` | **Modified** | **Identical** (diff ignored) |
  | **Ignore Comments** | `dbo.sp_CommentsTest` | Block & inline doc comments | Legacy short comment | **Modified** | **Identical** (diff ignored) |
  | **Ignore Whitespace** | `dbo.vw_WhitespaceTest` | Multi-line indented query | Single-line compact query | **Modified** | **Identical** (diff ignored) |
  | **Ignore Permissions & Roles** | `AppReadOnlyRole` & `sales.PaymentMethods` | Role created & `GRANT SELECT` assigned | Omitted | **Added** (Role & Grant) | **Ignored** |
  | **Ignore System Constraint Names** | `dbo.SystemConstraintTest` | Unnamed `DEFAULT (1)` | Unnamed `DEFAULT (1)` | System-generated random names normalized | Constraint names ignored |
