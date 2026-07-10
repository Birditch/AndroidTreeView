using System.Net;
using System.Reflection;
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
        out FakeSettingsService settings,
        string version = "1.0.6",
        string? rid = "win-x64",
        string assetPrefix = "AndroidTreeView")
    {
        settings = new FakeSettingsService();
        var product = new UpdateProductOptions
        {
            Version = version,
            AppKey = assetPrefix == "AndroidTreeView-Mini" ? AppInfo.MiniUpdateKey : AppInfo.AppUpdateKey,
            LatestReleaseApiUrl = AppInfo.LatestReleaseApiUrl,
            ReleasesUrl = AppInfo.ReleasesUrl,
            ReleaseAssetPrefix = assetPrefix,
            ReleaseAssetRid = rid,
        };
        return new GitHubUpdateService(
            new HttpClient(handler),
            settings,
            NullLogger<GitHubUpdateService>.Instance,
            product);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string ReleaseJson(string tag, string prefix = "AndroidTreeView") =>
        $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/Birditch/AndroidTreeView/releases/tag/{{tag}}",
          "body": "Release notes for {{tag}}.",
          "assets": [
            {
              "name": "{{prefix}}-2.5.1-win-x64.zip",
              "browser_download_url": "https://github.com/Birditch/AndroidTreeView/releases/download/v2.5.1/{{prefix}}-2.5.1-win-x64.zip"
            },
            {
              "name": "{{prefix}}-2.5.1-win-x64.zip.sha256",
              "browser_download_url": "https://github.com/Birditch/AndroidTreeView/releases/download/v2.5.1/{{prefix}}-2.5.1-win-x64.zip.sha256"
            }
          ]
        }
        """;

    [Fact]
    public void Version_ComesFromAssemblyInformationalVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var expected = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Equal(expected.Split('+')[0], AppInfo.GetVersion(assembly));
    }

    [Fact]
    public async Task CheckForUpdates_SameVersion_ReturnsUpToDate()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, ReleaseJson("v1.0.6")));
        var service = CreateService(handler, out _);

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.False(result.UpdateAvailable);
        Assert.Equal("1.0.6", result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdates_NewerVersion_UsesGitHubReleaseAssets()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, ReleaseJson("v2.5.1")));
        var service = CreateService(handler, out _);

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.True(result.UpdateAvailable);
        Assert.Equal("2.5.1", result.LatestVersion);
        Assert.Equal("https://github.com/Birditch/AndroidTreeView/releases/tag/v2.5.1", result.ReleaseUrl);
        Assert.Equal("https://github.com/Birditch/AndroidTreeView/releases/download/v2.5.1/AndroidTreeView-2.5.1-win-x64.zip", result.DownloadUrl);
        Assert.Equal("https://github.com/Birditch/AndroidTreeView/releases/download/v2.5.1/AndroidTreeView-2.5.1-win-x64.zip.sha256", result.Sha256Url);
        Assert.Equal("Release notes for v2.5.1.", result.ReleaseNotes);
    }

    [Fact]
    public async Task CheckForUpdates_Mini_UsesMiniAsset()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, ReleaseJson("v2.5.1", "AndroidTreeView-Mini")));
        var service = CreateService(handler, out _, assetPrefix: "AndroidTreeView-Mini");

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Contains("AndroidTreeView-Mini-2.5.1-win-x64.zip", result.DownloadUrl);
    }

    [Fact]
    public async Task CheckForUpdates_NonWindows_LeavesInstallDisabled()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, ReleaseJson("v2.5.1")));
        var service = CreateService(handler, out _, rid: null);

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.True(result.UpdateAvailable);
        Assert.Null(result.DownloadUrl);
        Assert.Equal("https://github.com/Birditch/AndroidTreeView/releases/tag/v2.5.1", result.ReleaseUrl);
    }

    [Fact]
    public async Task CheckForUpdates_SendsRequiredGitHubHeaders()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, ReleaseJson("v1.0.6")));
        var service = CreateService(handler, out _);

        await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.UserAgent.Count > 0);
        Assert.Contains(handler.LastRequest.Headers.Accept, h => h.MediaType == "application/vnd.github+json");
        Assert.Equal(AppInfo.LatestReleaseApiUrl, handler.LastRequest.RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, UpdateCheckStatus.NoRelease)]
    [InlineData(HttpStatusCode.Forbidden, UpdateCheckStatus.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, UpdateCheckStatus.NetworkError)]
    public async Task CheckForUpdates_HttpFailure_ReturnsExpectedStatus(HttpStatusCode status, UpdateCheckStatus expected)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(status));
        var service = CreateService(handler, out _);

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(expected, result.Status);
        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdates_InvalidTag_ReturnsInvalidData()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, ReleaseJson("not-a-version")));
        var service = CreateService(handler, out _);

        var result = await service.CheckForUpdatesAsync(userInitiated: true);

        Assert.Equal(UpdateCheckStatus.InvalidData, result.Status);
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
    public async Task CheckForUpdates_DisabledAndNotUserInitiated_SkipsRequest()
    {
        var called = false;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            called = true;
            return JsonResponse(HttpStatusCode.OK, ReleaseJson("v2.5.1"));
        });
        var service = CreateService(handler, out var settings);
        settings.Current = new() { AutoCheckUpdates = false };

        var result = await service.CheckForUpdatesAsync(userInitiated: false);

        Assert.Equal(UpdateCheckStatus.Disabled, result.Status);
        Assert.False(called);
    }
}
