using System.Net;
using System.Text.Json;
using AndroidTreeView.Core;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Models;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Infrastructure.Update;

/// <summary>
/// Checks the internal NekoIndex update API for a newer version. Fully resilient: every failure mode maps
/// to an <see cref="UpdateCheckStatus"/> and is returned rather than thrown, so callers and the UI never
/// have to guard against exceptions. Cooperative cancellation via the caller's token still propagates.
/// </summary>
public sealed class NekoIndexUpdateService : IUpdateService
{
    private const string JsonAcceptHeader = "application/json";

    private const string OkProperty = "ok";
    private const string DataProperty = "data";
    private const string ErrorProperty = "error";
    private const string AppKeyProperty = "appKey";
    private const string VersionProperty = "version";
    private const string TitleProperty = "title";
    private const string ZipProperty = "zip";
    private const string DownloadUrlProperty = "downloadUrl";
    private const string Sha256Property = "sha256";

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settings;
    private readonly UpdateProductOptions _product;
    private readonly ILogger<NekoIndexUpdateService> _logger;

    public NekoIndexUpdateService(
        HttpClient httpClient,
        ISettingsService settings,
        ILogger<NekoIndexUpdateService> logger)
        : this(httpClient, settings, logger, UpdateProductOptions.ForMainApp())
    {
    }

    public NekoIndexUpdateService(
        HttpClient httpClient,
        ISettingsService settings,
        ILogger<NekoIndexUpdateService> logger,
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

    /// <inheritdoc />
    public string CurrentVersion => _product.Version;

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
            return UpdateCheckResult.Error(CurrentVersion, UpdateCheckStatus.InvalidData, "The update payload could not be parsed.");
        }
    }

    private HttpRequestMessage BuildRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _product.LatestReleaseApiUrl);
        request.Headers.UserAgent.ParseAdd(_product.Name.Replace(' ', '-') + "-UpdateChecker");
        request.Headers.Accept.ParseAdd(JsonAcceptHeader);
        return request;
    }

    private bool TryMapStatusFailure(HttpStatusCode status, out UpdateCheckResult failure)
    {
        switch (status)
        {
            case HttpStatusCode.NotFound:
                failure = UpdateCheckResult.Error(
                    CurrentVersion,
                    UpdateCheckStatus.NoRelease,
                    $"No published release was found for appKey '{_product.AppKey}'.");
                return true;
            case HttpStatusCode.Forbidden:
                failure = UpdateCheckResult.Error(
                    CurrentVersion,
                    UpdateCheckStatus.RateLimited,
                    "The update API rejected the request.");
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
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Invalid("The update payload root was not an object.");
        }

        if (!root.TryGetProperty(OkProperty, out var okElement) || okElement.ValueKind != JsonValueKind.True)
        {
            var error = ReadString(root, ErrorProperty) ?? "The update API returned ok=false.";
            return UpdateCheckResult.Error(CurrentVersion, UpdateCheckStatus.InvalidData, error);
        }

        if (!root.TryGetProperty(DataProperty, out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return Invalid("The update payload did not include a data object.");
        }

        var appKey = ReadString(data, AppKeyProperty);
        if (!string.Equals(appKey, _product.AppKey, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Update payload appKey '{PayloadAppKey}' did not match configured appKey '{ConfiguredAppKey}'.",
                appKey,
                _product.AppKey);
            return Invalid("The update payload was for a different application channel.");
        }

        var version = ReadString(data, VersionProperty);
        if (!SemanticVersion.TryParse(version, out var latest) || latest is null)
        {
            _logger.LogWarning("Update version '{Version}' is not a valid semantic version.", version);
            return Invalid("The update version was not a recognizable semantic version.");
        }

        var downloadUrl = ReadZipString(data, DownloadUrlProperty);
        var absoluteDownloadUrl = ResolveUpdateUrl(downloadUrl);
        var sha256 = ReadZipString(data, Sha256Property);
        var releaseNotes = ReadString(data, TitleProperty);

        var updateAvailable = !SemanticVersion.TryParse(CurrentVersion, out var current)
            || current is null
            || latest.IsNewerThan(current);

        return new UpdateCheckResult
        {
            CurrentVersion = CurrentVersion,
            LatestVersion = latest.ToString(),
            UpdateAvailable = updateAvailable,
            ReleaseUrl = absoluteDownloadUrl ?? _product.ReleasesUrl,
            DownloadUrl = absoluteDownloadUrl,
            Sha256 = sha256,
            ReleaseNotes = releaseNotes,
            Status = updateAvailable ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate,
        };
    }

    private UpdateCheckResult Invalid(string message) =>
        UpdateCheckResult.Error(CurrentVersion, UpdateCheckStatus.InvalidData, message);

    private static string? ReadZipString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(ZipProperty, out var zip) || zip.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadString(zip, propertyName);
    }

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private string? ResolveUpdateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmedUrl = url.Trim();
        var baseUri = new Uri(_product.UpdateServerBaseUrl.TrimEnd('/') + "/");
        if (trimmedUrl.StartsWith("/", StringComparison.Ordinal) && !trimmedUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return new Uri(baseUri, trimmedUrl.TrimStart('/')).ToString();
        }

        if (Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var absolute))
        {
            return absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                ? absolute.ToString()
                : null;
        }

        return new Uri(baseUri, trimmedUrl).ToString();
    }
}
