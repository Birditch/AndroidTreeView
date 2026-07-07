namespace AndroidTreeView.Core.Exceptions;

/// <summary>
/// Thrown when a command targets a device that is currently offline.
/// </summary>
public sealed class DeviceOfflineException : AdbException
{
    public DeviceOfflineException(string serial)
        : base($"Device '{serial}' is offline.")
    {
        Serial = serial;
    }

    public DeviceOfflineException(string serial, string message)
        : base(message)
    {
        Serial = serial;
    }

    /// <summary>Serial of the offline device.</summary>
    public string Serial { get; }
}
