using AndroidTreeView.App.Services;
using AndroidTreeView.Core;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Options;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Backs the settings page. Exposes every <see cref="AppSettings"/> field as a two-way bindable
/// property, persists them through <see cref="ISettingsService"/>, and applies theme + language live.
/// The device-monitor refresh interval is updated indirectly: <see cref="ISettingsService.SaveAsync"/>
/// raises <see cref="ISettingsService.SettingsChanged"/>, which the shell/monitor observe — this
/// view-model never news up or drives services directly.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localization;
    private readonly IUpdateService _updateService;
    private readonly IFilePickerService _filePicker;
    private readonly IAdbLocator _adbLocator;
    private readonly IAdbEnvironment _adbEnvironment;

    private string? _latestReleaseUrl;

    [ObservableProperty]
    private string? _adbPath;

    [ObservableProperty]
    private ThemeMode _theme;

    [ObservableProperty]
    private AppLanguage _language;

    [ObservableProperty]
    private bool _autoRefreshEnabled;

    [ObservableProperty]
    private int _deviceRefreshIntervalSeconds;

    [ObservableProperty]
    private int _batteryRefreshIntervalSeconds;

    [ObservableProperty]
    private int _logcatMaxLines;

    [ObservableProperty]
    private bool _autoCheckUpdates;

    [ObservableProperty]
    private string? _accentColor;

    [ObservableProperty]
    private bool _rememberLastSelectedDevice;

    [ObservableProperty]
    private StartupBehavior _startup;

    [ObservableProperty]
    private string? _latestVersion;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    public SettingsViewModel(
        ISettingsService settingsService,
        IThemeService themeService,
        ILocalizationService localization,
        IUpdateService updateService,
        IFilePickerService filePicker,
        IAdbLocator adbLocator,
        IAdbEnvironment adbEnvironment)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _localization = localization;
        _updateService = updateService;
        _filePicker = filePicker;
        _adbLocator = adbLocator;
        _adbEnvironment = adbEnvironment;

        CurrentVersion = AppInfo.Version;
        ThemeOptions = Enum.GetValues<ThemeMode>();
        LanguageOptions = Enum.GetValues<AppLanguage>();

        LoadFrom(_settingsService.Current);
    }

    /// <summary>The running application version (read-only).</summary>
    public string CurrentVersion { get; }

    /// <summary>Available theme options for selection controls.</summary>
    public ThemeMode[] ThemeOptions { get; }

    /// <summary>Available language options for selection controls.</summary>
    public AppLanguage[] LanguageOptions { get; }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct)
    {
        var settings = BuildSettings();
        await _settingsService.SaveAsync(settings, ct).ConfigureAwait(true);

        // Apply live so the change is visible without restarting.
        _themeService.Apply(settings.Theme);
        _themeService.ApplyAccent(settings.AccentColor);
        _localization.SetLanguage(settings.Language);
    }

    [RelayCommand]
    private async Task BrowseAdbAsync()
    {
        var path = await _filePicker.PickAdbExecutableAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
        {
            AdbPath = path;
        }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync(CancellationToken ct)
    {
        UpdateStatusText = _localization.Get("update.checking");
        var result = await _updateService.CheckForUpdatesAsync(userInitiated: true, ct).ConfigureAwait(true);
        LatestVersion = result.LatestVersion;
        _latestReleaseUrl = result.ReleaseUrl;
        UpdateStatusText = UpdatePresentation.Describe(result, _localization);
    }

    [RelayCommand]
    private Task OpenReleasesAsync()
        => _filePicker.OpenUrlAsync(_latestReleaseUrl ?? AppInfo.ReleasesUrl);

    [RelayCommand]
    private void Reset() => LoadFrom(new AppSettings());

    private void LoadFrom(AppSettings settings)
    {
        AdbPath = settings.AdbPath;
        Theme = settings.Theme;
        Language = settings.Language;
        AutoRefreshEnabled = settings.AutoRefreshEnabled;
        DeviceRefreshIntervalSeconds = settings.DeviceRefreshIntervalSeconds;
        BatteryRefreshIntervalSeconds = settings.BatteryRefreshIntervalSeconds;
        LogcatMaxLines = settings.LogcatMaxLines;
        AutoCheckUpdates = settings.AutoCheckUpdates;
        AccentColor = settings.AccentColor;
        RememberLastSelectedDevice = settings.RememberLastSelectedDevice;
        Startup = settings.Startup;
    }

    private AppSettings BuildSettings()
    {
        // Clone Current to preserve fields not surfaced here (e.g. LastSelectedSerial).
        var settings = _settingsService.Current.Clone();
        settings.AdbPath = string.IsNullOrWhiteSpace(AdbPath) ? null : AdbPath;
        settings.Theme = Theme;
        settings.Language = Language;
        settings.AutoRefreshEnabled = AutoRefreshEnabled;
        settings.DeviceRefreshIntervalSeconds = DeviceRefreshIntervalSeconds;
        settings.BatteryRefreshIntervalSeconds = BatteryRefreshIntervalSeconds;
        settings.LogcatMaxLines = LogcatMaxLines;
        settings.AutoCheckUpdates = AutoCheckUpdates;
        settings.AccentColor = string.IsNullOrWhiteSpace(AccentColor) ? null : AccentColor;
        settings.RememberLastSelectedDevice = RememberLastSelectedDevice;
        settings.Startup = Startup;
        return settings;
    }
}
