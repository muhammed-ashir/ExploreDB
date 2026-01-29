@echo off
echo ========================================
echo   DbExplore MSIX Installer Builder
echo ========================================
echo.

REM Step 1: Create a self-signed certificate for development
echo [1/4] Creating development certificate...
powershell -Command "if (-not (Test-Path 'DbExplore_TemporaryKey.pfx')) { $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=DbExplore' -KeyUsage DigitalSignature -FriendlyName 'DbExplore Development' -CertStoreLocation 'Cert:\CurrentUser\My' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}'); $pwd = ConvertTo-SecureString -String 'DbExplore123!' -Force -AsPlainText; Export-PfxCertificate -Cert $cert -FilePath 'DbExplore_TemporaryKey.pfx' -Password $pwd; Write-Host 'Certificate created: DbExplore_TemporaryKey.pfx' }"

REM Step 2: Clean previous builds
echo.
echo [2/4] Cleaning previous builds...
dotnet clean -c Release

REM Step 3: Build MSIX package
echo.
echo [3/4] Building MSIX package...
dotnet publish -f net8.0-windows10.0.19041.0 -c Release -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=true -p:PackageCertificateKeyFile=DbExplore_TemporaryKey.pfx -p:PackageCertificatePassword=DbExplore123!

REM Step 4: Find the generated MSIX
echo.
echo [4/4] Locating installer...
for /r "bin\Release\" %%f in (*.msix) do (
    echo.
    echo ========================================
    echo SUCCESS! Installer created at:
    echo %%f
    echo ========================================
    echo.
    echo To install:
    echo 1. Double-click the .msix file
    echo 2. Click "Install"
    echo.
    echo NOTE: You may need to trust the certificate first.
    echo       See trust_certificate.bat for details.
    goto :end
)

echo ERROR: Could not find generated .msix file
echo Check the build output above for errors.

:end
pause
