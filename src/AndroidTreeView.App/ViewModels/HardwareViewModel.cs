using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models.Hardware;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Hardware category: aggregated CPU, memory, screen and board information.
/// </summary>
public sealed partial class HardwareViewModel : DeviceCategoryViewModelBase
{
    private readonly IDeviceService _deviceService;
    private readonly ILogger<HardwareViewModel> _logger;

    [ObservableProperty]
    private HardwareInfo? _hardware;

    public HardwareViewModel(IDeviceService deviceService, ILogger<HardwareViewModel> logger)
    {
        _deviceService = deviceService;
        _logger = logger;
    }

    /// <inheritdoc />
    public override DeviceCategory Category => DeviceCategory.Hardware;

    /// <inheritdoc />
    public override Task LoadAsync(string serial, CancellationToken ct)
    {
        Serial = serial;
        return RunAsync(async token =>
        {
            _logger.LogDebug("Loading hardware for {Serial}", serial);
            Hardware = await _deviceService.GetHardwareAsync(serial, token);
        }, ct);
    }
}
