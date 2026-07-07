using AndroidTreeView.Models.Logs;

namespace AndroidTreeView.Core.Options;

/// <summary>
/// Options controlling a logcat streaming session.
/// </summary>
public sealed class LogcatOptions
{
    /// <summary>Minimum priority to include in the stream.</summary>
    public LogPriority MinPriority { get; init; } = LogPriority.Verbose;

    /// <summary>When true, the buffer is cleared before streaming begins.</summary>
    public bool ClearBeforeStart { get; init; }

    /// <summary>Optional tag filter applied to the stream.</summary>
    public string? TagFilter { get; init; }
}
