namespace AndroidTreeView.Mini.Mac;

public enum MiniLogLevel
{
    Info,
    Success,
    Warn,
    Error
}

public sealed class MiniLogEntry
{
    public MiniLogEntry(string time, string message, MiniLogLevel level)
    {
        Time = time;
        Message = message;
        Level = level;
    }

    public string Time { get; }

    public string Message { get; }

    public MiniLogLevel Level { get; }

    public string Display => $"[{Time}] {Message}";
}
