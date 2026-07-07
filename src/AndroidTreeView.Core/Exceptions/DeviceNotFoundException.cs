namespace AndroidTreeView.Core.Exceptions;

/// <summary>
/// Thrown when a command references a serial that is not currently connected.
/// </summary>
public sealed class DeviceNotFoundException : AdbException
{
    public DeviceNotFoundException(string serial)
        : base($"Device '{serial}' was not found.")
    {
        Serial = serial;
    }

    public DeviceNotFoundException(string serial, string message)
        : base(message)
    {
        Serial = serial;
    }

    /// <summary>Serial that could not be found.</summary>
    public string Serial { get; }
}
