@echo off
cd /d "%~dp0.."
echo ====================================================
echo   Building GitHub Release
echo ====================================================

echo 1. Setting up GitHub Manifest...
copy /Y src\ExploreDB\Platforms\Windows\Package.GitHub.appxmanifest src\ExploreDB\Platforms\Windows\Package.appxmanifest

echo 2. Cleaning previous builds (Hard Clean)...
if exist src\ExploreDB\bin rmdir /s /q src\ExploreDB\bin
if exist src\ExploreDB\obj rmdir /s /q src\ExploreDB\obj
dotnet clean

echo 3. Building and Signing Package...
dotnet publish -f net8.0-windows10.0.19041.0 -c Release -r win-x64 /p:Platform=x64 /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=true /p:GitHubRelease=true

echo 4. Copying to GitHubRelease folder...
if not exist GitHubRelease mkdir GitHubRelease
for /d %%D in (src\ExploreDB\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\AppPackages\ExploreDB_*) do (
    copy /Y "%%D\*.msix" GitHubRelease\
)

echo Done! GitHub package is in the GitHubRelease folder.
echo.
pause
