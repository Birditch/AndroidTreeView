namespace AndroidTreeView.Core.Services;

/// <summary>Describes one external process invocation.</summary>
public sealed class ExternalCommandRequest
{
    /// <summary>Executable name or path passed directly to the operating system.</summary>
    public required string FileName { get; init; }

    /// <summary>Individual arguments passed without shell interpolation.</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>Maximum time the process may run before its process tree is terminated.</summary>
    public required TimeSpan Timeout { get; init; }
}
