using AndroidTreeView.Models.Rooting;

namespace AndroidTreeView.Core.Interfaces;

/// <summary>Creates a verified local backup of an original boot image before any device write.</summary>
public interface IBootBackupService
{
    Task<string> BackupAsync(
        string sourcePath,
        string serial,
        BootPartitionTarget target,
        CancellationToken ct = default);
}
