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
    private readonly ILogger<OverviewViewModel> _logger;

    [ObservableProperty]
    private DeviceOverview? _overview;

    public OverviewViewModel(IDeviceService deviceService, ILogger<OverviewViewModel> logger)
    {
        _deviceService = deviceService;
        _logger = logger;
    }

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
