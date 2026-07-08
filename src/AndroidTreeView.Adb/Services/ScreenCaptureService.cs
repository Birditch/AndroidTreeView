using System.ComponentModel;
using AndroidTreeView.Adb.Commands;
using AndroidTreeView.Adb.Internal;
using AndroidTreeView.Core.Exceptions;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Adb.Services;

/// <summary>
/// Implements <see cref="IScreenCaptureService"/> using <c>adb exec-out screencap -p</c> for
/// frame capture and <c>adb shell input tap</c> for touch injection.
/// <para>
/// <see cref="CaptureFrameAsync"/> is intentionally resilient: it never throws except
/// <see cref="AdbNotFoundException"/> (adb genuinely unavailable), so the mirror polling loop can
/// keep retrying without special error handling.
/// </para>
/// </summary>
public sealed class ScreenCaptureService : IScreenCaptureService
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan TapTimeout     = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan PushTimeout    = TimeSpan.FromSeconds(120);
    private const string DefaultPushTargetDirectory = "/sdcard/Download/";

    private readonly IAdbEnvironment _environment;
    private readonly IAdbCommandExecutor _executor;
    private readonly ILogger<ScreenCaptureService> _logger;

    public ScreenCaptureService(
        IAdbEnvironment environment,
        IAdbCommandExecutor executor,
        ILogger<ScreenCaptureService> logger)
    {
        _environment = environment;
        _executor    = executor;
        _logger      = logger;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>adb -s &lt;serial&gt; exec-out screencap -p</c> via
    /// <see cref="ProcessRunner.RunBinaryAsync"/> so that stdout is captured as raw bytes —
    /// reading it through a text decoder would corrupt the PNG data.
    /// Only <see cref="AdbNotFoundException"/> propagates; every other failure returns
    /// <see langword="null"/> so the caller's polling loop is never interrupted.
    /// </remarks>
    public async Task<byte[]?> CaptureFrameAsync(string serial, CancellationToken ct = default)
    {
        // AdbNotFoundException propagates if adb is not currently available.
        var executable = _environment.ExecutablePath;

        var argv = BuildScreencapArgv(serial);

        try
        {
            var result = await ProcessRunner
                .RunBinaryAsync(executable, argv, CaptureTimeout, ct)
                .ConfigureAwait(false);

            if (result.ExitCode == 0 && result.StandardOutput.Length > 0)
            {
                return result.StandardOutput;
            }

            _logger.LogDebug(
                "screencap on {Serial} returned exit={ExitCode} bytes={ByteCount}. stderr: {Stderr}",
                serial, result.ExitCode, result.StandardOutput.Length, result.StandardError);

            return null;
        }
        catch (Win32Exception ex)
        {
            // The executable was reported as available but could not actually be launched.
            throw new AdbNotFoundException(
                $"The adb executable at '{executable}' could not be started.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CaptureFrameAsync on {Serial} failed.", serial);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task TapAsync(string serial, int x, int y, CancellationToken ct = default)
    {
        var request = new AdbCommandRequest
        {
            Serial     = serial,
            Arguments  = AdbArgs.InputTap(x, y),
            RunInShell = true,
            Timeout    = TapTimeout
        };

        try
        {
            var result = await _executor.ExecuteAsync(request, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                _logger.LogDebug(
                    "input tap ({X},{Y}) on {Serial} exited {Code}.",
                    x, y, serial, result.ExitCode);
            }
        }
        catch (AdbNotFoundException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TapAsync ({X},{Y}) on {Serial} failed.", x, y, serial);
        }
    }

    /// <inheritdoc/>
    public Task SwipeAsync(string serial, int x1, int y1, int x2, int y2, int durationMs, CancellationToken ct = default)
        => RunInputAsync(serial, AdbArgs.InputSwipe(x1, y1, x2, y2, durationMs), ct);

    /// <inheritdoc/>
    public Task KeyEventAsync(string serial, int keyCode, CancellationToken ct = default)
        => RunInputAsync(serial, AdbArgs.InputKeyEvent(keyCode), ct);

    private async Task RunInputAsync(string serial, string[] arguments, CancellationToken ct)
    {
        var request = new AdbCommandRequest
        {
            Serial     = serial,
            Arguments  = arguments,
            RunInShell = true,
            Timeout    = TapTimeout
        };

        try
        {
            var result = await _executor.ExecuteAsync(request, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                _logger.LogDebug("input command on {Serial} exited {Code}.", serial, result.ExitCode);
            }
        }
        catch (AdbNotFoundException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "input command on {Serial} failed.", serial);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> InstallApkAsync(string serial, string apkPath, CancellationToken ct = default)
    {
        try
        {
            var attempts = new[]
            {
                AdbArgs.InstallReplace(apkPath),
                AdbArgs.InstallReplaceAllowDowngrade(apkPath),
                AdbArgs.InstallReplaceNoStreaming(apkPath),
                AdbArgs.InstallReplaceAllowDowngradeNoStreaming(apkPath)
            };

            foreach (var arguments in attempts)
            {
                var request = new AdbCommandRequest
                {
                    Serial = serial,
                    Arguments = arguments,
                    RunInShell = false,
                    Timeout = InstallTimeout
                };

                var result = await _executor.ExecuteAsync(request, ct).ConfigureAwait(false);
                if (IsInstallSuccess(result))
                {
                    return true;
                }

                _logger.LogDebug(
                    "install '{Args}' on {Serial} exited {Code}. stderr: {Stderr}",
                    string.Join(' ', arguments), serial, result.ExitCode, result.StandardError);
            }

            return false;
        }
        catch (AdbNotFoundException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "InstallApkAsync on {Serial} for '{Apk}' failed.", serial, apkPath);
            return false;
        }
    }

    private static bool IsInstallSuccess(AdbCommandResult result)
    {
        if (!result.IsSuccess)
        {
            return false;
        }

        // adb prints "Success" on stdout on most builds, and stderr on a few older builds.
        var combined = result.StandardOutput + result.StandardError;
        return combined.Contains("Success", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<bool> PushFileAsync(
        string serial,
        string filePath,
        string? remoteDirectory = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        var preferredDirectory = NormalizeRemoteDirectory(remoteDirectory);
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        try
        {
            foreach (var directory in CandidateRemoteDirectories(preferredDirectory))
            {
                await PrepareFileTransferAsync(serial, directory, ct).ConfigureAwait(false);

                var request = new AdbCommandRequest
                {
                    Serial = serial,
                    Arguments = AdbArgs.Push(filePath, directory + fileName),
                    RunInShell = false,
                    Timeout = PushTimeout
                };

                var result = await _executor.ExecuteAsync(request, ct).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    return true;
                }

                _logger.LogDebug(
                    "push to {Directory} on {Serial} exited {Code}. stderr: {Stderr}",
                    directory, serial, result.ExitCode, result.StandardError);
            }

            return false;
        }
        catch (AdbNotFoundException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PushFileAsync on {Serial} for '{File}' failed.", serial, filePath);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> PrepareFileTransferAsync(
        string serial,
        string? remoteDirectory = null,
        CancellationToken ct = default)
    {
        try
        {
            var directory = NormalizeRemoteDirectory(remoteDirectory);
            return await EnsureRemoteDirectoryAsync(serial, directory, ct).ConfigureAwait(false);
        }
        catch (AdbNotFoundException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Preparing file-transfer target on {Serial} failed.", serial);
            return false;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static IReadOnlyList<string> BuildScreencapArgv(string serial)
    {
        var argv = new List<string>(2 + AdbArgs.ExecOutScreencap.Length) { "-s", serial };
        argv.AddRange(AdbArgs.ExecOutScreencap);
        return argv;
    }

    private async Task<bool> EnsureRemoteDirectoryAsync(string serial, string directory, CancellationToken ct)
    {
        var request = new AdbCommandRequest
        {
            Serial = serial,
            Arguments = AdbArgs.MkdirP(directory),
            RunInShell = true,
            Timeout = TapTimeout
        };

        var result = await _executor.ExecuteAsync(request, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            _logger.LogDebug(
                "mkdir for push target on {Serial} exited {Code}. stderr: {Stderr}",
                serial, result.ExitCode, result.StandardError);
        }

        return result.IsSuccess;
    }

    private static string NormalizeRemoteDirectory(string? remoteDirectory)
    {
        var value = string.IsNullOrWhiteSpace(remoteDirectory)
            ? DefaultPushTargetDirectory
            : remoteDirectory.Trim().Replace('\\', '/');

        return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
    }

    private static IReadOnlyList<string> CandidateRemoteDirectories(string preferredDirectory)
    {
        var candidates = new List<string> { preferredDirectory };

        foreach (var fallback in new[] { "/sdcard/Download/", "/sdcard/" })
        {
            if (!candidates.Contains(fallback, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(fallback);
            }
        }

        return candidates;
    }
}
