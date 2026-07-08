namespace AndroidTreeView.Models.Logs;

/// <summary>Parsed entry from <c>logcat -v threadtime</c>.</summary>
public sealed class LogcatEntry
{
    /// <summary>Timestamp text as emitted by logcat, for example <c>06-25 14:23:01.123</c>.</summary>
    public string? Timestamp { get; init; }

    /// <summary>Process id parsed from the logcat line.</summary>
    public int? Pid { get; init; }

    /// <summary>Thread id parsed from the logcat line.</summary>
    public int? Tid { get; init; }

    /// <summary>Priority/severity of the entry.</summary>
    public LogPriority Priority { get; init; } = LogPriority.Unknown;

    /// <summary>Log tag, when the line was parseable.</summary>
    public string? Tag { get; init; }

    /// <summary>Log message body, or the raw line when parsing fails.</summary>
    public string Message { get; init; } = string.Empty;
}
