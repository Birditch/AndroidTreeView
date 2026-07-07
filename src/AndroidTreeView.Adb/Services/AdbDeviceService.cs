using System.Globalization;
using AndroidTreeView.Adb.Commands;
using AndroidTreeView.Adb.Parsers;
using AndroidTreeView.Core.Exceptions;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Core.Services;
using AndroidTreeView.Models;
using AndroidTreeView.Models.Battery;
using AndroidTreeView.Models.Devices;
using AndroidTreeView.Models.Hardware;
using AndroidTreeView.Models.Network;
using AndroidTreeView.Models.Storage;
using AndroidTreeView.Models.System;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Adb.Services;

/// <summary>
/// Orchestrates adb commands and parsers to build structured device information. Each accessor
/// runs the minimal command set, reuses a single <c>getprop</c> read where possible, and is
/// resilient: a failed sub-command yields nulls rather than throwing for normal device errors.
/// </summary>
/// <remarks>
/// Declared in <c>AndroidTreeView.Adb.Services</c> but references <c>AndroidTreeView.Models.System</c>;
/// BCL types are therefore used unqualified to avoid the namespace collision.
/// </remarks>
public sealed class AdbDeviceService : IDeviceService
{
    private readonly IAdbCommandExecutor _executor;
    private readonly AdbOptions _options;
    private readonly ILogger<AdbDeviceService> _logger;

    public AdbDeviceService(
        IAdbCommandExecutor executor,
        AdbOptions options,
        ILogger<AdbDeviceService> logger)
    {
        _executor = executor;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken ct = default)
    {
        var request = new AdbCommandRequest
        {
            Arguments = AdbArgs.Devices,
            Timeout = _options.DeviceListTimeout
        };

        // May propagate AdbNotFoundException so the UI can show the setup screen.
        var result = await _executor.ExecuteAsync(request, ct).ConfigureAwait(false);
        return AdbDevicesParser.Parse(result.StandardOutput);
    }

    public async Task<DeviceProperties> GetPropertiesAsync(string serial, CancellationToken ct = default)
    {
        var props = await FetchPropsAsync(serial, ct).ConfigureAwait(false);
        return new DeviceProperties { Values = props };
    }

    public async Task<DeviceOverview> GetOverviewAsync(string serial, CancellationToken ct = default)
    {
        var props = await FetchPropsAsync(serial, ct).ConfigureAwait(false);
        return OverviewBuilder.Build(props);
    }

    public async Task<HardwareInfo> GetHardwareAsync(string serial, CancellationToken ct = default)
    {
        var props = await FetchPropsAsync(serial, ct).ConfigureAwait(false);
        var cpu = CpuInfoParser.Parse(await TryShellAsync(serial, AdbArgs.CatCpuInfo, ct).ConfigureAwait(false));
        var memory = MemInfoParser.Parse(await TryShellAsync(serial, AdbArgs.CatMemInfo, ct).ConfigureAwait(false));

        var resolution = ScreenParser.ParseSize(await TryShellAsync(serial, AdbArgs.WmSize, ct).ConfigureAwait(false));
        var density = ScreenParser.ParseDensity(await TryShellAsync(serial, AdbArgs.WmDensity, ct).ConfigureAwait(false));
        var screen = new ScreenInfo { Resolution = resolution, DensityDpi = density };

        return HardwareBuilder.Build(props, cpu, memory, screen);
    }

    public async Task<BatteryInfo> GetBatteryAsync(string serial, CancellationToken ct = default)
    {
        var dump = await TryShellAsync(serial, AdbArgs.DumpsysBattery, ct).ConfigureAwait(false) ?? string.Empty;
        var battery = BatteryParser.Parse(dump);

        if (battery.CycleCount is not null)
        {
            return battery;
        }

        var cycleCount = await ReadCycleCountAsync(serial, ct).ConfigureAwait(false);
        return cycleCount is null ? battery : BatteryParser.Parse(dump, cycleCount);
    }

