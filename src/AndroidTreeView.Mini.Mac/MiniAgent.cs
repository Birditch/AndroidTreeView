using System.Collections.ObjectModel;
using System.Diagnostics;
using AndroidTreeView.Core;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;
using AndroidTreeView.Models;
using AndroidTreeView.Models.Devices;
using Avalonia.Threading;

namespace AndroidTreeView.Mini.Mac;

public sealed class MiniAgent
{
    private const int MaxLogEntries = 500;
    private const int KeycodeWakeup = 224;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LaunchStagger = TimeSpan.FromMilliseconds(1500);

    private readonly IAdbLocator _locator;
    private readonly IAdbEnvironment _environment;
    private readonly IDeviceMonitor _monitor;
    private readonly IScrcpyLauncher _scrcpy;
    private readonly DeviceFileTransferService _fileTransfer;
    private readonly IUpdateService _updateService;
    private readonly IUpdateInstaller _updateInstaller;
    private readonly HashSet<string> _present = new(StringComparer.Ordinal);
    private readonly HashSet<string> _online = new(StringComparer.Ordinal);
    private readonly HashSet<string> _mirrored = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warnedAuth = new(StringComparer.Ordinal);
    private readonly Queue<(string Serial, string Title)> _launchQueue = new();
    private readonly Dictionary<string, Process> _mirrorProcesses = new(StringComparer.Ordinal);
    private readonly object _processGate = new();
    private bool _draining;
    private bool _stopping;
    private bool _subscribed;
    private bool _lastAdbAvailable = true;

    public MiniAgent(
        IAdbLocator locator,
        IAdbEnvironment environment,
        IDeviceMonitor monitor,
        IScrcpyLauncher scrcpy,
        DeviceFileTransferService fileTransfer,
        IUpdateService updateService,
        IUpdateInstaller updateInstaller)
    {
        _locator = locator;
        _environment = environment;
        _monitor = monitor;
        _scrcpy = scrcpy;
        _fileTransfer = fileTransfer;
        _updateService = updateService;
        _updateInstaller = updateInstaller;
    }

    public ObservableCollection<MiniLogEntry> Log { get; } = new();

    public bool AdbAvailable { get; private set; } = true;

    public int DeviceCount { get; private set; }

    public event EventHandler? StatusChanged;

    public async Task StartAsync(CancellationToken ct = default)
    {
        _stopping = false;
        AppendLog(MiniLogLevel.Info, $"AndroidTreeView Mini v{AppInfo.Version} listening");

        try
        {
            var location = await _locator.LocateAsync(null, ct);
            if (location is null)
            {
                _lastAdbAvailable = false;
                SetAdbAvailable(false);
                AppendLog(MiniLogLevel.Error, "adb not found. Install Android platform-tools or keep the bundled scrcpy folder intact.");
            }
            else
            {
                _environment.Set(location);
                _lastAdbAvailable = true;
                SetAdbAvailable(true);
                AppendLog(MiniLogLevel.Success, $"adb ready: {location.ExecutablePath}");
            }

            if (!_subscribed)
            {
                _monitor.DevicesChanged += OnDevicesChanged;
                _subscribed = true;
            }

            _monitor.UpdateInterval(PollInterval);
            _monitor.Start();
            _ = CheckAndInstallUpdatesAsync();

            AppendLog(MiniLogLevel.Info, "Watching for Android devices...");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppendLog(MiniLogLevel.Error, $"Start failed: {ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        _stopping = true;
        _launchQueue.Clear();

        if (_subscribed)
        {
            _monitor.DevicesChanged -= OnDevicesChanged;
            _subscribed = false;
        }

        try
        {
            await _monitor.StopAsync();
        }
        catch
        {
        }

        ShutdownMirrors();
    }

    public void ClearLog() => RunOnUiThread(Log.Clear);

    private void OnDevicesChanged(object? sender, DeviceListChangedEventArgs args)
    {
        RunOnUiThread(() => HandleDevices(args));
    }

    private void HandleDevices(DeviceListChangedEventArgs args)
    {
        ReportAdbAvailability(args);

        var current = new HashSet<string>(StringComparer.Ordinal);
        foreach (var device in args.Devices)
        {
            current.Add(device.Serial);
        }

        HandleRemovals(current);

        foreach (var device in args.Devices)
        {
            HandleDevice(device);
        }

        _present.Clear();
        _online.Clear();
        foreach (var serial in current)
        {
            _present.Add(serial);
        }

        foreach (var serial in args.Devices.Where(device => device.IsOnline).Select(device => device.Serial))
        {
            _online.Add(serial);
        }

        SetDeviceCount(args.Devices.Count);
    }

    public async Task HandleDroppedFilesAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        var targets = _online.ToArray();
        if (targets.Length == 0)
        {
            AppendLog(MiniLogLevel.Warn, "Drop ignored: no online ADB device.");
            return;
        }

        var result = await _fileTransfer.ProcessAsync(targets, paths, ct: ct);
        if (result.ValidFileCount == 0)
        {
            AppendLog(MiniLogLevel.Warn, "Drop ignored: no valid local files.");
            return;
        }

        var level = result.TotalFailed == 0
            ? MiniLogLevel.Success
            : result.TotalSucceeded == 0 ? MiniLogLevel.Error : MiniLogLevel.Warn;

        AppendLog(
            level,
            $"One-click transfer: APK ok {result.InstallSucceeded}, APK failed {result.InstallFailed}, file ok {result.TransferSucceeded}, file failed {result.TransferFailed}.");
    }

    private void ReportAdbAvailability(DeviceListChangedEventArgs args)
    {
        if (args.AdbAvailable == _lastAdbAvailable)
        {
            return;
        }

        _lastAdbAvailable = args.AdbAvailable;
        SetAdbAvailable(args.AdbAvailable);

        if (args.AdbAvailable)
        {
            AppendLog(MiniLogLevel.Success, "adb reconnected.");
        }
        else
        {
            var reason = string.IsNullOrWhiteSpace(args.Error) ? "adb unavailable" : args.Error!;
            AppendLog(MiniLogLevel.Error, $"adb lost: {reason}");
        }
    }

    private void HandleRemovals(HashSet<string> current)
    {
        var removed = new List<string>();
        foreach (var serial in _present)
        {
            if (!current.Contains(serial))
            {
                removed.Add(serial);
            }
        }

        foreach (var serial in removed)
        {
            AppendLog(MiniLogLevel.Info, $"Disconnected: {serial}");
            _mirrored.Remove(serial);
            _warnedAuth.Remove(serial);
        }
    }

    private void HandleDevice(AdbDevice device)
    {
        var serial = device.Serial;

        if (device.State == DeviceConnectionState.Unauthorized)
        {
            if (_warnedAuth.Add(serial))
            {
                AppendLog(MiniLogLevel.Warn, $"Waiting for authorization: {serial}");
            }

            return;
        }

        if (device.IsOnline && _mirrored.Add(serial))
        {
            var name = device.DisplayName;
            AppendLog(MiniLogLevel.Success, $"Authorized, launching mirror: {name} ({serial})");
            EnqueueLaunch(serial, name);
        }
    }

    private void EnqueueLaunch(string serial, string title)
    {
        if (_stopping)
        {
            return;
        }

        _launchQueue.Enqueue((serial, title));
        if (!_draining)
        {
            _draining = true;
            _ = DrainLaunchQueueAsync();
        }
    }

    private async Task DrainLaunchQueueAsync()
    {
        while (!_stopping && _launchQueue.Count > 0)
        {
            var (serial, title) = _launchQueue.Dequeue();

            try
            {
                _scrcpy.PrimeFirstFrame(serial);
            }
            catch
            {
            }

            LaunchScrcpy(serial, title);
            await Task.Delay(LaunchStagger);
        }

        _draining = false;
    }

    private void LaunchScrcpy(string serial, string title)
    {
        if (_stopping)
        {
            return;
        }

        var result = _scrcpy.Launch(serial, title);
        if (result.Process is { } process)
        {
            TrackMirror(serial, process);
        }

        if (!result.Started)
        {
            AppendLog(MiniLogLevel.Error, $"scrcpy launch failed: {serial} ({result.ExecutablePath}) {result.ErrorMessage}");
        }
    }

    private void TrackMirror(string serial, Process process)
    {
        lock (_processGate)
        {
            _mirrorProcesses[serial] = process;
        }

        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => UntrackMirror(serial, process);

            if (process.HasExited)
            {
                UntrackMirror(serial, process);
            }
        }
        catch
        {
        }
    }

