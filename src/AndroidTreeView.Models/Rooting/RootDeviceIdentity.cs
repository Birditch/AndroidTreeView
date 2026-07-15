namespace AndroidTreeView.Models.Rooting;

/// <summary>ADB identity locked at the start of a root session.</summary>
public sealed record RootDeviceIdentity
{
    public required string Serial { get; init; }

    public string? UsbPath { get; init; }

    public string? Product { get; init; }

    public string? Device { get; init; }

    public string? Model { get; init; }

    public string? TransportId { get; init; }
}
