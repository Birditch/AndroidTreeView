namespace AndroidTreeView.Mini.Models;

/// <summary>严重级别 (severity level) of a single terminal log line.</summary>
public enum MiniLogLevel
{
    /// <summary>普通信息 (informational, light gray).</summary>
    Info,

    /// <summary>成功 (success, green).</summary>
    Success,

    /// <summary>警告 (warning, amber).</summary>
    Warn,

    /// <summary>错误 (error, red).</summary>
    Error
}

/// <summary>
/// 一条彩色终端日志 (one colored terminal log row): timestamp, message and its <see cref="MiniLogLevel"/>.
/// Immutable so it is safe to hold in an <c>ObservableCollection</c> bound to the UI.
/// </summary>
public sealed class MiniLogEntry
{
    public MiniLogEntry(string time, string message, MiniLogLevel level)
    {
        Time = time;
        Message = message;
        Level = level;
    }

    /// <summary>时间戳 (HH:mm:ss).</summary>
    public string Time { get; }

    /// <summary>日志正文 (log message body).</summary>
    public string Message { get; }

    /// <summary>颜色级别 (color level).</summary>
    public MiniLogLevel Level { get; }

    /// <summary>Rendered "[HH:mm:ss] message" line shown in the terminal.</summary>
    public string Display => $"[{Time}] {Message}";
}
