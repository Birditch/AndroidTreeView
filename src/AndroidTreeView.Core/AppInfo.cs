using System.Reflection;

namespace AndroidTreeView.Core;

/// <summary>
/// Application identity and update endpoint constants. These are public app identifiers only, not secrets.
/// </summary>
public static class AppInfo
{
    public const string Name = "AndroidTreeView";

    /// <summary>The version embedded in the executable that is currently running.</summary>
    public static string Version => GetVersion();

    public const string GitHubOwner = "Birditch";
    public const string GitHubRepo = "AndroidTreeView";

    public const string ProjectUrl = "https://github.com/Birditch/AndroidTreeView";

    public const string UpdateServerBaseUrl = ProjectUrl;
    public const string AppUpdateKey = "android-tree-view-app";
    public const string MiniUpdateKey = "android-tree-view-mini";

    public const string ReleasesUrl = ProjectUrl + "/releases";

    public const string LatestReleaseApiUrl =
        "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";

    public static string GetVersion(Assembly? assembly = null)
    {
        assembly ??= GetProductAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataSeparator = informationalVersion.IndexOf('+');
            return metadataSeparator >= 0
                ? informationalVersion[..metadataSeparator]
                : informationalVersion;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static Assembly GetProductAssembly()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        var entryName = entryAssembly?.GetName().Name;
        return entryAssembly is not null
            && entryName is not null
            && entryName.StartsWith("AndroidTreeView.App", StringComparison.Ordinal)
                ? entryAssembly
                : typeof(AppInfo).Assembly;
    }
}
