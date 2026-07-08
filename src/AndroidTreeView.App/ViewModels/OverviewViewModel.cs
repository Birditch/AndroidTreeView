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

    public string OemUnlockSupportedText => FormatNullableBool(Overview?.OemUnlockSupported);

    public string OemUnlockAllowedText => FormatNullableBool(Overview?.OemUnlockAllowed);

    public string BootloaderLockText => FormatState(Overview?.BootloaderLockState);

    public string DeviceStateText => FormatState(Overview?.DeviceState);

    public string VerifiedBootText => FormatState(Overview?.VerifiedBootState);

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

    private string FormatNullableBool(bool? value) => value switch
    {
        true => _localization.Get("common.yes"),
        false => _localization.Get("common.no"),
        _ => _localization.Get("common.unavailable")
    };

    private string FormatState(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return _localization.Get("common.unavailable");
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "locked" => _localization.Get("state.locked"),
            "unlocked" => _localization.Get("state.unlocked"),
            "green" => _localization.Get("state.green"),
            "yellow" => _localization.Get("state.yellow"),
            "orange" => _localization.Get("state.orange"),
            "red" => _localization.Get("state.red"),
            var state => state
        };
    }
}
