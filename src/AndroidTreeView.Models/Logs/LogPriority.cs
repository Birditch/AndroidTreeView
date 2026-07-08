namespace AndroidTreeView.Models.Logs;

/// <summary>Android logcat priority levels ordered by severity.</summary>
public enum LogPriority
{
    Unknown = 0,
    Verbose = 1,
    Debug = 2,
    Info = 3,
    Warn = 4,
    Error = 5,
    Fatal = 6,
    Silent = 7
}
