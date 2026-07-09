#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes AndroidTreeView App or Mini and creates an upload-ready ZIP package.

.DESCRIPTION
    This script does not build MSI files. It runs `dotnet publish` for the selected
    product/RID, stages the matching scrcpy bundle, writes a release.json manifest,
    creates a Windows portable ZIP or macOS .app bundle ZIP, and writes a matching
    .sha256 sidecar for upload.

.EXAMPLE
    ./build-update-zip.ps1 -Product App -Rid win-x64

.EXAMPLE
    ./build-update-zip.ps1 -Product Mini -Rid osx-arm64
#>
[CmdletBinding()]
param(
    [ValidateSet('App', 'Mini')]
    [string]$Product = 'App',

    [ValidateSet('x64', 'arm64')]
    [string]$Arch = 'x64',

    [string]$Rid = '',

    [string]$Configuration = 'Release',

    [string]$Version = '1.0.6'
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$artifacts = Join-Path $repoRoot 'artifacts'
$scrcpyVersion = '4.0'

function Assert-UnderDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Parent
    )

    $comparison = [System.StringComparison]::OrdinalIgnoreCase
    $resolvedParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $resolvedPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($resolvedParent, $comparison)) {
        throw "Refusing to operate outside '$resolvedParent': $resolvedPath"
    }
}

function Get-RidInfo {
    param(
        [string]$RequestedRid,
        [string]$RequestedArch
    )

    if ([string]::IsNullOrWhiteSpace($RequestedRid)) {
        $RequestedRid = "win-$RequestedArch"
    }

    $supported = @('win-x64', 'osx-arm64')
    if ($supported -notcontains $RequestedRid) {
        throw "RID '$RequestedRid' is not supported. Use one of: $($supported -join ', ')."
    }

    $parts = $RequestedRid -split '-', 2
    return [pscustomobject]@{
        Rid = $RequestedRid
        Platform = $parts[0]
        Arch = $parts[1]
        IsWindows = $parts[0] -eq 'win'
        IsMacOS = $parts[0] -eq 'osx'
        ScrcpyExecutable = if ($parts[0] -eq 'win') { 'scrcpy.exe' } else { 'scrcpy' }
        FastbootExecutable = if ($parts[0] -eq 'win') { 'fastboot.exe' } else { 'fastboot' }
    }
}

function Get-ProductConfig {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('App', 'Mini')]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [pscustomobject]$RidInfo
    )

    switch ($Name) {
        'Mini' {
            $project = if ($RidInfo.IsMacOS) {
                Join-Path $repoRoot 'src/AndroidTreeView.Mini.Mac/AndroidTreeView.Mini.Mac.csproj'
            } else {
                Join-Path $repoRoot 'src/AndroidTreeView.Mini/AndroidTreeView.Mini.csproj'
            }

            return [pscustomobject]@{
                Project = $project
                ProductName = 'AndroidTreeView Mini'
                AppKey = 'android-tree-view-mini'
                ArtifactName = 'AndroidTreeView-Mini'
                Executable = if ($RidInfo.IsWindows) { 'AndroidTreeView.App.mini.exe' } else { 'AndroidTreeView.App.mini' }
                BundleFastboot = $false
            }
        }
        default {
            return [pscustomobject]@{
                Project = Join-Path $repoRoot 'src/AndroidTreeView.App/AndroidTreeView.App.csproj'
                ProductName = 'AndroidTreeView'
                AppKey = 'android-tree-view-app'
                ArtifactName = 'AndroidTreeView'
                Executable = if ($RidInfo.IsWindows) { 'AndroidTreeView.App.exe' } else { 'AndroidTreeView.App' }
                BundleFastboot = $true
            }
        }
    }
}

function Invoke-Download {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,

        [Parameter(Mandatory = $true)]
        [string]$OutFile
    )

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutFile) | Out-Null
    Write-Host "==> Downloading $Uri" -ForegroundColor Cyan

    $params = @{
        Uri = $Uri
        OutFile = $OutFile
        Headers = @{
            'User-Agent' = 'AndroidTreeView-packaging'
        }
    }

    if ($PSVersionTable.PSVersion.Major -lt 6) {
        $params.UseBasicParsing = $true
    }

    Invoke-WebRequest @params
}

