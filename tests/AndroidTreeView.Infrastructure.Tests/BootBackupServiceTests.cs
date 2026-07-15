using AndroidTreeView.Core.Exceptions;
using AndroidTreeView.Infrastructure.Rooting;
using AndroidTreeView.Models.Rooting;
using Xunit;

namespace AndroidTreeView.Infrastructure.Tests;

public sealed class BootBackupServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "AndroidTreeView-backup-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BackupAsync_CopiesContentAndReturnsAbsoluteUniquePath()
    {
        var source = await CreateSourceAsync("boot.img", [1, 2, 3, 4, 5]);
        var service = new BootBackupService(Path.Combine(_directory, "backups"));

        var first = await service.BackupAsync(source, "SERIAL-1", BootPartitionTarget.Boot);
        var second = await service.BackupAsync(source, "SERIAL-1", BootPartitionTarget.Boot);

        Assert.True(Path.IsPathFullyQualified(first));
        Assert.NotEqual(first, second);
        Assert.Equal(await File.ReadAllBytesAsync(source), await File.ReadAllBytesAsync(first));
        Assert.Contains("SERIAL-1-", Path.GetFileName(first), StringComparison.Ordinal);
        Assert.Contains("-boot-", Path.GetFileName(first), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BackupAsync_SanitizesSerialForEveryPlatform()
    {
        var source = await CreateSourceAsync("init_boot.img", [7, 8, 9]);
        var service = new BootBackupService(Path.Combine(_directory, "backups"));

        var backup = await service.BackupAsync(source, " ../USB:123\\name ", BootPartitionTarget.InitBoot);

        var fileName = Path.GetFileName(backup);
        Assert.StartsWith("_USB_123_name-", fileName, StringComparison.Ordinal);
        Assert.Contains("-init_boot-", fileName, StringComparison.Ordinal);
        Assert.DoesNotContain("..", fileName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BackupAsync_MissingSourceThrowsStableErrorCode()
    {
        var service = new BootBackupService(Path.Combine(_directory, "backups"));

        var exception = await Assert.ThrowsAsync<RootWorkflowException>(() =>
            service.BackupAsync(
                Path.Combine(_directory, "missing.img"),
                "SERIAL",
                BootPartitionTarget.Boot));

        Assert.Equal(RootErrorCode.BackupSourceMissing, exception.ErrorCode);
    }

    [Fact]
    public async Task BackupAsync_PreCancelledDoesNotLeaveFiles()
    {
        var source = await CreateSourceAsync("boot.img", new byte[1024]);
        var backupDirectory = Path.Combine(_directory, "backups");
        var service = new BootBackupService(backupDirectory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.BackupAsync(source, "SERIAL", BootPartitionTarget.Boot, cancellation.Token));

        Assert.False(Directory.Exists(backupDirectory));
    }

    [Fact]
    public async Task BackupAsync_UnknownTargetDoesNotCopy()
    {
        var source = await CreateSourceAsync("boot.img", [1]);
        var backupDirectory = Path.Combine(_directory, "backups");
        var service = new BootBackupService(backupDirectory);

        var exception = await Assert.ThrowsAsync<RootWorkflowException>(() =>
            service.BackupAsync(source, "SERIAL", BootPartitionTarget.Unknown));

        Assert.Equal(RootErrorCode.TargetPartitionUnknown, exception.ErrorCode);
        Assert.False(Directory.Exists(backupDirectory));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup for transient test runner file locks.
        }
    }

    private async Task<string> CreateSourceAsync(string fileName, byte[] content)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, fileName);
        await File.WriteAllBytesAsync(path, content);
        return path;
    }
}
