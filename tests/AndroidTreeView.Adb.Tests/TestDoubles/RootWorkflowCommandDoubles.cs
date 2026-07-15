using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;

namespace AndroidTreeView.Adb.Tests.TestDoubles;

internal sealed class RootTestExternalCommandRunner : IExternalCommandRunner
{
    public List<ExternalCommandRequest> Requests { get; } = [];

    public Func<ExternalCommandRequest, CancellationToken, Task<ExternalCommandResult>> Handler { get; set; }
        = static (_, _) => Task.FromResult(new ExternalCommandResult());

    public Task<ExternalCommandResult> RunAsync(ExternalCommandRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        return Handler(request, ct);
    }
}

internal sealed class RootTestAdbCommandExecutor : IAdbCommandExecutor
{
    public List<AdbCommandRequest> Requests { get; } = [];

    public Func<AdbCommandRequest, CancellationToken, Task<AdbCommandResult>> Handler { get; set; }
        = static (_, _) => Task.FromResult(new AdbCommandResult());

    public Task<AdbCommandResult> ExecuteAsync(AdbCommandRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        return Handler(request, ct);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        AdbCommandRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
