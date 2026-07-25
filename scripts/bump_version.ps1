$ErrorActionPreference = "Stop"

try {
    # Read current version from GitHub AppxManifest (as the source of truth)
    $manifestGithub = "src\ExploreDB\Platforms\Windows\Package.GitHub.appxmanifest"
    $manifestStore = "src\ExploreDB\Platforms\Windows\Package.Store.appxmanifest"
    
    $contentGithub = Get-Content $manifestGithub -Raw
    $contentStore = Get-Content $manifestStore -Raw
    
    $currentVersion = "Unknown"
    if ($contentGithub -match '<Identity[^>]*?Version="([^"]+)"') {
        $currentVersion = $matches[1]
    }
    
    Write-Host "========================================"
    Write-Host "  ExploreDB Automated Publisher"
    Write-Host "========================================"
    Write-Host ""
    Write-Host "Current Version: $currentVersion"
    Write-Host ""
    
    $NewVersion = Read-Host "Enter the NEW version number"
    
    if ([string]::IsNullOrWhiteSpace($NewVersion)) {
        Write-Error "No version provided."
        exit 1
    }
    
    if ($NewVersion -eq $currentVersion) {
        Write-Error "New version cannot be the same as the current version."
        exit 1
    }

    Write-Host ""
    Write-Host "Bumping versions to $NewVersion..."

    # 1. Update ExploreDB.csproj
    $csproj = "src\ExploreDB\ExploreDB.csproj"
    $csprojContent = Get-Content $csproj -Raw
    $csprojContent = $csprojContent -replace '(<Version>)[^<]+(</Version>)', "`${1}$NewVersion`${2}"
    $csprojContent = $csprojContent -replace '(<ApplicationDisplayVersion>)[^<]+(</ApplicationDisplayVersion>)', "`${1}$NewVersion`${2}"
    Set-Content -Path $csproj -Value $csprojContent -NoNewline
    Write-Host " - Updated $csproj"

    # 2. Update Both Package Manifest Templates
    $contentGithub = $contentGithub -replace '(?s)(<Identity[^>]*?Version=")[^"]+(")', "`${1}$NewVersion`${2}"
    Set-Content -Path $manifestGithub -Value $contentGithub -NoNewline
    Write-Host " - Updated $manifestGithub"

    $contentStore = $contentStore -replace '(?s)(<Identity[^>]*?Version=")[^"]+(")', "`${1}$NewVersion`${2}"
    Set-Content -Path $manifestStore -Value $contentStore -NoNewline
    Write-Host " - Updated $manifestStore"

    # 3. Update ExploreDB.appinstaller
    $installer = "ExploreDB.appinstaller"
    $installerContent = Get-Content $installer -Raw
    $installerContent = $installerContent -replace '(?s)(<AppInstaller[^>]*?Version=")[^"]+(")', "`${1}$NewVersion`${2}"
    $installerContent = $installerContent -replace '(?s)(<MainPackage[^>]*?Version=")[^"]+(")', "`${1}$NewVersion`${2}"
    
    # Update the GitHub download URL
    $installerContent = $installerContent -replace '(?<=Uri="https://github.com/zerinapps/ExploreDB-Releases/releases/download/)[^"]+', "v$NewVersion/ExploreDB.msix"
    Set-Content -Path $installer -Value $installerContent -NoNewline
    Write-Host " - Updated $installer"
    
    Write-Host "Success!"
}
catch {
    Write-Error "Failed to update version: $_"
    exit 1
}
