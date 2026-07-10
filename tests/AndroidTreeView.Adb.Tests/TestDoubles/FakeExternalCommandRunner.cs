using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;

namespace AndroidTreeView.Adb.Tests.TestDoubles;

internal sealed class FakeExternalCommandRunner : IExternalCommandRunner
{
    private readonly Queue<ExternalCommandResult> _results = new();
    private readonly List<ExternalCommandRequest> _requests = [];

    public IReadOnlyList<ExternalCommandRequest> Requests => _requests;

    public Exception? Exception { get; set; }

    public void Enqueue(ExternalCommandResult result) => _results.Enqueue(result);

    public Task<ExternalCommandResult> RunAsync(
        ExternalCommandRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _requests.Add(request);

        if (Exception is not null)
        {
            return Task.FromException<ExternalCommandResult>(Exception);
        }

        var result = _results.Count > 0
            ? _results.Dequeue()
            : new ExternalCommandResult { ExitCode = 0 };

        return Task.FromResult(result);
    }
}
