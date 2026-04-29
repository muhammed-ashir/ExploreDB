@echo off
echo ==============================================
echo Preparing Release Version...
echo ==============================================

echo 1. Cleaning old release files...
if exist "App\ReleaseVersion" rmdir /s /q "App\ReleaseVersion"
mkdir "App\ReleaseVersion"

echo 2. Copying new publish files...
:: Note: Adjust the source path below if your bin folder is inside a DbExplore subfolder
xcopy "bin\Release\net8.0-windows10.0.19041.0\win10-x64\publish\*" "App\ReleaseVersion\" /s /e /y

echo.
echo ==============================================
echo Done! The App\ReleaseVersion folder is updated.
echo You can now commit the new release using:
echo git add App/ReleaseVersion/
echo ==============================================
pause
