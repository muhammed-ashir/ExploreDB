@echo off
setlocal
cd /d "%~dp0.."

powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0publish_new_release.ps1"
if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] Publish process failed with exit code %ERRORLEVEL%.
)

pause
