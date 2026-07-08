using AndroidTreeView.App.Services;
using AndroidTreeView.Core;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Models;
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
    private readonly IUpdateInstaller _updateInstaller;
    private readonly IFilePickerService _filePicker;
    private readonly IAdbLocator _adbLocator;
    private readonly IAdbEnvironment _adbEnvironment;

    private string? _latestReleaseUrl;
    private UpdateCheckResult? _latestUpdate;

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool _canInstallUpdate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool _isInstallingUpdate;

    public SettingsViewModel(
        ISettingsService settingsService,
        IThemeService themeService,
        ILocalizationService localization,
        IUpdateService updateService,
        IUpdateInstaller updateInstaller,
        IFilePickerService filePicker,
        IAdbLocator adbLocator,
        IAdbEnvironment adbEnvironment)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _localization = localization;
        _updateService = updateService;
        _updateInstaller = updateInstaller;
        _filePicker = filePicker;
        _adbLocator = adbLocator;
        _adbEnvironment = adbEnvironment;

        CurrentVersion = AppInfo.Version;
        ThemeOptions = Enum.GetValues<ThemeMode>();
        LanguageOptions = Enum.GetValues<AppLanguage>();

        LoadFrom(_settingsService.Current);
        _updateStatusText = _localization.Get("update.ready");
    }

    /// <summary>The running application version (read-only).</summary>
    public string CurrentVersion { get; }

    public string UpdateSource => AppInfo.UpdateServerBaseUrl;

    public string UpdateKey => AppInfo.AppUpdateKey;

    /// <summary>Available theme options for selection controls.</summary>
    public ThemeMode[] ThemeOptions { get; }

    /// <summary>Available language options for selection controls.</summary>
    public AppLanguage[] LanguageOptions { get; }

    // Apply the language immediately on selection (hot reload) and persist just the language so it
    // survives a restart — without saving other in-progress (unsaved) edits on this page.
    partial void OnLanguageChanged(AppLanguage value)
    {
        _localization.SetLanguage(value);

        var current = _settingsService.Current;
        if (current.Language != value)
        {
            var updated = current.Clone();
            updated.Language = value;
            _ = _settingsService.SaveAsync(updated);
        }
    }

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
        _latestUpdate = result.UpdateAvailable ? result : null;
        CanInstallUpdate = result.UpdateAvailable && !string.IsNullOrWhiteSpace(result.DownloadUrl);
        UpdateStatusText = UpdatePresentation.Describe(result, _localization);
    }

    private bool CanInstallUpdateNow() => CanInstallUpdate && !IsInstallingUpdate;

    [RelayCommand(CanExecute = nameof(CanInstallUpdateNow))]
    private async Task InstallUpdateAsync(CancellationToken ct)
    {
        if (_latestUpdate is null)
        {
            return;
        }

        IsInstallingUpdate = true;
        UpdateStatusText = _localization.Get("update.downloading");

        try
        {
            var result = await _updateInstaller.InstallAsync(_latestUpdate, ct).ConfigureAwait(true);
            UpdateStatusText = UpdatePresentation.DescribeInstall(result, _localization);
            CanInstallUpdate = !result.Started;
        }
        finally
        {
            IsInstallingUpdate = false;
        }
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
