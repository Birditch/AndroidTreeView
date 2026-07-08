using AndroidTreeView.Core.Interfaces;

namespace AndroidTreeView.Core.Services;

/// <summary>
/// Shared APK-install / file-transfer pipeline used by the full app and Mini shells.
/// APK files are installed; all other files are pushed to the device Download directory.
/// </summary>
public sealed class DeviceFileTransferService
{
    public const string DefaultRemoteDirectory = "/sdcard/Download/";

    private readonly IScreenCaptureService _deviceFiles;

    public DeviceFileTransferService(IScreenCaptureService deviceFiles) =>
        _deviceFiles = deviceFiles ?? throw new ArgumentNullException(nameof(deviceFiles));

    public async Task<DeviceFileTransferResult> ProcessAsync(
        IReadOnlyList<string> serials,
        IReadOnlyList<string> paths,
        string remoteDirectory = DefaultRemoteDirectory,
        CancellationToken ct = default)
    {
        var targets = serials
            .Where(serial => !string.IsNullOrWhiteSpace(serial))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var existing = ExistingFilePaths(paths);
        var apks = existing.Where(IsApk).ToArray();
        var files = existing.Where(path => !IsApk(path)).ToArray();

        if (targets.Length == 0 || existing.Length == 0)
        {
            return new DeviceFileTransferResult(0, 0, 0, 0, existing.Length, targets.Length);
        }

        var results = await Task.WhenAll(
                targets.Select(serial => ProcessOneDeviceAsync(serial, apks, files, remoteDirectory, ct)))
            .ConfigureAwait(false);

        return new DeviceFileTransferResult(
            results.Sum(result => result.InstallSucceeded),
            results.Sum(result => result.InstallFailed),
            results.Sum(result => result.TransferSucceeded),
            results.Sum(result => result.TransferFailed),
            existing.Length,
            targets.Length);
    }

    private async Task<DeviceFileTransferResult> ProcessOneDeviceAsync(
        string serial,
        IReadOnlyList<string> apks,
        IReadOnlyList<string> files,
        string remoteDirectory,
        CancellationToken ct)
    {
        var installSucceeded = 0;
        var installFailed = 0;
        var transferSucceeded = 0;
        var transferFailed = 0;

        foreach (var apk in apks)
        {
            ct.ThrowIfCancellationRequested();
            if (await TryInstallApkAsync(serial, apk, ct).ConfigureAwait(false))
            {
                installSucceeded++;
            }
            else
            {
                installFailed++;
            }
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (await TryPushFileAsync(serial, file, remoteDirectory, ct).ConfigureAwait(false))
            {
                transferSucceeded++;
            }
            else
            {
                transferFailed++;
            }
        }

        return new DeviceFileTransferResult(
            installSucceeded,
            installFailed,
            transferSucceeded,
            transferFailed,
            apks.Count + files.Count,
            1);
    }

    private async Task<bool> TryInstallApkAsync(string serial, string apkPath, CancellationToken ct)
    {
        try
        {
            return await _deviceFiles.InstallApkAsync(serial, apkPath, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<bool> TryPushFileAsync(
        string serial,
        string filePath,
        string remoteDirectory,
        CancellationToken ct)
    {
        try
        {
            return await _deviceFiles.PushFileAsync(serial, filePath, remoteDirectory, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string[] ExistingFilePaths(IReadOnlyList<string> paths) =>
        paths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsApk(string path) =>
        path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase);
}

public sealed record DeviceFileTransferResult(
    int InstallSucceeded,
    int InstallFailed,
    int TransferSucceeded,
    int TransferFailed,
    int ValidFileCount,
    int TargetCount)
{
    public int TotalSucceeded => InstallSucceeded + TransferSucceeded;

    public int TotalFailed => InstallFailed + TransferFailed;
}
