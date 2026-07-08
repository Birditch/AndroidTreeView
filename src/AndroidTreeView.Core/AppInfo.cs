namespace AndroidTreeView.Core;

/// <summary>
/// Application identity and update endpoint constants. These are public app identifiers only, not secrets.
/// </summary>
public static class AppInfo
{
    public const string Name = "AndroidTreeView";

    /// <summary>Release version. Keep this aligned with the App and Mini project <c>Version</c> values.</summary>
    public const string Version = "1.0.4";

    public const string GitHubOwner = "Birditch";
    public const string GitHubRepo = "AndroidTreeView";

    public const string ProjectUrl = "https://github.com/Birditch/AndroidTreeView";

    public const string UpdateServerBaseUrl = "http://192.168.89.71:14000";
    public const string AppUpdateKey = "android-tree-view-app";
    public const string MiniUpdateKey = "android-tree-view-mini";

    public const string ReleasesUrl = UpdateServerBaseUrl;

    public const string LatestReleaseApiUrl =
        UpdateServerBaseUrl + "/api/update/" + AppUpdateKey + "/latest";
}
