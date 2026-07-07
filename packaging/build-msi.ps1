#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes AndroidTreeView.App and packages it into a Windows MSI with WiX v5.

.DESCRIPTION
    1. Runs `dotnet publish` on src/AndroidTreeView.App for the requested architecture
       (win-x64 or win-x86), framework-dependent by default or self-contained with
       -SelfContained.
    2. Builds packaging/AndroidTreeView.Package.wixproj against that publish output.
    3. Copies the result to artifacts/AndroidTreeView-<version>-<arch>.msi, writes a
       matching .sha256 checksum, and prints the final MSI path.

    Requires the .NET SDK on PATH. The WiX v5 SDK is restored automatically from nuget.org
    by the wixproj (no global `wix` tool required for the MSI itself).

.PARAMETER Arch
    Target architecture: x64 (default) or x86.

.PARAMETER SelfContained
    Publish a self-contained app (bundles the .NET runtime). Produces a larger MSI that has
    no .NET runtime prerequisite. Omit for a smaller framework-dependent MSI.

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER Version
    Product version. Default: 1.0.0. Must match AndroidTreeView.Core.AppInfo.Version.

.EXAMPLE
    ./build-msi.ps1 -Arch x64
    Framework-dependent x64 MSI.

.EXAMPLE
    ./build-msi.ps1 -Arch x86 -SelfContained
    Self-contained x86 MSI (no runtime prerequisite).
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86')]
    [string]$Arch = 'x64',

    [switch]$SelfContained,

    [string]$Configuration = 'Release',

    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'

$scriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot   = Split-Path -Parent $scriptDir
$appProject = Join-Path $repoRoot 'src/AndroidTreeView.App/AndroidTreeView.App.csproj'
$wixProject = Join-Path $scriptDir 'AndroidTreeView.Package.wixproj'
$artifacts  = Join-Path $repoRoot 'artifacts'
$publishDir = Join-Path $artifacts "publish/$Arch"
$rid        = "win-$Arch"
$scValue    = if ($SelfContained.IsPresent) { 'true' } else { 'false' }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK (dotnet) was not found on PATH. Install .NET 10 SDK first.'
}

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

Write-Host "==> Publishing AndroidTreeView.App ($rid, self-contained=$scValue)..." -ForegroundColor Cyan
dotnet publish $appProject `
    --configuration $Configuration `
    --runtime $rid `
    --self-contained $scValue `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

# WiX preprocessor expects PublishDir with a trailing directory separator.
$publishDirSlash = (Resolve-Path $publishDir).Path.TrimEnd('\', '/') + '\'

Write-Host "==> Building MSI for $Arch (version $Version)..." -ForegroundColor Cyan
dotnet build $wixProject `
    --configuration $Configuration `
    -p:Platform=$Arch `
    -p:ProductVersion=$Version `
    -p:SelfContained=$scValue `
    "-p:PublishDir=$publishDirSlash"
if ($LASTEXITCODE -ne 0) { throw "WiX build failed with exit code $LASTEXITCODE." }

$msiName = "AndroidTreeView-$Version-$Arch.msi"
$built = Get-ChildItem -Path (Join-Path $scriptDir 'bin') -Recurse -Filter $msiName -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $built) { throw "Expected MSI '$msiName' was not found under packaging/bin." }

$destMsi = Join-Path $artifacts $msiName
Copy-Item -Path $built.FullName -Destination $destMsi -Force

$sha256 = (Get-FileHash -Algorithm SHA256 -Path $destMsi).Hash.ToLowerInvariant()
$checksumFile = "$destMsi.sha256"
# Format matches `sha256sum -c` / `Get-FileHash` verification workflows.
Set-Content -Path $checksumFile -Value "$sha256 *$msiName" -NoNewline -Encoding ascii

Write-Host ''
Write-Host "==> MSI:      $destMsi" -ForegroundColor Green
Write-Host "==> SHA256:   $sha256" -ForegroundColor Green
Write-Host "==> Checksum: $checksumFile" -ForegroundColor Green

# Emit the MSI path as the script's object output for pipeline consumption.
$destMsi
