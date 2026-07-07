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
    public static DeviceOverview Build(IReadOnlyDictionary<string, string> props)
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
            BuildType = Get(props, PropKeys.BuildType)
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
}
