using AndroidTreeView.Core;

namespace AndroidTreeView.Core.Options;

/// <summary>
/// Identifies the concrete package channel used by the update API. The full app and Mini share the
/// update implementation, but each points at its own app key.
/// </summary>
public sealed class UpdateProductOptions
{
    public string Name { get; init; } = AppInfo.Name;

    public string Version { get; init; } = AppInfo.Version;

    public string AppKey { get; init; } = AppInfo.AppUpdateKey;

    public string UpdateServerBaseUrl { get; init; } = AppInfo.UpdateServerBaseUrl;

    public string ReleasesUrl { get; init; } = AppInfo.ReleasesUrl;

    public string LatestReleaseApiUrl =>
        UpdateServerBaseUrl.TrimEnd('/') + "/api/update/" + AppKey + "/latest";

    public static UpdateProductOptions ForMainApp() => new()
    {
        Name = AppInfo.Name,
        Version = AppInfo.Version,
        AppKey = AppInfo.AppUpdateKey,
        UpdateServerBaseUrl = AppInfo.UpdateServerBaseUrl,
        ReleasesUrl = AppInfo.ReleasesUrl,
    };

    public static UpdateProductOptions ForMiniApp() => new()
    {
        Name = AppInfo.Name + " Mini",
        Version = AppInfo.Version,
        AppKey = AppInfo.MiniUpdateKey,
        UpdateServerBaseUrl = AppInfo.UpdateServerBaseUrl,
        ReleasesUrl = AppInfo.ReleasesUrl,
    };
}
