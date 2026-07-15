namespace AndroidTreeView.Core.Interfaces;

/// <summary>
/// Optional extension for device file services that can surface live push progress.
/// </summary>
public interface IProgressiveFileTransferService
{
    /// <summary>Pushes a local file and reports per-file progress from 0.0 to 1.0 when available.</summary>
    Task<bool> PushFileAsync(
        string serial,
        string filePath,
        string? remoteDirectory,
        IProgress<double>? progress,
        CancellationToken ct = default);
}
