using AndroidTreeView.Models;

namespace AndroidTreeView.Core.Interfaces;

/// <summary>
/// Downloads an update package, verifies it when checksum metadata is present, then starts the automated
/// update flow. Users should not have to manually unzip or replace application files.
/// </summary>
public interface IUpdateInstaller
{
    Task<UpdateInstallResult> InstallAsync(UpdateCheckResult update, CancellationToken ct = default);
}