    public async Task<SystemInfo> GetSystemInfoAsync(string serial, CancellationToken ct = default)
    {
        var props = await FetchPropsAsync(serial, ct).ConfigureAwait(false);
        var kernel = SystemInfoBuilder.ParseKernel(
            await TryShellAsync(serial, AdbArgs.CatProcVersion, ct).ConfigureAwait(false));
        var selinux = await TryShellAsync(serial, AdbArgs.Getenforce, ct).ConfigureAwait(false);
        var uptime = SystemInfoBuilder.ParseUptime(
            await TryShellAsync(serial, AdbArgs.Uptime, ct).ConfigureAwait(false));

        return SystemInfoBuilder.Build(props, kernel, selinux, uptime);
    }

    public async Task<StorageInfo> GetStorageAsync(string serial, CancellationToken ct = default)
    {
        var output = await TryShellAsync(serial, AdbArgs.Df, ct).ConfigureAwait(false);
        return StorageParser.Parse(output);
    }

    public async Task<NetworkInfo> GetNetworkAsync(string serial, CancellationToken ct = default)
    {
        var ipOutput = await TryShellAsync(serial, AdbArgs.IpAddr, ct).ConfigureAwait(false)
                       ?? await TryShellAsync(serial, AdbArgs.Ifconfig, ct).ConfigureAwait(false);

        var interfaces = NetworkParser.Parse(ipOutput);
        var wifi = NetworkParser.FindWifi(interfaces);

        var wifiMac = wifi?.MacAddress;
        if (wifiMac is null)
        {
            var macOutput = await TryShellAsync(serial, AdbArgs.Cat(AdbArgs.WlanMacPath), ct).ConfigureAwait(false);
            wifiMac = NetworkParser.ParseMacAddress(macOutput);
        }

        return new NetworkInfo
        {
            WifiIpAddress = wifi?.IpAddress,
            WifiMacAddress = wifiMac,
            Interfaces = interfaces
        };
    }

    public async Task<RootStatus> GetRootStatusAsync(string serial, CancellationToken ct = default)
    {
        var id = await TryShellAsync(serial, AdbArgs.Id, ct).ConfigureAwait(false);

        var whichSu = await TryShellAsync(serial, AdbArgs.WhichSu, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(whichSu))
        {
            whichSu = await TryShellAsync(serial, AdbArgs.CommandVSu, ct).ConfigureAwait(false);
        }

        var suId = await TryShellAsync(serial, AdbArgs.SuId, ct).ConfigureAwait(false);
        var getenforce = await TryShellAsync(serial, AdbArgs.Getenforce, ct).ConfigureAwait(false);
        var magisk = await TryShellAsync(serial, AdbArgs.MagiskClientVersion, ct).ConfigureAwait(false);

        return RootStatusParser.Parse(id, whichSu, suId, getenforce, magisk);
    }

    private async Task<IReadOnlyDictionary<string, string>> FetchPropsAsync(string serial, CancellationToken ct)
    {
        var output = await TryShellAsync(serial, AdbArgs.GetProp, ct).ConfigureAwait(false);
        return GetPropParser.Parse(output);
    }

    private async Task<int?> ReadCycleCountAsync(string serial, CancellationToken ct)
    {
        foreach (var path in AdbArgs.CycleCountPaths)
        {
            var output = await TryShellAsync(serial, AdbArgs.Cat(path), ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(output))
            {
                continue;
            }

            if (int.TryParse(output.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
                value >= 0)
            {
                return value;
            }
        }

        return null;
    }

    private async Task<string?> TryShellAsync(string serial, string[] args, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(serial);

        try
        {
            var request = new AdbCommandRequest
            {
                Serial = serial,
                Arguments = args,
                RunInShell = true
            };

            var result = await _executor.ExecuteAsync(request, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                _logger.LogDebug(
                    "adb shell '{Args}' on {Serial} exited {Code}.",
                    string.Join(' ', args), serial, result.ExitCode);
                return null;
            }

            return result.StandardOutput;
        }
        catch (AdbNotFoundException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AdbException ex)
        {
            _logger.LogDebug(ex, "adb shell '{Args}' on {Serial} failed.", string.Join(' ', args), serial);
            return null;
        }
    }
}
