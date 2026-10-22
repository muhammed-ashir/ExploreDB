@echo off
cd /d "%~dp0.."
echo ====================================================
echo   Building GitHub Release
echo ====================================================

echo 1. Setting up GitHub Manifest...
copy /Y src\ExploreDB\Platforms\Windows\Package.GitHub.appxmanifest src\ExploreDB\Platforms\Windows\Package.appxmanifest
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Failed to set up GitHub appxmanifest.
    if "%~1" neq "nopause" pause
    exit /b %ERRORLEVEL%
)

echo 2. Cleaning previous builds (Hard Clean)...
if exist src\ExploreDB\bin rmdir /s /q src\ExploreDB\bin 2>nul
if exist src\ExploreDB\obj rmdir /s /q src\ExploreDB\obj 2>nul
if exist src\ExploreDB\AppPackages rmdir /s /q src\ExploreDB\AppPackages 2>nul
dotnet clean

echo 3. Building and Signing Package...
dotnet publish -f net8.0-windows10.0.19041.0 -c Release -r win-x64 /p:Platform=x64 /p:StoreBuild=false /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=true /p:GitHubRelease=true
if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] GitHub package build failed with exit code %ERRORLEVEL%!
    if "%~1" neq "nopause" pause
    exit /b %ERRORLEVEL%
)

echo 4. Copying to GitHubRelease folder...
if not exist GitHubRelease mkdir GitHubRelease
for /d %%D in (src\ExploreDB\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\AppPackages\ExploreDB_*) do (
    copy /Y "%%D\*.msix" GitHubRelease\
)
for /d %%D in (src\ExploreDB\AppPackages\ExploreDB_*) do (
    copy /Y "%%D\*.msix" GitHubRelease\
)

echo Done! GitHub package is in the GitHubRelease folder.
echo.
if "%~1" neq "nopause" pause
