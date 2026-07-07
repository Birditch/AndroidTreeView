namespace AndroidTreeView.Models.System;

/// <summary>
/// Operating-system / kernel level information for a device.
/// </summary>
public sealed class SystemInfo
{
    public string? KernelVersion { get; init; }

    /// <summary>SELinux status (Enforcing / Permissive) from <c>getenforce</c>.</summary>
    public string? SelinuxStatus { get; init; }

    /// <summary>Device uptime derived from <c>uptime</c>.</summary>
    public TimeSpan? Uptime { get; init; }

    public string? Bootloader { get; init; }

    /// <summary><c>ro.boot.verifiedbootstate</c>.</summary>
    public string? VerifiedBootState { get; init; }

    public string? BuildTags { get; init; }
    public string? BuildType { get; init; }
    public string? Locale { get; init; }
    public string? Timezone { get; init; }
    public int? SdkVersion { get; init; }
}
