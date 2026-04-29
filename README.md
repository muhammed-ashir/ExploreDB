# 🗄️ DbExplore

**DbExplore** is a powerful Windows Desktop application for SQL Server database exploration and intelligent query generation. It automatically discovers table relationships and generates complex JOIN queries using a smart "Pathfinder" algorithm.

![.NET MAUI](https://img.shields.io/badge/.NET%20MAUI-8.0-512BD4?logo=dotnet)
![C#](https://img.shields.io/badge/C%23-12.0-239120?logo=csharp)
![SQL Server](https://img.shields.io/badge/SQL%20Server-CC2927?logo=microsoftsqlserver)
![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows)

---

## ✨ Features

### ⚡ **Pathfinder - Auto-Join Query Generator**
- Select columns from any tables
- Automatically discovers the shortest JOIN path between tables
- **Intelligent JOIN type selection**:
  - Uses `LEFT JOIN` vs `INNER JOIN` based on FK nullable constraints
  - Considers parent-child relationship direction
  - Mixed JOIN strategies for optimal results
- Generates clean, alias-based SQL with proper formatting

### 🔍 **Table Explorer**
- Browse all database tables in a searchable grid
- View table schemas, column counts, and relationships
- Click any table to see detailed information

### 📊 **Detailed Table Views**
- See all columns with data types
- Visualize parent (upstream) and child (downstream) relationships
- Understand foreign key dependencies at a glance

### 🎨 **Modern UI**
- Dark-themed glassmorphic design
- Smooth animations and transitions
- Clean, emoji-based iconography (no external icon dependencies!)

---

## � Installation

### **Option 1: MSIX Installer (Recommended)**

1. **Build the installer**:
   ```bash
   build_installer.bat
   ```

2. **Trust the certificate** (first-time only):
   - Run `trust_certificate.bat` as Administrator

3. **Double-click** the `.msix` file from `bin\Release\...\*.msix`

4. Click **Install**

5. Done! Launch from Start Menu

### **Option 2: Portable Executable**
1. Close the App if running
2. **Build** the application:
   ```bash
   dotnet publish -f net8.0-windows10.0.19041.0 -c Release
   ```
   For Release creation use the update_release.bat script.
   ```bash
   ./update_release.bat
   ```
3. **Find** the portable zip at:
   ```
   App\DbExplore-portable.zip
   ```
4. **Extract** the zip and run `DbExplore.exe` directly, or share the zip file.

---

## � Usage

### **First Time Setup**
1. Launch DbExplore
2. Enter your SQL Server connection string:
   ```
   Server=YOUR_SERVER;Database=YOUR_DB;Integrated Security=true;TrustServerCertificate=True
   ```
   Or with username/password:
   ```
   Server=YOUR_SERVER;Database=YOUR_DB;User Id=YOUR_USER;Password=YOUR_PASS;TrustServerCertificate=True
   ```
3. Click **Connect**

### **Using Pathfinder**
1. Navigate to **⚡ Pathfinder** from the sidebar
2. Expand tables and **check the columns** you want in your query
3. Watch the **SQL auto-generate** on the right!
4. Click **🗑️ Clear All** to reset
5. Copy the generated SQL to use in your queries

### **Exploring Tables**
1. Navigate to **🔍 Table Explorer**
2. Use the search box to find tables
3. Click any table card to view details, columns, and relationships

---

## 🛠️ Building from Source

### **Prerequisites**
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10/11 (version 1809 or higher)
- Visual Studio 2022 (optional, for IDE development)

### **Build Steps**

1. **Clone the repository**:
   ```bash
   git clone https://github.com/YOUR_USERNAME/DbExplore.git
   cd DbExplore
   ```

2. **Restore dependencies**:
   ```bash
   dotnet restore
   ```

3. **Build the application**:
   ```bash
   dotnet build -c Release
   ```

4. **Run the application**:
   ```bash
   dotnet run
   ```

### **Creating an MSIX Installer**

Run the automated build script:
```bash
build_installer.bat
```

This will:
- Create a self-signed development certificate
- Build the MSIX package
- Output the installer to `bin\Release\...\*.msix`

---

## 📤 Distribution & Sharing

Once you've built the application, you have several options to share it with others:

### **Option A: GitHub Releases** (Recommended for Public Distribution)

1. **Create a GitHub repository** and push your code:
   ```bash
   git remote add origin https://github.com/YOUR_USERNAME/DbExplore.git
   git branch -M main
   git push -u origin main
   ```

2. **Create a Release**:
   - Go to your repo → Releases → "Create a new release"
   - Tag version (e.g., `v1.0.0`)
   - Upload the `.msix` file from `bin\Release\...\*.msix`
   - Upload `trust_certificate.bat` and `DbExplore_TemporaryKey.pfx`
   - Write release notes

3. **Users can download**:
   - The `.msix` installer for easy installation
   - Or the `publish` folder zip for portable use

**Pros**: ✅ Professional, ✅ Version control, ✅ Easy updates, ✅ Public/private options

---

### **Option B: Direct File Sharing** (For Colleagues/Internal Use)

**Create a distribution package**:

1. **For MSIX Installer** - Zip these files:
   ```
   📦 DbExplore-Installer.zip
   ├── DbExplore.msix
   ├── trust_certificate.bat
   ├── DbExplore_TemporaryKey.pfx
   └── INSTALL.txt (installation instructions)
   ```

2. **Share via**:
   - Email
   - Cloud storage (Google Drive, OneDrive, Dropbox)
   - Internal network share

3. **Users install by**:
   - Extracting the zip
   - Running `trust_certificate.bat` as Administrator (first time only)
   - Double-clicking the `.msix` file
   - Clicking "Install"

**Pros**: ✅ Quick sharing, ✅ Works offline

---

### **Option C: Portable Executable** (No Installation Required)

**Best for**: Quick testing, users without admin rights

1. **Build the portable version**:
   ```bash
   dotnet publish -f net8.0-windows10.0.19041.0 -c Release
   ```

2. **Zip the entire publish folder**:
   ```
   bin\Release\net8.0-windows10.0.19041.0\win10-x64\publish\
   ```
   Name it: `DbExplore-Portable-v1.0.0.zip`

3. **Share the zip file**

4. **Users run by**:
   - Extracting the zip
   - Double-clicking `DbExplore.exe`
   - No installation needed!

**Pros**: ✅ No installation, ✅ No admin rights needed  
**Cons**: ⚠️ Large file size (~150-200 MB)

---

### **Option D: Enterprise Distribution** (For Production Environments)

For enterprise environments, consider:

1. **Code Signing Certificate**:
   - Purchase a code signing certificate from a trusted CA (DigiCert, Sectigo, etc.)
   - Sign the MSIX with the real certificate
   - No trust warnings for users

2. **Internal App Store**:
   - Deploy via Microsoft Intune
   - Company Portal distribution

3. **Group Policy Deployment**:
   - Push via GPO in Active Directory environments

---

## 📝 Installation Instructions for End Users

Include these instructions when sharing:

### **MSIX Installer Method:**

1. **First Time Setup** (Administrator required):
   - Right-click `trust_certificate.bat`
   - Select "Run as Administrator"
   - Click "Yes" when prompted
   - Certificate is now trusted

2. **Install the App**:
   - Double-click the `.msix` file
   - Click "Install"
   - Wait for installation to complete

3. **Launch**:
   - Find "DbExplore" in Start Menu
   - Or search for "DbExplore"

### **Portable Method:**

1. Extract the zip file to any folder
2. Double-click `DbExplore.exe`
3. No installation required!

---

## � Technical Stack

- **Framework**: .NET MAUI Blazor Hybrid
- **Language**: C# 12.0
- **UI**: HTML5 + Bootstrap 5 + Custom CSS
- **Database**: SQL Server (via `Microsoft.Data.SqlClient`)
- **Data Access**: Dapper
- **Target**: Windows 10/11 Desktop

### **Architecture**
- **Services**: Dependency-injected services for Connection, Schema, and Pathfinder logic
- **Pathfinder Algorithm**: BFS-based graph traversal to find shortest JOIN paths
- **Smart JOIN Selection**: Analyzes FK constraints and relationship direction

---

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

---

## 📄 License

This project is licensed under the MIT License.

---

## 🙏 Acknowledgments

- Built with ❤️ using .NET MAUI
- Icons: Emoji (no external dependencies!)
- UI Framework: Bootstrap 5

---

**Made with ⚡ Pathfinder Technology**
