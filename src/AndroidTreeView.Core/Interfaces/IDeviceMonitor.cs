namespace AndroidTreeView.Core.Interfaces;

/// <summary>
/// Periodically polls the connected device list and raises change notifications.
/// </summary>
public interface IDeviceMonitor
{
    /// <summary>Raised whenever the device list (or adb availability) changes.</summary>
    event EventHandler<DeviceListChangedEventArgs>? DevicesChanged;

    /// <summary>True while the background polling loop is active.</summary>
    bool IsRunning { get; }

    /// <summary>Starts the polling loop. No-op when already running.</summary>
    void Start();

    /// <summary>Stops the polling loop and waits for it to drain.</summary>
    Task StopAsync();

    /// <summary>Performs an immediate refresh and returns the resulting snapshot.</summary>
    Task<DeviceListChangedEventArgs> RefreshNowAsync(CancellationToken ct = default);

    /// <summary>Updates the polling interval used by the loop.</summary>
    void UpdateInterval(TimeSpan interval);
}
