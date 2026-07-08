using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Mini.Models;
using AndroidTreeView.Mini.Services;
using AndroidTreeView.Models;
using AndroidTreeView.Models.Devices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace AndroidTreeView.Mini.ViewModels;

/// <summary>
/// Drives the always-listening companion: locates adb, watches the device list once per second and,
/// the moment a device connects and authorizes, auto-launches scrcpy. Everything the user sees is a
/// colored terminal line appended to <see cref="Log"/>. The device-change handler never throws.
/// </summary>
public sealed partial class MiniViewModel : ObservableObject
{
    // Bundled tool sub-paths (relative to the app output). Kept as consts, not scattered strings.
    // Everything now lives in one "scrcpy" folder; scrcpy ships its own adb next to scrcpy.exe.

    private const int MaxLogEntries = 500;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LaunchStagger = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan UsbScanInterval = TimeSpan.FromSeconds(2);

    private readonly IAdbLocator _locator;
    private readonly IAdbEnvironment _environment;
    private readonly IDeviceMonitor _monitor;
    private readonly IScrcpyLauncher _scrcpy;
    private readonly IUpdateService _updateService;
    private readonly IUpdateInstaller _updateInstaller;
    private readonly ILogger<MiniViewModel> _logger;

    // Diff state (only ever touched on the UI thread inside the handler).
    private readonly HashSet<string> _present = new(StringComparer.Ordinal);
    private readonly HashSet<string> _mirrored = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warnedAuth = new(StringComparer.Ordinal);
    // Android phones seen on USB with debugging off that we've already guided (de-dupe, keyed by serial/VID:PID).
    private readonly HashSet<string> _usbGuided = new(StringComparer.Ordinal);
    private readonly Queue<(string Serial, string Title)> _launchQueue = new();
    private readonly Dictionary<string, Process> _mirrorProcesses = new(StringComparer.Ordinal);
    private readonly object _processGate = new();
    private bool _draining;
    private bool _stopping;
    private SynchronizationContext? _uiContext;
    private WinFormsTimer? _usbTimer;

    private bool _subscribed;
    private bool _lastAdbAvailable = true;

    public MiniViewModel(
        IAdbLocator locator,
        IAdbEnvironment environment,
        IDeviceMonitor monitor,
        IScrcpyLauncher scrcpy,
        IUpdateService updateService,
        IUpdateInstaller updateInstaller,
        ILogger<MiniViewModel> logger)
    {
        _locator = locator;
        _environment = environment;
        _monitor = monitor;
        _scrcpy = scrcpy;
        _updateService = updateService;
        _updateInstaller = updateInstaller;
        _logger = logger;
    }

    /// <summary>Colored terminal log bound to the window's item list.</summary>
    public ObservableCollection<MiniLogEntry> Log { get; } = new();

    /// <summary>Header title shown next to the status dot.</summary>
    public string Header => "监听中 (Listening)";

    [ObservableProperty]
    private bool _isListening;

    [ObservableProperty]
    private int _deviceCount;

    [ObservableProperty]
    private bool _adbAvailable;

    /// <summary>
    /// Locates adb, wires it into the shared environment, then starts the 1s device-monitor loop.
    /// Safe to await from a fire-and-forget caller; failures are logged as terminal lines.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        CaptureUiContext();
        _stopping = false;
        AppendLog(MiniLogLevel.Info, Header);
        StartUsbScanning();

