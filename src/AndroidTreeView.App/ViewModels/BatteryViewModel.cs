using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models.Battery;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Battery category: charge level, status, health, temperature and cycle count.
/// </summary>
public sealed partial class BatteryViewModel : DeviceCategoryViewModelBase
{
    private readonly IDeviceService _deviceService;
    private readonly ILocalizationService _localization;
    private readonly ILogger<BatteryViewModel> _logger;

    [ObservableProperty]
    private BatteryInfo? _battery;

    [ObservableProperty]
    private int _levelPercent;

    [ObservableProperty]
    private string _chargingText = string.Empty;

    public BatteryViewModel(
        IDeviceService deviceService,
        ILocalizationService localization,
        ILogger<BatteryViewModel> logger)
    {
        _deviceService = deviceService;
        _localization = localization;
        _logger = logger;
    }

    /// <inheritdoc />
    public override DeviceCategory Category => DeviceCategory.Battery;

    /// <inheritdoc />
    public override Task LoadAsync(string serial, CancellationToken ct)
    {
        Serial = serial;
        return RunAsync(async token =>
        {
            _logger.LogDebug("Loading battery for {Serial}", serial);
            Battery = await _deviceService.GetBatteryAsync(serial, token);
        }, ct);
    }

    partial void OnBatteryChanged(BatteryInfo? value)
    {
        LevelPercent = value?.LevelPercent ?? 0;
        ChargingText = value?.IsCharging == true
            ? _localization.Get("status.charging")
            : string.Empty;
    }
}
