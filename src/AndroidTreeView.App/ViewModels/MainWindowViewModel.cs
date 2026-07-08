using AndroidTreeView.App.Services;
using AndroidTreeView.Core;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models;
using AndroidTreeView.Models.Devices;
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
    private readonly IUpdateInstaller _updateInstaller;
    private readonly IFilePickerService _filePicker;
    private readonly IDeviceService _deviceService;
    private readonly IScreenMirrorLauncher _screenLauncher;
    private readonly ICliLauncher _cli;
    private readonly IFastbootService _fastboot;
    private readonly ILogger<MainWindowViewModel> _logger;

    // Per-device enrichment state (accessed only on the UI thread from OnDevicesChanged).
    // Identity + root are re-probed on this cadence (not just once) so a card never drifts out of sync
    // with the detail page (e.g. root becoming visible after a grant).
    private static readonly TimeSpan StaticRefreshInterval = TimeSpan.FromSeconds(8);
    private readonly Dictionary<string, DateTimeOffset> _staticEnrichedAt = new(StringComparer.Ordinal);
    private readonly HashSet<string> _enriching = new(StringComparer.Ordinal);
    private readonly HashSet<string> _fastbootEnriching = new(StringComparer.Ordinal);

    private string? _releaseUrl;
    private UpdateCheckResult? _latestUpdate;
    private bool _monitorSubscribed;
    private CancellationTokenSource? _toastCts;

    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(2.4);

    /// <summary>The view model currently hosted in the content region (resolved via the ViewLocator).</summary>
    [ObservableProperty]
    private object? _currentContent;

    /// <summary>The active navigation section.</summary>
    [ObservableProperty]
    private NavSection _currentSection = NavSection.Devices;

    /// <summary>Localized title shown in the top bar for the active section.</summary>
    [ObservableProperty]
    private string _currentTitle = string.Empty;

    /// <summary>Whether the Devices section is active (device-specific top-bar chrome only shows here).</summary>
    [ObservableProperty]
    private bool _isDevicesSection = true;

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

    [ObservableProperty]
    private bool _isToastVisible;

    [ObservableProperty]
    private string _toastMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToastInfo))]
    [NotifyPropertyChangedFor(nameof(IsToastSuccess))]
    [NotifyPropertyChangedFor(nameof(IsToastWarning))]
    [NotifyPropertyChangedFor(nameof(IsToastError))]
    private NotifierLevel _toastLevel = NotifierLevel.Info;

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
        IUpdateInstaller updateInstaller,
        IFilePickerService filePicker,
        IDeviceService deviceService,
        IScreenMirrorLauncher screenLauncher,
        ICliLauncher cli,
        IFastbootService fastboot,
        IDialogService dialog,
        ILogger<MainWindowViewModel> logger)
    {
        Devices = devices;
        Dialog = dialog;
        _cli = cli;
        _fastboot = fastboot;
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
        _updateInstaller = updateInstaller;
        _filePicker = filePicker;
        _deviceService = deviceService;
        _screenLauncher = screenLauncher;
        _logger = logger;

        Devices.DeviceActivated += OnDeviceActivated;
        Devices.RefreshRequested += OnRefreshRequested;
        Devices.SetupRequested += OnSetupRequested;
        Devices.ScreenRequested += OnScreenRequested;
        Devices.CliRequested += OnCliRequested;
        _screenLauncher.MirrorStateChanged += OnMirrorStateChanged;
        Setup.AdbReady += OnAdbReady;
        _localization.LanguageChanged += OnLanguageChanged;

        _adbStatusText = _localization.Get("adb.status.missing");
        _currentTitle = TitleFor(NavSection.Devices);
        _currentContent = Devices;
    }

    /// <summary>The device grid view model (also the default content).</summary>
    public DevicesViewModel Devices { get; }

    /// <summary>The settings page view model.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>The ADB setup page view model, shown when adb is missing.</summary>
    public SetupViewModel Setup { get; }

    /// <summary>App name + version, shown as the OS window title.</summary>
    public string AppTitle { get; } = $"{AppInfo.Name} v{AppInfo.Version}";

    /// <summary>App name only (no version) — used for the sidebar brand so it never overflows the panel.</summary>
    public string AppName { get; } = AppInfo.Name;

    /// <summary>The app-wide modal confirmation dialog (bound by the shell for a blurred, centered prompt).</summary>
    public IDialogService Dialog { get; }

    public bool IsToastInfo => ToastLevel == NotifierLevel.Info;

    public bool IsToastSuccess => ToastLevel == NotifierLevel.Success;

    public bool IsToastWarning => ToastLevel == NotifierLevel.Warning;

    public bool IsToastError => ToastLevel == NotifierLevel.Error;

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
            // Always land on the device list; surface the "ADB not found" guidance inside it.
            Devices.Reconcile(Array.Empty<AdbDevice>(), adbAvailable: false);
            CurrentSection = NavSection.Devices;
            CurrentContent = Devices;
        }
        else
        {
            IsAdbAvailable = true;
            AdbStatusText = _localization.Get("adb.status.ready");
            _monitor.UpdateInterval(TimeSpan.FromSeconds(1));
            _monitor.Start();
            CurrentContent = Devices;
        }

        _ = CheckForUpdatesAsync();
    }

    public void ShowToast(string message, NotifierLevel level)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ShowToast(message, level));
            return;
        }

        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _toastCts = new CancellationTokenSource();

        ToastLevel = level;
        ToastMessage = message;
        IsToastVisible = true;

        _ = HideToastAfterAsync(_toastCts.Token);
    }

    private async Task HideToastAfterAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(ToastDuration, ct).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    IsToastVisible = false;
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Opens the detail page for the supplied device card.</summary>
    public void ShowDeviceDetail(DeviceCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!card.CanOpenDetail)
        {
            return;
        }

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
    private async Task InstallUpdateAsync(CancellationToken ct)
    {
        if (_latestUpdate is null)
        {
            await OpenReleaseAsync().ConfigureAwait(true);
            return;
        }

        UpdateBannerText = _localization.Get("update.downloading");
        var result = await _updateInstaller.InstallAsync(_latestUpdate, ct).ConfigureAwait(true);
        var message = UpdatePresentation.DescribeInstall(result, _localization);
        UpdateBannerText = message;
        ShowToast(message, result.Started ? NotifierLevel.Success : NotifierLevel.Warning);

        if (result.Started)
        {
            ShowUpdateBanner = false;
        }
    }

    [RelayCommand]
    private void DismissUpdate() => ShowUpdateBanner = false;

    public Task HandleDroppedFilesAsync(IReadOnlyList<string> paths) =>
        Devices.HandleDroppedFilesAsync(paths);

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

            EnrichDevices(e.Devices);
        });
    }

    private void OnDeviceActivated(object? sender, DeviceCardViewModel card) => ShowDeviceDetail(card);

    private void OnRefreshRequested(object? sender, EventArgs e) => _ = RefreshDevicesAsync();

    private void OnSetupRequested(object? sender, EventArgs e) => CurrentContent = Setup;

    private void OnScreenRequested(object? sender, DeviceCardViewModel card) =>
        _screenLauncher.Open(card.Serial, card.DisplayName);

    // "CLI" right-click item: adb terminal for online devices, fastboot terminal for bootloader ones.
    private void OnCliRequested(object? sender, DeviceCardViewModel card) =>
        _cli.Open(card.Serial, card.DisplayName, card.IsFastboot);

    // Keep the card's "投屏中" tag in sync with the live mirror session (raised on the UI thread).
    private void OnMirrorStateChanged(object? sender, MirrorStateChangedEventArgs e)
    {
        var card = Devices.FindCard(e.Serial);
        if (card is not null)
        {
            card.IsMirroring = e.IsMirroring;
        }
    }

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
            _monitor.UpdateInterval(TimeSpan.FromSeconds(1));
            _monitor.Start();
        }

        CurrentSection = NavSection.Devices;
        CurrentContent = Devices;
    }

    partial void OnCurrentSectionChanged(NavSection value)
    {
        IsDevicesSection = value == NavSection.Devices;
        CurrentTitle = TitleFor(value);
    }

    private string TitleFor(NavSection section) => section switch
    {
        NavSection.Settings => _localization.Get("settings.title"),
        NavSection.About => _localization.Get("about.title"),
        _ => _localization.Get("devices.title"),
    };

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        CurrentTitle = TitleFor(CurrentSection);
        AdbStatusText = IsAdbAvailable
            ? _localization.Format("adb.status.devices", DeviceCount)
            : _localization.Get("adb.status.missing");

        if (ShowUpdateBanner)
        {
            UpdateBannerText = _localization.Format("update.available", LatestVersion ?? string.Empty);
        }
    }

    // Fills each online card with real data: battery every tick (fast), and static identity + root once
    // per device. A per-serial guard prevents overlapping runs from piling up under the 1 Hz monitor.
    private void EnrichDevices(IReadOnlyList<AdbDevice> devices)
    {
        foreach (var device in devices)
        {
            // Fastboot devices have no OS to query over adb — read their fastboot getvar facts instead.
            if (device.State == DeviceConnectionState.Bootloader)
            {
                MaybeEnrichFastboot(device.Serial);
                continue;
            }

            if (!device.IsOnline || _enriching.Contains(device.Serial))
            {
                continue;
            }

            // Skip a device that is being mirrored: scrcpy needs the USB/adb channel to itself. Per-second
            // enrichment (battery / getprop / root / settings) over the same channel starves scrcpy's video
            // and control streams — the cause of the black-until-click, lag, and unresponsive control.
            if (_screenLauncher.IsMirroring(device.Serial))
            {
                continue;
            }

            _enriching.Add(device.Serial);
            _ = EnrichOneAsync(device.Serial);
        }
    }

    private async Task EnrichOneAsync(string serial)
    {
        try
        {
            var card = Devices.FindCard(serial);
            if (card is null)
            {
                return;
            }

            var battery = await _deviceService.GetBatteryAsync(serial).ConfigureAwait(true);
            card.ApplyBattery(battery);

            var due = !_staticEnrichedAt.TryGetValue(serial, out var last)
                      || DateTimeOffset.Now - last > StaticRefreshInterval;
            if (due)
            {
                var overview = await _deviceService.GetOverviewAsync(serial).ConfigureAwait(true);
                card.ApplyOverview(overview);

                var root = await _deviceService.GetRootStatusAsync(serial).ConfigureAwait(true);
                card.ApplyRoot(root);

                await card.RefreshActionStateAsync().ConfigureAwait(true);

                _staticEnrichedAt[serial] = DateTimeOffset.Now;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Enriching device {Serial} failed.", serial);
        }
        finally
        {
            _enriching.Remove(serial);
        }
    }

    // Fetch fastboot getvar facts once per fastboot device (until it has them or is unplugged).
    private void MaybeEnrichFastboot(string serial)
    {
        var card = Devices.FindCard(serial);
        if (card is null || card.FastbootFacts.Count > 0 || !_fastbootEnriching.Add(serial))
        {
            return;
        }

        _ = EnrichFastbootAsync(serial);
    }

    private async Task EnrichFastbootAsync(string serial)
    {
        try
        {
            var vars = await _fastboot.GetVariablesAsync(serial).ConfigureAwait(true);
            Devices.FindCard(serial)?.ApplyFastbootInfo(vars);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Fastboot enrichment for {Serial} failed.", serial);
        }
        finally
        {
            _fastbootEnriching.Remove(serial);
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
                _latestUpdate = result;
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
