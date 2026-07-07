namespace AndroidTreeView.Core.Exceptions;

/// <summary>
/// Thrown when ADB command output cannot be parsed into the expected shape.
/// </summary>
public sealed class OutputParseException : AdbException
{
    public OutputParseException()
        : base("Failed to parse ADB command output.")
    {
    }

    public OutputParseException(string message)
        : base(message)
    {
    }

    public OutputParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
