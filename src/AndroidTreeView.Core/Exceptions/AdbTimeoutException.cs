namespace AndroidTreeView.Core.Exceptions;

/// <summary>
/// Thrown when an ADB command exceeds its allotted timeout.
/// </summary>
public sealed class AdbTimeoutException : AdbException
{
    public AdbTimeoutException()
        : base("The ADB command timed out.")
    {
    }

    public AdbTimeoutException(string message)
        : base(message)
    {
    }

    public AdbTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
