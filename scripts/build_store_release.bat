@echo off
cd /d "%~dp0.."
echo ====================================================
echo   Building Microsoft Store Release
echo ====================================================

echo 1. Setting up Store Manifest...
copy /Y Platforms\Windows\Package.Store.appxmanifest Platforms\Windows\Package.appxmanifest

echo 2. Cleaning previous builds (Hard Clean)...
if exist bin rmdir /s /q bin
if exist obj rmdir /s /q obj
dotnet clean

echo 3. Building Store Package...
dotnet publish -f net8.0-windows10.0.19041.0 -c Release -r win-x64 /p:Platform=x64 /p:StoreBuild=true /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false

echo 4. Copying to StoreRelease folder...
if not exist StoreRelease mkdir StoreRelease
for /d %%D in (bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\AppPackages\ExploreDB_*) do (
    copy /Y "%%D\*.msix" StoreRelease\
)

echo Done! Store package is in the StoreRelease folder.
echo.
pause
