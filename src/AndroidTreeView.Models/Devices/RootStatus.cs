namespace AndroidTreeView.Models.Devices;

/// <summary>Confidence level of the root detection heuristics.</summary>
public enum RootDetectionLevel
{
    Unknown,
    NotRooted,
    Likely,
    Confirmed
}

/// <summary>
/// Result of root / superuser detection on a device.
/// </summary>
public sealed class RootStatus
{
    /// <summary>True when an <c>su</c> binary was located on the device.</summary>
    public bool SuBinaryExists { get; init; }

    /// <summary>True when <c>su -c id</c> returned <c>uid=0</c>.</summary>
    public bool SuGrantsRoot { get; init; }

    /// <summary>Output of <c>id</c> for the current shell user.</summary>
    public string? CurrentUserId { get; init; }

    /// <summary>Output of <c>su -c id</c>, when available.</summary>
    public string? RootUserId { get; init; }

    public string? MagiskVersion { get; init; }

    /// <summary>SELinux mode (Enforcing / Permissive).</summary>
    public string? SelinuxMode { get; init; }

    public RootDetectionLevel Level { get; init; }

    /// <summary>True when the device is confirmed or likely rooted.</summary>
    public bool IsRooted => Level is RootDetectionLevel.Confirmed or RootDetectionLevel.Likely;
}
