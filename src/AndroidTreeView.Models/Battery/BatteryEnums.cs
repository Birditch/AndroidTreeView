namespace AndroidTreeView.Models.Battery;

/// <summary>Charging status as reported by <c>dumpsys battery</c> (status field 1..5).</summary>
public enum BatteryStatus
{
    Unknown,
    Charging,
    Discharging,
    NotCharging,
    Full
}

/// <summary>Battery health as reported by <c>dumpsys battery</c> (health field).</summary>
public enum BatteryHealth
{
    Unknown,
    Good,
    Overheat,
    Dead,
    OverVoltage,
    UnspecifiedFailure,
    Cold
}

/// <summary>Power source the device is plugged into (plugged field).</summary>
public enum BatteryPluggedType
{
    None,
    Ac,
    Usb,
    Wireless,
    Dock
}
