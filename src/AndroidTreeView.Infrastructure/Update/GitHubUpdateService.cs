using System.Net;
using System.Text.Json;
using AndroidTreeView.Core;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Models;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Infrastructure.Update;

/// <summary>
/// Checks GitHub Releases for a newer version and resolves the matching Windows x64 package.
/// Failures are returned as UI-safe result states rather than thrown to callers.
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    private const string GitHubAcceptHeader = "application/vnd.github+json";
    private const string TagNameProperty = "tag_name";
    private const string HtmlUrlProperty = "html_url";
    private const string BodyProperty = "body";
    private const string AssetsProperty = "assets";
    private const string NameProperty = "name";
    private const string DownloadUrlProperty = "browser_download_url";

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settings;
    private readonly UpdateProductOptions _product;
    private readonly ILogger<GitHubUpdateService> _logger;

    public GitHubUpdateService(
        HttpClient httpClient,
        ISettingsService settings,
        ILogger<GitHubUpdateService> logger)
        : this(httpClient, settings, logger, UpdateProductOptions.ForMainApp())
    {
    }

    public GitHubUpdateService(
        HttpClient httpClient,
        ISettingsService settings,
        ILogger<GitHubUpdateService> logger,
        UpdateProductOptions product)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(product);

        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        _product = product;
    }

    public string CurrentVersion => _product.Version;

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool userInitiated, CancellationToken ct = default)
    {
        if (!userInitiated && !_settings.Current.AutoCheckUpdates)
        {
            return UpdateCheckResult.DisabledFor(CurrentVersion);
        }

        try
        {
            using var request = BuildRequest();
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (TryMapStatusFailure(response.StatusCode, out var failure))
            {
                return failure;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            return BuildResult(document.RootElement);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "GitHub update check timed out.");
            return UpdateCheckResult.Error(CurrentVersion, UpdateCheckStatus.NetworkError, "The update check timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "GitHub update check failed due to a network error.");
            return UpdateCheckResult.Error(CurrentVersion, UpdateCheckStatus.NetworkError, ex.Message);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "GitHub returned malformed release JSON.");
            return UpdateCheckResult.Error(CurrentVersion, UpdateCheckStatus.InvalidData, "The release payload could not be parsed.");
        }
    }

    private HttpRequestMessage BuildRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _product.LatestReleaseApiUrl);
        request.Headers.UserAgent.ParseAdd(_product.Name.Replace(' ', '-') + "-UpdateChecker");
        request.Headers.Accept.ParseAdd(GitHubAcceptHeader);
        return request;
    }

    private bool TryMapStatusFailure(HttpStatusCode status, out UpdateCheckResult failure)
    {
        switch (status)
        {
            case HttpStatusCode.NotFound:
                failure = UpdateCheckResult.Error(CurrentVersion, UpdateCheckStatus.NoRelease, "No published GitHub release was found.");
                return true;
            case HttpStatusCode.Forbidden:
                failure = UpdateCheckResult.Error(CurrentVersion, UpdateCheckStatus.RateLimited, "The GitHub API rate limit was reached.");
                return true;
        }

        if ((int)status is < 200 or >= 300)
        {
            _logger.LogWarning("GitHub update check returned unexpected status {Status}.", (int)status);
            failure = UpdateCheckResult.Error(CurrentVersion, UpdateCheckStatus.NetworkError, $"Unexpected HTTP status {(int)status}.");
            return true;
        }

        failure = default!;
        return false;
    }

    private UpdateCheckResult BuildResult(JsonElement root)
    {
        var tagName = ReadString(root, TagNameProperty);
        var releaseUrl = ReadString(root, HtmlUrlProperty) ?? _product.ReleasesUrl;
        var releaseNotes = ReadString(root, BodyProperty);

        if (!SemanticVersion.TryParse(tagName, out var latest) || latest is null)
        {
            _logger.LogWarning("GitHub release tag '{Tag}' is not a valid semantic version.", tagName);
            return new UpdateCheckResult
            {
                CurrentVersion = CurrentVersion,
                Status = UpdateCheckStatus.InvalidData,
                ReleaseUrl = releaseUrl,
                ReleaseNotes = releaseNotes,
                ErrorMessage = "The release tag was not a recognizable semantic version.",
            };
        }

        var updateAvailable = !SemanticVersion.TryParse(CurrentVersion, out var current)
            || current is null
            || latest.IsNewerThan(current);

        var packageName = GetPackageName(latest.ToString());
        var downloadUrl = packageName is null ? null : FindAssetUrl(root, packageName);
        var sha256Url = packageName is null ? null : FindAssetUrl(root, packageName + ".sha256");

        return new UpdateCheckResult
        {
            CurrentVersion = CurrentVersion,
            LatestVersion = latest.ToString(),
            UpdateAvailable = updateAvailable,
            ReleaseUrl = releaseUrl,
            DownloadUrl = downloadUrl,
            Sha256Url = sha256Url,
            ReleaseNotes = releaseNotes,
            Status = updateAvailable ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate,
        };
    }

    private string? GetPackageName(string version) =>
        string.IsNullOrWhiteSpace(_product.ReleaseAssetRid)
            ? null
            : $"{_product.ReleaseAssetPrefix}-{version}-{_product.ReleaseAssetRid}.zip";

    private static string? FindAssetUrl(JsonElement root, string assetName)
    {
        if (!root.TryGetProperty(AssetsProperty, out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            if (string.Equals(ReadString(asset, NameProperty), assetName, StringComparison.OrdinalIgnoreCase))
            {
                return ReadString(asset, DownloadUrlProperty);
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
