@echo off
echo ========================================
echo   DbExplore MSIX Installer Builder
echo ========================================
echo.

REM Step 1: Clean previous builds
echo [1/3] Cleaning previous builds...
dotnet clean -c Release

REM Step 2: Build MSIX package
echo.
echo [2/3] Building MSIX package (win-x64)...
dotnet publish DbExplore.csproj -f net8.0-windows10.0.19041.0 -c Release -r win-x64 -p:Platform=x64

REM Step 3: Sign the generated MSIX
echo.
echo [3/3] Signing the MSIX package...
set SIGNTOOL="C:\Users\muham\.nuget\packages\microsoft.windows.sdk.buildtools\10.0.22621.756\bin\10.0.22621.0\x64\signtool.exe"

for /r "bin\x64\Release\" %%f in (*.msix) do (
    echo Signing: %%f
    %SIGNTOOL% sign /fd SHA256 /a /f "DbExplore.pfx" /p "password" "%%f"
    
    echo.
    echo ========================================
    echo SUCCESS! Installer created and signed at:
    echo %%f
    echo ========================================
    echo.
    echo To install on this or another computer:
    echo 1. Give the user this .msix file, DbExplore.pfx, and trust_certificate.bat
    echo 2. Run trust_certificate.bat as Administrator (first time only)
    echo 3. Double-click the .msix file to install
    echo.
    goto :end
)

echo ERROR: Could not find generated .msix file. Check the build output above for errors.

:end
pause
