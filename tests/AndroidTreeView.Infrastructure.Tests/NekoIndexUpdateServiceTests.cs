using System.Net;
using System.Text;
using AndroidTreeView.Core;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Infrastructure.Update;
using AndroidTreeView.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AndroidTreeView.Infrastructure.Tests;

public sealed class NekoIndexUpdateServiceTests
{
    private static NekoIndexUpdateService CreateService(
        FakeHttpMessageHandler handler,
        out FakeSettingsService settings)
    {
        settings = new FakeSettingsService();
        var client = new HttpClient(handler);
        return new NekoIndexUpdateService(client, settings, NullLogger<NekoIndexUpdateService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string UpdateJson(
        string version,
        string downloadUrl = "/api/resources/android-tree-view-app/versions/latest/archive",
        string sha256 = "0123456789abcdef",
        string appKey = AppInfo.AppUpdateKey)
        => $$"""
        {
          "ok": true,
          "data": {
            "appKey": "{{appKey}}",
            "title": "AndroidTreeView",
            "version": "{{version}}",
            "releasedAt": "2026-07-08T07:40:04.829Z",
            "zip": {
              "size": 1344,
              "sha256": "{{sha256}}",
              "downloadUrl": "{{downloadUrl}}"
            },
            "files": []
          },
          "error": null
        }
        """;

    [Fact]
    public void CurrentVersion_MatchesAppInfo()
    {
        var service = CreateService(new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, UpdateJson("1.0.0"))), out _);
        Assert.Equal(AppInfo.Version, service.CurrentVersion);
    }

    [Fact]
    public async Task CheckForUpdates_SameVersion_ReturnsUpToDate()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, UpdateJson(AppInfo.Version)));
        var service = CreateService(handler, out _);

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.False(result.UpdateAvailable);
        Assert.Equal(AppInfo.Version, result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdates_NewerVersion_ReturnsUpdateAvailable()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, UpdateJson("v2.5.1")));
        var service = CreateService(handler, out _);

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.True(result.UpdateAvailable);
        Assert.Equal("2.5.1", result.LatestVersion);
        Assert.Equal("http://192.168.89.71:14000/api/resources/android-tree-view-app/versions/latest/archive", result.ReleaseUrl);
        Assert.Equal(result.ReleaseUrl, result.DownloadUrl);
        Assert.Equal("0123456789abcdef", result.Sha256);
        Assert.Equal("AndroidTreeView", result.ReleaseNotes);
    }

    [Fact]
    public async Task CheckForUpdates_SendsRequiredInternalApiHeaders()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, UpdateJson("1.0.0")));
        var service = CreateService(handler, out _);

        await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.UserAgent.Count > 0);
        Assert.Contains(handler.LastRequest.Headers.Accept, h => h.MediaType == "application/json");
        Assert.Equal(AppInfo.LatestReleaseApiUrl, handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task CheckForUpdates_UsesConfiguredMiniUpdateChannel()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            UpdateJson("2.0.0", appKey: AppInfo.MiniUpdateKey)));
        var settings = new FakeSettingsService();
        var product = UpdateProductOptions.ForMiniApp();
        var service = new NekoIndexUpdateService(
            new HttpClient(handler),
            settings,
            NullLogger<NekoIndexUpdateService>.Instance,
            product);

        await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(product.LatestReleaseApiUrl, handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task CheckForUpdates_MismatchedAppKey_ReturnsInvalidData()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            UpdateJson("2.0.0", appKey: AppInfo.MiniUpdateKey)));
        var service = CreateService(handler, out _);

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(UpdateCheckStatus.InvalidData, result.Status);
        Assert.False(result.UpdateAvailable);
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
    public async Task CheckForUpdates_InvalidVersion_ReturnsInvalidData()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, UpdateJson("not-a-version")));
        var service = CreateService(handler, out _);

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(UpdateCheckStatus.InvalidData, result.Status);
        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdates_OkFalse_ReturnsInvalidData()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, """{"ok":false,"data":null,"error":"missing"}"""));
        var service = CreateService(handler, out _);

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(UpdateCheckStatus.InvalidData, result.Status);
        Assert.Equal("missing", result.ErrorMessage);
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
            return JsonResponse(HttpStatusCode.OK, UpdateJson("9.9.9"));
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
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, UpdateJson("3.0.0")));
        var service = CreateService(handler, out var settings);
        settings.Current = new AppSettings { AutoCheckUpdates = false };

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
    }
}
