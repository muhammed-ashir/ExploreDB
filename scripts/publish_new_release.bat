@echo off
cd /d "%~dp0.."
echo ====================================================
echo   Publish New Release (Bump Version)
echo ====================================================

powershell -ExecutionPolicy Bypass -File "%~dp0bump_version.ps1"

echo.
echo Version bumped successfully!
echo.
echo Proceeding to build releases...

call "%~dp0build_github_release.bat"
call "%~dp0build_store_release.bat"

echo.
echo All releases have been built successfully!
pause
