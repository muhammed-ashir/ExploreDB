@echo off
setlocal
echo ========================================
echo ExploreDB Log Extractor
echo ========================================
echo.

set "ps_script=%TEMP%\ExtractExploreDBLogs.ps1"

echo Write-Host "Searching for ExploreDB logs..." -ForegroundColor Cyan > "%ps_script%"
echo $desktopPath = [Environment]::GetFolderPath("Desktop") >> "%ps_script%"
echo $destFolder = Join-Path $desktopPath "ExploreDB_Logs_Export" >> "%ps_script%"
echo if (!(Test-Path $destFolder)) { New-Item -ItemType Directory -Path $destFolder ^| Out-Null } >> "%ps_script%"
echo $foundLogs = $false >> "%ps_script%"
echo Get-ChildItem -Path $env:LOCALAPPDATA\Packages -Filter *ExploreDB* ^| ForEach-Object { >> "%ps_script%"
echo     $logPath = Join-Path $_.FullName "LocalCache\Local\ExploreDB\logs" >> "%ps_script%"
echo     if (Test-Path $logPath) { >> "%ps_script%"
echo         Write-Host "Found logs at: $logPath" -ForegroundColor Green >> "%ps_script%"
echo         Copy-Item -Path "$logPath\*.txt" -Destination $destFolder -Force >> "%ps_script%"
echo         $foundLogs = $true >> "%ps_script%"
echo     } >> "%ps_script%"
echo } >> "%ps_script%"
echo if ($foundLogs) { >> "%ps_script%"
echo     Write-Host "`nSuccess! All log files have been copied to: $destFolder" -ForegroundColor Green >> "%ps_script%"
echo     Invoke-Item $destFolder >> "%ps_script%"
echo } else { >> "%ps_script%"
echo     Write-Host "`nNo ExploreDB logs were found on this computer." -ForegroundColor Yellow >> "%ps_script%"
echo } >> "%ps_script%"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%ps_script%"
del "%ps_script%"

echo.
pause