function Expand-ToolArchive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    Assert-UnderDirectory -Path $Destination -Parent $artifacts
    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -Recurse -Force -LiteralPath $Destination
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null

    if ($ArchivePath.EndsWith('.zip', [System.StringComparison]::OrdinalIgnoreCase)) {
        Expand-Archive -LiteralPath $ArchivePath -DestinationPath $Destination -Force
        return
    }

    & tar -xzf $ArchivePath -C $Destination
    if ($LASTEXITCODE -ne 0) {
        throw "tar failed to extract '$ArchivePath' with exit code $LASTEXITCODE."
    }
}

function Resolve-ExtractedToolRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExtractDir,

        [Parameter(Mandatory = $true)]
        [string]$ExecutableName
    )

    $direct = Join-Path $ExtractDir $ExecutableName
    if (Test-Path -LiteralPath $direct) {
        return $ExtractDir
    }

    foreach ($dir in Get-ChildItem -LiteralPath $ExtractDir -Directory) {
        $candidate = Join-Path $dir.FullName $ExecutableName
        if (Test-Path -LiteralPath $candidate) {
            return $dir.FullName
        }
    }

    throw "Could not find '$ExecutableName' in extracted archive '$ExtractDir'."
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse -Force
    }
}

function Set-ExecutableBits {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$RidInfo,

        [Parameter(Mandatory = $true)]
        [string]$Directory,

        [Parameter(Mandatory = $true)]
        [string[]]$Names
    )

    if ($RidInfo.IsWindows) {
        return
    }

    foreach ($name in $Names) {
        $path = Join-Path $Directory $name
        if (Test-Path -LiteralPath $path) {
            & chmod +x $path
            if ($LASTEXITCODE -ne 0) {
                throw "chmod +x failed for '$path'."
            }
        }
    }
}

function ConvertTo-PlistString {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    return [System.Security.SecurityElement]::Escape($Value)
}

function New-IcnsFromPng {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePng,

        [Parameter(Mandatory = $true)]
        [string]$OutputIcns
    )

    # macOS .app icons are .icns files; the repo only ships a 256x256 PNG, so build the .icns from it
    # with the system tools (sips resizes each slot, iconutil packs the iconset). Requires macOS.
    if (-not (Get-Command sips -ErrorAction SilentlyContinue) -or -not (Get-Command iconutil -ErrorAction SilentlyContinue)) {
        Write-Warning 'sips/iconutil not found; skipping macOS app icon generation.'
        return $false
    }

    $iconsetDir = "$OutputIcns.iconset"
    if (Test-Path -LiteralPath $iconsetDir) {
        Remove-Item -Recurse -Force -LiteralPath $iconsetDir
    }
    New-Item -ItemType Directory -Force -Path $iconsetDir | Out-Null

    # name => pixel size for the standard iconset slots (1x + @2x). Upscaled slots beyond the
    # 256px source are slightly soft but valid; Dock/Finder common sizes stay crisp.
    $slots = [ordered]@{
        'icon_16x16.png'      = 16
        'icon_16x16@2x.png'   = 32
        'icon_32x32.png'      = 32
        'icon_32x32@2x.png'   = 64
        'icon_128x128.png'    = 128
        'icon_128x128@2x.png' = 256
        'icon_256x256.png'    = 256
        'icon_256x256@2x.png' = 512
        'icon_512x512.png'    = 512
        'icon_512x512@2x.png' = 1024
    }

    foreach ($entry in $slots.GetEnumerator()) {
        $target = Join-Path $iconsetDir $entry.Key
        & sips -z $entry.Value $entry.Value $SourcePng --out $target *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "sips failed to render '$($entry.Key)' from '$SourcePng'."
        }
    }

    & iconutil -c icns $iconsetDir -o $OutputIcns
    if ($LASTEXITCODE -ne 0) {
        throw "iconutil failed to build '$OutputIcns'."
    }

    Remove-Item -Recurse -Force -LiteralPath $iconsetDir
    return $true
}

