namespace AndroidTreeView.Models.Battery;

/// <summary>
/// Battery snapshot parsed from <c>dumpsys battery</c>.
/// </summary>
public sealed class BatteryInfo
{
    /// <summary>Charge level as a percentage (0..100), when derivable.</summary>
    public int? LevelPercent { get; init; }

    /// <summary>Raw level value before scaling.</summary>
    public int? RawLevel { get; init; }

    /// <summary>Scale the raw level is measured against (usually 100).</summary>
    public int? Scale { get; init; }

    public BatteryStatus Status { get; init; }
    public BatteryPluggedType Plugged { get; init; }
    public BatteryHealth Health { get; init; }

    /// <summary>Temperature in Celsius (dumpsys tenths-of-a-degree value divided by 10).</summary>
    public double? TemperatureCelsius { get; init; }

    public int? VoltageMillivolts { get; init; }
    public string? Technology { get; init; }
    public bool? Present { get; init; }

    /// <summary>
    /// Battery charge cycle count when the device exposes it (no root required, best-effort);
    /// <c>null</c> when unavailable. Never fabricated.
    /// </summary>
    public int? CycleCount { get; init; }

    /// <summary>True when charging or connected to any external power source.</summary>
    public bool IsCharging => Status == BatteryStatus.Charging || Plugged != BatteryPluggedType.None;
}
