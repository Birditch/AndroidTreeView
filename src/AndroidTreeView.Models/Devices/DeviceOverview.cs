namespace AndroidTreeView.Models.Devices;

/// <summary>
/// High-level identity/build summary for a device, built from <c>getprop</c> values.
/// </summary>
public sealed class DeviceOverview
{
    public string? DisplayName { get; init; }
    public string? Manufacturer { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public string? Product { get; init; }

    /// <summary><c>ro.product.device</c>.</summary>
    public string? Codename { get; init; }

    public string? SerialNumber { get; init; }

    /// <summary><c>ro.build.version.release</c>.</summary>
    public string? AndroidVersion { get; init; }

    /// <summary><c>ro.build.version.sdk</c>.</summary>
    public int? ApiLevel { get; init; }

    /// <summary><c>ro.build.display.id</c>.</summary>
    public string? BuildNumber { get; init; }

    public string? BuildFingerprint { get; init; }
    public string? SecurityPatch { get; init; }
    public string? BuildTags { get; init; }
    public string? BuildType { get; init; }
}
