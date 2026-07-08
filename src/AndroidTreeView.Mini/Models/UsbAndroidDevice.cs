namespace AndroidTreeView.Mini.Models;

/// <summary>
/// An Android phone seen on the Windows USB bus (via SetupAPI), independent of adb. This is populated even
/// when USB debugging is OFF, so the Mini can identify the phone and guide the user to enable debugging.
/// </summary>
public sealed class UsbAndroidDevice
{
    public UsbAndroidDevice(string key, string? manufacturer, string? name, string? serial, bool hasAdbInterface)
    {
        Key = key;
        Manufacturer = manufacturer;
        Name = name;
        Serial = serial;
        HasAdbInterface = hasAdbInterface;
    }

    /// <summary>Stable identity used to de-dupe guidance (serial when known, else VID:PID).</summary>
    public string Key { get; }

    /// <summary>Brand resolved from the USB vendor id (e.g. Google / Samsung / Xiaomi), or null.</summary>
    public string? Manufacturer { get; }

    /// <summary>USB friendly name / device description (approximate model), or null.</summary>
    public string? Name { get; }

    /// <summary>USB serial (iSerial) when the device exposes one, else null.</summary>
    public string? Serial { get; }

    /// <summary>True when the Android ADB USB interface is present → USB debugging is enabled.</summary>
    public bool HasAdbInterface { get; }
}
