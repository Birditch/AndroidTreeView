using AndroidTreeView.Core.Exceptions;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models.Devices;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Core.Services;

/// <summary>
/// Periodically polls the device list on a background task and raises <see cref="DevicesChanged"/>.
/// Resilient: ADB-not-found and transient errors are reported through the event, never thrown out of
/// the loop. Marshalling results onto a UI thread is the responsibility of subscribers.
/// </summary>
public sealed class DeviceMonitor : IDeviceMonitor, IAsyncDisposable
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(500);

    private readonly IDeviceService _deviceService;
    private readonly ILogger<DeviceMonitor> _logger;
    private readonly object _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile int _intervalMs = 3000;

    public DeviceMonitor(IDeviceService deviceService, ILogger<DeviceMonitor> logger)
    {
        _deviceService = deviceService;
        _logger = logger;
    }

    public event EventHandler<DeviceListChangedEventArgs>? DevicesChanged;

    public bool IsRunning { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            IsRunning = true;
            var token = _cts.Token;
            _loop = Task.Run(() => RunAsync(token));
        }

        _logger.LogInformation("Device monitor started (interval {Interval} ms).", _intervalMs);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
        }

        try
        {
            cts?.Cancel();
            if (loop is not null)
            {
                await loop.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on cancellation
        }
        finally
        {
            cts?.Dispose();
        }

        _logger.LogInformation("Device monitor stopped.");
    }

    public void UpdateInterval(TimeSpan interval)
    {
        var clamped = interval < MinInterval ? MinInterval : interval;
        _intervalMs = (int)clamped.TotalMilliseconds;
    }

    public async Task<DeviceListChangedEventArgs> RefreshNowAsync(CancellationToken ct = default)
    {
        var args = await QueryAsync(ct).ConfigureAwait(false);
        DevicesChanged?.Invoke(this, args);
        return args;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var args = await QueryAsync(ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested)
            {
                break;
            }

            DevicesChanged?.Invoke(this, args);

            try
            {
                await Task.Delay(_intervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<DeviceListChangedEventArgs> QueryAsync(CancellationToken ct)
    {
        try
        {
            var devices = await _deviceService.ListDevicesAsync(ct).ConfigureAwait(false);
            return new DeviceListChangedEventArgs { Devices = devices, AdbAvailable = true };
        }
        catch (AdbNotFoundException ex)
        {
            return new DeviceListChangedEventArgs
            {
                Devices = Array.Empty<AdbDevice>(),
                AdbAvailable = false,
                Error = ex.Message
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Device list refresh failed.");
            return new DeviceListChangedEventArgs
            {
                Devices = Array.Empty<AdbDevice>(),
                AdbAvailable = true,
                Error = ex.Message
            };
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
