namespace AndroidTreeView.Core.Exceptions;

/// <summary>
/// Thrown when the adb executable cannot be located or is unavailable.
/// </summary>
public sealed class AdbNotFoundException : AdbException
{
    private const string DefaultMessage =
        "The Android Debug Bridge (adb) executable could not be located.";

    public AdbNotFoundException()
        : base(DefaultMessage)
    {
    }

    public AdbNotFoundException(string message)
        : base(message)
    {
    }

    public AdbNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
