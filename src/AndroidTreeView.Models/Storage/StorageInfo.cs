namespace AndroidTreeView.Models.Storage;

/// <summary>
/// Collection of mounted storage partitions for a device.
/// </summary>
public sealed class StorageInfo
{
    public IReadOnlyList<StoragePartition> Partitions { get; init; } = Array.Empty<StoragePartition>();
}
