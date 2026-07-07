using AndroidTreeView.Models.Devices;

namespace AndroidTreeView.Core.Interfaces;

/// <summary>
/// Payload raised whenever the monitored device list changes (or adb availability changes).
/// </summary>
public sealed class DeviceListChangedEventArgs : EventArgs
{
    /// <summary>The current device snapshot.</summary>
    public required IReadOnlyList<AdbDevice> Devices { get; init; }

    /// <summary>True when adb was reachable when this snapshot was produced.</summary>
    public bool AdbAvailable { get; init; }

    /// <summary>Error message describing why the snapshot failed, when applicable.</summary>
    public string? Error { get; init; }
}
