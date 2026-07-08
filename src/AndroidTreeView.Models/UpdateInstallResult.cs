namespace AndroidTreeView.Models;

public enum UpdateInstallStatus
{
    Started,
    NoUpdateAvailable,
    MissingDownloadUrl,
    DownloadFailed,
    InvalidChecksum,
    UnsupportedPackage,
    InstallerLaunchFailed
}

public sealed class UpdateInstallResult
{
    public required UpdateInstallStatus Status { get; init; }

    public string? PackagePath { get; init; }

    public string? InstallerPath { get; init; }

    public string? ErrorMessage { get; init; }

    public bool Started => Status == UpdateInstallStatus.Started;

    public static UpdateInstallResult Error(UpdateInstallStatus status, string? message = null) =>
        new() { Status = status, ErrorMessage = message };
}
