# ExploreDB 🚀

ExploreDB is a powerful, modern database exploration and querying tool built with .NET 8 MAUI Blazor Hybrid. It connects directly to SQL Server databases and provides an intuitive, glassmorphic UI to navigate, analyze, and query your data.

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

ExploreDB is distributed via a web-based **App Installer** which automatically checks for and installs new updates silently in the background every time you open the app!

Because we use a custom development certificate, installing it on a brand new computer requires a quick one-time trust setup.

### **Installation Steps for End Users**

**Prerequisites**: Because this app is heavily optimized for a small file size (under 20 MB), it requires the **[.NET 8.0 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)** to be installed on the computer first.

1. **Trust the Certificate (First Time Only)**:
   - Download the **`ExploreDB.pfx`** and **`trust_certificate.bat`** files from our [GitHub Releases page](https://github.com/zerinapps/ExploreDB-Releases/releases).
   - Right-click on **`trust_certificate.bat`** and select **"Run as Administrator"**.
   - Press **Enter** on your keyboard, then click **Yes** on the prompt to allow the installation.
   - *Note: This tells Windows to trust our custom digital signature so the app can be installed safely.*

   **If the script fails, you can install the certificate manually:**
   - Double-click the **`ExploreDB.pfx`** file.
   - On the very first screen, select **Local Machine** (instead of Current User) and click Next.
   - Click **"Yes"** on the Windows User Account Control (UAC) prompt that asks for permission.
   - Click Next on the file path screen.
   - Type `password` for the password and click Next.
   - Choose **"Place all certificates in the following store"** and click **Browse**.
   - Select **Trusted Root Certification Authorities** and click OK, then Next, then Finish.

2. **Install the Application**:
   - Download the tiny **`ExploreDB.appinstaller`** file from our [GitHub Pages site](https://zerinapps.github.io/ExploreDB-Releases/ExploreDB.appinstaller).
   - Double-click the downloaded file.
   - A Windows App Installer window will appear. Click **Install**.
   - The app will securely download the heavy binaries from our GitHub Releases page and launch!

3. **Launch & Auto-Updates**:
   - For future uses, simply search for "ExploreDB" in your Windows Start Menu.
   - The app will automatically keep itself up-to-date!

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
   git clone https://github.com/YOUR_USERNAME/ExploreDB.git
   cd ExploreDB
   ```

2. **Publish a Release (Automated)**:
   If you are ready to publish a new version to GitHub, double-click **`scripts\publish_new_release.bat`**.
   - It will prompt you for the new version number.
   - It will automatically inject the new version into all XML files securely.
   - It will compile and digitally sign the MSIX package automatically.
   
3. **Run the Automated Build Script (Recommended)**:
   Double-click the **`scripts\build_installer.bat`** script in the project root.
   
   This script will automatically:
   - Clean the project.
   - Build the MSIX package natively for `win-x64`.
   - Digitally sign the installer using the local Windows SDK `signtool.exe` and `ExploreDB.pfx`.
   - Print out the exact location of the freshly built and signed `.msix` file.

### **Manual Build & Sign (Fallback)**

If the automated scripts fail, you can perform the steps manually:

0. **Bump the Version Numbers (Manual)**:
   - `ExploreDB.csproj`: Update the `<Version>` and `<ApplicationDisplayVersion>` tags.
   - `Platforms\Windows\Package.appxmanifest`: Update the `Version="..."` attribute in the `<Identity>` tag.
   - `ExploreDB.appinstaller`: Update both `Version="..."` attributes AND the URL at the bottom to point to your new GitHub tag (e.g., `.../download/v1.0.0.1/...`).

1. **Build and Publish (Terminal)**:
   ```bash
   dotnet publish ExploreDB.csproj -f net8.0-windows10.0.19041.0 -c Release -r win-x64 -p:Platform=x64
   ```

2. **Sign the Package**:
   Locate your Windows SDK `signtool.exe` (usually in `C:\Users\<User>\.nuget\packages\microsoft.windows.sdk.buildtools\...\signtool.exe` or `C:\Program Files (x86)\Windows Kits\10\bin\...\signtool.exe`) and run:
   ```cmd
   signtool sign /fd SHA256 /a /f "cert\ExploreDB.pfx" /p "password" "bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\AppPackages\ExploreDB_1.0.0.0_x64_Test\ExploreDB_1.0.0.0_x64.msix"
   ```

### **Publishing a Release (GitHub Releases & Pages)**

To publish an update to the team, you must upload the freshly built files from your `App` folder to the `ExploreDB-Releases` repository.

1. **Publish the Heavy Binaries (GitHub Releases)**:
   - Go to the `ExploreDB-Releases` repository on GitHub.
   - Click **Releases** -> **Draft a new release**.
   - Create a tag that exactly matches your new version (e.g., `v1.0.0.1`).
   - Drag and drop your compiled **`ExploreDB_X.X.X.X_x64.msix`** file into the "Attach binaries" box.
   - Publish the release.

2. **Trigger the Auto-Update (GitHub Pages)**:
   - Go to the **Code** tab of the `ExploreDB-Releases` repository.
   - Click **Add file** -> **Upload files**.
   - Drag and drop the updated 1 KB **`ExploreDB.appinstaller`** file to overwrite the old one in the root directory.
   - Commit the changes. Once GitHub Actions finishes deploying the Pages site, teammates' computers will automatically detect the update on their next launch!

### **Disaster Recovery: Rebuilding the Release Infrastructure**

If the `ExploreDB-Releases` repository ever gets accidentally deleted, the auto-update links will break. Here is exactly how to rebuild the infrastructure from scratch:

1. **Recreate the Repository**:
   - Go to GitHub and create a new **Public** repository named `ExploreDB-Releases`. (It *must* be public so the Windows installer can access the files without logging in).
   
2. **Re-enable GitHub Pages**:
   - In the new repo, go to **Settings** -> **Pages**.
   - Under "Build and deployment", set the Source to **Deploy from a branch**.
   - Select the `main` branch and click Save.

3. **Re-upload the Files**:
   - Go back to the **Code** tab and upload the `ExploreDB.appinstaller` file.
   - Go to the **Releases** tab, draft a new release (e.g., `v1.0.0.0`), and upload your `.msix`, `.pfx`, and `.bat` files.
   
*(Note: As long as you use the exact same repository name, the URL inside the `.appinstaller` will automatically match and everything will start working again instantly!)*

**What if I have to use a different repository name?**
If you rebuild the infrastructure using a *different* repository name (e.g., `ExploreDB-Updates`), the hardcoded URLs will break. To fix it:
1. Open your local `ExploreDB.appinstaller` file.
2. Update the two `Uri` links inside it to point to your new repository name.
3. Upload the modified `.appinstaller` to the new repo.
4. **Crucial Step**: Because the old link is broken, automatic updates will fail. You must tell all your teammates to manually download and run the new `.appinstaller` file one time. This will permanently repoint their computers to your new repository!

---

## 🤝 Contributing

Contributions are welcome! Please feel freSe to submit a Pull Request.

---

## 📄 License

This project is licensed under the MIT License.
