namespace AndroidTreeView.Models;

/// <summary>Outcome classification of an update check.</summary>
public enum UpdateCheckStatus
{
    /// <summary>The running version is the latest available.</summary>
    UpToDate,

    /// <summary>A newer release is available.</summary>
    UpdateAvailable,

    /// <summary>No published release was found for the configured app key.</summary>
    NoRelease,

    /// <summary>The check failed because of a network/connectivity problem.</summary>
    NetworkError,

    /// <summary>The update API rate limit was hit.</summary>
    RateLimited,

    /// <summary>The release payload could not be parsed into a valid version.</summary>
    InvalidData,

    /// <summary>Automatic update checks are disabled in settings.</summary>
    Disabled
}

/// <summary>
/// Result of checking the configured update source for a newer version. Never throws to callers; all failure
/// modes are represented by <see cref="Status"/>.
/// </summary>
public sealed class UpdateCheckResult
{
    /// <summary>The version currently running (e.g. <c>1.0.4</c>).</summary>
    public required string CurrentVersion { get; init; }

    /// <summary>The latest published version, when known.</summary>
    public string? LatestVersion { get; init; }

    /// <summary>True when <see cref="LatestVersion"/> is strictly newer than <see cref="CurrentVersion"/>.</summary>
    public bool UpdateAvailable { get; init; }

    /// <summary>URL opened by the UI for the available update.</summary>
    public string? ReleaseUrl { get; init; }

    /// <summary>Direct download URL of the update package, when available.</summary>
    public string? DownloadUrl { get; init; }

    /// <summary>URL of a companion SHA-256 checksum file, when available.</summary>
    public string? Sha256Url { get; init; }

    /// <summary>SHA-256 checksum of the package, when the update API provides it inline.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Release notes / body, when provided.</summary>
    public string? ReleaseNotes { get; init; }

    public UpdateCheckStatus Status { get; init; }

    /// <summary>Optional human-readable detail for error statuses.</summary>
    public string? ErrorMessage { get; init; }

    public static UpdateCheckResult DisabledFor(string currentVersion) =>
        new() { CurrentVersion = currentVersion, Status = UpdateCheckStatus.Disabled };

    public static UpdateCheckResult Error(string currentVersion, UpdateCheckStatus status, string? message = null) =>
        new() { CurrentVersion = currentVersion, Status = status, ErrorMessage = message };
}
