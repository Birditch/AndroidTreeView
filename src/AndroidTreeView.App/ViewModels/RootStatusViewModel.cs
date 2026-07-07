using System.Threading;
using System.Threading.Tasks;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models.Devices;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Detail category view model for root / superuser detection results
/// (su binary, uid, Magisk version, SELinux mode).
/// </summary>
public sealed partial class RootStatusViewModel : DeviceCategoryViewModelBase
{
    private readonly IDeviceService _deviceService;
    private readonly ILogger<RootStatusViewModel> _logger;

    [ObservableProperty]
    private RootStatus? _root;

    public RootStatusViewModel(IDeviceService deviceService, ILogger<RootStatusViewModel> logger)
    {
        _deviceService = deviceService;
        _logger = logger;
    }

    public override DeviceCategory Category => DeviceCategory.Root;

    public override Task LoadAsync(string serial, CancellationToken ct)
    {
        Serial = serial;
        _logger.LogDebug("Loading root status for device {Serial}", serial);

        return RunAsync(async token =>
        {
            Root = await _deviceService.GetRootStatusAsync(serial, token);
        }, ct);
    }
}
