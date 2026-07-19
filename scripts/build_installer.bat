@echo off
cd /d "%~dp0\.."
echo ========================================
echo   ExploreDB MSIX Installer Builder
echo ========================================
echo.

REM Step 1: Clean previous builds
echo [1/3] Cleaning previous builds...
dotnet clean -c Release
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"
if exist "App" rmdir /s /q "App"

REM Step 2: Build MSIX package
echo.
echo [2/3] Building MSIX package (win-x64)...
dotnet publish ExploreDB.csproj -f net8.0-windows10.0.19041.0 -c Release -r win-x64 -p:Platform=x64 -p:AppxPackageSigningEnabled=false

REM Step 3: Sign the generated MSIX
echo.
echo [3/3] Signing the MSIX package...
set SIGNTOOL="C:\Users\muham\.nuget\packages\microsoft.windows.sdk.buildtools\10.0.22621.756\bin\10.0.22621.0\x64\signtool.exe"

for /r "bin\x64\Release\" %%f in (ExploreDB_*_x64.msix) do (
    echo Signing: %%f
    %SIGNTOOL% sign /fd SHA256 /a /f "cert\ExploreDB.pfx" /p "password" "%%f"
    
    echo.
    echo [4/4] Packaging files into App folder...
    if not exist "App" mkdir "App"
    copy /Y "%%f" "App\"
    copy /Y "cert\ExploreDB.pfx" "App\"
    copy /Y "scripts\trust_certificate.bat" "App\"
    copy /Y "ExploreDB.appinstaller" "App\"

    echo.
    echo ========================================
    echo SUCCESS! Distribution folder created at:
    echo %CD%\App
    echo ========================================
    echo.
    echo To publish this release to the team:
    echo 1. Upload the .msix file in the App folder to GitHub Releases
    echo 2. Upload ExploreDB.appinstaller to GitHub Pages (Code Tab)
    echo.
    echo OR, to share manually without GitHub:
    echo 1. Zip the entire "App" folder and share it directly.
    echo 2. Tell them to run trust_certificate.bat as Administrator first.
    echo 3. Double-click the .appinstaller file to install (if it fails, double-click the .msix).
    echo.
    goto :end
)

echo ERROR: Could not find generated .msix file. Check the build output above for errors.

:end
pause
