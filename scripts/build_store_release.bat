@echo off
echo ====================================================
echo   Building Microsoft Store Release
echo ====================================================

echo 1. Setting up Store Manifest...
copy /Y Platforms\Windows\Package.Store.appxmanifest Platforms\Windows\Package.appxmanifest

echo 2. Cleaning previous builds...
dotnet clean

echo 3. Building Store Package...
dotnet publish -f net8.0-windows10.0.19041.0 -c Release /p:StoreBuild=true /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false

echo 4. Copying to StoreRelease folder...
if not exist StoreRelease mkdir StoreRelease
copy /Y bin\Release\net8.0-windows10.0.19041.0\win10-x64\AppPackages\ExploreDB_*\*.msix StoreRelease\

echo Done! Store package is in the StoreRelease folder.
