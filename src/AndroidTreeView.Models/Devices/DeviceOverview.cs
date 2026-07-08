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

    /// <summary>Whether the device advertises OEM unlock support (<c>ro.oem_unlock_supported</c>).</summary>
    public bool? OemUnlockSupported { get; init; }

    /// <summary>Whether OEM unlocking is currently allowed in Android settings (<c>sys.oem_unlock_allowed</c>).</summary>
    public bool? OemUnlockAllowed { get; init; }

    /// <summary>Bootloader lock state derived from boot properties such as <c>ro.boot.flash.locked</c>.</summary>
    public string? BootloaderLockState { get; init; }

    /// <summary>AVB / vbmeta device state, commonly <c>locked</c> or <c>unlocked</c>.</summary>
    public string? DeviceState { get; init; }

    /// <summary>Verified Boot color/state from <c>ro.boot.verifiedbootstate</c>.</summary>
    public string? VerifiedBootState { get; init; }

    /// <summary>True when the installed package list contains Magisk.</summary>
    public bool MagiskInstalled { get; init; }
}
