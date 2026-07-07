namespace AndroidTreeView.Core.Exceptions;

/// <summary>
/// Thrown when an ADB command completes with a non-zero exit code that cannot be mapped
/// to a more specific device error.
/// </summary>
public sealed class AdbCommandFailedException : AdbException
{
    public AdbCommandFailedException(string message, int exitCode, string? standardError = null)
        : base(message)
    {
        ExitCode = exitCode;
        StandardError = standardError;
    }

    public AdbCommandFailedException(string message, int exitCode, string? standardError, Exception innerException)
        : base(message, innerException)
    {
        ExitCode = exitCode;
        StandardError = standardError;
    }

    /// <summary>Process exit code returned by adb.</summary>
    public int ExitCode { get; }

    /// <summary>Captured standard error output, when available.</summary>
    public string? StandardError { get; }
}
