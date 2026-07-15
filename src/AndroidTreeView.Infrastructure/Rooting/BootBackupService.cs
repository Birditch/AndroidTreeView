using AndroidTreeView.Core.Exceptions;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models.Rooting;

namespace AndroidTreeView.Infrastructure.Rooting;

/// <summary>Creates durable, verified copies of original boot images before they are patched or flashed.</summary>
public sealed class BootBackupService : IBootBackupService
{
    private const string DefaultDeviceName = "device";
    private readonly string _backupDirectory;

    public BootBackupService()
        : this(DefaultBackupDirectory())
    {
    }

    internal BootBackupService(string backupDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        _backupDirectory = Path.GetFullPath(backupDirectory);
    }

    public async Task<string> BackupAsync(
        string sourcePath,
        string serial,
        BootPartitionTarget target,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        if (target == BootPartitionTarget.Unknown)
        {
            throw new RootWorkflowException(
                RootErrorCode.TargetPartitionUnknown,
                "A known boot partition is required before creating a backup.");
        }

        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new RootWorkflowException(
                RootErrorCode.BackupSourceMissing,
                "The original boot image does not exist.");
        }

        ct.ThrowIfCancellationRequested();
        var fileName = BuildFileName(serial, target);
        var destinationPath = Path.Combine(_backupDirectory, fileName);
        var temporaryPath = Path.Combine(
            _backupDirectory,
            $".{fileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(_backupDirectory);
            var sourceLength = new FileInfo(fullSourcePath).Length;
            await using (var source = new FileStream(
                fullSourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true))
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await source.CopyToAsync(destination, ct).ConfigureAwait(false);
                await destination.FlushAsync(ct).ConfigureAwait(false);
            }

            if (new FileInfo(temporaryPath).Length != sourceLength)
            {
                throw new RootWorkflowException(
                    RootErrorCode.BackupVerificationFailed,
                    "The boot image backup length did not match its source.");
            }

            ct.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destinationPath);
            return destinationPath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RootWorkflowException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new RootWorkflowException(
                RootErrorCode.BackupFailed,
                "The original boot image could not be backed up.",
                ex);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    internal static string SanitizeSerial(string serial)
    {
        var sanitized = new string(serial
            .Trim()
            .Select(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'
                    ? character
                    : '_')
            .ToArray())
            .Trim('.');

        return string.IsNullOrWhiteSpace(sanitized) ? DefaultDeviceName : sanitized;
    }

    private static string DefaultBackupDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            userProfile = AppContext.BaseDirectory;
        }

        return Path.Combine(userProfile, ".androidtreeview", "root-backups");
    }

    private static string BuildFileName(string serial, BootPartitionTarget target)
    {
        var targetName = target == BootPartitionTarget.InitBoot ? "init_boot" : "boot";
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'");
        return $"{SanitizeSerial(serial)}-{timestamp}-{targetName}-{Guid.NewGuid():N}-original.img";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The completed backup, if any, is never deleted because temporary cleanup failed.
        }
    }
}
