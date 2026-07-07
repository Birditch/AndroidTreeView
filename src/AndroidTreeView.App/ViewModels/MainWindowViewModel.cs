using AndroidTreeView.App.Services;
using AndroidTreeView.Core;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// The application shell view model. Owns navigation, responsive layout state, ADB availability, the
/// device-monitor subscription (marshalled to the UI thread) and the non-blocking update banner.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private const double WideThreshold = 1200d;
    private const double MediumThreshold = 800d;

    private readonly Func<AboutViewModel> _aboutFactory;
    private readonly Func<DeviceDetailViewModel> _detailFactory;
    private readonly IDeviceMonitor _monitor;
    private readonly ISettingsService _settingsService;
    private readonly IAdbLocator _locator;
    private readonly IAdbEnvironment _environment;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localization;
    private readonly IUpdateService _updateService;
    private readonly IFilePickerService _filePicker;
    private readonly ILogger<MainWindowViewModel> _logger;

    private string? _releaseUrl;
    private bool _monitorSubscribed;

    /// <summary>The view model currently hosted in the content region (resolved via the ViewLocator).</summary>
    [ObservableProperty]
    private object? _currentContent;

    /// <summary>The active navigation section.</summary>
    [ObservableProperty]
    private NavSection _currentSection = NavSection.Devices;

    /// <summary>The responsive layout mode derived from <see cref="WindowWidth"/>.</summary>
    [ObservableProperty]
    private AppLayoutMode _layoutMode = AppLayoutMode.Wide;

    /// <summary>The current client width of the window.</summary>
    [ObservableProperty]
    private double _windowWidth = WideThreshold;

    [ObservableProperty]
    private bool _isWide = true;

    [ObservableProperty]
    private bool _isMedium;

    [ObservableProperty]
    private bool _isNarrow;

    /// <summary>Whether the navigation sidebar is expanded.</summary>
    [ObservableProperty]
    private bool _isSidebarOpen = true;

    /// <summary>Whether adb was located and is usable.</summary>
    [ObservableProperty]
    private bool _isAdbAvailable;

    /// <summary>Localized adb status summary shown in the shell chrome.</summary>
    [ObservableProperty]
    private string _adbStatusText = string.Empty;

    /// <summary>Count of currently connected devices.</summary>
    [ObservableProperty]
    private int _deviceCount;

    /// <summary>Whether the update banner is visible.</summary>
    [ObservableProperty]
    private bool _showUpdateBanner;

    /// <summary>The latest available version, when an update was found.</summary>
    [ObservableProperty]
    private string? _latestVersion;

    /// <summary>Localized text shown inside the update banner.</summary>
    [ObservableProperty]
    private string _updateBannerText = string.Empty;

    public MainWindowViewModel(
        DevicesViewModel devices,
        SettingsViewModel settings,
        SetupViewModel setup,
        Func<AboutViewModel> aboutFactory,
        Func<DeviceDetailViewModel> detailFactory,
        IDeviceMonitor monitor,
        ISettingsService settingsService,
        IAdbLocator locator,
        IAdbEnvironment environment,
        IThemeService themeService,
        ILocalizationService localization,
        IUpdateService updateService,
        IFilePickerService filePicker,
        ILogger<MainWindowViewModel> logger)
    {
        Devices = devices;
        Settings = settings;
        Setup = setup;
        _aboutFactory = aboutFactory;
        _detailFactory = detailFactory;
        _monitor = monitor;
        _settingsService = settingsService;
        _locator = locator;
        _environment = environment;
        _themeService = themeService;
        _localization = localization;
        _updateService = updateService;
        _filePicker = filePicker;
        _logger = logger;

        Devices.DeviceActivated += OnDeviceActivated;
        Devices.RefreshRequested += OnRefreshRequested;
        Setup.AdbReady += OnAdbReady;
        _localization.LanguageChanged += OnLanguageChanged;

        _adbStatusText = _localization.Get("adb.status.missing");
        _currentContent = Devices;
    }

    /// <summary>The device grid view model (also the default content).</summary>
    public DevicesViewModel Devices { get; }

    /// <summary>The settings page view model.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>The ADB setup page view model, shown when adb is missing.</summary>
    public SetupViewModel Setup { get; }

    /// <summary>
    /// Loads settings, applies language/theme, locates adb, starts monitoring (or shows Setup) and kicks
    /// off a non-blocking update check. Safe to call once at startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        var settings = await _settingsService.LoadAsync().ConfigureAwait(true);

        _localization.SetLanguage(settings.Language);
        _themeService.Initialize();
        _themeService.Apply(settings.Theme);
        _themeService.ApplyAccent(settings.AccentColor);

        var location = await _locator.LocateAsync(settings.AdbPath).ConfigureAwait(true);
        _environment.Set(location);

        SubscribeMonitor();

        if (location is null)
        {
            IsAdbAvailable = false;
            AdbStatusText = _localization.Get("adb.status.missing");
            CurrentSection = NavSection.Devices;
            CurrentContent = Setup;
        }
        else
        {
            IsAdbAvailable = true;
            AdbStatusText = _localization.Get("adb.status.ready");
            _monitor.UpdateInterval(TimeSpan.FromSeconds(Math.Max(1, settings.DeviceRefreshIntervalSeconds)));
            _monitor.Start();
            CurrentContent = Devices;
        }

        _ = CheckForUpdatesAsync();
    }

    /// <summary>Opens the detail page for the supplied device card.</summary>
    public void ShowDeviceDetail(DeviceCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);

        var detail = _detailFactory();
        detail.BackRequested += OnDetailBackRequested;
        CurrentContent = detail;
        _ = detail.InitializeAsync(card);
    }

    /// <summary>Pushes a new client width and recomputes the responsive layout state.</summary>
    public void SetWindowWidth(double width)
    {
        if (double.IsNaN(width) || width <= 0)
        {
            return;
        }

        WindowWidth = width;

        var mode = width >= WideThreshold
            ? AppLayoutMode.Wide
            : width >= MediumThreshold
                ? AppLayoutMode.Medium
                : AppLayoutMode.Narrow;

        LayoutMode = mode;
        IsWide = mode == AppLayoutMode.Wide;
        IsMedium = mode == AppLayoutMode.Medium;
        IsNarrow = mode == AppLayoutMode.Narrow;
    }

    [RelayCommand]
    private void NavigateDevices()
    {
        CurrentSection = NavSection.Devices;
        CurrentContent = Devices;
    }

    [RelayCommand]
    private void NavigateSettings()
    {
        CurrentSection = NavSection.Settings;
        CurrentContent = Settings;
    }

    [RelayCommand]
    private void NavigateAbout()
    {
        CurrentSection = NavSection.About;
        CurrentContent = _aboutFactory();
    }

    [RelayCommand]
    private Task RefreshAsync() => RefreshDevicesAsync();

    private async Task RefreshDevicesAsync()
    {
        try
        {
            await _monitor.RefreshNowAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Manual device refresh failed.");
        }
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

    [RelayCommand]
    private void Back()
    {
        CurrentSection = NavSection.Devices;
        CurrentContent = Devices;
    }

    [RelayCommand]
    private async Task OpenReleaseAsync()
    {
        var url = string.IsNullOrWhiteSpace(_releaseUrl) ? AppInfo.ReleasesUrl : _releaseUrl;
        await _filePicker.OpenUrlAsync(url).ConfigureAwait(true);
    }

    [RelayCommand]
    private void DismissUpdate() => ShowUpdateBanner = false;

    private void SubscribeMonitor()
    {
        if (_monitorSubscribed)
        {
            return;
        }

        _monitor.DevicesChanged += OnDevicesChanged;
        _monitorSubscribed = true;
    }

    private void OnDevicesChanged(object? sender, DeviceListChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsAdbAvailable = e.AdbAvailable;
            DeviceCount = e.Devices.Count;
            AdbStatusText = e.AdbAvailable
                ? _localization.Format("adb.status.devices", e.Devices.Count)
                : _localization.Get("adb.status.missing");

            Devices.Reconcile(e.Devices, e.AdbAvailable);

            if (e.AdbAvailable && ReferenceEquals(CurrentContent, Setup))
            {
                CurrentContent = Devices;
            }
        });
    }

    private void OnDeviceActivated(object? sender, DeviceCardViewModel card) => ShowDeviceDetail(card);

    private void OnRefreshRequested(object? sender, EventArgs e) => _ = RefreshDevicesAsync();

    private void OnDetailBackRequested(object? sender, EventArgs e)
    {
        if (sender is DeviceDetailViewModel detail)
        {
            detail.BackRequested -= OnDetailBackRequested;
        }

        CurrentSection = NavSection.Devices;
        CurrentContent = Devices;
    }

    private void OnAdbReady(object? sender, EventArgs e)
    {
        IsAdbAvailable = true;
        AdbStatusText = _localization.Get("adb.status.ready");

        if (!_monitor.IsRunning)
        {
            _monitor.UpdateInterval(TimeSpan.FromSeconds(Math.Max(1, _settingsService.Current.DeviceRefreshIntervalSeconds)));
            _monitor.Start();
        }

        CurrentSection = NavSection.Devices;
        CurrentContent = Devices;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        AdbStatusText = IsAdbAvailable
            ? _localization.Format("adb.status.devices", DeviceCount)
            : _localization.Get("adb.status.missing");

        if (ShowUpdateBanner)
        {
            UpdateBannerText = _localization.Format("update.available", LatestVersion ?? string.Empty);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var result = await _updateService.CheckForUpdatesAsync(false).ConfigureAwait(true);
            if (result.Status == UpdateCheckStatus.UpdateAvailable && result.UpdateAvailable)
            {
                _releaseUrl = result.ReleaseUrl;
                LatestVersion = result.LatestVersion;
                UpdateBannerText = _localization.Format("update.available", result.LatestVersion ?? string.Empty);
                ShowUpdateBanner = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Startup update check failed.");
        }
    }
}
