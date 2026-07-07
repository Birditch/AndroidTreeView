namespace AndroidTreeView.Models.Devices;

/// <summary>
/// Lightweight device entry parsed from <c>adb devices -l</c>.
/// </summary>
public sealed class AdbDevice
{
    /// <summary>Device serial / transport identifier (unique per connected device).</summary>
    public required string Serial { get; init; }

    /// <summary>Normalized connection state.</summary>
    public DeviceConnectionState State { get; init; }

    /// <summary>Original state token as printed by adb (e.g. "device", "unauthorized").</summary>
    public string? RawState { get; init; }

    /// <summary>Marketing model descriptor (<c>model:...</c>).</summary>
    public string? Model { get; init; }

    /// <summary>Product descriptor (<c>product:...</c>).</summary>
    public string? Product { get; init; }

    /// <summary>Device codename descriptor (<c>device:...</c>).</summary>
    public string? Device { get; init; }

    /// <summary>Transport id descriptor (<c>transport_id:...</c>).</summary>
    public string? TransportId { get; init; }

    /// <summary>USB path descriptor (<c>usb:...</c>).</summary>
    public string? UsbPath { get; init; }

    /// <summary>All descriptor key/value pairs parsed from the <c>-l</c> long listing.</summary>
    public IReadOnlyDictionary<string, string> Descriptors { get; init; }
        = new Dictionary<string, string>();

    /// <summary>True when the device is fully online and usable.</summary>
    public bool IsOnline => State == DeviceConnectionState.Online;

    /// <summary>False only when the device explicitly reports an unauthorized state.</summary>
    public bool IsAuthorized => State != DeviceConnectionState.Unauthorized;

    /// <summary>Friendly name for the UI: the model when present, otherwise the serial.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Model) ? Serial : Model!;
}
