#Requires -Version 5.1
<#
.SYNOPSIS
    Verifies that the pinned scrcpy release matches the latest upstream release.

.DESCRIPTION
    Used by the GitHub Actions publish workflow so releases cannot ship with an
    outdated scrcpy bundle. The check compares the packaging pin against
    https://api.github.com/repos/Genymobile/scrcpy/releases/latest and verifies
    that the Windows x64 and macOS Apple Silicon release assets exist. If a local
    scrcpy executable is present, it also verifies that binary's --version output.
#>
[CmdletBinding()]
param(
    [string]$ScrcpyExe = '',
    [string]$ExpectedVersion = '4.0',
    [string]$LatestReleaseApi = 'https://api.github.com/repos/Genymobile/scrcpy/releases/latest'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ScrcpyExe)) {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    $ScrcpyExe = Join-Path $scriptRoot 'scrcpy/scrcpy.exe'
}

$headers = @{
    'User-Agent' = 'AndroidTreeView-release-check'
    'Accept' = 'application/vnd.github+json'
}
$latest = Invoke-RestMethod -Uri $LatestReleaseApi -Headers $headers
$latestTag = [string]$latest.tag_name
if ([string]::IsNullOrWhiteSpace($latestTag)) {
    throw 'GitHub latest release response did not contain tag_name.'
}

$latestVersion = $latestTag.Trim().TrimStart('v', 'V')
if ($ExpectedVersion -ne $latestVersion) {
    throw "Pinned scrcpy $ExpectedVersion is not latest upstream $latestVersion ($latestTag)."
}

$requiredAssets = @(
    "scrcpy-win64-v$ExpectedVersion.zip",
    "scrcpy-macos-aarch64-v$ExpectedVersion.tar.gz"
)
$assetNames = @($latest.assets | ForEach-Object { [string]$_.name })
foreach ($asset in $requiredAssets) {
    if ($assetNames -notcontains $asset) {
        throw "Latest scrcpy release '$latestTag' does not contain required asset '$asset'."
    }
}

if (Test-Path -LiteralPath $ScrcpyExe) {
    $versionOutput = & $ScrcpyExe --version
    if ($LASTEXITCODE -ne 0) {
        throw "scrcpy --version failed with exit code $LASTEXITCODE."
    }

    $firstLine = ($versionOutput | Select-Object -First 1)
    if ($firstLine -notmatch '^scrcpy\s+([0-9]+(?:\.[0-9]+){1,2})\b') {
        throw "Could not parse bundled scrcpy version from: $firstLine"
    }

    $bundledVersion = $Matches[1]
    if ($bundledVersion -ne $latestVersion) {
        throw "Bundled scrcpy $bundledVersion is not latest upstream $latestVersion ($latestTag)."
    }

    Write-Host "Bundled scrcpy $bundledVersion matches latest upstream $latestTag."
} else {
    Write-Host "Local scrcpy executable not found at '$ScrcpyExe'; verified release pin and assets only."
}

Write-Host "Pinned scrcpy $ExpectedVersion matches latest upstream $latestTag."
Write-Host "Required assets: $($requiredAssets -join ', ')."
