using AndroidTreeView.Core.Exceptions;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;
using AndroidTreeView.Models;
using AndroidTreeView.Models.Battery;
using AndroidTreeView.Models.Devices;
using AndroidTreeView.Models.Hardware;
using AndroidTreeView.Models.Network;
using AndroidTreeView.Models.Storage;
using AndroidTreeView.Models.System;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AndroidTreeView.Core.Tests;

public sealed class DeviceMonitorTests
{
    [Fact]
    public async Task StartThenStop_TogglesIsRunningWithoutThrowing()
    {
        var monitor = new DeviceMonitor(
            new FakeDeviceService(() => Array.Empty<AdbDevice>()),
            NullLogger<DeviceMonitor>.Instance);

        monitor.Start();
        Assert.True(monitor.IsRunning);

        // Second start is a no-op and must not throw.
        monitor.Start();

        await monitor.StopAsync();
        Assert.False(monitor.IsRunning);

        // Stopping again is a no-op and must not throw.
        await monitor.StopAsync();
    }

    [Fact]
    public async Task RefreshNowAsync_RaisesDevicesChanged_WithAdbAvailableTrue()
    {
        var devices = new AdbDevice[]
        {
            new() { Serial = "ABC123", State = DeviceConnectionState.Online },
            new() { Serial = "DEF456", State = DeviceConnectionState.Offline },
        };
        var monitor = new DeviceMonitor(
            new FakeDeviceService(() => devices),
            NullLogger<DeviceMonitor>.Instance);

        DeviceListChangedEventArgs? captured = null;
        monitor.DevicesChanged += (_, e) => captured = e;

        var result = await monitor.RefreshNowAsync();

        Assert.True(result.AdbAvailable);
        Assert.Null(result.Error);
        Assert.Equal(2, result.Devices.Count);

        Assert.NotNull(captured);
        Assert.True(captured!.AdbAvailable);
        Assert.Equal(2, captured.Devices.Count);
    }

    [Fact]
    public async Task RefreshNowAsync_AdbNotFound_RaisesEventWithAdbAvailableFalse()
    {
        var monitor = new DeviceMonitor(
            new FakeDeviceService(() => throw new AdbNotFoundException()),
            NullLogger<DeviceMonitor>.Instance);

        DeviceListChangedEventArgs? captured = null;
        monitor.DevicesChanged += (_, e) => captured = e;

        var result = await monitor.RefreshNowAsync();

        Assert.False(result.AdbAvailable);
        Assert.Empty(result.Devices);
        Assert.NotNull(result.Error);

        Assert.NotNull(captured);
        Assert.False(captured!.AdbAvailable);
    }

    private sealed class FakeDeviceService : IDeviceService
    {
        private readonly Func<IReadOnlyList<AdbDevice>> _listFactory;

        public FakeDeviceService(Func<IReadOnlyList<AdbDevice>> listFactory) => _listFactory = listFactory;

        public Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken ct = default) =>
            Task.FromResult(_listFactory());

        public Task<DeviceOverview> GetOverviewAsync(string serial, CancellationToken ct = default) =>
            Task.FromResult(new DeviceOverview());

        public Task<HardwareInfo> GetHardwareAsync(string serial, CancellationToken ct = default) =>
            Task.FromResult(new HardwareInfo());

        public Task<BatteryInfo> GetBatteryAsync(string serial, CancellationToken ct = default) =>
            Task.FromResult(new BatteryInfo());

        public Task<SystemInfo> GetSystemInfoAsync(string serial, CancellationToken ct = default) =>
            Task.FromResult(new SystemInfo());

        public Task<StorageInfo> GetStorageAsync(string serial, CancellationToken ct = default) =>
            Task.FromResult(new StorageInfo());

        public Task<NetworkInfo> GetNetworkAsync(string serial, CancellationToken ct = default) =>
            Task.FromResult(new NetworkInfo());

        public Task<RootStatus> GetRootStatusAsync(string serial, CancellationToken ct = default) =>
            Task.FromResult(new RootStatus());

        public Task<DeviceProperties> GetPropertiesAsync(string serial, CancellationToken ct = default) =>
            Task.FromResult(new DeviceProperties());
    }
}
