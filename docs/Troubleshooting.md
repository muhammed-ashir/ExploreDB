# ExploreDB Troubleshooting Guide

## Certificate Issues

### How to Uninstall the Development Certificate
If you need to verify or test the `ExploreDB_Certificate_Installer.bat` script, or if you simply want to remove the ExploreDB self-signed certificate from your computer, follow these steps:

1. Press the **Windows Key** on your keyboard, type **`certlm.msc`** (which stands for Certificate Local Machine), and hit **Enter**.
2. A "Certificates" window will open. In the left sidebar, click the little arrow next to **Trusted Root Certification Authorities** to expand it.
3. Click on the **Certificates** folder right below it.
4. In the main window, scroll down the alphabetical list until you find the certificate named **ExploreDB** (in the "Issued To" column).
5. Right-click on the **ExploreDB** certificate and select **Delete**.
6. Click **Yes** to confirm.

*(Note: Once deleted, Windows will block the installation of `ExploreDB.msix` until you run the `ExploreDB_Certificate_Installer.bat` script again!)*
