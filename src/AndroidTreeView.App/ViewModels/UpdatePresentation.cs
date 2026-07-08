using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Maps an <see cref="UpdateCheckResult"/> to a localized status string. Shared by the settings and
/// about view-models so update messaging stays consistent. Uses only keys from the master list
/// (<c>update.*</c>); every non-success status collapses to the generic <c>update.failed</c> message.
/// </summary>
internal static class UpdatePresentation
{
    public static string Describe(UpdateCheckResult result, ILocalizationService localization) => result.Status switch
    {
        UpdateCheckStatus.UpdateAvailable => localization.Format("update.available", result.LatestVersion ?? string.Empty),
        UpdateCheckStatus.UpToDate => localization.Get("update.uptodate"),
        UpdateCheckStatus.NoRelease => localization.Get("update.norelease"),
        UpdateCheckStatus.NetworkError => localization.Get("update.network"),
        UpdateCheckStatus.RateLimited => localization.Get("update.ratelimited"),
        UpdateCheckStatus.InvalidData => localization.Get("update.invalid"),
        UpdateCheckStatus.Disabled => localization.Get("update.disabled"),
        _ => localization.Get("update.failed"),
    };

    public static string DescribeInstall(UpdateInstallResult result, ILocalizationService localization) => result.Status switch
    {
        UpdateInstallStatus.Started => localization.Get("update.install.started"),
        UpdateInstallStatus.NoUpdateAvailable => localization.Get("update.install.noupdate"),
        UpdateInstallStatus.MissingDownloadUrl => localization.Get("update.install.missingdownload"),
        UpdateInstallStatus.InvalidChecksum => localization.Get("update.install.checksum"),
        UpdateInstallStatus.UnsupportedPackage => localization.Get("update.install.unsupported"),
        UpdateInstallStatus.InstallerLaunchFailed => localization.Get("update.install.failed"),
        UpdateInstallStatus.DownloadFailed => localization.Get("update.network"),
        _ => localization.Get("update.install.failed"),
    };
}
