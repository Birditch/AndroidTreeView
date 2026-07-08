using System.Diagnostics;
using AndroidTreeView.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Core.Services;

/// <summary>Shared scrcpy process launcher used by both the full App and the Mini companion.</summary>
public sealed class ScrcpyLauncher : IScrcpyLauncher
{
    private const string ScrcpyFolder = "scrcpy";
    private const string ScrcpyExe = "scrcpy.exe";
    private const int KeycodeWakeup = 224; // KEYCODE_WAKEUP.

    private static readonly TimeSpan FirstFramePrimeDelay = TimeSpan.FromMilliseconds(300);

    private readonly IAdbEnvironment _adb;
    private readonly IScreenCaptureService _capture;
    private readonly ILogger<ScrcpyLauncher> _logger;

    public ScrcpyLauncher(
        IAdbEnvironment adb,
        IScreenCaptureService capture,
        ILogger<ScrcpyLauncher> logger)
    {
        _adb = adb;
        _capture = capture;
        _logger = logger;
    }

    public ScrcpyLaunchResult Launch(string serial, string title)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, ScrcpyFolder, ScrcpyExe);
        var exe = File.Exists(bundled) ? bundled : ScrcpyExe;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = File.Exists(bundled)
                    ? Path.GetDirectoryName(bundled)!
                    : AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add(serial);
            psi.ArgumentList.Add("--no-audio");
            psi.ArgumentList.Add("--stay-awake");
            psi.ArgumentList.Add("--keep-active");
            psi.ArgumentList.Add("--window-title");
            psi.ArgumentList.Add(title);

            try
            {
                psi.Environment["ADB"] = _adb.ExecutablePath;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Shared adb path is unavailable; scrcpy will resolve adb itself.");
            }

            var process = Process.Start(psi);
            return new ScrcpyLaunchResult
            {
                ExecutablePath = exe,
                Process = process,
                ErrorMessage = process is null ? "scrcpy did not start." : null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to launch scrcpy for {Serial}.", serial);
            return new ScrcpyLaunchResult
            {
                ExecutablePath = exe,
                ErrorMessage = ex.Message,
            };
        }
    }

    public void PrimeFirstFrame(string serial)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(FirstFramePrimeDelay).ConfigureAwait(false);
                await _capture.KeyEventAsync(serial, KeycodeWakeup).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "First-frame wake failed for {Serial}.", serial);
            }
        });
    }
}
