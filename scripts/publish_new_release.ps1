param(
    [string]$Version,
    [string]$Target = ""
)

$ErrorActionPreference = "Stop"

try {
    # Resolve repo root relative to this script
    $repoRoot = Split-Path -Parent $PSScriptRoot
    if (-not $repoRoot) { $repoRoot = (Get-Item .).FullName }
    Set-Location $repoRoot

    $manifestGithub = Join-Path $repoRoot "src\ExploreDB\Platforms\Windows\Package.GitHub.appxmanifest"
    $manifestStore = Join-Path $repoRoot "src\ExploreDB\Platforms\Windows\Package.Store.appxmanifest"
    $csproj = Join-Path $repoRoot "src\ExploreDB\ExploreDB.csproj"
    $installer = Join-Path $repoRoot "ExploreDB.appinstaller"

    Write-Host "====================================================" -ForegroundColor Cyan
    Write-Host "           ExploreDB Release Publisher              " -ForegroundColor Cyan
    Write-Host "====================================================" -ForegroundColor Cyan
    Write-Host ""

    # 1. Detect current version
    $contentGithub = Get-Content $manifestGithub -Raw
    $currentVersion = "Unknown"
    if ($contentGithub -match '<Identity[^>]*?Version="([^"]+)"') {
        $currentVersion = $matches[1]
    }

    Write-Host "Current Version: " -NoNewline
    Write-Host "$currentVersion" -ForegroundColor Yellow
    Write-Host ""

    # 2. Version Prompt
    $inputVersion = $Version
    if ([string]::IsNullOrWhiteSpace($inputVersion)) {
        $inputVersion = Read-Host "Enter version number (press Enter to keep current '$currentVersion')"
    }
    
    $targetVersion = if ($inputVersion) { $inputVersion.Trim() } else { "" }

    if ([string]::IsNullOrWhiteSpace($targetVersion) -or $targetVersion -eq $currentVersion) {
        $targetVersion = $currentVersion
        Write-Host "Keeping current version: $targetVersion (no version bump needed)." -ForegroundColor Green
    }
    else {
        # Normalize 3-part version to 4-part (e.g. 2.1.6 -> 2.1.6.0)
        if ($targetVersion -match '^\d+\.\d+\.\d+$') {
            $targetVersion = "$targetVersion.0"
            Write-Host "Appended '.0' to meet MSIX 4-part version requirement: $targetVersion" -ForegroundColor Yellow
        }

        if ($targetVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
            Write-Error "Invalid version format '$targetVersion'. Must be 4 numbers separated by dots (e.g. 2.1.5.0)."
            exit 1
        }

        Write-Host ""
        Write-Host "Bumping versions to $targetVersion..." -ForegroundColor Cyan

        # 1. Update ExploreDB.csproj
        $csprojContent = Get-Content $csproj -Raw
        $csprojContent = $csprojContent -replace '(<Version>)[^<]+(</Version>)', "`${1}$targetVersion`${2}"
        $csprojContent = $csprojContent -replace '(<ApplicationDisplayVersion>)[^<]+(</ApplicationDisplayVersion>)', "`${1}$targetVersion`${2}"
        Set-Content -Path $csproj -Value $csprojContent -NoNewline
        Write-Host " - Updated $csproj" -ForegroundColor Green

        # 2. Update Package.GitHub.appxmanifest
        $contentGithub = $contentGithub -replace '(?s)(<Identity[^>]*?Version=")[^"]+(")', "`${1}$targetVersion`${2}"
        Set-Content -Path $manifestGithub -Value $contentGithub -NoNewline
        Write-Host " - Updated $manifestGithub" -ForegroundColor Green

        # 3. Update Package.Store.appxmanifest
        $contentStore = Get-Content $manifestStore -Raw
        $contentStore = $contentStore -replace '(?s)(<Identity[^>]*?Version=")[^"]+(")', "`${1}$targetVersion`${2}"
        Set-Content -Path $manifestStore -Value $contentStore -NoNewline
        Write-Host " - Updated $manifestStore" -ForegroundColor Green

        # 4. Update ExploreDB.appinstaller
        $installerContent = Get-Content $installer -Raw
        $installerContent = $installerContent -replace '(?s)(<AppInstaller[^>]*?Version=")[^"]+(")', "`${1}$targetVersion`${2}"
        $installerContent = $installerContent -replace '(?s)(<MainPackage[^>]*?Version=")[^"]+(")', "`${1}$targetVersion`${2}"
        $installerContent = $installerContent -replace '(?<=Uri="https://github.com/zerinapps/ExploreDB-Releases/releases/download/)[^"]+', "v$targetVersion/ExploreDB.msix"
        Set-Content -Path $installer -Value $installerContent -NoNewline
        Write-Host " - Updated $installer" -ForegroundColor Green

        Write-Host "Version successfully bumped to $targetVersion!" -ForegroundColor Green
    }

    # 3. Target Selection Prompt
    $selectedTarget = $Target.Trim()
    if ($selectedTarget -eq "Both") { $selectedTarget = "1" }
    elseif ($selectedTarget -eq "GitHub") { $selectedTarget = "2" }
    elseif ($selectedTarget -eq "Store") { $selectedTarget = "3" }

    if ([string]::IsNullOrWhiteSpace($selectedTarget)) {
        Write-Host ""
        Write-Host "Select release package(s) to generate:" -ForegroundColor Cyan
        Write-Host "  [1] Both (GitHub + Microsoft Store) [Default]"
        Write-Host "  [2] GitHub Release only"
        Write-Host "  [3] Microsoft Store Release only"
        Write-Host ""
        $selectedTarget = Read-Host "Enter choice [1-3] (Default: 1)"
        if ([string]::IsNullOrWhiteSpace($selectedTarget)) {
            $selectedTarget = "1"
        }
    }

    while ($selectedTarget -notmatch '^[123]$') {
        Write-Host "Invalid choice '$selectedTarget'. Please enter 1, 2, or 3." -ForegroundColor Yellow
        $selectedTarget = Read-Host "Enter choice [1-3] (Default: 1)"
        if ([string]::IsNullOrWhiteSpace($selectedTarget)) {
            $selectedTarget = "1"
        }
    }

    # Store validation reminder
    if ($selectedTarget -eq "1" -or $selectedTarget -eq "3") {
        if (-not $targetVersion.EndsWith(".0")) {
            Write-Host ""
            Write-Host "[WARNING] Microsoft Store strictly requires the 4th digit (revision) to be 0 (e.g. 2.1.5.0)." -ForegroundColor Yellow
            Write-Host "Current version is '$targetVersion'. Microsoft Store certification may reject non-zero revisions." -ForegroundColor Yellow
            Write-Host ""
        }
    }

    $startTime = Get-Date

    # 4. Execute Selected Build(s)
    if ($selectedTarget -eq "1" -or $selectedTarget -eq "2") {
        Write-Host ""
        Write-Host "====================================================" -ForegroundColor Cyan
        Write-Host "  Building GitHub Release (v$targetVersion)..." -ForegroundColor Cyan
        Write-Host "====================================================" -ForegroundColor Cyan
        $ghScript = Join-Path $PSScriptRoot "build_github_release.bat"
        & cmd.exe /c "`"$ghScript`" nopause"
        if ($LASTEXITCODE -ne 0) {
            Write-Error "GitHub release build failed with exit code $LASTEXITCODE."
            exit $LASTEXITCODE
        }
    }

    if ($selectedTarget -eq "1" -or $selectedTarget -eq "3") {
        Write-Host ""
        Write-Host "====================================================" -ForegroundColor Cyan
        Write-Host "  Building Microsoft Store Release (v$targetVersion)..." -ForegroundColor Cyan
        Write-Host "====================================================" -ForegroundColor Cyan
        $storeScript = Join-Path $PSScriptRoot "build_store_release.bat"
        & cmd.exe /c "`"$storeScript`" nopause"
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Store release build failed with exit code $LASTEXITCODE."
            exit $LASTEXITCODE
        }
    }

    $elapsed = (Get-Date) - $startTime

    # 5. Output Summary
    Write-Host ""
    Write-Host "====================================================" -ForegroundColor Green
    Write-Host "  Publish Complete! (Elapsed: $([math]::Round($elapsed.TotalSeconds, 1))s)" -ForegroundColor Green
    Write-Host "====================================================" -ForegroundColor Green
    Write-Host "Version: $targetVersion"

    if ($selectedTarget -eq "1" -or $selectedTarget -eq "2") {
        Write-Host ""
        Write-Host "GitHub Release Package:" -ForegroundColor Cyan
        $ghPkg = Join-Path $repoRoot "GitHubRelease\ExploreDB_${targetVersion}_x64.msix"
        if (Test-Path $ghPkg) {
            $item = Get-Item $ghPkg
            Write-Host "  - $($item.FullName) ($([math]::Round($item.Length / 1MB, 2)) MB)" -ForegroundColor White
        }
        else {
            Write-Host "  - Notice: Check GitHubRelease\ folder." -ForegroundColor Yellow
        }
    }

    if ($selectedTarget -eq "1" -or $selectedTarget -eq "3") {
        Write-Host ""
        Write-Host "Microsoft Store Release Package:" -ForegroundColor Cyan
        $storePkg = Join-Path $repoRoot "StoreRelease\ExploreDB_${targetVersion}_x64.msix"
        if (Test-Path $storePkg) {
            $item = Get-Item $storePkg
            Write-Host "  - $($item.FullName) ($([math]::Round($item.Length / 1MB, 2)) MB)" -ForegroundColor White
        }
        else {
            Write-Host "  - Notice: Check StoreRelease\ folder." -ForegroundColor Yellow
        }
    }

    Write-Host "====================================================" -ForegroundColor Green
}
catch {
    Write-Error "Release publisher failed: $_"
    exit 1
}