function New-MacOSAppBundle {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$ProductConfig,

        [Parameter(Mandatory = $true)]
        [pscustomobject]$RidInfo,

        [Parameter(Mandatory = $true)]
        [string]$SourceDir,

        [Parameter(Mandatory = $true)]
        [string]$BundleStageDir,

        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    if (-not $RidInfo.IsMacOS) {
        throw 'New-MacOSAppBundle can only be used for macOS RIDs.'
    }

    Assert-UnderDirectory -Path $BundleStageDir -Parent $artifacts
    if (Test-Path -LiteralPath $BundleStageDir) {
        Remove-Item -Recurse -Force -LiteralPath $BundleStageDir
    }

    $bundleName = if ($ProductConfig.ArtifactName -eq 'AndroidTreeView-Mini') {
        'AndroidTreeView Mini.app'
    } else {
        'AndroidTreeView.app'
    }

    $bundleRoot = Join-Path $BundleStageDir $bundleName
    $contentsDir = Join-Path $bundleRoot 'Contents'
    $macOSDir = Join-Path $contentsDir 'MacOS'
    $resourcesDir = Join-Path $contentsDir 'Resources'

    New-Item -ItemType Directory -Force -Path $macOSDir | Out-Null
    New-Item -ItemType Directory -Force -Path $resourcesDir | Out-Null

    Copy-DirectoryContents -Source $SourceDir -Destination $macOSDir

    $bundleExecutable = Join-Path $macOSDir $ProductConfig.Executable
    if (-not (Test-Path -LiteralPath $bundleExecutable)) {
        throw "macOS app bundle did not contain executable '$bundleExecutable'."
    }

    $manifestPath = Join-Path $macOSDir 'release.json'
    if (Test-Path -LiteralPath $manifestPath) {
        Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $resourcesDir 'release.json') -Force
    }

    $bundleIdentifier = "com.birditch.$($ProductConfig.AppKey.Replace('-', '.'))"
    $displayName = $ProductConfig.ProductName
    $executableName = $ProductConfig.Executable

    # Build the app icon (.icns) from the App's PNG so the bundle shows in Dock/Finder.
    $iconSourcePng = Join-Path $repoRoot 'src/AndroidTreeView.App/Assets/atv-icon.png'
    $iconFileName = ''
    if (Test-Path -LiteralPath $iconSourcePng) {
        $icnsPath = Join-Path $resourcesDir 'AppIcon.icns'
        if (New-IcnsFromPng -SourcePng $iconSourcePng -OutputIcns $icnsPath) {
            $iconFileName = 'AppIcon'
        }
    } else {
        Write-Warning "Icon source '$iconSourcePng' not found; bundle will have no icon."
    }

    $iconPlistEntry = if ($iconFileName) {
        "  <key>CFBundleIconFile</key>`n  <string>$(ConvertTo-PlistString $iconFileName)</string>`n"
    } else {
        ''
    }

    $infoPlist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleDisplayName</key>
  <string>$(ConvertTo-PlistString $displayName)</string>
  <key>CFBundleExecutable</key>
  <string>$(ConvertTo-PlistString $executableName)</string>
$iconPlistEntry  <key>CFBundleIdentifier</key>
  <string>$(ConvertTo-PlistString $bundleIdentifier)</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>$(ConvertTo-PlistString $displayName)</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>$(ConvertTo-PlistString $Version)</string>
  <key>CFBundleVersion</key>
  <string>$(ConvertTo-PlistString $Version)</string>
  <key>LSMinimumSystemVersion</key>
  <string>11.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
"@

    Set-Content -Path (Join-Path $contentsDir 'Info.plist') -Value $infoPlist -Encoding utf8

    Set-ExecutableBits -RidInfo $RidInfo -Directory $macOSDir -Names @($ProductConfig.Executable)
    Set-ExecutableBits -RidInfo $RidInfo -Directory (Join-Path $macOSDir 'scrcpy') -Names @('scrcpy', 'adb', 'fastboot')

    return $BundleStageDir
}

