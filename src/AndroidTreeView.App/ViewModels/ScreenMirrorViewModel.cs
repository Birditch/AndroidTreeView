using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AndroidTreeView.Core.Interfaces;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.App.ViewModels;

/// <summary>
/// Backs the screen-mirror (投屏) window: polls device screenshots (~1.4 fps) into a <see cref="Bitmap"/>,
/// and installs APKs dropped onto the window. This is a lightweight, universal, no-root preview — not a
/// real-time scrcpy stream.
/// </summary>
public sealed partial class ScreenMirrorViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(700);

    private readonly IScreenCaptureService _capture;
    private readonly ILocalizationService _localization;
    private readonly ILogger<ScreenMirrorViewModel> _logger;

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private Bitmap? _frame;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _statusMessage;

    public ScreenMirrorViewModel(
        IScreenCaptureService capture,
        ILocalizationService localization,
        ILogger<ScreenMirrorViewModel> logger)
    {
        _capture = capture;
        _localization = localization;
        _logger = logger;
    }

    public string Serial { get; private set; } = string.Empty;

    public void Initialize(string serial, string title)
    {
        Serial = serial;
        Title = title;
    }

    [RelayCommand]
    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        IsRunning = true;
        StatusMessage = _localization.Get("common.loading");
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    [RelayCommand]
    public void Stop()
    {
        IsRunning = false;
        _cts?.Cancel();
    }

    /// <summary>Installs an APK dropped onto the window.</summary>
    public async Task InstallApkAsync(string apkPath)
    {
        StatusMessage = _localization.Get("screen.installing");
        try
        {
            var ok = await _capture.InstallApkAsync(Serial, apkPath).ConfigureAwait(true);
            StatusMessage = _localization.Get(ok ? "screen.installed" : "screen.installfailed");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "APK install failed for {Serial}.", Serial);
            StatusMessage = _localization.Get("screen.installfailed");
        }
    }

    /// <summary>Installs APKs and transfers ordinary files dropped onto the window.</summary>
    public async Task HandleDroppedFilesAsync(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            {
                await InstallApkAsync(path).ConfigureAwait(true);
            }
            else
            {
                await PushFileAsync(path).ConfigureAwait(true);
            }
        }
    }

    private async Task PushFileAsync(string path)
    {
        StatusMessage = _localization.Get("screen.transferring");
        try
        {
            var ok = await _capture.PushFileAsync(Serial, path, "/sdcard/Download/").ConfigureAwait(true);
            StatusMessage = _localization.Get(ok ? "screen.transferred" : "screen.transferfailed");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "File transfer failed for {Serial}.", Serial);
            StatusMessage = _localization.Get("screen.transferfailed");
        }
    }

    // ---- Remote control (called from the mirror window) ------------------------------------------

    /// <summary>Taps at device pixel coordinates.</summary>
    public Task TapAsync(int x, int y) => SafeInputAsync(ct => _capture.TapAsync(Serial, x, y, ct));

    /// <summary>Swipes/drags between device pixel coordinates over <paramref name="durationMs"/>.</summary>
    public Task SwipeAsync(int x1, int y1, int x2, int y2, int durationMs) =>
        SafeInputAsync(ct => _capture.SwipeAsync(Serial, x1, y1, x2, y2, durationMs, ct));

    [RelayCommand]
    private Task Back() => SafeInputAsync(ct => _capture.KeyEventAsync(Serial, AndroidKeyCode.Back, ct));

    [RelayCommand]
    private Task Home() => SafeInputAsync(ct => _capture.KeyEventAsync(Serial, AndroidKeyCode.Home, ct));

    [RelayCommand]
    private Task Recents() => SafeInputAsync(ct => _capture.KeyEventAsync(Serial, AndroidKeyCode.Recents, ct));

    private async Task SafeInputAsync(Func<CancellationToken, Task> action)
    {
        try
        {
            await action(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Screen input failed on {Serial}.", Serial);
        }
    }

    private static class AndroidKeyCode
    {
        public const int Home = 3;
        public const int Back = 4;
        public const int Recents = 187;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var bytes = await _capture.CaptureFrameAsync(Serial, ct).ConfigureAwait(false);
                if (bytes is { Length: > 0 })
                {
                    var bitmap = DecodeBitmap(bytes);
                    if (bitmap is not null)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var previous = Frame;
                            Frame = bitmap;
                            previous?.Dispose();
                            StatusMessage = null;
                        });
                    }
                }
                else
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        StatusMessage = _localization.Get("screen.failed"));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Screen capture frame failed for {Serial}.", Serial);
            }

            try
            {
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static Bitmap? DecodeBitmap(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        Frame?.Dispose();
    }
}
