namespace AndroidTreeView.Core.Services;

/// <summary>
/// A resolved adb executable location and how it was found.
/// </summary>
public sealed class AdbLocation
{
    /// <summary>Absolute path to the adb executable.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>Version string reported by <c>adb version</c>, when known.</summary>
    public string? Version { get; init; }

    /// <summary>How this location was discovered.</summary>
    public AdbLocationSource Source { get; init; }
}
