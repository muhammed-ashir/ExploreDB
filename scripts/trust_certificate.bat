@echo off
echo ========================================
echo   Trust ExploreDB Development Certificate
echo ========================================
echo.
echo This script will securely install the ExploreDB certificate
echo into your computer's Trusted Root Certification Authorities.
echo.
echo This is required (one-time only) to install the app safely.
echo.
echo INSTRUCTIONS:
echo 1. Press ENTER on your keyboard right now.
echo 2. A Windows Administrator prompt (UAC) will appear.
echo 3. Please click "Yes" to allow the installation.
echo 4. A blue window will briefly appear to confirm success.
echo.
pause

powershell -Command "Start-Process powershell -Verb RunAs -ArgumentList '-Command', 'Import-PfxCertificate -FilePath ''%~dp0ExploreDB.pfx'' -CertStoreLocation Cert:\LocalMachine\Root -Password (ConvertTo-SecureString -String password -Force -AsPlainText); Write-Host \"\" ; Write-Host \"SUCCESS: Certificate installed perfectly!\" -ForegroundColor Green; Write-Host \"You may now close this blue window and install the app.\" -ForegroundColor Cyan; pause'"

echo.
echo Process complete! If the blue window said SUCCESS, you can now install the app.
pause
