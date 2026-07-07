using AndroidTreeView.Models;
using AndroidTreeView.Models.Battery;
using AndroidTreeView.Models.Devices;
using AndroidTreeView.Models.Hardware;
using AndroidTreeView.Models.Network;
using AndroidTreeView.Models.Storage;
using AndroidTreeView.Models.System;

namespace AndroidTreeView.Core.Interfaces;

/// <summary>
/// Reads structured information from connected Android devices.
/// Implementations are resilient: normal device errors yield empty/null fields rather than throwing.
/// </summary>
public interface IDeviceService
{
    Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken ct = default);

    Task<DeviceOverview> GetOverviewAsync(string serial, CancellationToken ct = default);

    Task<HardwareInfo> GetHardwareAsync(string serial, CancellationToken ct = default);

    Task<BatteryInfo> GetBatteryAsync(string serial, CancellationToken ct = default);

    Task<SystemInfo> GetSystemInfoAsync(string serial, CancellationToken ct = default);

    Task<StorageInfo> GetStorageAsync(string serial, CancellationToken ct = default);

    Task<NetworkInfo> GetNetworkAsync(string serial, CancellationToken ct = default);

    Task<RootStatus> GetRootStatusAsync(string serial, CancellationToken ct = default);

    Task<DeviceProperties> GetPropertiesAsync(string serial, CancellationToken ct = default);
}
