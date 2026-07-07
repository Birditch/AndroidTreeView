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
        _ => localization.Get("update.failed"),
    };
}
