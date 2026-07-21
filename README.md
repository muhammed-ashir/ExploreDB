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

### **Method 1: Microsoft Store (Recommended)**
ExploreDB is officially available on the Microsoft Store for seamless installation and completely silent background updates!
1. Open the **Microsoft Store** app on Windows.
2. Search for **ExploreDB** and click **Install**.
*(Updates will be handled automatically by Windows!)*

---

### **Method 2: Manual Installation (GitHub Beta / Internal)**
*(Use this method if you need to install internal test builds)*

**🔗 Links to share with your team:** 
*(Note: Your teammates only need to do Step 1 if this is their very first time installing it!)*
- **Step 1 (Certificates)**: Download both [ExploreDB_Certificate.pfx](https://zerinapps.github.io/ExploreDB-Releases/ExploreDB_Certificate.pfx) and [ExploreDB_Certificate_Installer.bat](https://zerinapps.github.io/ExploreDB-Releases/ExploreDB_Certificate_Installer.bat)
- **Step 2 (App Installer)**: [Download the App Installer](https://zerinapps.github.io/ExploreDB-Releases/ExploreDB.appinstaller)

ExploreDB is distributed via a web-based **App Installer** which automatically checks for and installs new updates silently in the background every time you open the app!

Because we use a custom development certificate, installing it on a brand new computer requires a quick one-time trust setup.

#### **Installation Steps for End Users**

**Prerequisites**: Because this app is heavily optimized for a small file size (under 20 MB), it requires the **[.NET 8.0 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)** to be installed on the computer first.

1. **Trust the Certificate (First Time Only)**:
   - Download the **`ExploreDB_Certificate.pfx`** and **`trust_certificate.bat`** files from the links above.
   - Right-click on **`trust_certificate.bat`** and select **"Run as Administrator"**.
   - Press **Enter** on your keyboard, then click **Yes** on the prompt to allow the installation.
   - *Note: This tells Windows to trust our custom digital signature so the app can be installed safely.*

   **If the script fails, you can install the certificate manually:**
   - Double-click the **`ExploreDB_Certificate.pfx`** file.
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

2. **Build and Package Scripts**

   > You **do not** need to run both of the scripts below. They serve different purposes depending on whether you are releasing to the Microsoft Store or GitHub.

   **Option A: Build for Microsoft Store (Public)**
   Double-click the **`scripts\build_store_release.bat`** script. 
   - It will automatically inject the Microsoft Store cryptographic identity.
   - It will strip out the internal GitHub auto-updater UI to comply with Store policies.
   - The final `.msix` file will be placed in the **`StoreRelease`** folder, ready to drag-and-drop into the Microsoft Partner Center Dashboard.

   **Option B: Build for GitHub (Internal / Beta)**
   Double-click the **`scripts\build_github_release.bat`** script.
   - It will compile the app natively for `win-x64`.
   - It will digitally sign the installer using your local `ExploreDB_Certificate.pfx`.
   - The final `.msix` and `.appinstaller` files will be placed in the **`GitHubRelease`** folder.

   **Publishing a New Release:**
   If you are ready to officially launch a new version, double-click **`scripts\publish_new_release.bat`**. 
   - It will prompt you for a new version number and automatically bump it in all files.
   - It will give you a menu asking if you want to build for the Microsoft Store, GitHub, or Both simultaneously!

### **Manual Build & Sign (Fallback)**

If the automated scripts fail, you can perform the steps manually:

0. **Bump the Version Numbers (Manual)**:
   - `ExploreDB.csproj`: Update the `<Version>` and `<ApplicationDisplayVersion>` tags.
   - `Platforms\Windows\Package.appxmanifest`: Update the `Version="..."` attribute in the `<Identity>` tag.
   - `ExploreDB.appinstaller` (GitHub Only): Update both `Version="..."` attributes AND the URL at the bottom to point to your new GitHub tag.

1. **Inject the Correct Manifest**:
   *(The .NET compiler only looks for a file named `Package.appxmanifest`. Because we use a dual-channel architecture, you must manually copy the correct template over the main file before building!)*
   - **For Microsoft Store**: Go into the `Platforms\Windows` folder, copy `Package.Store.appxmanifest`, and rename the copy to `Package.appxmanifest` (overwriting the existing one).
   - **For GitHub**: Go into the `Platforms\Windows` folder, copy `Package.GitHub.appxmanifest`, and rename the copy to `Package.appxmanifest` (overwriting the existing one).

2. **Build (Terminal)**:
   - **For GitHub**:
     ```bash
     dotnet publish ExploreDB.csproj -f net8.0-windows10.0.19041.0 -c Release -r win-x64 -p:Platform=x64 -p:AppxPackageSigningEnabled=false
     ```
   - **For Microsoft Store** (Adds `-p:StoreBuild=true`):
     ```bash
     dotnet publish ExploreDB.csproj -f net8.0-windows10.0.19041.0 -c Release -r win-x64 -p:Platform=x64 -p:AppxPackageSigningEnabled=false -p:StoreBuild=true
     ```

3. **Sign the Package (GitHub Only)**:
   *(Microsoft Store builds do not need to be manually signed)*
   Locate your Windows SDK `signtool.exe` and run:
   ```cmd
   signtool sign /fd SHA256 /a /f "cert\ExploreDB_Certificate.pfx" /p "password" "bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\AppPackages\ExploreDB_1.0.0.0_x64_Test\ExploreDB_1.0.0.0_x64.msix"
   ```

### **Testing the Microsoft Store Build Locally**

Because the Microsoft Store version strips out digital signatures (so Microsoft can sign it themselves), you cannot easily install the Store `.msix` file on your local machine. 

To test Microsoft Store-specific logic locally, you must run it in Visual Studio:

1. **Add x64 Configuration**: 
   - Open `ExploreDB.sln` in Visual Studio.
   - Click the **Configuration Manager** dropdown (next to the green Play button).
   - Under "Active solution platform", select **`<New...>`**.
   - Type `x64` and ensure "Create new project platforms" is checked, then hit OK.

2. **Enable Store Mode in Code**:
   - Open your `ExploreDB.csproj` file.
   - Add the following code inside the main `<PropertyGroup>` block at the top of the file:
   ```xml
   <StoreBuild>true</StoreBuild>
   ```

3. **Launch the App**:
   - In the top toolbar next to the green Play button, select **ExploreDB (Package)** (the one with the solid green triangle icon).
   - Open **Configuration Manager** again, and make sure the **Deploy** checkbox is checked for the ExploreDB project.
   - Hit **F5**. Visual Studio will automatically deploy a temporary container and launch the app in full Microsoft Store mode!

> [!WARNING]
> Remember to delete `<StoreBuild>true</StoreBuild>` from your `.csproj` when you are finished testing!

### **Testing the GitHub Build Locally**

By default, Visual Studio automatically builds the GitHub version of ExploreDB when you press **F5** (as long as `<StoreBuild>true</StoreBuild>` is not in your `.csproj`). 

To test any new features or changes for the GitHub release:
1. Open `ExploreDB.sln` in Visual Studio.
2. In the top toolbar next to the green Play button, select **ExploreDB (Package)** (the one with the solid green triangle icon).
3. Hit **F5**. Visual Studio will automatically deploy the temporary container and launch the app.
4. *(Optional)* If you ever need to specifically test the auto-updater functionality, you can temporarily lower your `<ApplicationDisplayVersion>` and `<Version>` in the `.csproj` file before hitting F5. This will trick the app into thinking it's out of date and trigger the update banner!

### **Publishing a Release (Microsoft Store)**

To publish an update to the Microsoft Store, you must upload the freshly built files from your `StoreRelease` folder to the Microsoft Partner Center.

1. **Create a New Submission**:
   - Go to your [Microsoft Partner Center Dashboard](https://partner.microsoft.com/en-us/dashboard/windows/overview).
   - Navigate to **Apps and games** -> **ExploreDB**.
   - Under the "Update" section, click **Update** (or Create a new submission).

2. **Upload the Package**:
   - Click on **Packages**.
   - Drag and drop your compiled **`ExploreDB.msix`** (located in the `StoreRelease` folder) into the upload box.
   - Click **Save**.

3. **Submit to the Store**:
   - Review your Pricing and Store Listing to ensure nothing needs changing.
   - Click **Submit to the Store**. Microsoft will review the update and automatically push it to all users!

---

### **Publishing a Release (GitHub Releases & Pages)**

To publish an update to the team, you must upload the freshly built files from your `GitHubRelease` folder to the `ExploreDB-Releases` repository.

1. **Publish the Heavy Binaries (GitHub Releases)**:
   - Go to the `ExploreDB-Releases` repository on GitHub.
   - Click **Releases** -> **Draft a new release**.
   - Create a tag that exactly matches your new version (e.g., `v1.0.0.1`). **CRITICAL: You must include the `v` at the beginning of the tag!**
   - Drag and drop your compiled **`ExploreDB_X.X.X.X_x64.msix`** file into the "Attach binaries" box.
   - Click the green **Publish release** button at the bottom. **CRITICAL: Do not save it as a "Draft" or the installer will fail with a 404 error!**
   
   > [!TIP]
   > **Forcing an Update (Mandatory Updates)**
   > If you want to force users to update (blocking them from using the app until they do), simply type the exact keyword **`[CRITICAL UPDATE]`** anywhere inside the Release Notes box when publishing your release on GitHub. The app will detect this keyword and throw a full-screen, unclosable blocker modal!

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
   - Go back to the **Code** tab and upload `ExploreDB.appinstaller`, `ExploreDB_Certificate.pfx`, and `ExploreDB_Certificate_Installer.bat`.
   - Go to the **Releases** tab, draft a new release (e.g., `v1.0.0.0`), and upload your `.msix` file.
   
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
