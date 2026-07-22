@echo off
echo ====================================================
echo   Building GitHub Release
echo ====================================================

echo 1. Setting up GitHub Manifest...
copy /Y Platforms\Windows\Package.GitHub.appxmanifest Platforms\Windows\Package.appxmanifest

echo 2. Cleaning previous builds...
dotnet clean

echo 3. Building and Signing Package...
dotnet publish -f net8.0-windows10.0.19041.0 -c Release /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=true /p:PackageCertificateKeyFile="ExploreDB_Certificate.pfx"

echo 4. Copying to GitHubRelease folder...
if not exist GitHubRelease mkdir GitHubRelease
copy /Y bin\Release\net8.0-windows10.0.19041.0\win10-x64\AppPackages\ExploreDB_*\*.msix GitHubRelease\

echo Done! GitHub package is in the GitHubRelease folder.
