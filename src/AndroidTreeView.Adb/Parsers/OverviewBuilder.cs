using System.Globalization;
using AndroidTreeView.Adb.Commands;
using AndroidTreeView.Models.Devices;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>
/// Builds a <see cref="DeviceOverview"/> from a parsed <c>getprop</c> dictionary.
/// Deterministic and stateless.
/// </summary>
public static class OverviewBuilder
{
    public static DeviceOverview Build(IReadOnlyDictionary<string, string> props, string? packageListOutput = null)
    {
        ArgumentNullException.ThrowIfNull(props);

        var manufacturer = Get(props, PropKeys.Manufacturer);
        var model = Get(props, PropKeys.Model);

        return new DeviceOverview
        {
            DisplayName = BuildDisplayName(manufacturer, model),
            Manufacturer = manufacturer,
            Brand = Get(props, PropKeys.Brand),
            Model = model,
            Product = Get(props, PropKeys.ProductName),
            Codename = Get(props, PropKeys.Device),
            SerialNumber = GetFirst(props, PropKeys.SerialKeys),
            AndroidVersion = Get(props, PropKeys.AndroidVersion),
            ApiLevel = GetInt(props, PropKeys.Sdk),
            BuildNumber = Get(props, PropKeys.BuildDisplayId),
            BuildFingerprint = Get(props, PropKeys.Fingerprint),
            SecurityPatch = Get(props, PropKeys.SecurityPatch),
            BuildTags = Get(props, PropKeys.BuildTags),
            BuildType = Get(props, PropKeys.BuildType),
            OemUnlockSupported = GetBool(props, PropKeys.OemUnlockSupported),
            OemUnlockAllowed = GetBool(props, PropKeys.OemUnlockAllowed),
            BootloaderLockState = GetBootloaderLockState(props),
            DeviceState = NormalizeState(GetFirst(props, PropKeys.DeviceStateKeys)),
            VerifiedBootState = NormalizeState(Get(props, PropKeys.VerifiedBootState)),
            MagiskInstalled = ContainsMagiskPackage(packageListOutput)
        };
    }

    private static string? BuildDisplayName(string? manufacturer, string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return manufacturer;
        }

        if (string.IsNullOrWhiteSpace(manufacturer) ||
            model.Contains(manufacturer, StringComparison.OrdinalIgnoreCase))
        {
            return model;
        }

        return $"{manufacturer} {model}";
    }

    private static string? Get(IReadOnlyDictionary<string, string> props, string key) =>
        props.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    private static string? GetFirst(IReadOnlyDictionary<string, string> props, string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Get(props, key);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static int? GetInt(IReadOnlyDictionary<string, string> props, string key) =>
        props.TryGetValue(key, out var value) &&
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static bool? GetBool(IReadOnlyDictionary<string, string> props, string key) =>
        Get(props, key) is { } value ? ParseBool(value) : null;

    private static bool? ParseBool(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "1" or "true" or "yes" or "y" or "on" or "enabled" or "allowed" => true,
            "0" or "false" or "no" or "n" or "off" or "disabled" or "disallowed" => false,
            _ => null
        };
    }

    private static string? GetBootloaderLockState(IReadOnlyDictionary<string, string> props)
    {
        if (Get(props, PropKeys.BootFlashLocked) is { } flashLocked)
        {
            return ParseBool(flashLocked) switch
            {
                true => "locked",
                false => "unlocked",
                _ => NormalizeState(flashLocked)
            };
        }

        if (Get(props, PropKeys.BootUnlocked) is { } unlocked)
        {
            return ParseBool(unlocked) switch
            {
                true => "unlocked",
                false => "locked",
                _ => NormalizeState(unlocked)
            };
        }

        return null;
    }

    private static string? NormalizeState(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static bool ContainsMagiskPackage(string? packageListOutput)
    {
        if (string.IsNullOrWhiteSpace(packageListOutput))
        {
            return false;
        }

        foreach (var rawLine in packageListOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
            {
                line = line["package:".Length..];
            }

            if (line.Equals("com.topjohnwu.magisk", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("magisk", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
