namespace AndroidTreeView.Models.Storage;

/// <summary>
/// A single mounted filesystem entry parsed from <c>df</c>.
/// </summary>
public sealed class StoragePartition
{
    /// <summary>Filesystem device name or friendly label.</summary>
    public required string Name { get; init; }

    public string? MountPoint { get; init; }
    public long? TotalBytes { get; init; }
    public long? UsedBytes { get; init; }
    public long? AvailableBytes { get; init; }

    /// <summary>Percentage of the partition in use (0..100).</summary>
    public double? UsePercent { get; init; }
}
