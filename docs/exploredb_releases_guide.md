# Publishing to ExploreDB-Releases

Since your `ExploreDB` folder on your computer is already linked to your private main code repo, the absolute easiest way to put your installer files into your new public `ExploreDB-Releases` repository is to do it directly through the GitHub website!

Here are the exact 3 steps to do right now:

---

### Step 1: Upload `ExploreDB.appinstaller` to the Code Tab
1. Open your web browser and go to `https://github.com/zerinapps/ExploreDB-Releases`.
2. Click the **Add file** button (near the green Code button) and select **Upload files**.
3. Open your computer's File Explorer, go to `D:\apps\ExploreDB\App`, and drag **ONLY** the `ExploreDB.appinstaller` file into the browser.
4. Click the green **Commit changes** button.

---

### Step 2: Turn on GitHub Pages
1. On that same `ExploreDB-Releases` GitHub page, click the **Settings** tab at the top.
2. On the left sidebar, click **Pages**.
3. Under "Build and deployment":
   - **Source:** Deploy from a branch
   - **Branch:** Select `main` (or `master`) and the `/ (root)` folder.
4. Click **Save**.

*(Your static link `https://zerinapps.github.io/ExploreDB-Releases/ExploreDB.appinstaller` is now active!)*

---

### Step 3: Upload the Installer to the Releases Tab
1. Go back to the main page of `ExploreDB-Releases`.
2. On the right sidebar, click **Create a new release** (or click the "Releases" heading and click Draft a new release).
3. Click **Choose a tag** and type exactly: **`v1.0.0.0`** (and click "Create new tag").
4. Make the Release Title: `Version 1.0.0.0`
5. At the bottom, in the large box that says **"Attach binaries by dropping them here"**, drag and drop the remaining 3 files from your `D:\apps\ExploreDB\App` folder:
   - `ExploreDB.appinstaller`
   - `ExploreDB_Certificate.pfx`
   - `trust_certificate.bat`
6. Click the green **Publish release** button.

---

### You are Done! 🎉

Wait about 1-2 minutes for GitHub Pages to sync. Then, you can send your teammates this exact installation link:

👉 `ms-appinstaller:?source=https://zerinapps.github.io/ExploreDB-Releases/ExploreDB.appinstaller`

When they click that link, Windows will install the app and link it to your GitHub for automatic updates forever!
