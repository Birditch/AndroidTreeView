namespace AndroidTreeView.Models.Network;

/// <summary>
/// Aggregated networking summary for a device.
/// </summary>
public sealed class NetworkInfo
{
    public string? WifiIpAddress { get; init; }
    public string? WifiMacAddress { get; init; }
    public string? MobileNetworkState { get; init; }

    public IReadOnlyList<NetworkInterfaceInfo> Interfaces { get; init; } = Array.Empty<NetworkInterfaceInfo>();
    public IReadOnlyList<string> DnsServers { get; init; } = Array.Empty<string>();
}
