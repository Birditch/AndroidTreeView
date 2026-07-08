using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Models;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Infrastructure.Update;

/// <summary>
/// End-to-end update installer: downloads the package, verifies the checksum when one is available,
/// unwraps ZIP packages, then starts the automated update flow.
/// </summary>
public sealed class UpdateInstaller : IUpdateInstaller
{
    private const string DefaultPackageExtension = ".zip";
    private const string ReleaseManifestFileName = "release.json";
    private const string PortablePackageKind = "portable-x64";
    private const string PackageKindProperty = "packageKind";
    private const string AppKeyProperty = "appKey";
    private const string VersionProperty = "version";
    private const string ArchProperty = "arch";
    private const string ExecutableProperty = "executable";
    private static readonly string[] InstallerExtensions = [".msi", ".exe"];

    private readonly HttpClient _httpClient;
    private readonly UpdateProductOptions _product;
    private readonly IInstallerLauncher _launcher;
    private readonly ILogger<UpdateInstaller> _logger;
    private readonly string? _stagingRoot;

    public UpdateInstaller(
        HttpClient httpClient,
        UpdateProductOptions product,
        ILogger<UpdateInstaller> logger)
        : this(httpClient, product, new ProcessInstallerLauncher(), logger)
    {
    }

    internal UpdateInstaller(
        HttpClient httpClient,
        UpdateProductOptions product,
        IInstallerLauncher launcher,
        ILogger<UpdateInstaller> logger,
        string? stagingRoot = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _product = product;
        _launcher = launcher;
        _logger = logger;
        _stagingRoot = stagingRoot;
    }

    public async Task<UpdateInstallResult> InstallAsync(UpdateCheckResult update, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (!update.UpdateAvailable)
        {
            return UpdateInstallResult.Error(UpdateInstallStatus.NoUpdateAvailable);
        }

        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            return UpdateInstallResult.Error(UpdateInstallStatus.MissingDownloadUrl);
        }

        try
        {
            var stageDir = CreateStageDirectory(update.LatestVersion ?? update.CurrentVersion);
            Directory.CreateDirectory(stageDir);

            var packagePath = await DownloadPackageAsync(update, stageDir, ct).ConfigureAwait(false);
            var checksumOk = await VerifyChecksumAsync(update, packagePath, ct).ConfigureAwait(false);
            if (!checksumOk)
            {
                return new UpdateInstallResult
                {
                    Status = UpdateInstallStatus.InvalidChecksum,
                    PackagePath = packagePath,
                    ErrorMessage = "The downloaded update package did not match its SHA-256 checksum.",
                };
            }

            var installerPath = PrepareInstaller(packagePath, stageDir, update.LatestVersion);
            if (installerPath is null)
            {
                return new UpdateInstallResult
                {
                    Status = UpdateInstallStatus.UnsupportedPackage,
                    PackagePath = packagePath,
                    ErrorMessage = "The update package did not contain a supported x64 release package or installer.",
                };
            }

            if (!_launcher.Launch(installerPath, out var launchError))
            {
                return new UpdateInstallResult
                {
                    Status = UpdateInstallStatus.InstallerLaunchFailed,
                    PackagePath = packagePath,
                    InstallerPath = installerPath,
                    ErrorMessage = launchError,
                };
            }

            return new UpdateInstallResult
            {
                Status = UpdateInstallStatus.Started,
                PackagePath = packagePath,
                InstallerPath = installerPath,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update install failed for {AppKey}.", _product.AppKey);
            return UpdateInstallResult.Error(UpdateInstallStatus.DownloadFailed, ex.Message);
        }
    }

    private async Task<string> DownloadPackageAsync(
        UpdateCheckResult update,
        string stageDir,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var fileName = ResolvePackageFileName(update, response.Content.Headers.ContentDisposition);
        var packagePath = Path.Combine(stageDir, fileName);

        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = File.Create(packagePath);
        await input.CopyToAsync(output, ct).ConfigureAwait(false);

        return packagePath;
    }

