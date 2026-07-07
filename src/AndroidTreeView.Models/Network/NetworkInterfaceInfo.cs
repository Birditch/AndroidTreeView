namespace AndroidTreeView.Models.Network;

/// <summary>
/// A single network interface parsed from <c>ip addr</c> / <c>ifconfig</c>.
/// </summary>
public sealed class NetworkInterfaceInfo
{
    /// <summary>Interface name, e.g. "wlan0".</summary>
    public required string Name { get; init; }

    public string? IpAddress { get; init; }
    public string? MacAddress { get; init; }

    /// <summary>Link state, e.g. "UP" / "DOWN".</summary>
    public string? State { get; init; }
}