function Ensure-ScrcpyBundle {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$RidInfo
    )

    $scrcpyRoot = Join-Path (Join-Path (Join-Path $artifacts 'tools') 'scrcpy') $RidInfo.Rid
    $scrcpyProbe = Join-Path $scrcpyRoot $RidInfo.ScrcpyExecutable
    if (Test-Path -LiteralPath $scrcpyProbe) {
        return $scrcpyRoot
    }

    $assetName = switch ($RidInfo.Rid) {
        'win-x64' { "scrcpy-win64-v$scrcpyVersion.zip" }
        'osx-arm64' { "scrcpy-macos-aarch64-v$scrcpyVersion.tar.gz" }
        default { throw "No scrcpy asset is mapped for RID '$($RidInfo.Rid)'." }
    }

    $downloadDir = Join-Path (Join-Path (Join-Path $artifacts 'downloads') 'scrcpy') $RidInfo.Rid
    $archivePath = Join-Path $downloadDir $assetName
    $extractDir = Join-Path $downloadDir 'extract'
    $url = "https://github.com/Genymobile/scrcpy/releases/download/v$scrcpyVersion/$assetName"

    if (-not (Test-Path -LiteralPath $archivePath)) {
        Invoke-Download -Uri $url -OutFile $archivePath
    }

    Expand-ToolArchive -ArchivePath $archivePath -Destination $extractDir
    $toolRoot = Resolve-ExtractedToolRoot -ExtractDir $extractDir -ExecutableName $RidInfo.ScrcpyExecutable

    Assert-UnderDirectory -Path $scrcpyRoot -Parent $artifacts
    if (Test-Path -LiteralPath $scrcpyRoot) {
        Remove-Item -Recurse -Force -LiteralPath $scrcpyRoot
    }

    Copy-DirectoryContents -Source $toolRoot -Destination $scrcpyRoot
    Set-ExecutableBits -RidInfo $RidInfo -Directory $scrcpyRoot -Names @('scrcpy', 'adb', 'fastboot')

    if (-not (Test-Path -LiteralPath $scrcpyProbe)) {
        throw "scrcpy bundle for '$($RidInfo.Rid)' did not produce '$scrcpyProbe'."
    }

    return $scrcpyRoot
}

function Ensure-Fastboot {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$RidInfo,

        [Parameter(Mandatory = $true)]
        [string]$ScrcpyRoot
    )

    $fastbootPath = Join-Path $ScrcpyRoot $RidInfo.FastbootExecutable
    if (Test-Path -LiteralPath $fastbootPath) {
        return
    }

    $assetName = switch ($RidInfo.Rid) {
        'win-x64' { 'platform-tools-latest-windows.zip' }
        'osx-arm64' { 'platform-tools-latest-darwin.zip' }
        default { throw "No platform-tools asset is mapped for RID '$($RidInfo.Rid)'." }
    }

    $downloadDir = Join-Path (Join-Path (Join-Path $artifacts 'downloads') 'platform-tools') $RidInfo.Rid
    $archivePath = Join-Path $downloadDir $assetName
    $extractDir = Join-Path $downloadDir 'extract'
    $url = "https://dl.google.com/android/repository/$assetName"

    if (-not (Test-Path -LiteralPath $archivePath)) {
        Invoke-Download -Uri $url -OutFile $archivePath
    }

    Expand-ToolArchive -ArchivePath $archivePath -Destination $extractDir

    $platformTools = Join-Path $extractDir 'platform-tools'
    $sourceFastboot = Join-Path $platformTools $RidInfo.FastbootExecutable
    if (-not (Test-Path -LiteralPath $sourceFastboot)) {
        throw "platform-tools did not include '$($RidInfo.FastbootExecutable)'."
    }

    Copy-Item -LiteralPath $sourceFastboot -Destination $fastbootPath -Force

    if ($RidInfo.IsWindows) {
        $winPthread = Join-Path $platformTools 'libwinpthread-1.dll'
        if (Test-Path -LiteralPath $winPthread) {
            Copy-Item -LiteralPath $winPthread -Destination (Join-Path $ScrcpyRoot 'libwinpthread-1.dll') -Force
        }
    } else {
        Set-ExecutableBits -RidInfo $RidInfo -Directory $ScrcpyRoot -Names @($RidInfo.FastbootExecutable)
    }
}

