using System.IO.Compression;
using AndroidTreeView.Adb.Commands;
using AndroidTreeView.Adb.Parsers;
using AndroidTreeView.Core.Exceptions;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;
using AndroidTreeView.Models.Rooting;

namespace AndroidTreeView.Adb.Services;

/// <summary>Safely extracts an evidence-backed boot image from ZIP, Pixel nested ZIP, or payload.bin.</summary>
public sealed class BootImageExtractor : IBootImageExtractor
{
    private const long MaxBootImageBytes = 512L * 1024 * 1024;
    private const long MaxNestedZipBytes = 8L * 1024 * 1024 * 1024;
    private const long MaxPayloadBytes = 16L * 1024 * 1024 * 1024;
    private const long MaxArchiveExpandedBytes = 20L * 1024 * 1024 * 1024;
    private const int MaxMetadataBytes = 256 * 1024;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PayloadTimeout = TimeSpan.FromMinutes(10);

    private readonly IAdbCommandExecutor _adb;
    private readonly IExternalCommandRunner _runner;
    private readonly RootToolPaths _paths;

    public BootImageExtractor(
        IAdbCommandExecutor adb,
        IExternalCommandRunner runner,
        RootToolPaths paths)
    {
        _adb = adb;
        _runner = runner;
        _paths = paths;
    }

