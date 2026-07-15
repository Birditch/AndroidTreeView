namespace AndroidTreeView.Adb.Services;

/// <summary>Resolved locations for fixed-version root assets and per-session working data.</summary>
public sealed class RootToolPaths
{
    public RootToolPaths(
        string? applicationBaseDirectory = null,
        string? workRootDirectory = null)
    {
        var appBase = Path.GetFullPath(applicationBaseDirectory ?? AppContext.BaseDirectory);
        RootToolsDirectory = Path.Combine(appBase, "root-tools");
        MagiskApkPath = Path.Combine(RootToolsDirectory, "magisk", "Magisk-v30.7.apk");
        PayloadDumperPath = Path.Combine(
            RootToolsDirectory,
            "payload-dumper",
            OperatingSystem.IsWindows() ? "payload-dumper-go.exe" : "payload-dumper-go");

        var defaultWorkRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".androidtreeview",
            "root-work");
        WorkRootDirectory = Path.GetFullPath(workRootDirectory ?? defaultWorkRoot);
    }

    public string RootToolsDirectory { get; }

    public string MagiskApkPath { get; }

    public string PayloadDumperPath { get; }

    public string WorkRootDirectory { get; }
}
