using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models.Network;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Detail category view model for networking: Wi-Fi IP / MAC plus the per-interface list.
/// </summary>
public sealed partial class NetworkViewModel : DeviceCategoryViewModelBase
{
    private readonly IDeviceService _deviceService;
    private readonly ILogger<NetworkViewModel> _logger;

    [ObservableProperty]
    private NetworkInfo? _network;

    public NetworkViewModel(IDeviceService deviceService, ILogger<NetworkViewModel> logger)
    {
        _deviceService = deviceService;
        _logger = logger;
    }

    /// <summary>Individual network interfaces (wlan0, rmnet, …).</summary>
    public ObservableCollection<NetworkInterfaceInfo> Interfaces { get; } = new();

    public override DeviceCategory Category => DeviceCategory.Network;

    public override Task LoadAsync(string serial, CancellationToken ct)
    {
        Serial = serial;
        _logger.LogDebug("Loading network information for device {Serial}", serial);

        return RunAsync(async token =>
        {
            var network = await _deviceService.GetNetworkAsync(serial, token);

            Network = network;
            Interfaces.Clear();
            foreach (var iface in network.Interfaces)
            {
                Interfaces.Add(iface);
            }
        }, ct);
    }
}
