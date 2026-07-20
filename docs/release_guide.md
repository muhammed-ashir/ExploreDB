# ExploreDB Release Guide

This document explains the exact step-by-step process for publishing a new version of ExploreDB in the future. Because you are using `.appinstaller`, your teammates' computers will automatically detect these updates and install them seamlessly!

---

## Step 1: Bump the Version Numbers
Before building a new release, you must increase the version number in **three** places. For example, if you are moving from `1.0.0.0` to `1.0.0.1`:

1. **`ExploreDB.csproj`**
   Open this file and find the `<Version>` and `<ApplicationVersion>` tags. Update them to your new version.
2. **`Platforms\Windows\Package.appxmanifest`**
   Open this file and find the `Version="..."` attribute inside the `<Identity>` tag at the very top. Update it.
3. **`ExploreDB.appinstaller`**
   Open this file and update the version number in **three** places:
   - `<AppInstaller Version="1.0.0.1"`
   - `<MainPackage Version="1.0.0.1"`
   - Update the GitHub download URL so it points to the new tag and new filename (e.g., `.../download/v1.0.0.1/ExploreDB_1.0.0.1_x64.msix`).

---

## Step 2: Build the Installer
Once the version numbers are bumped, compile the new package:
1. Double-click the **`scripts\build_github_release.bat`** script in your main repository folder.
2. Wait for it to finish. It will automatically clean, compile, sign with `ExploreDB_Certificate.pfx`, and package everything into the `GitHubRelease` folder.

---

## Step 3: Publish to GitHub Releases (The Binary)
1. Go to your public `ExploreDB-Releases` repository on GitHub.
2. Click **Releases** on the right side, then click **Draft a new release**.
3. Create a new tag that matches your version (e.g., `v1.0.0.1`).
4. Set the Release title to `Version 1.0.0.1`.
5. Drag and drop the massive **`ExploreDB_1.0.0.1_x64.msix`** file from your local `App` folder into the "Attach binaries" box.
   > [!NOTE]
   > You do not need to re-upload the `.pfx` or `.bat` files for future updates. Those are only needed for the very first installation on a brand new computer.
6. Click **Publish release**.

---

## Step 4: Publish to GitHub Pages (The Trigger)
This step is what actually triggers the automatic update on your teammates' computers!
1. Go to the **Code** tab of your `ExploreDB-Releases` repository.
2. Click **Add file** -> **Upload files**.
3. Drag and drop the tiny 1 KB **`ExploreDB.appinstaller`** file from your local `App` folder into the browser.
4. Click **Commit changes** to overwrite the old one.

> [!TIP]
> Wait about 60 seconds for GitHub Actions to deploy the new `.appinstaller` file. As soon as it finishes, the next time your teammates open ExploreDB, Windows will silently notice the new version and gracefully prompt them to update!