    private void UntrackMirror(string serial, Process process)
    {
        lock (_processGate)
        {
            if (_mirrorProcesses.TryGetValue(serial, out var current) && ReferenceEquals(current, process))
            {
                _mirrorProcesses.Remove(serial);
            }
        }
    }

    private void ShutdownMirrors()
    {
        List<Process> processes;
        lock (_processGate)
        {
            processes = new List<Process>(_mirrorProcesses.Values);
            _mirrorProcesses.Clear();
        }

        _mirrored.Clear();

        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private async Task CheckAndInstallUpdatesAsync()
    {
        try
        {
            var update = await _updateService.CheckForUpdatesAsync(userInitiated: false);
            if (update.Status != UpdateCheckStatus.UpdateAvailable || !update.UpdateAvailable)
            {
                return;
            }

            AppendLog(MiniLogLevel.Info, $"Mini update {update.LatestVersion} found; downloading...");
            var install = await _updateInstaller.InstallAsync(update);
            AppendLog(install.Started ? MiniLogLevel.Success : MiniLogLevel.Error, DescribeInstallResult(install));
        }
        catch (Exception ex)
        {
            AppendLog(MiniLogLevel.Error, $"Auto-update failed: {ex.Message}");
        }
    }

    private static string DescribeInstallResult(UpdateInstallResult result) => result.Status switch
    {
        UpdateInstallStatus.Started => "Update started; Mini will restart automatically.",
        UpdateInstallStatus.NoUpdateAvailable => "No installable update is available.",
        UpdateInstallStatus.MissingDownloadUrl => "The update channel did not provide a download URL.",
        UpdateInstallStatus.InvalidChecksum => "The update package checksum did not match.",
        UpdateInstallStatus.UnsupportedPackage => "The update package is not supported on this platform.",
        UpdateInstallStatus.InstallerLaunchFailed => "Could not start the update flow.",
        UpdateInstallStatus.DownloadFailed => "The update package download failed.",
        _ => result.ErrorMessage ?? "Auto-update failed.",
    };

    private void SetAdbAvailable(bool value)
    {
        AdbAvailable = value;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetDeviceCount(int value)
    {
        DeviceCount = value;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AppendLog(MiniLogLevel level, string message)
    {
        RunOnUiThread(() =>
        {
            Log.Add(new MiniLogEntry(DateTime.Now.ToString("HH:mm:ss"), message, level));
            while (Log.Count > MaxLogEntries)
            {
                Log.RemoveAt(0);
            }
        });
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }
}