        try
        {
            var location = await _locator.LocateAsync(null, ct).ConfigureAwait(false);
            if (location is null)
            {
                _lastAdbAvailable = false;
                AdbAvailable = false;
                AppendLog(MiniLogLevel.Error, "未找到 adb (adb not found)。请连接设备，或安装 platform-tools 后重启。");
            }
            else
            {
                _environment.Set(location);
                _lastAdbAvailable = true;
                AdbAvailable = true;
                AppendLog(MiniLogLevel.Success, $"adb 已就绪 (adb ready): {location.ExecutablePath}");
            }

            if (!_subscribed)
            {
                _monitor.DevicesChanged += OnDevicesChanged;
                _subscribed = true;
            }

            _monitor.UpdateInterval(PollInterval);
            _monitor.Start();
            _ = CheckAndInstallUpdatesAsync();

            IsListening = true;
            AppendLog(MiniLogLevel.Info, "开始监听设备 (watching for devices)...");
        }
        catch (OperationCanceledException)
        {
            // Shutting down; nothing to report.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mini listener failed to start.");
            AppendLog(MiniLogLevel.Error, $"启动失败 (start failed): {ex.Message}");
        }
    }

    /// <summary>Stops the monitor and detaches the handler. Called on shutdown.</summary>
    public async Task StopAsync()
    {
        _stopping = true;
        _launchQueue.Clear();
        StopUsbScanning();

        if (_subscribed)
        {
            _monitor.DevicesChanged -= OnDevicesChanged;
            _subscribed = false;
        }

        try
        {
            await _monitor.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mini listener failed to stop cleanly.");
        }

        IsListening = false;
        ShutdownMirrors();
    }

    // ---- USB detection (no adb / no admin) --------------------------------------------------------
    // A phone with USB debugging off never appears in `adb devices`, so we poll the Windows USB bus to
    // detect it, identify the brand / approximate model, and guide the user to turn on USB debugging.

    private void StartUsbScanning()
    {
        void Begin()
        {
            if (_usbTimer is not null)
            {
                return;
            }

            _usbTimer = new WinFormsTimer { Interval = (int)UsbScanInterval.TotalMilliseconds };
            _usbTimer.Tick += (_, _) => ScanUsb();
            _usbTimer.Start();
            ScanUsb();
        }

        RunOnUiThread(Begin);
    }

    private void StopUsbScanning()
    {
        void Stop()
        {
            _usbTimer?.Stop();
            _usbTimer = null;
        }

        RunOnUiThread(Stop);
    }

    // Runs on the UI thread (DispatcherTimer.Tick). Guides for phones present on USB with no ADB interface
    // (= debugging off); phones with debugging on are left to the adb flow (authorize / mirror) above.
    private void ScanUsb()
    {
        try
        {
            var devices = AndroidUsb.Scan();
            var present = new HashSet<string>(StringComparer.Ordinal);

            foreach (var device in devices)
            {
                present.Add(device.Key);

                if (device.HasAdbInterface)
                {
                    continue; // USB debugging is on; adb handles authorization + mirroring.
                }

                if (!_usbGuided.Add(device.Key))
                {
                    continue; // already guided for this device
                }

                var who = device.Manufacturer ?? "未知厂商 (unknown brand)";
                var model = device.Name is { Length: > 0 } n ? $"，型号={n}" : string.Empty;
                var sn = device.Serial is { Length: > 0 } s ? $"，SN={s}" : string.Empty;

                AppendLog(MiniLogLevel.Warn, $"检测到 {who} 设备{model}{sn}，但未开启 USB 调试 - 当前没有 ADB 权限。");
                AppendLog(MiniLogLevel.Info, "开启方法：设置 -> 关于手机 -> 连点“版本号”7 次 -> 返回 -> 开发者选项 -> 打开“USB 调试”，再允许本机授权，投屏会自动弹出。");
            }

            // Forget devices that were unplugged so re-inserting guides again.
            _usbGuided.RemoveWhere(key => !present.Contains(key));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "USB scan failed.");
        }
    }

    [RelayCommand]
    private void Clear() => Log.Clear();

    private void OnDevicesChanged(object? sender, DeviceListChangedEventArgs args)
    {
        // Marshal to the UI thread; the monitor raises this from a background loop.
        RunOnUiThread(() => HandleDevices(args));
    }

    private void HandleDevices(DeviceListChangedEventArgs args)
    {
        try
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
            foreach (var serial in current)
            {
                _present.Add(serial);
            }

            DeviceCount = args.Devices.Count;
        }
        catch (Exception ex)
        {
            // Never let the handler throw out of the event.
            _logger.LogError(ex, "Error while handling a device change.");
        }
    }

    private void ReportAdbAvailability(DeviceListChangedEventArgs args)
    {
        if (args.AdbAvailable == _lastAdbAvailable)
        {
            return;
        }

        _lastAdbAvailable = args.AdbAvailable;
        AdbAvailable = args.AdbAvailable;

        if (args.AdbAvailable)
        {
            AppendLog(MiniLogLevel.Success, "adb 已恢复 (adb reconnected)。");
        }
        else
        {
            var reason = string.IsNullOrWhiteSpace(args.Error) ? "adb 不可用 (adb unavailable)" : args.Error!;
            AppendLog(MiniLogLevel.Error, $"adb 连接中断 (adb lost): {reason}");
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
            AppendLog(MiniLogLevel.Info, $"已断开 (disconnected): {serial}");
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
                AppendLog(MiniLogLevel.Warn, $"等待授权 (waiting for authorization): {serial}");
            }

            return;
        }

        if (device.IsOnline && _mirrored.Add(serial))
        {
            var name = device.DisplayName;
            AppendLog(MiniLogLevel.Success, $"已连接并授权，启动投屏 (launching mirror): {name} ({serial})");
            EnqueueLaunch(serial, name);
        }
    }

    // Launch mirrors one at a time (staggered) so multiple devices connecting together don't race the
    // adb / scrcpy server (which caused "Connection refused" and some mirrors never opening).
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
                // Wake the screen first so scrcpy renders content immediately (avoids a black frame).
                _scrcpy.PrimeFirstFrame(serial);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Wake before mirror failed for {Serial}.", serial);
            }

            LaunchScrcpy(serial, title);
            await Task.Delay(LaunchStagger).ConfigureAwait(true);
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
            AppendLog(
                MiniLogLevel.Error,
                $"scrcpy launch failed: {serial} ({result.ExecutablePath}) {result.ErrorMessage}");
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not observe Mini scrcpy exit for {Serial}.", serial);
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
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to close a Mini scrcpy process on shutdown.");
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
            var update = await _updateService.CheckForUpdatesAsync(userInitiated: false).ConfigureAwait(false);
            if (update.Status != UpdateCheckStatus.UpdateAvailable || !update.UpdateAvailable)
            {
                return;
            }

            AppendLog(MiniLogLevel.Info, $"发现 Mini 新版本 {update.LatestVersion}，正在自动下载并应用更新...");
            var install = await _updateInstaller.InstallAsync(update).ConfigureAwait(false);
            AppendLog(
                install.Started ? MiniLogLevel.Success : MiniLogLevel.Error,
                DescribeInstallResult(install));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mini auto-update failed.");
            AppendLog(MiniLogLevel.Error, $"自动更新失败: {ex.Message}");
        }
    }

    private static string DescribeInstallResult(UpdateInstallResult result) => result.Status switch
    {
        UpdateInstallStatus.Started => "更新已启动，Mini 会自动重启。",
        UpdateInstallStatus.NoUpdateAvailable => "当前没有可安装的更新。",
        UpdateInstallStatus.MissingDownloadUrl => "更新通道没有提供下载地址。",
        UpdateInstallStatus.InvalidChecksum => "更新包校验失败。",
        UpdateInstallStatus.UnsupportedPackage => "更新包内没有受支持的 x64 发布包。",
        UpdateInstallStatus.InstallerLaunchFailed => "无法启动更新流程。",
        UpdateInstallStatus.DownloadFailed => "更新包下载失败。",
        _ => result.ErrorMessage ?? "自动更新失败。",
    };

    private void AppendLog(MiniLogLevel level, string message)
    {
        RunOnUiThread(() => AppendCore(level, message));
    }

    private void AppendCore(MiniLogLevel level, string message)
    {
        Log.Add(new MiniLogEntry(DateTime.Now.ToString("HH:mm:ss"), message, level));
        while (Log.Count > MaxLogEntries)
        {
            Log.RemoveAt(0);
        }
    }

    private void CaptureUiContext()
    {
        _uiContext ??= SynchronizationContext.Current;
    }

    private void RunOnUiThread(Action action)
    {
        var context = _uiContext;
        if (context is null || SynchronizationContext.Current == context)
        {
            action();
            return;
        }

        context.Post(_ => action(), null);
    }
}