    private async Task<bool> VerifyChecksumAsync(UpdateCheckResult update, string packagePath, CancellationToken ct)
    {
        var expected = NormalizeSha256(update.Sha256);
        if (expected is null && !string.IsNullOrWhiteSpace(update.Sha256Url))
        {
            var checksumText = await _httpClient.GetStringAsync(update.Sha256Url, ct).ConfigureAwait(false);
            expected = NormalizeSha256(checksumText);
        }

        if (expected is null)
        {
            return true;
        }

        await using var stream = File.OpenRead(packagePath);
        var actualBytes = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        var actual = Convert.ToHexString(actualBytes).ToLowerInvariant();
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private string? PrepareInstaller(string packagePath, string stageDir, string? expectedVersion)
    {
        var extension = Path.GetExtension(packagePath);
        if (IsInstallerExtension(extension))
        {
            return packagePath;
        }

        if (!string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var extractDir = Path.Combine(stageDir, "extracted");
        if (Directory.Exists(extractDir))
        {
            Directory.Delete(extractDir, recursive: true);
        }

        ZipFile.ExtractToDirectory(packagePath, extractDir);
        return TryCreatePortableUpdateScript(extractDir, stageDir, expectedVersion);
    }

    private string? TryCreatePortableUpdateScript(string extractDir, string stageDir, string? expectedVersion)
    {
        var manifestPath = Path.Combine(extractDir, ReleaseManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            var packageKind = ReadManifestString(root, PackageKindProperty);
            if (!string.Equals(packageKind, PortablePackageKind, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var appKey = ReadManifestString(root, AppKeyProperty);
            if (!string.Equals(appKey, _product.AppKey, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Portable update appKey '{PackageAppKey}' did not match configured appKey '{ConfiguredAppKey}'.",
                    appKey,
                    _product.AppKey);
                return null;
            }

            var arch = ReadManifestString(root, ArchProperty);
            if (!string.Equals(arch, "x64", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Portable update architecture '{Arch}' is not accepted.", arch);
                return null;
            }

            var version = ReadManifestString(root, VersionProperty);
            if (!string.IsNullOrWhiteSpace(expectedVersion)
                && !VersionsMatch(version, expectedVersion))
            {
                _logger.LogWarning(
                    "Portable update version '{PackageVersion}' did not match expected version '{ExpectedVersion}'.",
                    version,
                    expectedVersion);
                return null;
            }

            var executable = ReadManifestString(root, ExecutableProperty);
            if (string.IsNullOrWhiteSpace(executable))
            {
                _logger.LogWarning("Portable update manifest did not include an executable.");
                return null;
            }

            var executablePath = ResolveExtractedRelativePath(extractDir, executable);
            if (executablePath is null || !File.Exists(executablePath))
            {
                _logger.LogWarning("Portable update executable '{Executable}' was not found in the package.", executable);
                return null;
            }

            return CreatePortableUpdateScripts(stageDir, extractDir, executable);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Portable update manifest was invalid JSON.");
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Portable update manifest could not be read.");
            return null;
        }
    }

    private string CreatePortableUpdateScripts(string stageDir, string sourceDir, string executable)
    {
        var scriptPath = Path.Combine(stageDir, "apply-update.ps1");
        var commandPath = Path.Combine(stageDir, "apply-update.cmd");
        var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var processId = Environment.ProcessId;

        var script = $$"""
$ErrorActionPreference = 'Stop'

$processId = {{processId}}
$sourceDir = @'
{{sourceDir}}
'@
$installDir = @'
{{installDir}}
'@
$executable = @'
{{executable}}
'@

function Test-DirectoryWritable {
    param([Parameter(Mandatory = $true)][string]$Path)

    try {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
        $probe = Join-Path $Path ('.atv-update-write-test-' + [Guid]::NewGuid().ToString('N'))
        Set-Content -Path $probe -Value 'ok' -Encoding ascii
        Remove-Item -Force -Path $probe
        return $true
    } catch {
        return $false
    }
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $pathFull = [System.IO.Path]::GetFullPath($Path)

    if (-not $pathFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }

    return $pathFull.Substring($rootFull.Length).Replace('\', '/')
}

function Test-PreservedConfigPath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $fileName = [System.IO.Path]::GetFileName($RelativePath)
    $extension = [System.IO.Path]::GetExtension($fileName).ToLowerInvariant()

    if ($fileName -in @('.env', 'settings.json', 'appsettings.json')) {
        return $true
    }

    if ($fileName -like 'appsettings.*.json' -or
        $fileName -like '*.local.json' -or
        $fileName -like '*.user') {
        return $true
    }

    return $extension -in @('.config', '.ini', '.json', '.yaml', '.yml', '.toml')
}

function Remove-ExtraneousNonConfigFiles {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $files = Get-ChildItem -LiteralPath $Destination -Recurse -File -Force
    foreach ($file in $files) {
        $relative = Get-RelativePath -Root $Destination -Path $file.FullName
        if ($null -eq $relative) {
            continue
        }

        $relativeNative = $relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $sourceFile = Join-Path $Source $relativeNative
        if (Test-Path -LiteralPath $sourceFile -PathType Leaf) {
            continue
        }

        if (Test-PreservedConfigPath $relative) {
            continue
        }

        Remove-Item -LiteralPath $file.FullName -Force
    }

    $directories = Get-ChildItem -LiteralPath $Destination -Recurse -Directory -Force |
        Sort-Object FullName -Descending
    foreach ($directory in $directories) {
        if (-not (Get-ChildItem -LiteralPath $directory.FullName -Force)) {
            Remove-Item -LiteralPath $directory.FullName -Force
        }
    }
}

if (-not (Test-DirectoryWritable $installDir)) {
    $quotedScript = '"' + $PSCommandPath.Replace('"', '\"') + '"'
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File $quotedScript"
    exit
}

Start-Sleep -Seconds 2

try {
    Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
} catch {
}

try {
    Wait-Process -Id $processId -Timeout 60 -ErrorAction SilentlyContinue
} catch {
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null

& robocopy.exe $sourceDir $installDir /E /NFL /NDL /NJH /NJS /NC /NS /NP
$robocopyExit = $LASTEXITCODE
if ($robocopyExit -ge 8) {
    exit $robocopyExit
}

Remove-ExtraneousNonConfigFiles -Source $sourceDir -Destination $installDir

Start-Process -FilePath (Join-Path $installDir $executable)
""";

        var command = """
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0apply-update.ps1"
""";

        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(commandPath, command, Encoding.ASCII);
        return commandPath;
    }

    private string CreateStageDirectory(string version)
    {
        var root = _stagingRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.GetTempPath();
        }

        return Path.Combine(root, AppFolderName(_product.Name), "Updates", SafeSegment(version));
    }

    private string ResolvePackageFileName(UpdateCheckResult update, ContentDispositionHeaderValue? contentDisposition)
    {
        var fileName = contentDisposition?.FileNameStar
                       ?? contentDisposition?.FileName?.Trim('"')
                       ?? FileNameFromUrl(update.DownloadUrl);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"{SafeSegment(_product.AppKey)}-{SafeSegment(update.LatestVersion ?? "latest")}{DefaultPackageExtension}";
        }

        if (string.IsNullOrEmpty(Path.GetExtension(fileName)))
        {
            fileName += DefaultPackageExtension;
        }

        return SafeFileName(fileName);
    }

    private static string? FileNameFromUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var fileName = Path.GetFileName(uri.LocalPath);
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
    }

    private static bool IsInstallerExtension(string? extension) =>
        InstallerExtensions.Contains(extension ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    private static string? ReadManifestString(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool VersionsMatch(string? packageVersion, string expectedVersion) =>
        string.Equals(NormalizeVersion(packageVersion), NormalizeVersion(expectedVersion), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeVersion(string? version) =>
        (version ?? string.Empty).Trim().TrimStart('v', 'V');

    private static string? ResolveExtractedRelativePath(string rootDirectory, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var fullRoot = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
        return candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ? candidate : null;
    }

    private static string? NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var token = value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(part => part.Length == 64 && part.All(Uri.IsHexDigit));

        return token?.ToLowerInvariant();
    }

    private static string AppFolderName(string name) => SafeSegment(name.Replace(' ', '-'));

    private static string SafeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalid.Contains(ch) ? '-' : ch));
    }

    private static string SafeFileName(string value) => Path.GetFileName(SafeSegment(value));
}

internal interface IInstallerLauncher
{
    bool Launch(string installerPath, out string? errorMessage);
}

internal sealed class ProcessInstallerLauncher : IInstallerLauncher
{
    public bool Launch(string installerPath, out string? errorMessage)
    {
        try
        {
            var extension = Path.GetExtension(installerPath);
            var startInfo = string.Equals(extension, ".msi", StringComparison.OrdinalIgnoreCase)
                ? BuildMsiStartInfo(installerPath)
                : string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)
                    ? BuildCommandStartInfo(installerPath)
                : new ProcessStartInfo(installerPath) { UseShellExecute = true };

            var process = Process.Start(startInfo);
            errorMessage = process is null ? "The installer process did not start." : null;
            return process is not null;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static ProcessStartInfo BuildMsiStartInfo(string installerPath)
    {
        var startInfo = new ProcessStartInfo("msiexec.exe")
        {
            UseShellExecute = true,
        };

        startInfo.ArgumentList.Add("/i");
        startInfo.ArgumentList.Add(installerPath);
        startInfo.ArgumentList.Add("/passive");
        startInfo.ArgumentList.Add("REBOOT=ReallySuppress");
        return startInfo;
    }

    private static ProcessStartInfo BuildCommandStartInfo(string commandPath)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(commandPath);
        return startInfo;
    }
}
