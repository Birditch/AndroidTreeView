namespace AndroidTreeView.Core.Services;

/// <summary>
/// Describes a single adb invocation. Use <see cref="Shell"/> for device shell commands
/// and <see cref="Global"/> for adb-level (non-device) commands.
/// </summary>
public sealed class AdbCommandRequest
{
    /// <summary>Target device serial; <see langword="null"/> for a global adb command.</summary>
    public string? Serial { get; init; }

    /// <summary>Arguments passed to adb (excluding the <c>-s serial</c> / <c>shell</c> prefix).</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>Optional per-command timeout; falls back to the executor default when null.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>When true the command is run inside the device shell (<c>-s serial shell ...</c>).</summary>
    public bool RunInShell { get; init; }

    /// <summary>Creates a device shell command request (<c>adb -s serial shell args...</c>).</summary>
    public static AdbCommandRequest Shell(string serial, params string[] args) => new()
    {
        Serial = serial,
        Arguments = args,
        RunInShell = true
    };

    /// <summary>Creates a global adb command request (no target device).</summary>
    public static AdbCommandRequest Global(params string[] args) => new()
    {
        Arguments = args
    };
}
