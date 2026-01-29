# DbExplore

**DbExplore** is a modern, standalone Windows Desktop utility designed for developers and data analysts. It allows you to explore SQL Server schemas and, crucially, **automatically discover SQL JOIN paths** between distant tables to generate complex queries instantly.

Built with **.NET MAUI Blazor Hybrid**, it combines the raw performance of native Windows apps with the flexibility and beauty of modern Web UI (HTML5/CSS3).

## 🚀 Key Features

### 1. Dynamic Connection Manager
- **Connect Anything**: Connect to any SQL Server instance using a standard connection string.
- **Persistence**: Remembers your last successful connection for quick access.
- **Safe**: Connection strings are stored locally in your user profile.

### 2. Schema Explorer
- **Visual Navigation**: Browse your database tables in a sleek, dark-themed sidebar.
- **Relationship Graph**: View any table to see its **Parents** (upstream foreign keys) and **Children** (downstream dependencies).
- **Interactive**: Click on any related table to instantly navigate the schema graph.

### 3. "Pathfinder" Auto-Join Engine 🧠
- **The Problem**: You need data from `Users` and `Products`, but they are 4 tables apart (`Users` -> `Orders` -> `OrderDetails` -> `Products`).
- **The Solution**: Select the columns you want, and DbExplore uses **Graph Algorithms (BFS)** to find the shortest path of Foreign Keys connecting them.
- **Auto-SQL**: Instantly generates the correct `JOIN` query with table aliases (`T1`, `T2`...).

---

## 🛠 Technical Stack

- **Architecture**: .NET MAUI Blazor Hybrid (Windows .EXE)
- **UI Framework**: HTML5 + Bootstrap 5 (Custom Dark Theme)
- **Backend Logic**: C# .NET 8
- **Data Access**: Dapper (High-performance micro-ORM)
- **Graph Logic**: Adjacency Lists + Breadth-First Search (BFS) algorithm

---

## 💻 How to Run (Developer)

### Prerequisites
- Visual Studio 2022 (with .NET MAUI workload) **OR**
- .NET 8 SDK

### Run Locally
Open the project folder in your terminal:
```powershell
dotnet run
```
Or open `DbExplore.csproj` in Visual Studio and press **F5**.

---

## 📦 How to Build (.EXE)

To generate a standalone executable that connects to the database without needing Visual Studio:

1. Open your terminal in the project folder.
2. Run the publish command:
   ```powershell
   dotnet publish -f net8.0-windows10.0.19041.0 -c Release
   ```
3. Locate your EXE file here:
   `bin\Release\net8.0-windows10.0.19041.0\win10-x64\publish\DbExplore.exe`

   *Note: You can copy this entire `publish` folder to any Windows 10/11 PC to run it.*

---

## 📖 Usage Guide

1. **Connect**:
   - Launch the app.
   - Enter your SQL Server connection string (e.g., `Server=.;Database=MyDb;Trusted_Connection=True;TrustServerCertificate=True`).
   - Click **Connect**.

2. **Explore**:
   - Use the **Sidebar** to search and select tables.
   - View columns, types, and relationships in the main view.

3. **Auto-Join (Pathfinder)**:
   - Click **Pathfinder** in the top-left.
   - Search for columns in the left panel (e.g., "Email", "OrderDate").
   - Check the boxes for the columns you need.
   - Watch as the **Generated SQL** panel instantly updates with the full `SELECT` statement including all necessary `JOIN`s!

---

## 📂 Project Structure

- **`MauiProgram.cs`**: App entry point & Dependency Injection.
- **`Services/SchemaService.cs`**: Scans the database and builds the node graph.
- **`Services/PathfinderService.cs`**: The logic engine that calculates shortest paths.
- **`Components/Pages/Home.razor`**: The main Pathfinder UI.
- **`Components/Pages/TableDetail.razor`**: The Schema Explorer UI.
