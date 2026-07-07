namespace AndroidTreeView.Core.Options;

/// <summary>
/// User-configurable application settings, persisted as JSON by the settings service.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Explicit path to the adb executable; null lets the locator auto-discover it.</summary>
    public string? AdbPath { get; set; }

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public bool AutoRefreshEnabled { get; set; } = true;

    public int DeviceRefreshIntervalSeconds { get; set; } = 3;

    public int BatteryRefreshIntervalSeconds { get; set; } = 10;

    public int LogcatMaxLines { get; set; } = 5000;

    public StartupBehavior Startup { get; set; } = StartupBehavior.Normal;

    public bool RememberLastSelectedDevice { get; set; } = true;

    public string? LastSelectedSerial { get; set; }

    /// <summary>UI language; defaults to Simplified Chinese.</summary>
    public AppLanguage Language { get; set; } = AppLanguage.ChineseSimplified;

    /// <summary>Whether the app checks GitHub Releases for updates on startup and periodically.</summary>
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>Optional accent color as a hex string (e.g. <c>#0A84FF</c>); null uses the theme default.</summary>
    public string? AccentColor { get; set; }

    /// <summary>Creates a deep, independent copy of these settings.</summary>
    public AppSettings Clone() => new()
    {
        AdbPath = AdbPath,
        Theme = Theme,
        AutoRefreshEnabled = AutoRefreshEnabled,
        DeviceRefreshIntervalSeconds = DeviceRefreshIntervalSeconds,
        BatteryRefreshIntervalSeconds = BatteryRefreshIntervalSeconds,
        LogcatMaxLines = LogcatMaxLines,
        Startup = Startup,
        RememberLastSelectedDevice = RememberLastSelectedDevice,
        LastSelectedSerial = LastSelectedSerial,
        Language = Language,
        AutoCheckUpdates = AutoCheckUpdates,
        AccentColor = AccentColor
    };
}
