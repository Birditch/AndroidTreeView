using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;
using Xunit;

namespace AndroidTreeView.Core.Tests;

public sealed class DeviceFileTransferServiceTests
{
    [Fact]
    public async Task ProcessAsync_installs_apks_and_pushes_other_files_to_download_for_all_targets()
    {
        var fake = new FakeScreenCaptureService();
        var service = new DeviceFileTransferService(fake);
        var apk = TempFile(".apk");
        var text = TempFile(".txt");

        try
        {
            var result = await service.ProcessAsync(["A", "B"], [apk, text]);

            Assert.Equal(2, result.InstallSucceeded);
            Assert.Equal(0, result.InstallFailed);
            Assert.Equal(2, result.TransferSucceeded);
            Assert.Equal(0, result.TransferFailed);
            Assert.Equal(2, result.ValidFileCount);
            Assert.Equal(2, result.TargetCount);
            Assert.Equal((new FileInfo(apk).Length + new FileInfo(text).Length) * 2, result.TotalBytes);

            Assert.Equal(new[] { ("A", apk), ("B", apk) }, fake.Installed);
            (string Serial, string FilePath, string? RemoteDirectory)[] expectedPushes =
            [
                ("A", text, DeviceFileTransferService.DefaultRemoteDirectory),
                ("B", text, DeviceFileTransferService.DefaultRemoteDirectory)
            ];
            Assert.Equal(expectedPushes, fake.Pushed);
        }
        finally
        {
            File.Delete(apk);
            File.Delete(text);
        }
    }

    [Fact]
    public async Task ProcessAsync_missing_files_returns_no_actions()
    {
        var fake = new FakeScreenCaptureService();
        var service = new DeviceFileTransferService(fake);

        var result = await service.ProcessAsync(["A"], ["missing.apk"]);

        Assert.Equal(0, result.ValidFileCount);
        Assert.Empty(fake.Installed);
        Assert.Empty(fake.Pushed);
    }

    [Fact]
    public async Task ProcessAsync_reports_progress_for_each_target_file()
    {
        var fake = new FakeScreenCaptureService();
        var service = new DeviceFileTransferService(fake);
        var apk = TempFile(".apk");
        var text = TempFile(".txt");
        var reports = new List<DeviceFileTransferProgress>();

        try
        {
            await service.ProcessAsync(["A", "B"], [apk, text], progress: new CapturingProgress<DeviceFileTransferProgress>(reports.Add));

            Assert.Equal(9, reports.Count);
            Assert.Equal(0, reports[0].CompletedCount);
            Assert.Equal(4, reports[0].TotalCount);
            Assert.Equal(0, reports[0].CompletedBytes);
            Assert.Equal((new FileInfo(apk).Length + new FileInfo(text).Length) * 2, reports[0].TotalBytes);
            Assert.Equal(new[] { 1, 2, 3, 4 }, reports.Where(report => report.Succeeded is not null).Select(report => report.CompletedCount).Order().ToArray());
            Assert.Equal(4, reports.Count(report => report.Succeeded is null && report.Operation is not null));
            Assert.All(reports.Skip(1), report =>
            {
                Assert.Equal(4, report.TotalCount);
                Assert.Equal(reports[0].TotalBytes, report.TotalBytes);
                Assert.NotNull(report.Serial);
                Assert.NotNull(report.Path);
                Assert.NotNull(report.Operation);
            });
            Assert.Equal(reports[0].TotalBytes, reports.Max(report => report.CompletedBytes));
        }
        finally
        {
            File.Delete(apk);
            File.Delete(text);
        }
    }

    [Fact]
    public async Task ProcessAsync_reports_live_push_bytes_when_supported()
    {
        var fake = new ProgressiveScreenCaptureService();
        var service = new DeviceFileTransferService(fake);
        var file = TempFile(".bin");
        var reports = new List<DeviceFileTransferProgress>();

        try
        {
            await service.ProcessAsync(["A"], [file], progress: new CapturingProgress<DeviceFileTransferProgress>(reports.Add));

            var totalBytes = new FileInfo(file).Length;
            Assert.Contains(reports, report =>
                report.Operation == DeviceFileTransferOperation.TransferFile &&
                report.Succeeded is null &&
                report.CompletedBytes > 0 &&
                report.CompletedBytes < totalBytes);
            Assert.Equal(totalBytes, reports.Max(report => report.CompletedBytes));
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static string TempFile(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), "AndroidTreeView-" + Path.GetRandomFileName() + extension);
        File.WriteAllText(path, "payload");
        return path;
    }

    private sealed class CapturingProgress<T>(Action<T> capture) : IProgress<T>
    {
        public void Report(T value) => capture(value);
    }

    private sealed class FakeScreenCaptureService : IScreenCaptureService
    {
        public List<(string Serial, string ApkPath)> Installed { get; } = [];

        public List<(string Serial, string FilePath, string? RemoteDirectory)> Pushed { get; } = [];

        public Task<byte[]?> CaptureFrameAsync(string serial, CancellationToken ct = default) =>
            Task.FromResult<byte[]?>(null);

        public Task TapAsync(string serial, int x, int y, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<bool> InstallApkAsync(string serial, string apkPath, CancellationToken ct = default)
        {
            Installed.Add((serial, apkPath));
            return Task.FromResult(true);
        }

        public Task<bool> PushFileAsync(
            string serial,
            string filePath,
            string? remoteDirectory = null,
            CancellationToken ct = default)
        {
            Pushed.Add((serial, filePath, remoteDirectory));
            return Task.FromResult(true);
        }

        public Task<bool> PrepareFileTransferAsync(
            string serial,
            string? remoteDirectory = null,
            CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task SwipeAsync(
            string serial,
            int x1,
            int y1,
            int x2,
            int y2,
            int durationMs,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task KeyEventAsync(string serial, int keyCode, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class ProgressiveScreenCaptureService : IScreenCaptureService, IProgressiveFileTransferService
    {
        public Task<byte[]?> CaptureFrameAsync(string serial, CancellationToken ct = default) =>
            Task.FromResult<byte[]?>(null);

        public Task TapAsync(string serial, int x, int y, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<bool> InstallApkAsync(string serial, string apkPath, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> PushFileAsync(
            string serial,
            string filePath,
            string? remoteDirectory = null,
            CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> PushFileAsync(
            string serial,
            string filePath,
            string? remoteDirectory,
            IProgress<double>? progress,
            CancellationToken ct = default)
        {
            progress?.Report(0.5);
            return Task.FromResult(true);
        }

        public Task<bool> PrepareFileTransferAsync(
            string serial,
            string? remoteDirectory = null,
            CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task SwipeAsync(
            string serial,
            int x1,
            int y1,
            int x2,
            int y2,
            int durationMs,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task KeyEventAsync(string serial, int keyCode, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
