using System.Net;
using System.Text;
using AndroidTreeView.Core;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Infrastructure.Update;
using AndroidTreeView.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AndroidTreeView.Infrastructure.Tests;

public sealed class GitHubUpdateServiceTests
{
    private static GitHubUpdateService CreateService(
        FakeHttpMessageHandler handler,
        out FakeSettingsService settings)
    {
        settings = new FakeSettingsService();
        var client = new HttpClient(handler);
        return new GitHubUpdateService(client, settings, NullLogger<GitHubUpdateService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string ReleaseJson(string tag) =>
        $$"""
        { "tag_name": "{{tag}}", "html_url": "https://github.com/AndroidTreeView/AndroidTreeView/releases/tag/{{tag}}", "body": "Release notes for {{tag}}." }
        """;

    [Fact]
    public void CurrentVersion_MatchesAppInfo()
    {
        var service = CreateService(new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, ReleaseJson("1.0.0"))), out _);
        Assert.Equal(AppInfo.Version, service.CurrentVersion);
    }

    [Fact]
    public async Task CheckForUpdates_SameVersion_ReturnsUpToDate()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, ReleaseJson(AppInfo.Version)));
        var service = CreateService(handler, out _);

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.False(result.UpdateAvailable);
        Assert.Equal(AppInfo.Version, result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdates_NewerVersion_ReturnsUpdateAvailable()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, ReleaseJson("v2.5.1")));
        var service = CreateService(handler, out _);

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.True(result.UpdateAvailable);
        Assert.Equal("2.5.1", result.LatestVersion);
        Assert.Equal("https://github.com/AndroidTreeView/AndroidTreeView/releases/tag/v2.5.1", result.ReleaseUrl);
        Assert.Equal("Release notes for v2.5.1.", result.ReleaseNotes);
    }

    [Fact]
    public async Task CheckForUpdates_SendsRequiredGitHubHeaders()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, ReleaseJson("1.0.0")));
        var service = CreateService(handler, out _);

        await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.UserAgent.Count > 0);
        Assert.Contains(handler.LastRequest.Headers.Accept, h => h.MediaType == "application/vnd.github+json");
        Assert.Equal(AppInfo.LatestReleaseApiUrl, handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task CheckForUpdates_Forbidden_ReturnsRateLimited()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var service = CreateService(handler, out _);

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(UpdateCheckStatus.RateLimited, result.Status);
        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdates_NotFound_ReturnsNoRelease()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = CreateService(handler, out _);

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(UpdateCheckStatus.NoRelease, result.Status);
    }

    [Fact]
    public async Task CheckForUpdates_NetworkFailure_ReturnsNetworkError()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var service = CreateService(handler, out _);

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(UpdateCheckStatus.NetworkError, result.Status);
    }

    [Fact]
    public async Task CheckForUpdates_InvalidTag_ReturnsInvalidData()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, ReleaseJson("not-a-version")));
        var service = CreateService(handler, out _);

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(UpdateCheckStatus.InvalidData, result.Status);
        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdates_MalformedJson_ReturnsInvalidData()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, "{ broken"));
        var service = CreateService(handler, out _);

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(UpdateCheckStatus.InvalidData, result.Status);
    }

    [Fact]
    public async Task CheckForUpdates_DisabledAndNotUserInitiated_ReturnsDisabledWithoutHttpCall()
    {
        var called = false;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            called = true;
            return JsonResponse(HttpStatusCode.OK, ReleaseJson("9.9.9"));
        });
        var service = CreateService(handler, out var settings);
        settings.Current = new AppSettings { AutoCheckUpdates = false };

        var result = await service.CheckForUpdatesAsync(userInitiated: false);

        Assert.Equal(UpdateCheckStatus.Disabled, result.Status);
        Assert.Equal(AppInfo.Version, result.CurrentVersion);
        Assert.False(called);
    }

    [Fact]
    public async Task CheckForUpdates_DisabledButUserInitiated_StillChecks()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, ReleaseJson("3.0.0")));
        var service = CreateService(handler, out var settings);
        settings.Current = new AppSettings { AutoCheckUpdates = false };

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
    }
}
