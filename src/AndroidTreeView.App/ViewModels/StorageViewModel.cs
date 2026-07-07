using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models.Storage;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Detail category view model for storage partitions parsed from <c>df</c>.
/// Exposes an observable list of <see cref="StoragePartition"/> for the storage table / usage bars.
/// </summary>
public sealed partial class StorageViewModel : DeviceCategoryViewModelBase
{
    private readonly IDeviceService _deviceService;
    private readonly ILogger<StorageViewModel> _logger;

    public StorageViewModel(IDeviceService deviceService, ILogger<StorageViewModel> logger)
    {
        _deviceService = deviceService;
        _logger = logger;
    }

    /// <summary>Mounted partitions with total / used / free byte counts.</summary>
    public ObservableCollection<StoragePartition> Partitions { get; } = new();

    public override DeviceCategory Category => DeviceCategory.Storage;

    public override Task LoadAsync(string serial, CancellationToken ct)
    {
        Serial = serial;
        _logger.LogDebug("Loading storage partitions for device {Serial}", serial);

        return RunAsync(async token =>
        {
            var storage = await _deviceService.GetStorageAsync(serial, token);

            Partitions.Clear();
            foreach (var partition in storage.Partitions)
            {
                Partitions.Add(partition);
            }
        }, ct);
    }
}
