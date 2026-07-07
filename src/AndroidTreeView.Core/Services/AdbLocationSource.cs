namespace AndroidTreeView.Core.Services;

/// <summary>
/// Indicates how the adb executable was discovered.
/// </summary>
public enum AdbLocationSource
{
    Configured,
    EnvironmentPath,
    CommonSdkLocation,
    NotFound
}
