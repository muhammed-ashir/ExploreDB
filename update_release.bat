@echo off
echo ==============================================
echo Preparing Release Version...
echo ==============================================

echo 1. Stopping any running instances of DbExplore...
taskkill /IM DbExplore.exe /F /FI "STATUS eq RUNNING" >nul 2>&1
timeout /t 1 >nul

echo 2. Cleaning old release files...
if exist "App\ReleaseVersion" rmdir /s /q "App\ReleaseVersion"
mkdir "App\ReleaseVersion"

echo 3. Copying new publish files...
:: Note: Adjust the source path below if your bin folder is inside a DbExplore subfolder
robocopy "bin\Release\net8.0-windows10.0.19041.0\win10-x64\publish" "App\ReleaseVersion" /E /R:1 /W:1 /MT:8

echo.
echo ==============================================
echo Done! The App\ReleaseVersion folder is updated.
echo You can now commit the new release using:
echo git add App/ReleaseVersion/
echo ==============================================
pause
