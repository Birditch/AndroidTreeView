namespace AndroidTreeView.Core.Exceptions;

/// <summary>
/// Thrown when a device rejects a command because USB debugging has not been authorized.
/// </summary>
public sealed class DeviceUnauthorizedException : AdbException
{
    public DeviceUnauthorizedException(string serial)
        : base($"Device '{serial}' is unauthorized. Accept the USB debugging prompt on the device.")
    {
        Serial = serial;
    }

    public DeviceUnauthorizedException(string serial, string message)
        : base(message)
    {
        Serial = serial;
    }

    /// <summary>Serial of the unauthorized device.</summary>
    public string Serial { get; }
}
