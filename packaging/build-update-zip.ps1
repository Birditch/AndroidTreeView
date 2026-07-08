#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes AndroidTreeView App or Mini and creates an upload-ready x64 ZIP package.

.DESCRIPTION
    This script does not build MSI files. It runs `dotnet publish` for the selected
    x64 product, writes a release.json manifest into the publish folder, compresses
    that folder into artifacts/<product>-<version>-x64.zip, and writes a matching
    .sha256 sidecar for upload.

.EXAMPLE
    ./build-update-zip.ps1 -Product App -Arch x64

.EXAMPLE
    ./build-update-zip.ps1 -Product Mini -Arch x64
#>
[CmdletBinding()]
param(
    [ValidateSet('App', 'Mini')]
    [string]$Product = 'App',

    [ValidateSet('x64')]
    [string]$Arch = 'x64',

    [string]$Configuration = 'Release',

    [string]$Version = '1.0.4'
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$artifacts = Join-Path $repoRoot 'artifacts'
$rid = "win-$Arch"

function Get-ProductConfig {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('App', 'Mini')]
        [string]$Name
    )

    switch ($Name) {
        'Mini' {
            return [pscustomobject]@{
                Project = Join-Path $repoRoot 'src/AndroidTreeView.Mini/AndroidTreeView.Mini.csproj'
                ProductName = 'AndroidTreeView Mini'
                AppKey = 'android-tree-view-mini'
                ArtifactName = 'AndroidTreeView-Mini'
                Executable = 'AndroidTreeView.App.mini.exe'
            }
        }
        default {
            return [pscustomobject]@{
                Project = Join-Path $repoRoot 'src/AndroidTreeView.App/AndroidTreeView.App.csproj'
                ProductName = 'AndroidTreeView'
                AppKey = 'android-tree-view-app'
                ArtifactName = 'AndroidTreeView'
                Executable = 'AndroidTreeView.App.exe'
            }
        }
    }
}

function Assert-UnderDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Parent
    )

    $resolvedParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $resolvedPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($resolvedParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside '$resolvedParent': $resolvedPath"
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK (dotnet) was not found on PATH. Install .NET 10 SDK first.'
}

$productConfig = Get-ProductConfig -Name $Product
$baseName = "$($productConfig.ArtifactName)-$Version-$Arch"
$publishDir = Join-Path $artifacts ("publish/{0}/{1}" -f $Product, $Arch)
$zipPath = Join-Path $artifacts "$baseName.zip"
$zipChecksumPath = "$zipPath.sha256"

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
Assert-UnderDirectory -Path $publishDir -Parent $artifacts
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force -LiteralPath $publishDir
}

Write-Host "==> Publishing $($productConfig.ProductName) ($rid)..." -ForegroundColor Cyan
$publishArgs = @(
    'publish',
    $productConfig.Project,
    '--configuration', $Configuration,
    '--runtime', $rid,
    '--self-contained', 'false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '--output', $publishDir
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$manifest = [ordered]@{
    packageKind = 'portable-x64'
    product = $Product
    productName = $productConfig.ProductName
    appKey = $productConfig.AppKey
    version = $Version
    arch = $Arch
    executable = $productConfig.Executable
}

$manifestJson = $manifest | ConvertTo-Json -Depth 3
Set-Content -Path (Join-Path $publishDir 'release.json') -Value $manifestJson -Encoding utf8

if (Test-Path $zipPath) {
    Remove-Item -Force -LiteralPath $zipPath
}

Write-Host "==> Creating upload ZIP $baseName.zip..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force

$zipSha256 = (Get-FileHash -Algorithm SHA256 -Path $zipPath).Hash.ToLowerInvariant()
Set-Content -Path $zipChecksumPath -Value "$zipSha256 *$baseName.zip" -NoNewline -Encoding ascii

Assert-UnderDirectory -Path $publishDir -Parent $artifacts
Remove-Item -Recurse -Force -LiteralPath $publishDir

$publishProductDir = Split-Path -Parent $publishDir
if ((Test-Path $publishProductDir) -and -not (Get-ChildItem -LiteralPath $publishProductDir -Force)) {
    Remove-Item -Force -LiteralPath $publishProductDir
}

$publishRoot = Join-Path $artifacts 'publish'
if ((Test-Path $publishRoot) -and -not (Get-ChildItem -LiteralPath $publishRoot -Force)) {
    Remove-Item -Force -LiteralPath $publishRoot
}

Write-Host ''
Write-Host "==> ZIP:      $zipPath" -ForegroundColor Green
Write-Host "==> SHA256:   $zipSha256" -ForegroundColor Green
Write-Host "==> Checksum: $zipChecksumPath" -ForegroundColor Green

$zipPath
