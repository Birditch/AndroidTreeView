using AndroidTreeView.Adb.Commands;
using AndroidTreeView.Core.Exceptions;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Adb.Services;

/// <summary>
/// Implements <see cref="IDeviceActionsService"/> using adb shell / global commands.
/// All commands are universal and non-destructive; none require root.
/// Ordinary non-zero exits are logged at debug and swallowed.
/// Only <see cref="AdbNotFoundException"/> and <see cref="OperationCanceledException"/>
/// propagate from mutating methods. The <c>Is*</c> query methods never throw.
/// </summary>
public sealed class AdbDeviceActionsService : IDeviceActionsService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(8);

    private readonly IAdbCommandExecutor _executor;
    private readonly ILogger<AdbDeviceActionsService> _logger;

    public AdbDeviceActionsService(
        IAdbCommandExecutor executor,
        ILogger<AdbDeviceActionsService> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task RebootAsync(string serial, RebootTarget target, CancellationToken ct = default)
    {
        var args = target switch
        {
            RebootTarget.Recovery   => AdbArgs.RebootRecovery,
            RebootTarget.Bootloader => AdbArgs.RebootBootloader,
            _                       => AdbArgs.Reboot
        };

        // Global adb command (not a shell command): adb -s <serial> reboot [mode]
        var request = new AdbCommandRequest
        {
            Serial     = serial,
            Arguments  = args,
            RunInShell = false,
            Timeout    = CommandTimeout
        };

        return RunMutatingAsync(request, ct);
    }

    /// <inheritdoc/>
    public Task PowerOffAsync(string serial, CancellationToken ct = default)
        => RunShellMutatingAsync(serial, AdbArgs.PowerOff, ct);

    /// <inheritdoc/>
    public async Task RemoveFrpAsync(string serial, CancellationToken ct = default)
    {
        await RunShellMutatingAsync(serial, AdbArgs.SettingsPutSecureUserSetupComplete, ct).ConfigureAwait(false);
        await RunShellMutatingAsync(serial, AdbArgs.SettingsPutGlobalDeviceProvisioned, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> IsFrpRemovedAsync(string serial, CancellationToken ct = default)
    {
        // FRP is effectively cleared once setup is complete AND the device is provisioned — the two flags
        // RemoveFrpAsync sets. On an already-set-up device both are "1", so the action is a no-op (greyed).
        var setup = await TryShellQueryAsync(serial, AdbArgs.SettingsGetSecureUserSetupComplete, ct)
            .ConfigureAwait(false);
        var provisioned = await TryShellQueryAsync(serial, AdbArgs.SettingsGetGlobalDeviceProvisioned, ct)
            .ConfigureAwait(false);
        return setup?.Trim() == "1" && provisioned?.Trim() == "1";
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private Task RunShellMutatingAsync(string serial, string[] args, CancellationToken ct)
    {
        var request = new AdbCommandRequest
        {
            Serial     = serial,
            Arguments  = args,
            RunInShell = true,
            Timeout    = CommandTimeout
        };
        return RunMutatingAsync(request, ct);
    }

    /// <summary>
    /// Executes a command whose result is not inspected. Non-zero exits are logged at debug.
    /// Propagates only <see cref="AdbNotFoundException"/> and <see cref="OperationCanceledException"/>.
    /// </summary>
    private async Task RunMutatingAsync(AdbCommandRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _executor.ExecuteAsync(request, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                _logger.LogDebug(
                    "adb '{Args}' on {Serial} exited {Code}.",
                    string.Join(' ', request.Arguments), request.Serial, result.ExitCode);
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
            _logger.LogDebug(ex,
                "adb '{Args}' on {Serial} failed.",
                string.Join(' ', request.Arguments), request.Serial);
        }
    }

    /// <summary>
    /// Runs a shell query and returns stdout, or <see langword="null"/> on any failure.
    /// Never throws.
    /// </summary>
    private async Task<string?> TryShellQueryAsync(string serial, string[] args, CancellationToken ct)
    {
        try
        {
            var request = new AdbCommandRequest
            {
                Serial     = serial,
                Arguments  = args,
                RunInShell = true,
                Timeout    = CommandTimeout
            };
            var result = await _executor.ExecuteAsync(request, ct).ConfigureAwait(false);
            return result.IsSuccess ? result.StandardOutput : null;
        }
        catch
        {
            return null;
        }
    }
}
