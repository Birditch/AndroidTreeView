namespace AndroidTreeView.Core.Services;

/// <summary>Buffered result of a completed external process invocation.</summary>
public sealed class ExternalCommandResult
{
    public int ExitCode { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;

    /// <summary>True when the command was terminated after exceeding its timeout.</summary>
    public bool TimedOut { get; init; }

    /// <summary>Wall-clock duration of the command.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>True when the command exited cleanly before its timeout.</summary>
    public bool IsSuccess => ExitCode == 0 && !TimedOut;
}