    public async Task<BootImageInfo> ExtractAsync(
        string packagePath,
        RootDeviceIdentity device,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(device);
        var fullPackagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPackagePath))
        {
            throw new RootWorkflowException(RootErrorCode.PackageNotFound, "The selected firmware package does not exist.");
        }

        var workDirectory = Path.Combine(_paths.WorkRootDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        try
        {
            ct.ThrowIfCancellationRequested();
            var header = new byte[4];
            await using (var stream = new FileStream(fullPackagePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, useAsync: true))
            {
                var read = await stream.ReadAsync(header, ct).ConfigureAwait(false);
                if (read < header.Length)
                {
                    throw new RootWorkflowException(RootErrorCode.PackageUnsupported, "The selected firmware package is too short.");
                }
            }

            if (PackageTypeDetector.IsPayloadHeader(header))
            {
                var payloadLength = new FileInfo(fullPackagePath).Length;
                if (payloadLength <= 0 || payloadLength > MaxPayloadBytes)
                {
                    throw new RootWorkflowException(RootErrorCode.PackageSizeLimitExceeded,
                        "The payload exceeds the safety limit.");
                }

                var probe = await ProbeDeviceAsync(device.Serial, ct).ConfigureAwait(false);
                var detection = BootPartitionTargetDetector.Detect(
                    packageHasBoot: true,
                    packageHasInitBoot: true,
                    probe.HasInitBoot,
                    probe.AndroidSdk,
                    probe.KernelVersion);
                EnsureSupported(detection);
                var metadata = FirmwarePackageMetadataParser.Parse(
                    fullPackagePath,
                    FirmwarePackageType.Payload,
                    device,
                    otaMetadata: null,
                    androidInfo: null);
                var extracted = await ExtractPayloadAsync(
                    fullPackagePath,
                    workDirectory,
                    detection.Target,
                    ct).ConfigureAwait(false);
                await EnsureRamdiskEvidenceAsync(extracted, detection.Target, ct).ConfigureAwait(false);
                return BuildInfo(extracted, workDirectory, fullPackagePath, detection.Target,
                    BootImageSource.Payload, metadata);
            }

            if (!PackageTypeDetector.IsZipHeader(header))
            {
                throw new RootWorkflowException(RootErrorCode.PackageUnsupported, "The selected package is not a supported ZIP or payload.bin.");
            }

            return await ExtractZipAsync(fullPackagePath, workDirectory, device, ct).ConfigureAwait(false);
        }
        catch
        {
            TryDeleteDirectory(workDirectory);
            throw;
        }
    }

    private async Task<BootImageInfo> ExtractZipAsync(
        string packagePath,
        string workDirectory,
        RootDeviceIdentity device,
        CancellationToken ct)
    {
        try
        {
            using var outer = ZipFile.OpenRead(packagePath);
            ValidateArchive(outer, workDirectory);
            var type = PackageTypeDetector.DetectZipEntries(outer.Entries.Select(static entry => entry.FullName));
            if (type == FirmwarePackageType.Unknown)
            {
                throw new RootWorkflowException(RootErrorCode.PackageUnsupported, "The ZIP contains no supported boot image or payload.");
            }

            var otaMetadata = await ReadOptionalTextAsync(outer, "META-INF/com/android/metadata", ct).ConfigureAwait(false);
            var androidInfo = await ReadOptionalTextAsync(outer, "android-info.txt", ct).ConfigureAwait(false);
            ZipArchive imageArchive = outer;
            string? nestedPath = null;
            if (type == FirmwarePackageType.NestedZip)
            {
                var nestedEntries = outer.Entries.Where(entry => PackageTypeDetector.IsTopLevelPixelImageZip(entry.FullName)).ToArray();
                if (nestedEntries.Length != 1)
                {
                    throw new RootWorkflowException(RootErrorCode.PackageCorrupt, "The package contains duplicate or ambiguous Pixel image ZIPs.");
                }

                nestedPath = Path.Combine(workDirectory, "pixel-image.zip");
                await CopyEntryAsync(nestedEntries[0], nestedPath, MaxNestedZipBytes, ct).ConfigureAwait(false);
                imageArchive = ZipFile.OpenRead(nestedPath);
                ValidateArchive(imageArchive, workDirectory);
                if (imageArchive.Entries.Any(entry => PackageTypeDetector.IsTopLevelPixelImageZip(entry.FullName)))
                {
                    throw new RootWorkflowException(RootErrorCode.PackageUnsupported, "Only one Pixel nested ZIP level is supported.");
                }

                androidInfo ??= await ReadOptionalTextAsync(imageArchive, "android-info.txt", ct).ConfigureAwait(false);
            }

            try
            {
                var effectiveType = type == FirmwarePackageType.NestedZip
                    ? FirmwarePackageType.NestedZip
                    : type;
                var metadata = FirmwarePackageMetadataParser.Parse(
                    packagePath,
                    effectiveType,
                    device,
                    otaMetadata,
                    androidInfo);
                if (metadata.MatchStatus == FirmwarePackageMatchStatus.Mismatched)
                {
                    throw new RootWorkflowException(RootErrorCode.PackageMetadataMismatch,
                        "Firmware package metadata identifies a different device.");
                }

                var bootEntries = imageArchive.Entries
                    .Where(entry => PackageTypeDetector.IsTopLevelBootImage(entry.FullName))
                    .ToArray();
                var boot = SingleEntry(bootEntries, "boot.img");
                var initBoot = SingleEntry(bootEntries, "init_boot.img");
                var payload = imageArchive.Entries
                    .Where(entry => Path.GetFileName(entry.FullName).Equals("payload.bin", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                var probe = await ProbeDeviceAsync(device.Serial, ct).ConfigureAwait(false);
                var detection = BootPartitionTargetDetector.Detect(
                    boot is not null || type == FirmwarePackageType.Payload,
                    initBoot is not null || type == FirmwarePackageType.Payload,
                    probe.HasInitBoot,
                    probe.AndroidSdk,
                    probe.KernelVersion);
                EnsureSupported(detection);

                string outputPath;
                BootImageSource source;
                if (type == FirmwarePackageType.Payload)
                {
                    if (payload.Length != 1)
                    {
                        throw new RootWorkflowException(RootErrorCode.PackageCorrupt, "The package contains duplicate or missing payload.bin entries.");
                    }

                    var payloadPath = Path.Combine(workDirectory, "payload.bin");
                    await CopyEntryAsync(payload[0], payloadPath, MaxPayloadBytes, ct).ConfigureAwait(false);
                    outputPath = await ExtractPayloadAsync(payloadPath, workDirectory, detection.Target, ct).ConfigureAwait(false);
                    source = BootImageSource.Payload;
                }
                else
                {
                    var selected = detection.Target == BootPartitionTarget.InitBoot ? initBoot : boot;
                    if (selected is null)
                    {
                        throw new RootWorkflowException(RootErrorCode.TargetImageMissing, "The selected target image is absent from the package.");
                    }

                    outputPath = Path.Combine(workDirectory, FastbootArgs.PartitionName(detection.Target) + "-original.img");
                    await CopyEntryAsync(selected, outputPath, MaxBootImageBytes, ct).ConfigureAwait(false);
                    source = type == FirmwarePackageType.NestedZip ? BootImageSource.NestedZip : BootImageSource.PlainZip;
                }

                await EnsureRamdiskEvidenceAsync(outputPath, detection.Target, ct).ConfigureAwait(false);
                return BuildInfo(outputPath, workDirectory, packagePath, detection.Target, source, metadata);
            }
            finally
            {
                if (!ReferenceEquals(imageArchive, outer))
                {
                    imageArchive.Dispose();
                }

                if (nestedPath is not null)
                {
                    TryDeleteFile(nestedPath);
                }
            }
        }
        catch (InvalidDataException ex)
        {
            throw new RootWorkflowException(RootErrorCode.PackageCorrupt, "The selected ZIP is corrupt.", ex);
        }
    }

    private async Task<string> ExtractPayloadAsync(
        string payloadPath,
        string workDirectory,
        BootPartitionTarget target,
        CancellationToken ct)
    {
        if (!File.Exists(_paths.PayloadDumperPath))
        {
            throw new RootWorkflowException(RootErrorCode.PayloadToolUnavailable, "The bundled payload extraction tool is unavailable.");
        }

        var outputDirectory = Path.Combine(workDirectory, "payload-output");
        Directory.CreateDirectory(outputDirectory);
        var partition = FastbootArgs.PartitionName(target);
        ExternalCommandResult result;
        try
        {
            result = await _runner.RunAsync(new ExternalCommandRequest
            {
                FileName = _paths.PayloadDumperPath,
                Arguments = new[] { "-p", partition, "-o", outputDirectory, Path.GetFullPath(payloadPath) },
                Timeout = PayloadTimeout
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RootWorkflowException(RootErrorCode.PayloadExtractionFailed,
                "Payload extraction could not be started.", ex);
        }

        if (!result.IsSuccess)
        {
            throw new RootWorkflowException(RootErrorCode.PayloadExtractionFailed, "Payload extraction failed.")
            {
                DiagnosticSummary = Sanitize(result.StandardError, result.TimedOut)
            };
        }

        var source = Path.Combine(outputDirectory, partition + ".img");
        if (!File.Exists(source) || new FileInfo(source).Length == 0 || new FileInfo(source).Length > MaxBootImageBytes)
        {
            throw new RootWorkflowException(RootErrorCode.TargetImageMissing, "Payload extraction did not produce a valid target image.");
        }

        var destination = Path.Combine(workDirectory, partition + "-original.img");
        File.Move(source, destination, overwrite: true);
        return destination;
    }

    private async Task<DeviceProbe> ProbeDeviceAsync(string serial, CancellationToken ct)
    {
        var initTask = ExecuteShellAsync(serial,
            new[] { "test", "-e", "/dev/block/by-name/init_boot" },
            ct);
        var sdkTask = ExecuteShellAsync(serial, new[] { "getprop", "ro.build.version.sdk" }, ct);
        var kernelTask = ExecuteShellAsync(serial, new[] { "uname", "-r" }, ct);
        try
        {
            await Task.WhenAll(initTask, sdkTask, kernelTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RootWorkflowException(RootErrorCode.DeviceUnavailable,
                "Required device boot-partition evidence could not be read.", ex);
        }
        var initResult = await initTask.ConfigureAwait(false);
        var sdkResult = await sdkTask.ConfigureAwait(false);
        var kernelResult = await kernelTask.ConfigureAwait(false);
        var sdk = BootPartitionTargetDetector.ParseAndroidSdk(sdkResult.StandardOutput);
        var kernel = BootPartitionTargetDetector.ParseKernelVersion(kernelResult.StandardOutput);
        var initBootExists = ParseFileExistsResult(initResult);
        if (initBootExists is null
            || !sdkResult.IsSuccess
            || !kernelResult.IsSuccess
            || sdk is null
            || kernel is null)
        {
            throw new RootWorkflowException(RootErrorCode.DeviceUnavailable,
                "Required device boot-partition evidence could not be read.");
        }

        return new DeviceProbe(
            initBootExists.Value,
            sdk,
            kernel);
    }

    private static bool? ParseFileExistsResult(AdbCommandResult result)
    {
        if (result.IsSuccess)
        {
            return true;
        }

        return result.ExitCode == 1
            && !result.TimedOut
            && string.IsNullOrWhiteSpace(result.StandardOutput)
            && string.IsNullOrWhiteSpace(result.StandardError)
                ? false
                : null;
    }

    private Task<AdbCommandResult> ExecuteShellAsync(
        string serial,
        IReadOnlyList<string> arguments,
        CancellationToken ct)
        => _adb.ExecuteAsync(new AdbCommandRequest
        {
            Serial = serial,
            Arguments = arguments,
            RunInShell = true,
            Timeout = ProbeTimeout
        }, ct);

    private static async Task EnsureRamdiskEvidenceAsync(
        string imagePath,
        BootPartitionTarget target,
        CancellationToken ct)
    {
        var header = new byte[AndroidBootImageHeaderParser.MaximumHeaderSize];
        int read;
        long length;
        await using (var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, useAsync: true))
        {
            length = stream.Length;
            read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct)
                .ConfigureAwait(false);
        }

        var evidence = AndroidBootImageHeaderParser.Parse(header.AsSpan(0, read), length);
        var validation = BootPartitionTargetDetector.ValidateRamdisk(target, evidence);
        if (!validation.IsSupported)
        {
            throw new RootWorkflowException(validation.ErrorCode,
                validation.ErrorCode == RootErrorCode.RecoveryOnlyUnsupported
                    ? "The selected boot image has no ramdisk and requires unsupported recovery-mode installation."
                    : "The selected image did not provide reliable boot ramdisk evidence.");
        }
    }

    private static void ValidateArchive(ZipArchive archive, string extractionRoot)
    {
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            ValidateEntryPath(extractionRoot, entry.FullName);
            try
            {
                total = checked(total + entry.Length);
            }
            catch (OverflowException)
            {
                throw new RootWorkflowException(RootErrorCode.PackageSizeLimitExceeded, "ZIP expanded size exceeds the safety limit.");
            }

            if (total > MaxArchiveExpandedBytes)
            {
                throw new RootWorkflowException(RootErrorCode.PackageSizeLimitExceeded, "ZIP expanded size exceeds the safety limit.");
            }
        }
    }

    private static void ValidateEntryPath(string extractionRoot, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || entryName.IndexOf('\0') >= 0)
        {
            throw new RootWorkflowException(RootErrorCode.PackagePathUnsafe, "ZIP contains an invalid entry path.");
        }

        try
        {
            var root = Path.GetFullPath(extractionRoot) + Path.DirectorySeparatorChar;
            var normalizedEntry = entryName
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var candidate = Path.GetFullPath(Path.Combine(extractionRoot, normalizedEntry));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!candidate.StartsWith(root, comparison))
            {
                throw new RootWorkflowException(RootErrorCode.PackagePathUnsafe,
                    "ZIP contains a path outside the extraction directory.");
            }
        }
        catch (RootWorkflowException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            throw new RootWorkflowException(RootErrorCode.PackagePathUnsafe,
                "ZIP contains an invalid entry path.", ex);
        }
    }

    private static ZipArchiveEntry? SingleEntry(IEnumerable<ZipArchiveEntry> entries, string fileName)
    {
        var matches = entries
            .Where(entry => Path.GetFileName(entry.FullName).Equals(fileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length > 1)
        {
            throw new RootWorkflowException(RootErrorCode.PackageCorrupt, $"The package contains duplicate {fileName} entries.");
        }

        return matches.SingleOrDefault();
    }

    private static async Task CopyEntryAsync(
        ZipArchiveEntry entry,
        string destination,
        long limit,
        CancellationToken ct)
    {
        if (entry.Length <= 0 || entry.Length > limit)
        {
            throw new RootWorkflowException(RootErrorCode.PackageSizeLimitExceeded, "ZIP entry exceeds its safety limit.");
        }

        await using var source = entry.Open();
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            written += read;
            if (written > limit)
            {
                throw new RootWorkflowException(RootErrorCode.PackageSizeLimitExceeded, "ZIP entry exceeded its safety limit while streaming.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
    }

    private static async Task<string?> ReadOptionalTextAsync(
        ZipArchive archive,
        string name,
        CancellationToken ct)
    {
        var matches = archive.Entries
            .Where(entry => entry.FullName.Replace('\\', '/').Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        if (matches.Length > 1 || matches[0].Length > MaxMetadataBytes)
        {
            throw new RootWorkflowException(RootErrorCode.PackageCorrupt, "Package metadata is duplicated or too large.");
        }

        using var reader = new StreamReader(matches[0].Open());
        ct.ThrowIfCancellationRequested();
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    private static BootImageInfo BuildInfo(
        string path,
        string workDirectory,
        string packagePath,
        BootPartitionTarget target,
        BootImageSource source,
        FirmwarePackageMetadata metadata)
        => new()
        {
            Path = Path.GetFullPath(path),
            WorkDirectory = Path.GetFullPath(workDirectory),
            OriginalPackageName = Path.GetFileName(packagePath),
            TargetPartition = target,
            Source = source,
            PackageMetadata = metadata
        };

    private static void EnsureSupported(BootPartitionDetection detection)
    {
        if (!detection.IsSupported)
        {
            throw new RootWorkflowException(detection.ErrorCode, "Device and package evidence did not establish a supported boot target.");
        }
    }

    private static string Sanitize(string? value, bool timedOut)
    {
        var normalized = string.Join(' ', (value ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (normalized.Length > 300)
        {
            normalized = normalized[..300];
        }

        return timedOut ? $"Command timed out. {normalized}".Trim() : normalized;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort after the primary error; callers still receive the original stable failure.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort for the transient nested archive.
        }
    }

    private sealed record DeviceProbe(bool HasInitBoot, int? AndroidSdk, Version? KernelVersion);
}
