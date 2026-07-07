using System;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models.Battery;
using AndroidTreeView.Models.Devices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// A single glass device card in the grid. Maps an <see cref="AdbDevice"/> (plus optional battery / root
/// enrichment) into localized, display-ready properties. Battery and root values are preserved across
/// refreshes when not re-supplied, and cycle count is never fabricated.
/// </summary>
public sealed partial class DeviceCardViewModel : ViewModelBase
{
    private const int LowBatteryThresholdPercent = 20;

    private readonly ILocalizationService _localization;
    private readonly Action<DeviceCardViewModel> _onOpen;

    [ObservableProperty]
    private string _serial = string.Empty;

    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _manufacturer = string.Empty;

    [ObservableProperty]
    private string _model = string.Empty;

    [ObservableProperty]
    private string _androidVersion = string.Empty;

    [ObservableProperty]
    private DeviceConnectionState _state;

    [ObservableProperty]
    private DeviceBadgeKind _statusKind;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private int? _batteryPercent;

    [ObservableProperty]
    private bool _isCharging;

    [ObservableProperty]
    private string _chargingText = string.Empty;

    [ObservableProperty]
    private double? _batteryTemperatureCelsius;

    [ObservableProperty]
    private string _temperatureText = string.Empty;

    [ObservableProperty]
    private int? _cycleCount;

    [ObservableProperty]
    private bool _cycleCountAvailable;

    [ObservableProperty]
    private string _cycleCountText = string.Empty;

    [ObservableProperty]
    private bool? _isRooted;

    [ObservableProperty]
    private string _rootText = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _lastRefresh;

    [ObservableProperty]
    private string _lastRefreshText = string.Empty;

    [ObservableProperty]
    private bool _isLowBattery;

    [ObservableProperty]
    private bool _isUnauthorized;

    [ObservableProperty]
    private bool _isOnline;

    public DeviceCardViewModel(ILocalizationService localization, Action<DeviceCardViewModel> onOpen)
    {
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _onOpen = onOpen ?? throw new ArgumentNullException(nameof(onOpen));
    }

    /// <summary>Opens this device's detail page (delegates to the owning <see cref="DevicesViewModel"/>).</summary>
    [RelayCommand]
    private void Open() => _onOpen(this);

    /// <summary>
    /// Refreshes the card from the latest device snapshot. <paramref name="battery"/> and
    /// <paramref name="root"/> are optional: when <c>null</c> the previously mapped values are kept so a
    /// lightweight device-list refresh never wipes enriched data.
    /// </summary>
    public void UpdateFrom(AdbDevice device, BatteryInfo? battery, RootStatus? root)
    {
        ArgumentNullException.ThrowIfNull(device);

        var firstMap = LastRefresh is null;

        Serial = device.Serial;
        Model = device.Model ?? string.Empty;
        DisplayName = device.DisplayName;
        State = device.State;
        IsOnline = device.IsOnline;
        IsUnauthorized = device.State == DeviceConnectionState.Unauthorized;
        StatusKind = MapKind(device.State);
        StatusText = MapStatusText(device);

        if (battery is not null)
        {
            BatteryPercent = battery.LevelPercent;
            IsCharging = battery.IsCharging;
            ChargingText = battery.IsCharging ? _localization.Get("common.yes") : _localization.Get("common.no");
            BatteryTemperatureCelsius = battery.TemperatureCelsius;
            TemperatureText = battery.TemperatureCelsius is { } celsius
                ? string.Format(_localization.CurrentCulture, "{0:0.0} ℃", celsius)
                : _localization.Get("common.unavailable");
            CycleCount = battery.CycleCount;
            CycleCountAvailable = battery.CycleCount.HasValue;
            CycleCountText = battery.CycleCount is { } cycles
                ? string.Format(_localization.CurrentCulture, "{0}: {1}", _localization.Get("card.cycle"), cycles)
                : _localization.Get("card.cycle.unavailable");
            IsLowBattery = battery.LevelPercent is { } level && level <= LowBatteryThresholdPercent && !battery.IsCharging;
        }
        else if (firstMap)
        {
            ChargingText = _localization.Get("common.unavailable");
            TemperatureText = _localization.Get("common.unavailable");
            CycleCountAvailable = false;
            CycleCountText = _localization.Get("card.cycle.unavailable");
        }

        if (root is not null)
        {
            IsRooted = root.IsRooted;
            RootText = root.IsRooted ? _localization.Get("status.rooted") : _localization.Get("root.notdetected");
        }
        else if (firstMap)
        {
            RootText = _localization.Get("common.unavailable");
        }

        LastRefresh = DateTimeOffset.Now;
        LastRefreshText = string.Format(
            _localization.CurrentCulture,
            "{0}: {1}",
            _localization.Get("card.lastrefresh"),
            LastRefresh.Value.LocalDateTime.ToString("HH:mm:ss", _localization.CurrentCulture));
    }

    private static DeviceBadgeKind MapKind(DeviceConnectionState state) => state switch
    {
        DeviceConnectionState.Online => DeviceBadgeKind.Online,
        DeviceConnectionState.Unauthorized => DeviceBadgeKind.Unauthorized,
        DeviceConnectionState.Offline or DeviceConnectionState.Disconnected => DeviceBadgeKind.Offline,
        _ => DeviceBadgeKind.Other,
    };

    private string MapStatusText(AdbDevice device) => device.State switch
    {
        DeviceConnectionState.Online => _localization.Get("status.online"),
        DeviceConnectionState.Unauthorized => _localization.Get("status.unauthorized"),
        DeviceConnectionState.Offline or DeviceConnectionState.Disconnected => _localization.Get("status.offline"),
        _ => string.IsNullOrWhiteSpace(device.RawState) ? _localization.Get("status.offline") : device.RawState!,
    };
}
