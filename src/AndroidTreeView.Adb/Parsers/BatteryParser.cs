using System.Globalization;
using AndroidTreeView.Models.Battery;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>
/// Parses <c>dumpsys battery</c> output into a <see cref="BatteryInfo"/>. Deterministic and stateless.
/// </summary>
public static class BatteryParser
{
    /// <summary>
    /// Parses the dumpsys text. When the dump does not report a cycle count,
    /// <paramref name="fallbackCycleCount"/> (typically read from sysfs) is used instead.
    /// </summary>
    public static BatteryInfo Parse(string? output, int? fallbackCycleCount = null)
    {
        var values = ReadKeyValues(output);

        int? rawLevel = ReadInt(values, "level");
        int? scale = ReadInt(values, "scale");
        int? cycle = ReadInt(values, "cycle count") ?? ReadInt(values, "cycle_count");

        return new BatteryInfo
        {
            RawLevel = rawLevel,
            Scale = scale,
            LevelPercent = ComputeLevelPercent(rawLevel, scale),
            Status = MapStatus(ReadInt(values, "status")),
            Health = MapHealth(ReadInt(values, "health")),
            Plugged = MapPlugged(values),
            TemperatureCelsius = ComputeTemperature(ReadInt(values, "temperature")),
            VoltageMillivolts = ReadInt(values, "voltage"),
            Technology = ReadString(values, "technology"),
            Present = ReadBool(values, "present"),
            CycleCount = cycle ?? fallbackCycleCount
        };
    }

    private static int? ComputeLevelPercent(int? rawLevel, int? scale)
    {
        if (rawLevel is null)
        {
            return null;
        }

        if (scale is > 0 && scale != 100)
        {
            return (int)Math.Round(rawLevel.Value * 100.0 / scale.Value);
        }

        return rawLevel;
    }

    private static double? ComputeTemperature(int? tenths) =>
        tenths is null ? null : tenths.Value / 10.0;

    private static BatteryStatus MapStatus(int? value) => value switch
    {
        1 => BatteryStatus.Unknown,
        2 => BatteryStatus.Charging,
        3 => BatteryStatus.Discharging,
        4 => BatteryStatus.NotCharging,
        5 => BatteryStatus.Full,
        _ => BatteryStatus.Unknown
    };

    private static BatteryHealth MapHealth(int? value) => value switch
    {
        1 => BatteryHealth.Unknown,
        2 => BatteryHealth.Good,
        3 => BatteryHealth.Overheat,
        4 => BatteryHealth.Dead,
        5 => BatteryHealth.OverVoltage,
        6 => BatteryHealth.UnspecifiedFailure,
        7 => BatteryHealth.Cold,
        _ => BatteryHealth.Unknown
    };

    private static BatteryPluggedType MapPlugged(IReadOnlyDictionary<string, string> values)
    {
        var plugged = ReadInt(values, "plugged");
        if (plugged is not null)
        {
            return plugged switch
            {
                1 => BatteryPluggedType.Ac,
                2 => BatteryPluggedType.Usb,
                4 => BatteryPluggedType.Wireless,
                8 => BatteryPluggedType.Dock,
                _ => BatteryPluggedType.None
            };
        }

        // Fall back to the boolean "* powered" flags when no numeric plugged field is present.
        if (ReadBool(values, "ac powered") == true)
        {
            return BatteryPluggedType.Ac;
        }

        if (ReadBool(values, "usb powered") == true)
        {
            return BatteryPluggedType.Usb;
        }

        if (ReadBool(values, "wireless powered") == true)
        {
            return BatteryPluggedType.Wireless;
        }

        return BatteryPluggedType.None;
    }

    private static Dictionary<string, string> ReadKeyValues(string? output)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(output))
        {
            return values;
        }

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length > 0)
            {
                values[key] = value;
            }
        }

        return values;
    }

    private static int? ReadInt(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var text) &&
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string? ReadString(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var text) && text.Length > 0 ? text : null;

    private static bool? ReadBool(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var text))
        {
            return null;
        }

        return text.Trim().ToLowerInvariant() switch
        {
            "true" => true,
            "false" => false,
            _ => null
        };
    }
}
