#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes AndroidTreeView App or Mini and packages it into a Windows MSI with WiX v5.

.DESCRIPTION
    1. Runs `dotnet publish` on the selected product for win-x64, framework-dependent
       by default or self-contained with -SelfContained.
    2. Builds packaging/AndroidTreeView.Package.wixproj against that publish output.
    3. Copies the result to artifacts/<product>-<version>-x64.msi, writes a matching
       .sha256 checksum, and prints the final MSI path.

    Requires the .NET SDK on PATH. The WiX v5 SDK is restored automatically from nuget.org
    by the wixproj (no global `wix` tool required for the MSI itself).

.PARAMETER Product
    Product to package: App (default) or Mini.

.PARAMETER Arch
    Target architecture. Only x64 is accepted.

.PARAMETER SelfContained
    Publish a self-contained app (bundles the .NET runtime). Produces a larger MSI that has
    no .NET runtime prerequisite. Omit for a smaller framework-dependent MSI.

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER Version
    Product version. Default: 1.0.6. Must match AndroidTreeView.Core.AppInfo.Version.

.EXAMPLE
    ./build-msi.ps1 -Product App -Arch x64
    Framework-dependent x64 MSI for the full app.

.EXAMPLE
    ./build-msi.ps1 -Product Mini -Arch x64 -SelfContained
    Self-contained x64 MSI for Mini.
#>
[CmdletBinding()]
param(
    [ValidateSet('App', 'Mini')]
    [string]$Product = 'App',

    [ValidateSet('x64')]
    [string]$Arch = 'x64',

    [switch]$SelfContained,

    [string]$Configuration = 'Release',

    [string]$Version = '1.0.6'
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$wixProject = Join-Path $scriptDir 'AndroidTreeView.Package.wixproj'
$artifacts = Join-Path $repoRoot 'artifacts'
$rid = "win-$Arch"
$scValue = if ($SelfContained.IsPresent) { 'true' } else { 'false' }

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
                Executable = 'AndroidTreeView.App.mini.exe'
                ArtifactName = 'AndroidTreeView-Mini'
                InstallFolder = 'AndroidTreeView Mini'
                RegistryKey = 'AndroidTreeViewMini'
                UpgradeCode = '{9F19BD66-7F4B-4E1B-8D0C-506C5C18A1D2}'
                Description = 'Auto-mirror Android devices over ADB'
            }
        }
        default {
            return [pscustomobject]@{
                Project = Join-Path $repoRoot 'src/AndroidTreeView.App/AndroidTreeView.App.csproj'
                ProductName = 'AndroidTreeView'
                Executable = 'AndroidTreeView.App.exe'
                ArtifactName = 'AndroidTreeView'
                InstallFolder = 'AndroidTreeView'
                RegistryKey = 'AndroidTreeView'
                UpgradeCode = '{6C0B8E2A-4F1D-4B7C-9A3E-2D5F8B1C0E4A}'
                Description = 'Explore Android devices over ADB'
            }
        }
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK (dotnet) was not found on PATH. Install .NET 10 SDK first.'
}

$productConfig = Get-ProductConfig -Name $Product
$publishDir = Join-Path $artifacts ("publish/{0}/{1}" -f $Product, $Arch)

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

Write-Host "==> Publishing $($productConfig.ProductName) ($rid, self-contained=$scValue)..." -ForegroundColor Cyan
$publishArgs = @(
    'publish',
    $productConfig.Project,
    '--configuration', $Configuration,
    '--runtime', $rid,
    '--self-contained', $scValue,
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '--output', $publishDir
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

# WiX preprocessor expects PublishDir with a trailing directory separator.
$publishDirSlash = (Resolve-Path $publishDir).Path.TrimEnd('\', '/') + '\'

Write-Host "==> Building $($productConfig.ProductName) MSI for $Arch (version $Version)..." -ForegroundColor Cyan
$wixArgs = @(
    'build',
    $wixProject,
    '--configuration', $Configuration,
    "-p:Platform=$Arch",
    "-p:ProductVersion=$Version",
    "-p:ProductName=$($productConfig.ProductName)",
    "-p:ProductExecutable=$($productConfig.Executable)",
    "-p:ProductUpgradeCode=$($productConfig.UpgradeCode)",
    "-p:ProductInstallFolder=$($productConfig.InstallFolder)",
    "-p:ProductRegistryKey=$($productConfig.RegistryKey)",
    "-p:ProductArtifactName=$($productConfig.ArtifactName)",
    "-p:ProductDescription=$($productConfig.Description)",
    "-p:SelfContained=$scValue",
    "-p:PublishDir=$publishDirSlash"
)
& dotnet @wixArgs
if ($LASTEXITCODE -ne 0) { throw "WiX build failed with exit code $LASTEXITCODE." }

$msiName = "$($productConfig.ArtifactName)-$Version-$Arch.msi"
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
