namespace AndroidTreeView.Core;

/// <summary>
/// <summary>
/// 应用身份常量 (application identity constants)。这里不存放任何密钥；更新检查和发布页链接会使用这些 GitHub 坐标。
/// </summary>
public static class AppInfo
{
    public const string Name = "AndroidTreeView";

    /// <summary>v1 发布版本 (v1 release version)。需要与 App 程序集 <c>Version</c> 保持一致。</summary>
    public const string Version = "1.0.0";

    public const string GitHubOwner = "Birditch";
    public const string GitHubRepo = "AndroidTreeView";

    public const string ProjectUrl = "https://github.com/Birditch/AndroidTreeView";
    public const string ReleasesUrl = "https://github.com/Birditch/AndroidTreeView/releases";
    public const string LatestReleaseApiUrl =
        "https://api.github.com/repos/Birditch/AndroidTreeView/releases/latest";
}
