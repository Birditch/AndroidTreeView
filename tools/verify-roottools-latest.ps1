#Requires -Version 5.1
<#
.SYNOPSIS
    Verifies the pinned Root tool releases, assets, and payload-dumper checksums.

.DESCRIPTION
    This is a metadata-only release check used by GitHub Actions. It does not
    download the APK or binary archives. When a local Root tools directory is
    supplied, the pinned Magisk APK hash is checked as well.
#>
[CmdletBinding()]
param(
    [string]$RootToolsDir = '',
    [string]$MagiskVersion = '30.7',
    [string]$MagiskSha256 = 'e0d32d2123532860f97123d927b1bb86c4e08e6fd8a48bfc6b5bee0afae9ebd5',
    [string]$PayloadDumperVersion = '1.3.0',
    [string]$PayloadDumperWindowsSha256 = '0f96e07477963327f7f50a03bf2aa9dac5c76dba110ab332dc759321ae345d52',
    [string]$PayloadDumperMacSha256 = 'e6b95df4b08e4bf452077e35cc2c0d644ce8fd454696d1aceedde6887ef0df84'
)

$ErrorActionPreference = 'Stop'

$headers = @{
    'User-Agent' = 'AndroidTreeView-release-check'
    'Accept' = 'application/vnd.github+json'
}

$magiskAsset = "Magisk-v$MagiskVersion.apk"
$magiskRelease = Invoke-RestMethod `
    -Uri "https://api.github.com/repos/topjohnwu/Magisk/releases/tags/v$MagiskVersion" `
    -Headers $headers
$magiskAssets = @($magiskRelease.assets | ForEach-Object { [string]$_.name })
if ($magiskAssets -notcontains $magiskAsset) {
    throw "Magisk v$MagiskVersion does not contain required asset '$magiskAsset'."
}

$payloadAssets = @(
    "payload-dumper-go_${PayloadDumperVersion}_windows_amd64.tar.gz",
    "payload-dumper-go_${PayloadDumperVersion}_darwin_arm64.tar.gz",
    'payload-dumper-go_sha256checksums.txt'
)
$payloadRelease = Invoke-RestMethod `
    -Uri "https://api.github.com/repos/ssut/payload-dumper-go/releases/tags/$PayloadDumperVersion" `
    -Headers $headers
$payloadAssetNames = @($payloadRelease.assets | ForEach-Object { [string]$_.name })
foreach ($asset in $payloadAssets) {
    if ($payloadAssetNames -notcontains $asset) {
        throw "payload-dumper-go $PayloadDumperVersion does not contain required asset '$asset'."
    }
}

$checksums = Invoke-RestMethod `
    -Uri "https://github.com/ssut/payload-dumper-go/releases/download/$PayloadDumperVersion/payload-dumper-go_sha256checksums.txt" `
    -Headers $headers
$expectedChecksums = @{
    $payloadAssets[0] = $PayloadDumperWindowsSha256
    $payloadAssets[1] = $PayloadDumperMacSha256
}
foreach ($entry in $expectedChecksums.GetEnumerator()) {
    $escapedName = [regex]::Escape($entry.Key)
    $match = [regex]::Match([string]$checksums, "(?im)^([a-f0-9]{64})\s+\*?$escapedName\s*$")
    if (-not $match.Success) {
        throw "Upstream checksum list does not contain '$($entry.Key)'."
    }

    $actual = $match.Groups[1].Value.ToLowerInvariant()
    if ($actual -ne $entry.Value.ToLowerInvariant()) {
        throw "Pinned SHA-256 for '$($entry.Key)' is '$($entry.Value)', upstream publishes '$actual'."
    }
}

if (-not [string]::IsNullOrWhiteSpace($RootToolsDir)) {
    $magiskPath = Join-Path (Join-Path $RootToolsDir 'magisk') $magiskAsset
    if (-not (Test-Path -LiteralPath $magiskPath)) {
        throw "Local Root tools directory is missing '$magiskPath'."
    }

    $actualMagiskSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $magiskPath).Hash.ToLowerInvariant()
    if ($actualMagiskSha256 -ne $MagiskSha256.ToLowerInvariant()) {
        throw "Local Magisk APK SHA-256 is '$actualMagiskSha256', expected '$MagiskSha256'."
    }
}

Write-Host "Verified Magisk v$MagiskVersion asset '$magiskAsset'."
Write-Host "Verified payload-dumper-go $PayloadDumperVersion assets and upstream SHA-256 values."
