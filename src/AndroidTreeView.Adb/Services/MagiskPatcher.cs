using System.IO.Compression;
using AndroidTreeView.Adb.Parsers;
using AndroidTreeView.Core.Exceptions;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;
using AndroidTreeView.Models.Rooting;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Adb.Services;

/// <summary>
/// Wraps the fixed official Magisk APK patch components on the explicitly selected ADB device.
/// This command sequence remains diagnosable because the M0 real-device matrix has not been verified.
/// </summary>
public sealed class MagiskPatcher : IMagiskPatcher
{
    private const long MaxComponentBytes = 128L * 1024 * 1024;
    private const long MaxAllComponentsBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PatchTimeout = TimeSpan.FromMinutes(5);

    private readonly IAdbCommandExecutor _adb;
    private readonly RootToolPaths _paths;
    private readonly ILogger<MagiskPatcher> _logger;

    public MagiskPatcher(
        IAdbCommandExecutor adb,
        RootToolPaths paths,
        ILogger<MagiskPatcher> logger)
    {
        _adb = adb;
        _paths = paths;
        _logger = logger;
    }

    public async Task<string> PatchAsync(
        string serial,
        BootImageInfo bootImage,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(bootImage);
        if (!File.Exists(_paths.MagiskApkPath))
        {
            throw new RootWorkflowException(RootErrorCode.MagiskToolUnavailable, "The fixed official Magisk APK is unavailable.");
        }

        if (!File.Exists(bootImage.Path) || new FileInfo(bootImage.Path).Length == 0)
        {
            throw new RootWorkflowException(RootErrorCode.TargetImageMissing, "The original boot image is missing or empty.");
        }

        var session = Guid.NewGuid().ToString("N");
        var remoteRoot = $"/data/local/tmp/atv_root/{session}";
        var localComponentRoot = Path.Combine(bootImage.WorkDirectory, $".magisk-components-{session}");
        var patchedPath = Path.Combine(bootImage.WorkDirectory, "boot-patched.img");
        try
        {
            TryDeleteLocal(patchedPath);
            var installResult = await ExecuteRequiredAsync(
                serial,
                new[] { "install", "-r", _paths.MagiskApkPath },
                runInShell: false,
                InstallTimeout,
                RootErrorCode.MagiskInstallFailed,
                ct).ConfigureAwait(false);
            if (!HasInstallSuccess(installResult))
            {
                throw new RootWorkflowException(RootErrorCode.MagiskInstallFailed,
                    "ADB did not confirm that the Magisk APK was installed.")
                {
                    DiagnosticSummary = Sanitize(
                        installResult.StandardOutput + "\n" + installResult.StandardError,
                        installResult.TimedOut)
                };
            }

            var abiResult = await ExecuteRequiredAsync(
                serial,
                new[] { "getprop", "ro.product.cpu.abi" },
                runInShell: true,
                CommandTimeout,
                RootErrorCode.DeviceAbiUnsupported,
                ct).ConfigureAwait(false);
            var abi = CpuAbiParser.Parse(abiResult.StandardOutput);
            if (abi is null)
            {
                throw new RootWorkflowException(RootErrorCode.DeviceAbiUnsupported, "The device ABI is not supported by the bundled Magisk APK.");
            }

            await ExecuteRequiredAsync(serial, new[] { "mkdir", "-p", remoteRoot }, true,
                CommandTimeout, RootErrorCode.ImagePushFailed, ct).ConfigureAwait(false);
            await ExecuteRequiredAsync(serial, new[] { "push", bootImage.Path, $"{remoteRoot}/boot.img" }, false,
                CommandTimeout, RootErrorCode.ImagePushFailed, ct).ConfigureAwait(false);

            // Extract a fixed allowlist from the verified official APK on the desktop. This avoids
            // depending on an optional device-side unzip binary; the M0 patch execution is still unverified.
            var componentEntries = new (string Entry, string RemoteName)[]
            {
                ("assets/boot_patch.sh", "boot_patch.sh"),
                ("assets/util_functions.sh", "util_functions.sh"),
                ("assets/app_functions.sh", "app_functions.sh"),
                ("assets/stub.apk", "stub.apk"),
                ($"lib/{abi}/libmagiskboot.so", "magiskboot"),
                ($"lib/{abi}/libmagiskinit.so", "magiskinit"),
                ($"lib/{abi}/libmagisk.so", "magisk"),
                ($"lib/{abi}/libinit-ld.so", "init-ld")
            };
            var localComponents = await ExtractOfficialComponentsAsync(
                _paths.MagiskApkPath,
                localComponentRoot,
                componentEntries,
                ct).ConfigureAwait(false);
            foreach (var component in localComponents)
            {
                await ExecuteRequiredAsync(serial,
                    new[] { "push", component.LocalPath, $"{remoteRoot}/{component.RemoteName}" },
                    false,
                    CommandTimeout,
                    RootErrorCode.ImagePushFailed,
                    ct).ConfigureAwait(false);
            }

            await ExecuteRequiredAsync(serial,
                new[] { "chmod", "0700", $"{remoteRoot}/boot_patch.sh", $"{remoteRoot}/util_functions.sh",
                    $"{remoteRoot}/app_functions.sh",
                    $"{remoteRoot}/magiskboot", $"{remoteRoot}/magiskinit", $"{remoteRoot}/magisk", $"{remoteRoot}/init-ld" },
                true,
                CommandTimeout,
                RootErrorCode.MagiskPatchFailed,
                ct).ConfigureAwait(false);

            var flags = await ProbePatchFlagsAsync(serial, remoteRoot, ct).ConfigureAwait(false);

            await ExecuteRequiredAsync(serial,
                new[]
                {
                    $"KEEPVERITY={Sh(flags.KeepVerity)}",
                    $"KEEPFORCEENCRYPT={Sh(flags.KeepForceEncrypt)}",
                    $"PATCHVBMETAFLAG={Sh(flags.PatchVbmetaFlag)}",
                    $"RECOVERYMODE={Sh(flags.RecoveryMode)}",
                    $"LEGACYSAR={Sh(flags.LegacySar)}",
                    "sh", $"{remoteRoot}/boot_patch.sh", $"{remoteRoot}/boot.img"
                },
                true,
                PatchTimeout,
                RootErrorCode.MagiskPatchFailed,
                ct).ConfigureAwait(false);

            await VerifyPatchedFlagsAsync(serial, remoteRoot, flags, ct).ConfigureAwait(false);

            await ExecuteRequiredAsync(serial,
                new[] { "pull", $"{remoteRoot}/new-boot.img", patchedPath },
                false,
                CommandTimeout,
                RootErrorCode.PatchedImagePullFailed,
                ct).ConfigureAwait(false);
            if (!File.Exists(patchedPath) || new FileInfo(patchedPath).Length == 0)
            {
                throw new RootWorkflowException(RootErrorCode.PatchedImageInvalid,
                    "Magisk patching did not produce a non-empty local image.");
            }

            return Path.GetFullPath(patchedPath);
        }
        finally
        {
            TryDeleteDirectory(localComponentRoot);
            try
            {
                await _adb.ExecuteAsync(new AdbCommandRequest
                {
                    Serial = serial,
                    Arguments = new[] { "rm", "-rf", remoteRoot },
                    RunInShell = true,
                    Timeout = CommandTimeout
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Magisk temporary directory cleanup failed for the selected device.");
            }
        }
    }

    private static string Sh(bool value) => value ? "true" : "false";

    /// <summary>
    /// Runs official Magisk's own <c>app_init</c> probe on the device to derive the patch flags, exactly as the
    /// Magisk app does before invoking <c>boot_patch.sh</c>.
    /// </summary>
    /// <remarks>
    /// This must be a separate shell invocation from <c>boot_patch.sh</c>: <c>app_functions.sh</c> replaces
    /// <c>grep_prop</c> with an empty stub, which would break the <c>SHA1</c> lookup inside <c>boot_patch.sh</c>.
    /// </remarks>
    private async Task<MagiskPatchFlags> ProbePatchFlagsAsync(
        string serial,
        string remoteRoot,
        CancellationToken ct)
    {
        var result = await ExecuteRequiredAsync(serial,
            new[]
            {
                "cd", remoteRoot, "&&",
                ".", "./util_functions.sh", "&&",
                ".", "./app_functions.sh", "&&",
                "api_level_arch_detect", "&&",
                "app_init"
            },
            true,
            CommandTimeout,
            RootErrorCode.MagiskFlagProbeFailed,
            ct).ConfigureAwait(false);

        var flags = MagiskFlagsParser.Parse(result.StandardOutput);
        if (flags is null)
        {
            throw new RootWorkflowException(RootErrorCode.MagiskFlagProbeFailed,
                "The device probe did not report a complete set of Magisk patch flags.")
            {
                DiagnosticSummary = Sanitize(result.StandardOutput + "\n" + result.StandardError, result.TimedOut)
            };
        }

        _logger.LogInformation(
            "Magisk patch flags probed: KEEPVERITY={KeepVerity} KEEPFORCEENCRYPT={KeepForceEncrypt} " +
            "PATCHVBMETAFLAG={PatchVbmeta} RECOVERYMODE={RecoveryMode} LEGACYSAR={LegacySar}",
            flags.KeepVerity, flags.KeepForceEncrypt, flags.PatchVbmetaFlag, flags.RecoveryMode, flags.LegacySar);
        return flags;
    }

    /// <summary>
    /// Reads the config back out of the patched ramdisk and asserts it matches the probed flags.
    /// </summary>
    /// <remarks>
    /// <c>boot_patch.sh</c> ends in a bare <c>true</c>, so its exit code proves nothing, and a wrongly-flagged
    /// image is structurally valid — neither the exit code nor a header check can catch it. Reading back the
    /// config that Magisk itself recorded is the only gate that can.
    /// </remarks>
    private async Task VerifyPatchedFlagsAsync(
        string serial,
        string remoteRoot,
        MagiskPatchFlags expected,
        CancellationToken ct)
    {
        var result = await ExecuteRequiredAsync(serial,
            // magiskboot restores the cpio entry's own mode (0000), so the extracted file has to be
            // chmod-ed before it can be read back.
            new[]
            {
                "cd", remoteRoot, "&&",
                "rm", "-f", "config.verify", "&&",
                "./magiskboot", "cpio", "ramdisk.cpio", "'extract .backup/.magisk config.verify'", "&&",
                "chmod", "0600", "config.verify", "&&",
                "cat", "config.verify"
            },
            true,
            CommandTimeout,
            RootErrorCode.PatchedImageFlagMismatch,
            ct).ConfigureAwait(false);

        var actual = MagiskFlagsParser.ParsePatchedConfig(result.StandardOutput);
        if (actual is null)
        {
            throw new RootWorkflowException(RootErrorCode.PatchedImageFlagMismatch,
                "The patched image did not carry a readable Magisk config.")
            {
                DiagnosticSummary = Sanitize(result.StandardOutput + "\n" + result.StandardError, result.TimedOut)
            };
        }

        if (actual.Value.KeepVerity != expected.KeepVerity
            || actual.Value.KeepForceEncrypt != expected.KeepForceEncrypt)
        {
            throw new RootWorkflowException(RootErrorCode.PatchedImageFlagMismatch,
                $"The patched image recorded KEEPVERITY={Sh(actual.Value.KeepVerity)} " +
                $"KEEPFORCEENCRYPT={Sh(actual.Value.KeepForceEncrypt)} but the device requires " +
                $"KEEPVERITY={Sh(expected.KeepVerity)} KEEPFORCEENCRYPT={Sh(expected.KeepForceEncrypt)}. " +
                "Flashing it would leave the device unable to mount its system partition.");
        }
    }

    private async Task<AdbCommandResult> ExecuteRequiredAsync(
        string serial,
        IReadOnlyList<string> arguments,
        bool runInShell,
        TimeSpan timeout,
        RootErrorCode errorCode,
        CancellationToken ct)
    {
        AdbCommandResult result;
        try
        {
            result = await _adb.ExecuteAsync(new AdbCommandRequest
            {
                Serial = serial,
                Arguments = arguments,
                RunInShell = runInShell,
                Timeout = timeout
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RootWorkflowException(errorCode, "A required Magisk patch command could not be executed.", ex);
        }

        if (!result.IsSuccess)
        {
            throw new RootWorkflowException(errorCode, "A required Magisk patch command failed.")
            {
                DiagnosticSummary = Sanitize(result.StandardError, result.TimedOut)
            };
        }

        return result;
    }

    private static async Task<IReadOnlyList<ExtractedComponent>> ExtractOfficialComponentsAsync(
        string apkPath,
        string destinationDirectory,
        IReadOnlyList<(string Entry, string RemoteName)> requested,
        CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(destinationDirectory);
            using var archive = ZipFile.OpenRead(apkPath);
            var extracted = new List<ExtractedComponent>(requested.Count);
            long total = 0;
            foreach (var component in requested)
            {
                var matches = archive.Entries
                    .Where(entry => entry.FullName.Equals(component.Entry, StringComparison.Ordinal))
                    .ToArray();
                if (matches.Length != 1 || matches[0].Length <= 0 || matches[0].Length > MaxComponentBytes)
                {
                    throw new RootWorkflowException(RootErrorCode.MagiskToolUnavailable,
                        "The official Magisk APK is missing a required component or contains an invalid component.");
                }

                total = checked(total + matches[0].Length);
                if (total > MaxAllComponentsBytes)
                {
                    throw new RootWorkflowException(RootErrorCode.MagiskToolUnavailable,
                        "The official Magisk component set exceeds the safety limit.");
                }

                var destination = Path.Combine(destinationDirectory, component.RemoteName);
                await using var source = matches[0].Open();
                await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, bufferSize: 81920, useAsync: true);
                await source.CopyToAsync(target, ct).ConfigureAwait(false);
                if (target.Length != matches[0].Length)
                {
                    throw new RootWorkflowException(RootErrorCode.MagiskToolUnavailable,
                        "A Magisk component could not be extracted completely.");
                }

                extracted.Add(new ExtractedComponent(destination, component.RemoteName));
            }

            return extracted;
        }
        catch (RootWorkflowException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or OverflowException)
        {
            throw new RootWorkflowException(RootErrorCode.MagiskToolUnavailable,
                "The official Magisk APK could not be read safely.", ex);
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

    private static bool HasInstallSuccess(AdbCommandResult result)
        => (result.StandardOutput + "\n" + result.StandardError)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(static line => line.Equals("Success", StringComparison.OrdinalIgnoreCase));

    private static void TryDeleteLocal(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A subsequent pull reports the stable failure if the destination cannot be replaced.
        }
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
            // Cleanup is best effort and must not hide the primary patch failure.
        }
    }

    private sealed record ExtractedComponent(string LocalPath, string RemoteName);
}
