using AndroidTreeView.Models;

namespace AndroidTreeView.Core.Interfaces;

/// <summary>
/// Checks the configured update source for a newer version. Non-blocking and resilient: never throws to the UI;
/// all failures surface through <see cref="UpdateCheckResult.Status"/>.
/// </summary>
public interface IUpdateService
{
    /// <summary>The running application version (e.g. <c>1.0.4</c>).</summary>
    string CurrentVersion { get; }

    /// <summary>
    /// Queries the latest published package and compares versions.
    /// </summary>
    /// <param name="userInitiated">
    /// When true the check runs even if auto-update is disabled in settings (manual "Check for updates").
    /// </param>
    Task<UpdateCheckResult> CheckForUpdatesAsync(bool userInitiated, CancellationToken ct = default);
}
