using AndroidTreeView.Core.Services;

namespace AndroidTreeView.Core.Interfaces;

/// <summary>Runs an external process without invoking a command shell.</summary>
public interface IExternalCommandRunner
{
    /// <summary>Runs a command to completion and returns its captured output.</summary>
    Task<ExternalCommandResult> RunAsync(
        ExternalCommandRequest request,
        CancellationToken ct = default);
}
