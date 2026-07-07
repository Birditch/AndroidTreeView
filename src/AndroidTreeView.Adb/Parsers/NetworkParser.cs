using System.Text.RegularExpressions;
using AndroidTreeView.Models.Network;

namespace AndroidTreeView.Adb.Parsers;

/// <summary>
/// Parses <c>ip addr</c> output into network interfaces and extracts Wi-Fi details.
/// Deterministic and stateless.
/// </summary>
public static partial class NetworkParser
{
    [GeneratedRegex(@"^\d+:\s+([^:@\s]+)(?:@\S+)?:\s+<([^>]*)>")]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"link/\w+\s+([0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5})")]
    private static partial Regex MacRegex();

    [GeneratedRegex(@"inet\s+(\d{1,3}(?:\.\d{1,3}){3})")]
    private static partial Regex Inet4Regex();

    [GeneratedRegex(@"^([0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5})$")]
    private static partial Regex MacOnlyRegex();

    /// <summary>Parses interfaces from <c>ip addr</c> output.</summary>
    public static IReadOnlyList<NetworkInterfaceInfo> Parse(string? output)
    {
        var interfaces = new List<NetworkInterfaceInfo>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return interfaces;
        }

        string? name = null;
        string? state = null;
        string? mac = null;
        string? ip = null;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd();
            var header = HeaderRegex().Match(line.TrimStart());

            if (header.Success)
            {
                FlushInterface(interfaces, name, state, mac, ip);

                name = header.Groups[1].Value;
                state = header.Groups[2].Value.Contains("UP", StringComparison.Ordinal) ? "UP" : "DOWN";
                mac = null;
                ip = null;
                continue;
            }

            if (name is null)
            {
                continue;
            }

            var trimmed = line.TrimStart();
            var macMatch = MacRegex().Match(trimmed);
            if (macMatch.Success)
            {
                mac ??= macMatch.Groups[1].Value;
            }

            if (trimmed.StartsWith("inet ", StringComparison.Ordinal))
            {
                var ipMatch = Inet4Regex().Match(trimmed);
                if (ipMatch.Success)
                {
                    ip ??= ipMatch.Groups[1].Value;
                }
            }
        }

        FlushInterface(interfaces, name, state, mac, ip);
        return interfaces;
    }

    /// <summary>Finds the Wi-Fi (wlan*) interface, if present.</summary>
    public static NetworkInterfaceInfo? FindWifi(IEnumerable<NetworkInterfaceInfo> interfaces) =>
        interfaces.FirstOrDefault(i => i.Name.StartsWith("wlan", StringComparison.OrdinalIgnoreCase));

    /// <summary>Parses a raw MAC address value (e.g. from a sysfs read); returns null when invalid.</summary>
    public static string? ParseMacAddress(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var candidate = output.Trim();
        return MacOnlyRegex().IsMatch(candidate) ? candidate : null;
    }

    private static void FlushInterface(
        ICollection<NetworkInterfaceInfo> interfaces,
        string? name,
        string? state,
        string? mac,
        string? ip)
    {
        if (name is null)
        {
            return;
        }

        interfaces.Add(new NetworkInterfaceInfo
        {
            Name = name,
            State = state,
            MacAddress = mac,
            IpAddress = ip
        });
    }
}
