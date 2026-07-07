namespace AndroidTreeView.Core.Options;

/// <summary>
/// Static defaults / knobs for ADB command execution.
/// </summary>
public sealed class AdbOptions
{
    /// <summary>Default timeout applied to a command when the request does not specify one.</summary>
    public TimeSpan DefaultCommandTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Timeout for the device-listing command.</summary>
    public TimeSpan DeviceListTimeout { get; set; } = TimeSpan.FromSeconds(8);
}