function New-PackageArchive {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$RidInfo,

        [Parameter(Mandatory = $true)]
        [string]$SourceDir,

        [Parameter(Mandatory = $true)]
        [string]$ZipPath
    )

    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -Force -LiteralPath $ZipPath
    }

    if ($RidInfo.IsMacOS) {
        $zipFullPath = [System.IO.Path]::GetFullPath($ZipPath)
        Push-Location $SourceDir
        try {
            & zip -qry $zipFullPath .
            if ($LASTEXITCODE -ne 0) {
                throw "zip failed with exit code $LASTEXITCODE."
            }
        }
        finally {
            Pop-Location
        }

        return
    }

    Compress-Archive -Path (Join-Path $SourceDir '*') -DestinationPath $ZipPath -CompressionLevel Optimal -Force
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK (dotnet) was not found on PATH. Install .NET 10 SDK first.'
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version '$Version' must be major.minor.patch."
}

$ridInfo = Get-RidInfo -RequestedRid $Rid -RequestedArch $Arch
$productConfig = Get-ProductConfig -Name $Product -RidInfo $ridInfo
$baseName = "$($productConfig.ArtifactName)-$Version-$($ridInfo.Rid)"
$publishDir = Join-Path (Join-Path (Join-Path $artifacts 'publish') $Product) $ridInfo.Rid
$packageStageDir = Join-Path (Join-Path (Join-Path $artifacts 'package') $Product) $ridInfo.Rid
$zipPath = Join-Path $artifacts "$baseName.zip"
$zipChecksumPath = "$zipPath.sha256"
$packageKind = if ($ridInfo.IsWindows) { 'portable-x64' } else { "portable-$($ridInfo.Rid)" }
$bundleFastbootValue = if ($productConfig.BundleFastboot) { 'true' } else { 'false' }
# macOS has no system-level "install .NET runtime" prompt like Windows, so a framework-dependent
# .app silently fails to launch on machines without the runtime. Bundle the runtime into the macOS
# .app (self-contained). Windows stays framework-dependent: its apphost shows the OS download prompt.
$selfContainedValue = if ($ridInfo.IsMacOS) { 'true' } else { 'false' }

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
Assert-UnderDirectory -Path $publishDir -Parent $artifacts
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -Recurse -Force -LiteralPath $publishDir
}
Assert-UnderDirectory -Path $packageStageDir -Parent $artifacts
if (Test-Path -LiteralPath $packageStageDir) {
    Remove-Item -Recurse -Force -LiteralPath $packageStageDir
}

$scrcpyDir = Ensure-ScrcpyBundle -RidInfo $ridInfo
if ($productConfig.BundleFastboot) {
    Ensure-Fastboot -RidInfo $ridInfo -ScrcpyRoot $scrcpyDir
}

