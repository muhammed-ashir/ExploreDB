@echo off
cd /d "%~dp0.."
echo ====================================================
echo   Building Microsoft Store Release
echo ====================================================

echo 1. Setting up Store Manifest...
copy /Y src\ExploreDB\Platforms\Windows\Package.Store.appxmanifest src\ExploreDB\Platforms\Windows\Package.appxmanifest
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Failed to set up Store appxmanifest.
    if "%~1" neq "nopause" pause
    exit /b %ERRORLEVEL%
)

echo 2. Cleaning previous builds (Hard Clean)...
if exist src\ExploreDB\bin rmdir /s /q src\ExploreDB\bin 2>nul
if exist src\ExploreDB\obj rmdir /s /q src\ExploreDB\obj 2>nul
if exist src\ExploreDB\AppPackages rmdir /s /q src\ExploreDB\AppPackages 2>nul
dotnet clean

echo 3. Building Store Package...
dotnet publish -f net8.0-windows10.0.19041.0 -c Release -r win-x64 /p:Platform=x64 /p:StoreBuild=true /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false /p:GitHubRelease=false
if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] Store package build failed with exit code %ERRORLEVEL%!
    if "%~1" neq "nopause" pause
    exit /b %ERRORLEVEL%
)

echo 4. Copying to StoreRelease folder...
if not exist StoreRelease mkdir StoreRelease
for /d %%D in (src\ExploreDB\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\AppPackages\ExploreDB_*) do (
    copy /Y "%%D\*.msix" StoreRelease\
)
for /d %%D in (src\ExploreDB\AppPackages\ExploreDB_*) do (
    copy /Y "%%D\*.msix" StoreRelease\
)

echo Done! Store package is in the StoreRelease folder.
echo.
if "%~1" neq "nopause" pause
