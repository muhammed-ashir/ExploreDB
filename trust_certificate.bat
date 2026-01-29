@echo off
echo ========================================
echo   Trust DbExplore Development Certificate
echo ========================================
echo.
echo This script will install the DbExplore certificate
echo into your Trusted Root Certification Authorities.
echo.
echo This allows you to install the MSIX package without warnings.
echo.
echo NOTE: Run this as Administrator!
echo.
pause

powershell -Command "Start-Process powershell -Verb RunAs -ArgumentList '-Command', 'Import-PfxCertificate -FilePath DbExplore_TemporaryKey.pfx -CertStoreLocation Cert:\LocalMachine\Root -Password (ConvertTo-SecureString -String DbExplore123! -Force -AsPlainText); Write-Host Certificate installed successfully! -ForegroundColor Green; pause'"

echo.
echo Done! You can now install the .msix file.
pause
