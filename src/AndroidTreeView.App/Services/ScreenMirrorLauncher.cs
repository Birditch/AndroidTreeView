using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AndroidTreeView.App.ViewModels;
using AndroidTreeView.App.Views;
using AndroidTreeView.Core.Interfaces;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.App.Services;

/// <summary>Reports a device starting or stopping screen mirroring.</summary>
public sealed class MirrorStateChangedEventArgs : EventArgs
{
    public MirrorStateChangedEventArgs(string serial, bool isMirroring)
    {
        Serial = serial;
        IsMirroring = isMirroring;
    }

    public string Serial { get; }

    public bool IsMirroring { get; }
}

/// <summary>Opens screen mirroring for a device. Must be called on the UI thread.</summary>
public interface IScreenMirrorLauncher
{
    /// <summary>Opens a mirror for the device. No-op if one is already open (one session per device).</summary>
    void Open(string serial, string title);

    /// <summary>Whether a mirror session is currently open for this device.</summary>
    bool IsMirroring(string serial);

    /// <summary>Raised on the UI thread when a device starts or stops mirroring.</summary>
    event EventHandler<MirrorStateChangedEventArgs>? MirrorStateChanged;

    /// <summary>Closes every mirror this launcher started (called on app shutdown for a graceful exit).</summary>
    void ShutdownAll();
}

/// <summary>
/// Launches scrcpy for real-time mirroring and falls back to the built-in screenshot window if scrcpy
/// cannot be launched. One live mirror session is allowed per device.
/// </summary>
public sealed class ScreenMirrorLauncher : IScreenMirrorLauncher
{
    private readonly Func<ScreenMirrorViewModel> _fallbackFactory;
    private readonly IScrcpyLauncher _scrcpy;
    private readonly ILogger<ScreenMirrorLauncher> _logger;

    private readonly HashSet<string> _active = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Process> _processes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScreenMirrorWindow> _fallbackWindows = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public ScreenMirrorLauncher(
        Func<ScreenMirrorViewModel> fallbackFactory,
        IScrcpyLauncher scrcpy,
        ILogger<ScreenMirrorLauncher> logger)
    {
        _fallbackFactory = fallbackFactory;
        _scrcpy = scrcpy;
        _logger = logger;
    }

    public event EventHandler<MirrorStateChangedEventArgs>? MirrorStateChanged;

    public bool IsMirroring(string serial)
    {
        lock (_gate)
        {
            return _active.Contains(serial);
        }
    }

    public void Open(string serial, string title)
    {
        lock (_gate)
        {
            if (!_active.Add(serial))
            {
                _logger.LogDebug("Ignoring duplicate mirror request for {Serial} (already mirroring).", serial);
                return;
            }
        }

        RaiseState(serial, isMirroring: true);
        OpenCore(serial, title);
    }

    private void OpenCore(string serial, string title)
    {
        var result = _scrcpy.Launch(serial, title);
        if (result.Process is { } process)
        {
            lock (_gate)
            {
                _processes[serial] = process;
            }

            ObserveExit(process, serial);
            _scrcpy.PrimeFirstFrame(serial);
            return;
        }

        _logger.LogWarning(
            "Failed to launch scrcpy for {Serial}; falling back to screenshot mirror. {Message}",
            serial,
            result.ErrorMessage);
        _scrcpy.PrimeFirstFrame(serial);
        OpenFallbackWindow(serial, title);
    }

    private void ObserveExit(Process process, string serial)
    {
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Deactivate(serial);

            if (process.HasExited)
            {
                Deactivate(serial);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not observe scrcpy exit for {Serial}; clearing mirror state.", serial);
            Deactivate(serial);
        }
    }

    private void OpenFallbackWindow(string serial, string title)
    {
        try
        {
            var viewModel = _fallbackFactory();
            viewModel.Initialize(serial, title);

            var window = new ScreenMirrorWindow { DataContext = viewModel };
            lock (_gate)
            {
                _fallbackWindows[serial] = window;
            }

            window.Closed += (_, _) =>
            {
                lock (_gate)
                {
                    _fallbackWindows.Remove(serial);
                }

                Deactivate(serial);
            };

            window.Show();
            viewModel.Start();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open the fallback mirror window for {Serial}.", serial);
            lock (_gate)
            {
                _fallbackWindows.Remove(serial);
            }

            Deactivate(serial);
        }
    }

    private void Deactivate(string serial)
    {
        lock (_gate)
        {
            _processes.Remove(serial);
            if (!_active.Remove(serial))
            {
                return;
            }
        }

        RaiseState(serial, isMirroring: false);
    }

    public void ShutdownAll()
    {
        List<Process> processes;
        List<ScreenMirrorWindow> windows;
        lock (_gate)
        {
            processes = _processes.Values.ToList();
            windows = _fallbackWindows.Values.ToList();
            _processes.Clear();
            _fallbackWindows.Clear();
            _active.Clear();
        }

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
                _logger.LogDebug(ex, "Failed to close a scrcpy mirror on shutdown.");
            }
        }

        void CloseWindows()
        {
            foreach (var window in windows)
            {
                try
                {
                    window.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to close a fallback mirror window on shutdown.");
                }
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            CloseWindows();
        }
        else
        {
            Dispatcher.UIThread.Post(CloseWindows);
        }
    }

    private void RaiseState(string serial, bool isMirroring)
    {
        void Raise() => MirrorStateChanged?.Invoke(this, new MirrorStateChangedEventArgs(serial, isMirroring));

        if (Dispatcher.UIThread.CheckAccess())
        {
            Raise();
        }
        else
        {
            Dispatcher.UIThread.Post(Raise);
        }
    }
}
