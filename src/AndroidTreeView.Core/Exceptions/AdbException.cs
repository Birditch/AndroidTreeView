namespace AndroidTreeView.Core.Exceptions;

/// <summary>
/// Base type for all ADB-related failures surfaced by the Core/Adb layers.
/// </summary>
public class AdbException : Exception
{
    public AdbException()
    {
    }

    public AdbException(string message)
        : base(message)
    {
    }

    public AdbException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
