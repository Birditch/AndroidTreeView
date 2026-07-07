namespace AndroidTreeView.Core.Services;

/// <summary>
/// Result of executing an <see cref="AdbCommandRequest"/>.
/// </summary>
public sealed class AdbCommandResult
{
    public int ExitCode { get; init; }

    public string StandardOutput { get; init; } = "";

    public string StandardError { get; init; } = "";

    /// <summary>True when the command was terminated because it exceeded its timeout.</summary>
    public bool TimedOut { get; init; }

    /// <summary>Wall-clock duration of the command.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>True when the command exited cleanly and did not time out.</summary>
    public bool IsSuccess => ExitCode == 0 && !TimedOut;
}
