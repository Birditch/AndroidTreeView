namespace AndroidTreeView.Models.Hardware;

/// <summary>
/// CPU details parsed from <c>/proc/cpuinfo</c>.
/// </summary>
public sealed class CpuInfo
{
    public string? Model { get; init; }
    public string? Hardware { get; init; }
    public string? Architecture { get; init; }
    public int? CoreCount { get; init; }

    /// <summary>CPU feature flags (e.g. neon, aes).</summary>
    public IReadOnlyList<string> Features { get; init; } = Array.Empty<string>();
}
