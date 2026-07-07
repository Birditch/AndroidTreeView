using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using AndroidTreeView.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Device detail shell: a header snapshot plus the nine category tabs. Selecting a tab loads that
/// category's view model; <c>Refresh</c> reloads the current tab and the volatile header data;
/// <c>Back</c> raises <see cref="BackRequested"/> so the shell can return to the device grid.
/// </summary>
public sealed partial class DeviceDetailViewModel : ViewModelBase
{
    private readonly OverviewViewModel _overview;
    private readonly HardwareViewModel _hardware;
    private readonly BatteryViewModel _battery;
    private readonly SystemInfoViewModel _system;
    private readonly StorageViewModel _storage;
    private readonly NetworkViewModel _network;
    private readonly RootStatusViewModel _root;
    private readonly LogcatViewModel _logcat;
    private readonly RawPropertiesViewModel _rawProperties;
    private readonly ILocalizationService _localization;
    private readonly IDeviceService _deviceService;
    private readonly ILogger<DeviceDetailViewModel> _logger;

    private CancellationTokenSource? _tabCts;

    [ObservableProperty]
    private string _serial = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _model = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private int? _batteryPercent;

    [ObservableProperty]
    private string _rootText = string.Empty;

    [ObservableProperty]
    private DeviceBadgeKind _statusKind;

    [ObservableProperty]
    private DetailTab? _selectedTab;

    [ObservableProperty]
    private DeviceCategoryViewModelBase? _currentCategory;

    public DeviceDetailViewModel(
        OverviewViewModel overview,
        HardwareViewModel hardware,
        BatteryViewModel battery,
        SystemInfoViewModel system,
        StorageViewModel storage,
        NetworkViewModel network,
        RootStatusViewModel root,
        LogcatViewModel logcat,
        RawPropertiesViewModel rawProperties,
        ILocalizationService localization,
        IDeviceService deviceService,
        ILogger<DeviceDetailViewModel> logger)
    {
        _overview = overview;
        _hardware = hardware;
        _battery = battery;
        _system = system;
        _storage = storage;
        _network = network;
        _root = root;
        _logcat = logcat;
        _rawProperties = rawProperties;
        _localization = localization;
        _deviceService = deviceService;
        _logger = logger;
    }

    /// <summary>The nine category tabs, built once on first initialization.</summary>
    public ObservableCollection<DetailTab> Tabs { get; } = new();

    /// <summary>Raised when the user presses Back so the shell can navigate to the device grid.</summary>
    public event EventHandler? BackRequested;

    /// <summary>
    /// Seeds the header from <paramref name="card"/>, builds the tabs, selects Overview (which triggers
    /// its load) and refreshes the volatile header values.
    /// </summary>
    public async Task InitializeAsync(DeviceCardViewModel card, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(card);

        Serial = card.Serial;
        DisplayName = card.DisplayName;
        Model = card.Model;
        StatusText = card.StatusText;
        StatusKind = card.StatusKind;
        BatteryPercent = card.BatteryPercent;
        RootText = card.RootText;

        BuildTabs();
        SelectedTab = Tabs.Count > 0 ? Tabs[0] : null;

        await RefreshHeaderAsync(ct).ConfigureAwait(false);
    }

    partial void OnSelectedTabChanged(DetailTab? value)
    {
        if (value is null)
        {
            return;
        }

        CurrentCategory = value.ViewModel;
        _ = LoadTabAsync(value);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        await RefreshHeaderAsync(ct).ConfigureAwait(false);

        var tab = SelectedTab;
        if (tab is not null)
        {
            await LoadTabAsync(tab).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private void Back()
    {
        _tabCts?.Cancel();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BuildTabs()
    {
        if (Tabs.Count > 0)
        {
            return;
        }

        Tabs.Add(new DetailTab(DeviceCategory.Overview, _localization.Get("detail.tab.overview"), "\U0001F4CB", _overview));
        Tabs.Add(new DetailTab(DeviceCategory.Hardware, _localization.Get("detail.tab.hardware"), "\U0001F5A5", _hardware));
        Tabs.Add(new DetailTab(DeviceCategory.Battery, _localization.Get("detail.tab.battery"), "\U0001F50B", _battery));
        Tabs.Add(new DetailTab(DeviceCategory.System, _localization.Get("detail.tab.system"), "⚙", _system));
        Tabs.Add(new DetailTab(DeviceCategory.Storage, _localization.Get("detail.tab.storage"), "\U0001F4BE", _storage));
        Tabs.Add(new DetailTab(DeviceCategory.Network, _localization.Get("detail.tab.network"), "\U0001F310", _network));
        Tabs.Add(new DetailTab(DeviceCategory.Root, _localization.Get("detail.tab.root"), "\U0001F513", _root));
        Tabs.Add(new DetailTab(DeviceCategory.Logcat, _localization.Get("detail.tab.logcat"), "\U0001F4DC", _logcat));
        Tabs.Add(new DetailTab(DeviceCategory.RawProperties, _localization.Get("detail.tab.raw"), "\U0001F5C2", _rawProperties));
    }

    private async Task LoadTabAsync(DetailTab tab)
    {
        _tabCts?.Cancel();
        _tabCts?.Dispose();
        var cts = new CancellationTokenSource();
        _tabCts = cts;

        try
        {
            await tab.ViewModel.LoadAsync(Serial, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when switching tabs quickly.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load detail tab {Category} for {Serial}", tab.Category, Serial);
        }
    }

    private async Task RefreshHeaderAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(Serial))
        {
            return;
        }

        try
        {
            var battery = await _deviceService.GetBatteryAsync(Serial, ct).ConfigureAwait(false);
            BatteryPercent = battery.LevelPercent;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Header battery refresh failed for {Serial}", Serial);
        }

        try
        {
            var root = await _deviceService.GetRootStatusAsync(Serial, ct).ConfigureAwait(false);
            RootText = root.IsRooted ? _localization.Get("status.rooted") : _localization.Get("root.notdetected");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Header root refresh failed for {Serial}", Serial);
        }
    }
}
