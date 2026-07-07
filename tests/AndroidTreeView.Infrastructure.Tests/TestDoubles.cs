using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Options;

namespace AndroidTreeView.Infrastructure.Tests;

/// <summary>Minimal in-memory <see cref="ISettingsService"/> used to drive the update service.</summary>
internal sealed class FakeSettingsService : ISettingsService
{
    public AppSettings Current { get; set; } = new();

    public event EventHandler<AppSettings>? SettingsChanged;

    public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(Current);

    public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        Current = settings;
        SettingsChanged?.Invoke(this, settings);
        return Task.CompletedTask;
    }
}

/// <summary>An <see cref="HttpMessageHandler"/> that returns a scripted response or fault.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRequest = request;
        try
        {
            return Task.FromResult(_responder(request));
        }
        catch (Exception ex)
        {
            return Task.FromException<HttpResponseMessage>(ex);
        }
    }
}
