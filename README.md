# DbExplore 🚀

DbExplore is a powerful, modern database exploration and querying tool built with .NET 8 MAUI Blazor Hybrid. It connects directly to SQL Server databases and provides an intuitive, glassmorphic UI to navigate, analyze, and query your data.

---

## ✨ Features

### ⚡ **Pathfinder (Auto-Join Query Generator)**
- Select columns from any tables in your database.
- Automatically discovers the shortest JOIN path between tables.
- **Intelligent JOIN type selection**:
  - Uses `LEFT JOIN` vs `INNER JOIN` based on FK nullable constraints.
  - Considers parent-child relationship direction.
  - Mixed JOIN strategies for optimal results.
- Generates clean, alias-based SQL with proper formatting.

### 🏃 **Query Runner**
- Write and execute custom SQL queries instantly.
- Beautiful results grid with dynamic column generation.
- Handles multiple result sets seamlessly.

### 🔍 **Table, View, & SP Explorers**
- Browse all database tables, views, and stored procedures in a searchable grid.
- View table schemas, column data types, and row counts.
- Visualize parent (upstream) and child (downstream) relationships.
- Understand foreign key dependencies at a glance.

### 🎨 **Modern UI**
- Dark-themed glassmorphic design.
- Smooth animations and transitions.
- Clean, emoji-based iconography.

---

## 📦 Installation Guide

Since DbExplore is distributed as an **MSIX Package** signed with a custom development certificate, installing it on a new computer requires a quick one-time trust setup.

When sharing DbExplore with a colleague or deploying it to another PC, you must provide them with a zip folder containing **three files**:

1. **`DbExplore_1.0.0.0_x64.msix`** (The application installer)
2. **`DbExplore.pfx`** (The digital certificate)
3. **`trust_certificate.bat`** (A helper script to install the certificate)

### **Installation Steps for End Users**

**Prerequisites**: Because this app is heavily optimized for a small file size (under 20 MB), it requires the **[.NET 8.0 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)** to be installed on the computer first. If you don't have it, the app will not launch.

1. **Trust the Certificate (One-Time Setup)**:
   - Extract the zip folder on the new computer.
   - Right-click on **`trust_certificate.bat`** and select **"Run as Administrator"**.
   - A command prompt will appear and prompt you for a password.
   - Enter the password: `password` (and press Enter).
   - *Note: This tells Windows to trust our custom digital signature so the app can be installed safely.*

   **If the script fails, you can install the certificate manually:**
   - Double-click the **`DbExplore.pfx`** file.
   - On the very first screen, select **Local Machine** (instead of Current User) and click Next.
   - Click **"Yes"** on the Windows User Account Control (UAC) prompt that asks for permission.
   - Click Next on the file path screen.
   - Type `password` for the password and click Next.
   - Choose **"Place all certificates in the following store"** and click **Browse**.
   - Select **Trusted Root Certification Authorities** and click OK, then Next, then Finish.

2. **Install the Application**:
   - Double-click the **`DbExplore_1.0.0.0_x64.msix`** file.
   - A Windows App Installer window will appear.
   - Click the **"Install"** button.
   - The app will install and automatically launch!

3. **Launch**:
   - For future uses, simply search for "DbExplore" in the Windows Start Menu.

---

## 🛠️ Building from Source

### **Prerequisites**
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10/11 (version 1809 or higher)
- Visual Studio 2022 (optional, for IDE development)

### **Compiling the MSIX Installer**

To compile a fresh MSIX package with all dependencies (including Win2D native assets):

1. **Clone the repository**:
   ```bash
   git clone https://github.com/YOUR_USERNAME/DbExplore.git
   cd DbExplore
   ```

2. **Run the Automated Build Script (Recommended)**:
   Double-click the **`build_installer.bat`** script in the project root.
   
   This script will automatically:
   - Clean the project.
   - Build the MSIX package natively for `win-x64`.
   - Digitally sign the installer using the local Windows SDK `signtool.exe` and `DbExplore.pfx`.
   - Print out the exact location of the freshly built and signed `.msix` file.

### **Manual Build & Sign (Fallback)**

If the automated script fails, you can perform the steps manually in a terminal:

1. **Build and Publish**:
   ```bash
   dotnet publish DbExplore.csproj -f net8.0-windows10.0.19041.0 -c Release -r win-x64 -p:Platform=x64
   ```

2. **Sign the Package**:
   Locate your Windows SDK `signtool.exe` (usually in `C:\Users\<User>\.nuget\packages\microsoft.windows.sdk.buildtools\...\signtool.exe` or `C:\Program Files (x86)\Windows Kits\10\bin\...\signtool.exe`) and run:
   ```cmd
   signtool sign /fd SHA256 /a /f "DbExplore.pfx" /p "password" "bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\AppPackages\DbExplore_1.0.0.0_x64_Test\DbExplore_1.0.0.0_x64.msix"
   ```

---

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

---

## 📄 License

This project is licensed under the MIT License.