Write-Host "==> Publishing $($productConfig.ProductName) ($($ridInfo.Rid))..." -ForegroundColor Cyan
$publishArgs = @(
    'publish',
    $productConfig.Project,
    '--configuration', $Configuration,
    '--runtime', $ridInfo.Rid,
    '--self-contained', $selfContainedValue,
    "-p:Version=$Version",
    "-p:AssemblyVersion=$Version.0",
    "-p:FileVersion=$Version.0",
    "-p:InformationalVersion=$Version",
    "-p:ScrcpyVersion=$scrcpyVersion",
    "-p:ScrcpyDir=$scrcpyDir",
    "-p:ScrcpyExecutableName=$($ridInfo.ScrcpyExecutable)",
    "-p:FastbootExecutableName=$($ridInfo.FastbootExecutable)",
    "-p:AndroidTreeViewBundleFastboot=$bundleFastbootValue",
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '--output', $publishDir
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedScrcpyDir = Join-Path $publishDir 'scrcpy'
Assert-UnderDirectory -Path $publishedScrcpyDir -Parent $publishDir
if (Test-Path -LiteralPath $publishedScrcpyDir) {
    Remove-Item -Recurse -Force -LiteralPath $publishedScrcpyDir
}

Copy-DirectoryContents -Source $scrcpyDir -Destination $publishedScrcpyDir
if (-not $productConfig.BundleFastboot) {
    $publishedFastboot = Join-Path $publishedScrcpyDir $ridInfo.FastbootExecutable
    if (Test-Path -LiteralPath $publishedFastboot) {
        Remove-Item -Force -LiteralPath $publishedFastboot
    }
}

Set-ExecutableBits -RidInfo $ridInfo -Directory $publishDir -Names @($productConfig.Executable)
Set-ExecutableBits -RidInfo $ridInfo -Directory $publishedScrcpyDir -Names @('scrcpy', 'adb', 'fastboot')

$manifest = [ordered]@{
    packageKind = $packageKind
    product = $Product
    productName = $productConfig.ProductName
    appKey = $productConfig.AppKey
    version = $Version
    platform = $ridInfo.Platform
    arch = $ridInfo.Arch
    rid = $ridInfo.Rid
    executable = $productConfig.Executable
}

$manifestJson = $manifest | ConvertTo-Json -Depth 3
Set-Content -Path (Join-Path $publishDir 'release.json') -Value $manifestJson -Encoding utf8

$archiveSourceDir = $publishDir
if ($ridInfo.IsMacOS) {
    Write-Host "==> Creating macOS .app bundle..." -ForegroundColor Cyan
    $archiveSourceDir = New-MacOSAppBundle `
        -ProductConfig $productConfig `
        -RidInfo $ridInfo `
        -SourceDir $publishDir `
        -BundleStageDir $packageStageDir `
        -Version $Version
}

Write-Host "==> Creating upload ZIP $baseName.zip..." -ForegroundColor Cyan
New-PackageArchive -RidInfo $ridInfo -SourceDir $archiveSourceDir -ZipPath $zipPath

$zipSha256 = (Get-FileHash -Algorithm SHA256 -Path $zipPath).Hash.ToLowerInvariant()
Set-Content -Path $zipChecksumPath -Value "$zipSha256 *$baseName.zip" -NoNewline -Encoding ascii

Assert-UnderDirectory -Path $publishDir -Parent $artifacts
Remove-Item -Recurse -Force -LiteralPath $publishDir

if ($ridInfo.IsMacOS -and (Test-Path -LiteralPath $packageStageDir)) {
    Assert-UnderDirectory -Path $packageStageDir -Parent $artifacts
    Remove-Item -Recurse -Force -LiteralPath $packageStageDir
}

$packageProductDir = Split-Path -Parent $packageStageDir
if ((Test-Path -LiteralPath $packageProductDir) -and -not (Get-ChildItem -LiteralPath $packageProductDir -Force)) {
    Remove-Item -Force -LiteralPath $packageProductDir
}

$publishProductDir = Split-Path -Parent $publishDir
if ((Test-Path -LiteralPath $publishProductDir) -and -not (Get-ChildItem -LiteralPath $publishProductDir -Force)) {
    Remove-Item -Force -LiteralPath $publishProductDir
}

$publishRoot = Join-Path $artifacts 'publish'
if ((Test-Path -LiteralPath $publishRoot) -and -not (Get-ChildItem -LiteralPath $publishRoot -Force)) {
    Remove-Item -Force -LiteralPath $publishRoot
}

$packageRoot = Join-Path $artifacts 'package'
if ((Test-Path -LiteralPath $packageRoot) -and -not (Get-ChildItem -LiteralPath $packageRoot -Force)) {
    Remove-Item -Force -LiteralPath $packageRoot
}

Write-Host ''
Write-Host "==> ZIP:      $zipPath" -ForegroundColor Green
Write-Host "==> SHA256:   $zipSha256" -ForegroundColor Green
Write-Host "==> Checksum: $zipChecksumPath" -ForegroundColor Green

$zipPath
