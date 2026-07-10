using AndroidTreeView.Adb.Internal;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;

namespace AndroidTreeView.Adb.Services;

/// <summary><see cref="IExternalCommandRunner"/> adapter over the shared process runner.</summary>
public sealed class ExternalCommandRunner : IExternalCommandRunner
{
    public async Task<ExternalCommandResult> RunAsync(
        ExternalCommandRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await ProcessRunner.RunAsync(
            request.FileName,
            request.Arguments,
            request.Timeout,
            ct).ConfigureAwait(false);

        return new ExternalCommandResult
        {
            ExitCode = result.ExitCode,
            StandardOutput = result.StandardOutput,
            StandardError = result.StandardError,
            TimedOut = result.TimedOut,
            Duration = result.Duration
        };
    }
}
