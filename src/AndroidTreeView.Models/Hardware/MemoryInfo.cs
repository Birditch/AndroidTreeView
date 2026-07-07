namespace AndroidTreeView.Models.Hardware;

/// <summary>
/// System memory totals parsed from <c>/proc/meminfo</c> (bytes).
/// </summary>
public sealed class MemoryInfo
{
    public long? TotalBytes { get; init; }
    public long? AvailableBytes { get; init; }
    public long? FreeBytes { get; init; }
}
