using System.Text.RegularExpressions;
using AndroidTreeView.Models.Devices;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>
/// Parses <c>adb devices -l</c> output into <see cref="AdbDevice"/> entries.
/// Deterministic and stateless.
/// </summary>
public static partial class AdbDevicesParser
{
    private const string HeaderPrefix = "List of devices attached";

    // Long-listing descriptor keys emitted by `adb devices -l`.
    private static readonly HashSet<string> DescriptorKeys = new(StringComparer.Ordinal)
    {
        "product", "model", "device", "transport_id", "usb"
    };

    [GeneratedRegex(@"\b(product|model|device|transport_id|usb):(\S+)")]
    private static partial Regex DescriptorRegex();

    /// <summary>Parses the device listing. Header, blank and daemon lines are skipped.</summary>
    public static IReadOnlyList<AdbDevice> Parse(string? output)
    {
        var devices = new List<AdbDevice>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return devices;
        }

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (!IsDeviceLine(line))
            {
                continue;
            }

            var device = ParseLine(line);
            if (device is not null)
            {
                devices.Add(device);
            }
        }

        return devices;
    }

    private static bool IsDeviceLine(string line)
    {
        if (line.Length == 0)
        {
            return false;
        }

        if (line.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Daemon / server chatter starts with '*' or is an 'adb server ...' notice.
        if (line.StartsWith('*') ||
            (line.StartsWith("adb", StringComparison.OrdinalIgnoreCase) &&
             line.Contains("server", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // A device line always has a serial plus at least a state token.
        return line.IndexOfAny(new[] { ' ', '\t' }) > 0;
    }

    private static AdbDevice? ParseLine(string line)
    {
        var firstBreak = line.IndexOfAny(new[] { ' ', '\t' });
        if (firstBreak <= 0)
        {
            return null;
        }

        var serial = line[..firstBreak];
        var remainder = line[firstBreak..].Trim();

        var descriptors = ExtractDescriptors(ref remainder);
        var stateText = remainder.Trim();
        var (state, rawState) = MapState(stateText);

        descriptors.TryGetValue("model", out var model);
        descriptors.TryGetValue("product", out var product);
        descriptors.TryGetValue("device", out var codename);
        descriptors.TryGetValue("transport_id", out var transportId);
        descriptors.TryGetValue("usb", out var usb);

        return new AdbDevice
        {
            Serial = serial,
            State = state,
            RawState = rawState,
            Model = model,
            Product = product,
            Device = codename,
            TransportId = transportId,
            UsbPath = usb,
            Descriptors = descriptors
        };
    }

    private static Dictionary<string, string> ExtractDescriptors(ref string remainder)
    {
        var descriptors = new Dictionary<string, string>(StringComparer.Ordinal);
        var text = remainder;

        foreach (Match match in DescriptorRegex().Matches(text))
        {
            var key = match.Groups[1].Value;
            if (DescriptorKeys.Contains(key))
            {
                descriptors[key] = match.Groups[2].Value;
            }
        }

        // Remove matched descriptors so only the state phrase remains.
        remainder = DescriptorRegex().Replace(text, string.Empty);
        return descriptors;
    }

    private static (DeviceConnectionState State, string RawState) MapState(string stateText)
    {
        if (stateText.StartsWith("no permission", StringComparison.OrdinalIgnoreCase))
        {
            return (DeviceConnectionState.NoPermission, "no permissions");
        }

        var token = FirstToken(stateText);
        var state = token.ToLowerInvariant() switch
        {
            "device" => DeviceConnectionState.Online,
            "unauthorized" => DeviceConnectionState.Unauthorized,
            "offline" => DeviceConnectionState.Offline,
            "bootloader" => DeviceConnectionState.Bootloader,
            "recovery" => DeviceConnectionState.Recovery,
            "sideload" => DeviceConnectionState.Sideload,
            "authorizing" => DeviceConnectionState.Authorizing,
            "connecting" => DeviceConnectionState.Connecting,
            "disconnected" => DeviceConnectionState.Disconnected,
            _ => DeviceConnectionState.Unknown
        };

        return (state, token.Length == 0 ? "unknown" : token);
    }

    private static string FirstToken(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var breakIndex = trimmed.IndexOfAny(new[] { ' ', '\t' });
        return breakIndex < 0 ? trimmed : trimmed[..breakIndex];
    }
}
