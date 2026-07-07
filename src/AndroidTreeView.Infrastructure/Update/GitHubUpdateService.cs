using System.Net;
using System.Text.Json;
using AndroidTreeView.Core;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Infrastructure.Update;

/// <summary>
/// Checks the GitHub Releases API for a newer version. Fully resilient: every failure mode maps to an
/// <see cref="UpdateCheckStatus"/> and is returned rather than thrown, so callers (and the UI) never
/// have to guard against exceptions. Cooperative cancellation via the caller's token still propagates.
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    private const string UserAgentValue = AppInfo.Name + "-UpdateChecker";
    private const string GitHubAcceptHeader = "application/vnd.github+json";

    private const string TagNameProperty = "tag_name";
    private const string HtmlUrlProperty = "html_url";
    private const string BodyProperty = "body";

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settings;
    private readonly ILogger<GitHubUpdateService> _logger;

    public GitHubUpdateService(HttpClient httpClient, ISettingsService settings, ILogger<GitHubUpdateService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public string CurrentVersion => AppInfo.Version;

    /// <inheritdoc />
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
            _logger.LogWarning(ex, "Update check timed out.");
            return UpdateCheckResult.Error(CurrentVersion, UpdateCheckStatus.NetworkError, "The update check timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Update check failed due to a network error.");
            return UpdateCheckResult.Error(CurrentVersion, UpdateCheckStatus.NetworkError, ex.Message);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Update check returned malformed JSON.");
            return UpdateCheckResult.Error(CurrentVersion, UpdateCheckStatus.InvalidData, "The release payload could not be parsed.");
        }
    }

    private static HttpRequestMessage BuildRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, AppInfo.LatestReleaseApiUrl);
        request.Headers.UserAgent.ParseAdd(UserAgentValue);
        request.Headers.Accept.ParseAdd(GitHubAcceptHeader);
        return request;
    }

    private bool TryMapStatusFailure(HttpStatusCode status, out UpdateCheckResult failure)
    {
        switch (status)
        {
            case HttpStatusCode.NotFound:
                failure = UpdateCheckResult.Error(CurrentVersion, UpdateCheckStatus.NoRelease, "No published release was found.");
                return true;
            case HttpStatusCode.Forbidden:
                failure = UpdateCheckResult.Error(CurrentVersion, UpdateCheckStatus.RateLimited, "The GitHub API rate limit was reached.");
                return true;
        }

        if ((int)status is < 200 or >= 300)
        {
            _logger.LogWarning("Update check returned unexpected status {Status}.", (int)status);
            failure = UpdateCheckResult.Error(CurrentVersion, UpdateCheckStatus.NetworkError, $"Unexpected HTTP status {(int)status}.");
            return true;
        }

        failure = default!;
        return false;
    }

    private UpdateCheckResult BuildResult(JsonElement root)
    {
        var tagName = ReadString(root, TagNameProperty);
        var releaseUrl = ReadString(root, HtmlUrlProperty);
        var releaseNotes = ReadString(root, BodyProperty);

        if (!SemanticVersion.TryParse(tagName, out var latest) || latest is null)
        {
            _logger.LogWarning("Release tag '{Tag}' is not a valid semantic version.", tagName);
            return new UpdateCheckResult
            {
                CurrentVersion = CurrentVersion,
                Status = UpdateCheckStatus.InvalidData,
                ReleaseUrl = releaseUrl,
                ReleaseNotes = releaseNotes,
                ErrorMessage = "The release tag was not a recognizable version.",
            };
        }

        var updateAvailable = !SemanticVersion.TryParse(CurrentVersion, out var current)
            || current is null
            || latest.IsNewerThan(current);

        return new UpdateCheckResult
        {
            CurrentVersion = CurrentVersion,
            LatestVersion = latest.ToString(),
            UpdateAvailable = updateAvailable,
            ReleaseUrl = releaseUrl,
            ReleaseNotes = releaseNotes,
            Status = updateAvailable ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate,
        };
    }

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
