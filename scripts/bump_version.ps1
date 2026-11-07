param(
    [string]$NewVersion = ""
)

$ErrorActionPreference = "Stop"

try {
    # Resolve repo root relative to this script
    $repoRoot = Split-Path -Parent $PSScriptRoot
    if (-not $repoRoot) { $repoRoot = (Get-Item .).FullName }

    $manifestGithub = Join-Path $repoRoot "src\ExploreDB\Platforms\Windows\Package.GitHub.appxmanifest"
    $manifestStore = Join-Path $repoRoot "src\ExploreDB\Platforms\Windows\Package.Store.appxmanifest"
    $csproj = Join-Path $repoRoot "src\ExploreDB\ExploreDB.csproj"
    $installer = Join-Path $repoRoot "ExploreDB.appinstaller"

    $contentGithub = Get-Content $manifestGithub -Raw
    $contentStore = Get-Content $manifestStore -Raw

    $currentVersion = "Unknown"
    if ($contentGithub -match '<Identity[^>]*?Version="([^"]+)"') {
        $currentVersion = $matches[1]
    }

    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  ExploreDB Version Manager" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Current Version: " -NoNewline
    Write-Host "$currentVersion" -ForegroundColor Yellow
    Write-Host ""

    if ([string]::IsNullOrWhiteSpace($NewVersion)) {
        $NewVersion = Read-Host "Enter the NEW version number (press Enter to keep current '$currentVersion')"
    }

    $NewVersion = $NewVersion.Trim()

    if ([string]::IsNullOrWhiteSpace($NewVersion) -or $NewVersion -eq $currentVersion) {
        Write-Host "Keeping current version: $currentVersion (no version bump needed)." -ForegroundColor Green
        exit 0
    }

    # Normalize 3-part version to 4-part (e.g. 2.1.6 -> 2.1.6.0)
    if ($NewVersion -match '^\d+\.\d+\.\d+$') {
        $NewVersion = "$NewVersion.0"
        Write-Host "Appended '.0' to meet MSIX 4-part version requirement: $NewVersion" -ForegroundColor Yellow
    }

    if ($NewVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
        Write-Error "Invalid version format '$NewVersion'. Must be 4 numbers separated by dots (e.g. 2.1.5.0)."
        exit 1
    }

    Write-Host ""
    Write-Host "Bumping versions to $NewVersion..." -ForegroundColor Cyan

    # 1. Update ExploreDB.csproj
    $csprojContent = Get-Content $csproj -Raw
    $csprojContent = $csprojContent -replace '(<Version>)[^<]+(</Version>)', "`${1}$NewVersion`${2}"
    $csprojContent = $csprojContent -replace '(<ApplicationDisplayVersion>)[^<]+(</ApplicationDisplayVersion>)', "`${1}$NewVersion`${2}"
    Set-Content -Path $csproj -Value $csprojContent -NoNewline
    Write-Host " - Updated $csproj" -ForegroundColor Green

    # 2. Update Both Package Manifest Templates
    $contentGithub = $contentGithub -replace '(?s)(<Identity[^>]*?Version=")[^"]+(")', "`${1}$NewVersion`${2}"
    Set-Content -Path $manifestGithub -Value $contentGithub -NoNewline
    Write-Host " - Updated $manifestGithub" -ForegroundColor Green

    $contentStore = $contentStore -replace '(?s)(<Identity[^>]*?Version=")[^"]+(")', "`${1}$NewVersion`${2}"
    Set-Content -Path $manifestStore -Value $contentStore -NoNewline
    Write-Host " - Updated $manifestStore" -ForegroundColor Green

    # 3. Update ExploreDB.appinstaller
    $installerContent = Get-Content $installer -Raw
    $installerContent = $installerContent -replace '(?s)(<AppInstaller[^>]*?Version=")[^"]+(")', "`${1}$NewVersion`${2}"
    $installerContent = $installerContent -replace '(?s)(<MainPackage[^>]*?Version=")[^"]+(")', "`${1}$NewVersion`${2}"
    $installerContent = $installerContent -replace '(?<=Uri="https://github.com/zerinapps/ExploreDB-Releases/releases/download/)[^"]+', "v$NewVersion/ExploreDB.msix"
    Set-Content -Path $installer -Value $installerContent -NoNewline
    Write-Host " - Updated $installer" -ForegroundColor Green

    Write-Host "Success! Version updated to $NewVersion." -ForegroundColor Green
}
catch {
    Write-Error "Failed to update version: $_"
    exit 1
}
