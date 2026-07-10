using AndroidTreeView.Core;

namespace AndroidTreeView.Core.Options;

/// <summary>
/// Identifies the GitHub Release asset used by each shipping executable.
/// </summary>
public sealed class UpdateProductOptions
{
    public string Name { get; init; } = AppInfo.Name;

    public string Version { get; init; } = AppInfo.Version;

    public string AppKey { get; init; } = AppInfo.AppUpdateKey;

    public string UpdateServerBaseUrl { get; init; } = AppInfo.UpdateServerBaseUrl;

    public string ReleasesUrl { get; init; } = AppInfo.ReleasesUrl;

    public string LatestReleaseApiUrl { get; init; } = AppInfo.LatestReleaseApiUrl;

    public string ReleaseAssetPrefix { get; init; } = "AndroidTreeView";

    public string? ReleaseAssetRid { get; init; } = GetInstallableRid();

    public static UpdateProductOptions ForMainApp() => new()
    {
        Name = AppInfo.Name,
        Version = AppInfo.Version,
        AppKey = AppInfo.AppUpdateKey,
        UpdateServerBaseUrl = AppInfo.UpdateServerBaseUrl,
        ReleasesUrl = AppInfo.ReleasesUrl,
        LatestReleaseApiUrl = AppInfo.LatestReleaseApiUrl,
        ReleaseAssetPrefix = "AndroidTreeView",
    };

    public static UpdateProductOptions ForMiniApp() => new()
    {
        Name = AppInfo.Name + " Mini",
        Version = AppInfo.Version,
        AppKey = AppInfo.MiniUpdateKey,
        UpdateServerBaseUrl = AppInfo.UpdateServerBaseUrl,
        ReleasesUrl = AppInfo.ReleasesUrl,
        LatestReleaseApiUrl = AppInfo.LatestReleaseApiUrl,
        ReleaseAssetPrefix = "AndroidTreeView-Mini",
    };

    private static string? GetInstallableRid() =>
        OperatingSystem.IsWindows() && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            == System.Runtime.InteropServices.Architecture.X64
                ? "win-x64"
                : null;
}
