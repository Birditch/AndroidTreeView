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
        IProgress<DeviceFileTransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        var targets = serials
            .Where(serial => !string.IsNullOrWhiteSpace(serial))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var existing = ExistingFilePaths(paths);
        var apks = existing.Where(IsApk).ToArray();
        var files = existing.Where(path => !IsApk(path)).ToArray();
        var fileSizes = existing.ToDictionary(
            path => path,
            path => new FileInfo(path).Length,
            StringComparer.OrdinalIgnoreCase);
        var totalBytes = fileSizes.Values.Sum() * targets.Length;

        if (targets.Length == 0 || existing.Length == 0)
        {
            return new DeviceFileTransferResult(0, 0, 0, 0, existing.Length, targets.Length, totalBytes);
        }

        var completed = 0;
        var completedBytes = 0L;
        var total = targets.Length * existing.Length;
        var progressGate = new object();
        var activeBytes = new Dictionary<string, long>(StringComparer.Ordinal);
        progress?.Report(DeviceFileTransferProgress.Start(total, totalBytes));

        void ReportActive(string serial, string path, DeviceFileTransferOperation operation, long bytes)
        {
            if (progress is null)
            {
                return;
            }

            var key = ProgressKey(serial, path, operation);
            var size = fileSizes.TryGetValue(path, out var fileSize) ? fileSize : 0;
            var clamped = Math.Clamp(bytes, 0, size);
            int done;
            long doneBytes;

            lock (progressGate)
            {
                if (clamped < size &&
                    activeBytes.TryGetValue(key, out var previous) &&
                    Math.Abs(clamped - previous) < ActiveProgressStep(size))
                {
                    return;
                }

                activeBytes[key] = clamped;
                done = completed;
                doneBytes = completedBytes + activeBytes.Values.Sum();
            }

            progress.Report(new DeviceFileTransferProgress(
                done,
                total,
                doneBytes,
                totalBytes,
                serial,
                path,
                operation,
                null));
        }

        void ReportComplete(string serial, string path, DeviceFileTransferOperation operation, bool succeeded)
        {
            var key = ProgressKey(serial, path, operation);
            var size = fileSizes.TryGetValue(path, out var fileSize) ? fileSize : 0;
            int done;
            long doneBytes;

            lock (progressGate)
            {
                activeBytes.Remove(key);
                completed++;
                completedBytes += size;
                done = completed;
                doneBytes = completedBytes + activeBytes.Values.Sum();
            }

            progress?.Report(new DeviceFileTransferProgress(
                done,
                total,
                doneBytes,
                totalBytes,
                serial,
                path,
                operation,
                succeeded));
        }

        var results = await Task.WhenAll(
                targets.Select(serial => ProcessOneDeviceAsync(
                    serial,
                    apks,
                    files,
                    remoteDirectory,
                    ReportActive,
                    ReportComplete,
                    ct)))
            .ConfigureAwait(false);

        return new DeviceFileTransferResult(
            results.Sum(result => result.InstallSucceeded),
            results.Sum(result => result.InstallFailed),
            results.Sum(result => result.TransferSucceeded),
            results.Sum(result => result.TransferFailed),
            existing.Length,
            targets.Length,
            totalBytes);
    }

    private async Task<DeviceFileTransferResult> ProcessOneDeviceAsync(
        string serial,
        IReadOnlyList<string> apks,
        IReadOnlyList<string> files,
        string remoteDirectory,
        Action<string, string, DeviceFileTransferOperation, long> reportActive,
        Action<string, string, DeviceFileTransferOperation, bool> reportComplete,
        CancellationToken ct)
    {
        var installSucceeded = 0;
        var installFailed = 0;
        var transferSucceeded = 0;
        var transferFailed = 0;

        foreach (var apk in apks)
        {
            ct.ThrowIfCancellationRequested();
            reportActive(serial, apk, DeviceFileTransferOperation.InstallApk, 0);
            var succeeded = await TryInstallApkAsync(serial, apk, ct).ConfigureAwait(false);
            if (succeeded)
            {
                installSucceeded++;
            }
            else
            {
                installFailed++;
            }

            reportComplete(serial, apk, DeviceFileTransferOperation.InstallApk, succeeded);
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var fileSize = new FileInfo(file).Length;
            reportActive(serial, file, DeviceFileTransferOperation.TransferFile, 0);
            var fileProgress = new InlineProgress<double>(fraction =>
                reportActive(
                    serial,
                    file,
                    DeviceFileTransferOperation.TransferFile,
                    (long)Math.Round(fileSize * Math.Clamp(fraction, 0, 1))));
            var succeeded = await TryPushFileAsync(serial, file, remoteDirectory, fileProgress, ct).ConfigureAwait(false);
            if (succeeded)
            {
                transferSucceeded++;
            }
            else
            {
                transferFailed++;
            }

            reportComplete(serial, file, DeviceFileTransferOperation.TransferFile, succeeded);
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
        IProgress<double>? progress,
        CancellationToken ct)
    {
        try
        {
            if (_deviceFiles is IProgressiveFileTransferService progressive)
            {
                return await progressive.PushFileAsync(serial, filePath, remoteDirectory, progress, ct)
                    .ConfigureAwait(false);
            }

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

    private static string ProgressKey(string serial, string path, DeviceFileTransferOperation operation) =>
        string.Concat(serial, '\0', path, '\0', operation.ToString());

    private static long ActiveProgressStep(long fileSize) =>
        fileSize < 512 * 1024 ? 1 : Math.Max(512 * 1024, fileSize / 1000);

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}

public sealed record DeviceFileTransferResult(
    int InstallSucceeded,
    int InstallFailed,
    int TransferSucceeded,
    int TransferFailed,
    int ValidFileCount,
    int TargetCount,
    long TotalBytes = 0)
{
    public int TotalSucceeded => InstallSucceeded + TransferSucceeded;

    public int TotalFailed => InstallFailed + TransferFailed;
}

public enum DeviceFileTransferOperation
{
    InstallApk,
    TransferFile
}

public sealed record DeviceFileTransferProgress(
    int CompletedCount,
    int TotalCount,
    long CompletedBytes,
    long TotalBytes,
    string? Serial,
    string? Path,
    DeviceFileTransferOperation? Operation,
    bool? Succeeded)
{
    public static DeviceFileTransferProgress Start(int totalCount, long totalBytes) =>
        new(0, totalCount, 0, totalBytes, null, null, null, null);
}
