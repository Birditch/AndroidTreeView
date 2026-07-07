using AndroidTreeView.Core.Services;

namespace AndroidTreeView.Core.Interfaces;

/// <summary>
/// Executes adb commands, both as one-shot invocations and as line streams.
/// </summary>
public interface IAdbCommandExecutor
{
    /// <summary>Runs a command to completion and returns its captured result.</summary>
    Task<AdbCommandResult> ExecuteAsync(AdbCommandRequest request, CancellationToken ct = default);

    /// <summary>Runs a command and yields its standard-output lines as they arrive.</summary>
    IAsyncEnumerable<string> StreamAsync(AdbCommandRequest request, CancellationToken ct = default);
}
