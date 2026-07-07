namespace AndroidTreeView.Models;

/// <summary>
/// Raw <c>getprop</c> key/value snapshot for a device.
/// </summary>
public sealed class DeviceProperties
{
    /// <summary>All property key/value pairs read from the device.</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();

    /// <summary>Number of properties captured.</summary>
    public int Count => Values.Count;
}
