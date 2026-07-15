using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models.Devices;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Overview category: device identity and build summary derived from <c>getprop</c>.
/// </summary>
public sealed partial class OverviewViewModel : DeviceCategoryViewModelBase
{
    private readonly IDeviceService _deviceService;
    private readonly ILocalizationService _localization;
    private readonly ILogger<OverviewViewModel> _logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OemUnlockSupportedText))]
    [NotifyPropertyChangedFor(nameof(OemUnlockAllowedText))]
    [NotifyPropertyChangedFor(nameof(BootloaderLockText))]
    [NotifyPropertyChangedFor(nameof(DeviceStateText))]
    [NotifyPropertyChangedFor(nameof(VerifiedBootText))]
    [NotifyPropertyChangedFor(nameof(MagiskInstalledText))]
    private DeviceOverview? _overview;

    public OverviewViewModel(
        IDeviceService deviceService,
        ILocalizationService localization,
        ILogger<OverviewViewModel> logger)
    {
        _deviceService = deviceService;
        _localization = localization;
        _logger = logger;
    }

    public string OemUnlockSupportedText =>
        UnlockStateFormatter.FormatNullableBool(Overview?.OemUnlockSupported, _localization);

    public string OemUnlockAllowedText => UnlockStateFormatter.FormatOemUnlockAllowed(
        Overview?.OemUnlockAllowed,
        Overview?.BootloaderLockState,
        Overview?.DeviceState,
        Overview?.VerifiedBootState,
        _localization);

    public string BootloaderLockText => UnlockStateFormatter.FormatBootloaderLock(
        Overview?.BootloaderLockState,
        Overview?.DeviceState,
        Overview?.VerifiedBootState,
        _localization);

    public string DeviceStateText => UnlockStateFormatter.FormatState(Overview?.DeviceState, _localization);

    public string VerifiedBootText => UnlockStateFormatter.FormatState(Overview?.VerifiedBootState, _localization);

    public string MagiskInstalledText => Overview?.MagiskInstalled == true
        ? _localization.Get("common.yes")
        : _localization.Get("common.no");

    /// <inheritdoc />
    public override DeviceCategory Category => DeviceCategory.Overview;

    /// <inheritdoc />
    public override Task LoadAsync(string serial, CancellationToken ct)
    {
        Serial = serial;
        return RunAsync(async token =>
        {
            _logger.LogDebug("Loading overview for {Serial}", serial);
            Overview = await _deviceService.GetOverviewAsync(serial, token);
        }, ct);
    }

}
