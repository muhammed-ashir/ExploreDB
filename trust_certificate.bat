@echo off
echo ========================================
echo   Trust ExploreDB Development Certificate
echo ========================================
echo.
echo This script will install the ExploreDB certificate
echo into your Trusted Root Certification Authorities.
echo.
echo This allows you to install the MSIX package without warnings.
echo.
echo NOTE: Run this as Administrator!
echo.
pause

powershell -Command "Start-Process powershell -Verb RunAs -ArgumentList '-Command', 'Import-PfxCertificate -FilePath ExploreDB.pfx -CertStoreLocation Cert:\LocalMachine\Root -Password (ConvertTo-SecureString -String password -Force -AsPlainText); Write-Host Certificate installed successfully! -ForegroundColor Green; pause'"

echo.
echo Done! You can now install the .msix file.
pause
