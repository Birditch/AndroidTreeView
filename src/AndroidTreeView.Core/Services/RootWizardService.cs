using AndroidTreeView.Core.Exceptions;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models.Rooting;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Core.Services;

/// <summary>Coordinates extraction, backup, patching and strictly gated fastboot writes.</summary>
public sealed class RootWizardService : IRootWizardService
{
    private static readonly TimeSpan FastbootWaitTimeout = TimeSpan.FromSeconds(45);

    private readonly IBootImageExtractor _extractor;
    private readonly IBootBackupService _backup;
    private readonly IMagiskPatcher _patcher;
    private readonly IRootFastbootService _fastboot;
    private readonly ILogger<RootWizardService> _logger;
    private readonly object _sync = new();

    private RootWizardSnapshot _snapshot = new();
    private CancellationTokenSource? _activeCancellation;
    private Task? _activeOperation;
    private RetryStep _retryStep;
    private bool _flashCommandStarted;

    public RootWizardService(
        IBootImageExtractor extractor,
        IBootBackupService backup,
        IMagiskPatcher patcher,
        IRootFastbootService fastboot,
        ILogger<RootWizardService> logger)
    {
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _backup = backup ?? throw new ArgumentNullException(nameof(backup));
        _patcher = patcher ?? throw new ArgumentNullException(nameof(patcher));
        _fastboot = fastboot ?? throw new ArgumentNullException(nameof(fastboot));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public RootWizardSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public event EventHandler<RootWizardSnapshot>? Changed;

    public void SelectDevice(RootDeviceIdentity device)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(device.Serial);
        EnsureSelectionAllowed();

        Publish(current => new RootWizardSnapshot
        {
            SessionId = Guid.NewGuid(),
            DeviceIdentity = device,
            PackagePath = current.PackagePath,
            State = current.PackagePath is null ? RootWizardState.Idle : RootWizardState.PackageSelected,
        });
        _retryStep = RetryStep.None;
    }

    public void SelectPackage(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        EnsureSelectionAllowed();
        TryCleanupWorkDirectory(Snapshot);

        Publish(current => new RootWizardSnapshot
        {
            SessionId = Guid.NewGuid(),
            DeviceIdentity = current.DeviceIdentity,
            PackagePath = Path.GetFullPath(packagePath),
            State = RootWizardState.PackageSelected,
        });
        _retryStep = RetryStep.ExtractAndPatch;
    }

    public Task ExtractAndPatchAsync(CancellationToken ct = default) =>
        StartOperationAsync(ExtractAndPatchCoreAsync, ct);

    public Task ConfirmBootloaderAsync(CancellationToken ct = default) =>
        StartOperationAsync(ConfirmBootloaderCoreAsync, ct);

    public Task DetectFastbootAsync(CancellationToken ct = default) =>
        StartOperationAsync(DetectFastbootCoreAsync, ct);

    public Task ConfirmFlashAsync(bool riskAcknowledged, CancellationToken ct = default)
    {
        if (!riskAcknowledged)
        {
            Publish(current => current with { ErrorCode = RootErrorCode.RiskNotAcknowledged });
            return Task.CompletedTask;
        }

        return StartOperationAsync(FlashAndRebootCoreAsync, ct);
    }

    public Task RetryAsync(CancellationToken ct = default)
    {
        if (Snapshot.State == RootWizardState.FlashOutcomeUnknown)
        {
            return Task.CompletedTask;
        }

        return StartOperationAsync(async token =>
        {
            switch (_retryStep)
            {
                case RetryStep.ExtractAndPatch:
                    await ExtractAndPatchCoreAsync(token).ConfigureAwait(false);
                    break;
                case RetryStep.BackupAndPatch:
                    await BackupAndPatchCoreAsync(token).ConfigureAwait(false);
                    break;
                case RetryStep.Bootloader:
                    await ConfirmBootloaderCoreAsync(token).ConfigureAwait(false);
                    break;
                case RetryStep.DetectFastboot:
                    await DetectFastbootCoreAsync(token).ConfigureAwait(false);
                    break;
                case RetryStep.Flash:
                    PrepareFlashRetry();
                    break;
                case RetryStep.Reboot:
                    await RebootCoreAsync(token).ConfigureAwait(false);
                    break;
                default:
                    throw Failure(RootErrorCode.InvalidState, "The current root step cannot be retried safely.");
            }
        }, ct);
    }

    public async Task CancelAsync()
    {
        Task? operation;
        lock (_sync)
        {
            if (_activeOperation is null && Snapshot.State == RootWizardState.FlashOutcomeUnknown)
            {
                return;
            }

            operation = _activeOperation;
            _activeCancellation?.Cancel();
        }

        if (operation is not null)
        {
            await operation.ConfigureAwait(false);
            return;
        }

        Publish(current => current with
        {
            State = RootWizardState.Cancelled,
            ErrorCode = RootErrorCode.OperationCancelled,
        });
        TryCleanupWorkDirectory(Snapshot);
    }

    private async Task ExtractAndPatchCoreAsync(CancellationToken ct)
    {
        var start = Snapshot;
        if (start.State != RootWizardState.PackageSelected
            && !(start.State == RootWizardState.Failed && _retryStep == RetryStep.ExtractAndPatch)
            || start.DeviceIdentity is null)
        {
            throw Failure(
                start.DeviceIdentity is null ? RootErrorCode.DeviceNotSelected : RootErrorCode.InvalidState,
                "A device and firmware package must be selected before extraction.");
        }

        _retryStep = RetryStep.ExtractAndPatch;
        Publish(current => current with { State = RootWizardState.Extracting, ErrorCode = RootErrorCode.None });
        var image = await _extractor
            .ExtractAsync(start.PackagePath!, start.DeviceIdentity, ct)
            .ConfigureAwait(false);

        if (image.TargetPartition == BootPartitionTarget.Unknown)
        {
            throw Failure(RootErrorCode.TargetPartitionUnknown, "The target boot partition is unknown.");
        }

        if (image.PackageMetadata?.MatchStatus == FirmwarePackageMatchStatus.Mismatched)
        {
            throw Failure(RootErrorCode.PackageMetadataMismatch, "The firmware package targets another device.");
        }

        Publish(current => current with
        {
            State = RootWizardState.BootExtracted,
            BootImage = image,
            WorkDirectory = image.WorkDirectory,
            PackageMetadata = image.PackageMetadata,
        });

        _retryStep = RetryStep.BackupAndPatch;
        await BackupAndPatchCoreAsync(ct).ConfigureAwait(false);
    }

    private async Task BackupAndPatchCoreAsync(CancellationToken ct)
    {
        var current = Snapshot;
        if (current.BootImage is null || current.DeviceIdentity is null)
        {
            throw Failure(RootErrorCode.InvalidState, "An extracted boot image is required before patching.");
        }

        var backupPath = current.BackupPath;
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            backupPath = await _backup
                .BackupAsync(
                    current.BootImage.Path,
                    current.DeviceIdentity.Serial,
                    current.BootImage.TargetPartition,
                    ct)
                .ConfigureAwait(false);
            Publish(snapshot => snapshot with { BackupPath = backupPath });
        }

        Publish(snapshot => snapshot with { State = RootWizardState.Patching });
        var patchedPath = await _patcher
            .PatchAsync(current.DeviceIdentity.Serial, current.BootImage, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(patchedPath) || !File.Exists(patchedPath))
        {
            throw Failure(RootErrorCode.PatchedImageInvalid, "The patched boot image was not produced.");
        }

        Publish(current => current with
        {
            State = RootWizardState.BootPatched,
            PatchedImagePath = Path.GetFullPath(patchedPath),
        });
        Publish(current => current with { State = RootWizardState.AwaitingBootloaderConfirm });
        _retryStep = RetryStep.Bootloader;
    }

    private async Task ConfirmBootloaderCoreAsync(CancellationToken ct)
    {
        var current = Snapshot;
        if (current.State != RootWizardState.AwaitingBootloaderConfirm
            && !(current.State == RootWizardState.Failed && _retryStep == RetryStep.Bootloader)
            || current.DeviceIdentity is null)
        {
            throw Failure(RootErrorCode.InvalidState, "The workflow is not awaiting bootloader confirmation.");
        }

        _retryStep = RetryStep.Bootloader;
        var baseline = await _fastboot.CaptureBaselineAsync(ct).ConfigureAwait(false);
        Publish(snapshot => snapshot with
        {
            FastbootBaseline = baseline,
            State = RootWizardState.RebootingToBootloader,
            ErrorCode = RootErrorCode.None,
        });
        await _fastboot
            .RebootToBootloaderAsync(current.DeviceIdentity.Serial, ct)
            .ConfigureAwait(false);
        _retryStep = RetryStep.DetectFastboot;
    }

    private async Task DetectFastbootCoreAsync(CancellationToken ct)
    {
        var current = Snapshot;
        if (current.State is not (
                RootWizardState.RebootingToBootloader
                or RootWizardState.BlockedFastbootIdentity
                or RootWizardState.BlockedLocked
                or RootWizardState.BlockedBootLayout)
            && !(current.State == RootWizardState.Failed && _retryStep == RetryStep.DetectFastboot)
            || current.DeviceIdentity is null
            || current.FastbootBaseline is null
            || current.BootImage is null)
        {
            throw Failure(RootErrorCode.InvalidState, "Fastboot detection prerequisites are incomplete.");
        }

        _retryStep = RetryStep.DetectFastboot;
        var match = current.FastbootIdentityMatch?.IsVerified == true
            ? current.FastbootIdentityMatch
            : await _fastboot.WaitForMatchingDeviceAsync(
                current.DeviceIdentity,
                current.FastbootBaseline,
                FastbootWaitTimeout,
                ct).ConfigureAwait(false);

        if (!match.IsVerified)
        {
            Publish(snapshot => snapshot with
            {
                FastbootIdentityMatch = match,
                State = RootWizardState.BlockedFastbootIdentity,
                ErrorCode = match.Status == FastbootIdentityMatchStatus.ConflictingEvidence
                    ? RootErrorCode.FastbootIdentityConflict
                    : RootErrorCode.FastbootIdentityUnverified,
            });
            return;
        }

        Publish(snapshot => snapshot with
        {
            FastbootIdentityMatch = match,
            State = RootWizardState.InFastboot,
            ErrorCode = RootErrorCode.None,
        });

        var serial = match.Device!.Serial;
        if (!await _fastboot.IsBootloaderUnlockedAsync(serial, ct).ConfigureAwait(false))
        {
            Publish(snapshot => snapshot with
            {
                State = RootWizardState.BlockedLocked,
                ErrorCode = RootErrorCode.BootloaderLocked,
            });
            return;
        }

        var layout = await _fastboot
            .GetBootLayoutAsync(serial, current.BootImage.TargetPartition, ct)
            .ConfigureAwait(false);
        if (!layout.IsKnown)
        {
            Publish(snapshot => snapshot with
            {
                BootLayout = layout,
                State = RootWizardState.BlockedBootLayout,
                ErrorCode = RootErrorCode.BootLayoutUnknown,
            });
            return;
        }

        Publish(snapshot => snapshot with
        {
            BootLayout = layout,
            State = RootWizardState.AwaitingFlashConfirm,
            ErrorCode = RootErrorCode.None,
        });
        _retryStep = RetryStep.Flash;
    }

    private async Task FlashAndRebootCoreAsync(CancellationToken ct)
    {
        var current = Snapshot;
        if (current.State is not (RootWizardState.AwaitingFlashConfirm or RootWizardState.FailedPartialFlash)
            && !(current.State == RootWizardState.Failed && _retryStep == RetryStep.Flash)
            || current.FastbootIdentityMatch?.IsVerified != true
            || current.BootLayout?.IsKnown != true
            || current.DeviceIdentity is null
            || current.BootImage is null
            || string.IsNullOrWhiteSpace(current.PatchedImagePath)
            || string.IsNullOrWhiteSpace(current.BackupPath)
            || !File.Exists(current.BackupPath))
        {
            throw Failure(RootErrorCode.InvalidState, "The prerequisites for flashing are incomplete.");
        }

        var expectedSerial = current.MatchedFastbootSerial!;
        var freshIdentity = await _fastboot
            .VerifyCurrentIdentityAsync(current.DeviceIdentity!, expectedSerial, ct)
            .ConfigureAwait(false);
        if (!freshIdentity.IsVerified
            || !string.Equals(freshIdentity.Device!.Serial, expectedSerial, StringComparison.Ordinal))
        {
            Publish(snapshot => snapshot with
            {
                FastbootIdentityMatch = freshIdentity,
                State = RootWizardState.BlockedFastbootIdentity,
                ErrorCode = freshIdentity.Status == FastbootIdentityMatchStatus.ConflictingEvidence
                    ? RootErrorCode.FastbootIdentityConflict
                    : RootErrorCode.FastbootIdentityUnverified,
            });
            _retryStep = RetryStep.DetectFastboot;
            return;
        }

        if (!await _fastboot.IsBootloaderUnlockedAsync(expectedSerial, ct).ConfigureAwait(false))
        {
            Publish(snapshot => snapshot with
            {
                FastbootIdentityMatch = freshIdentity,
                State = RootWizardState.BlockedLocked,
                ErrorCode = RootErrorCode.BootloaderLocked,
            });
            _retryStep = RetryStep.DetectFastboot;
            return;
        }

        var freshLayout = await _fastboot
            .GetBootLayoutAsync(expectedSerial, current.BootImage.TargetPartition, ct)
            .ConfigureAwait(false);
        if (!freshLayout.IsKnown || freshLayout != current.BootLayout)
        {
            Publish(snapshot => snapshot with
            {
                FastbootIdentityMatch = freshIdentity,
                BootLayout = freshLayout,
                State = RootWizardState.BlockedBootLayout,
                ErrorCode = freshLayout.IsKnown
                    ? RootErrorCode.BootLayoutConflict
                    : RootErrorCode.BootLayoutUnknown,
            });
            _retryStep = RetryStep.DetectFastboot;
            return;
        }

        _retryStep = RetryStep.Flash;
        Publish(snapshot => snapshot with
        {
            FastbootIdentityMatch = freshIdentity,
            BootLayout = freshLayout,
            State = RootWizardState.Flashing,
            ErrorCode = RootErrorCode.None,
        });
        _flashCommandStarted = true;
        FlashResult result;
        try
        {
            result = await _fastboot.FlashAsync(
                expectedSerial,
                current.BootImage.TargetPartition,
                current.PatchedImagePath,
                current.BootLayout,
                current.FlashResult?.SucceededPartitions,
                ct).ConfigureAwait(false);
        }
        finally
        {
            _flashCommandStarted = false;
        }

        Publish(snapshot => snapshot with { FlashResult = result });
        if (result.Outcome == FlashOutcome.PartiallyWritten)
        {
            Publish(snapshot => snapshot with
            {
                State = RootWizardState.FailedPartialFlash,
                ErrorCode = RootErrorCode.FlashPartiallyWritten,
            });
            return;
        }

        if (result.Outcome == FlashOutcome.Unknown)
        {
            Publish(snapshot => snapshot with
            {
                State = RootWizardState.FlashOutcomeUnknown,
                ErrorCode = RootErrorCode.FlashOutcomeUnknown,
            });
            _retryStep = RetryStep.None;
            return;
        }

        if (result.Outcome != FlashOutcome.Succeeded)
        {
            var errorCode = result.ErrorCode == RootErrorCode.None
                ? RootErrorCode.FlashFailed
                : result.ErrorCode;
            throw Failure(errorCode, "No boot partition was confirmed written.");
        }

        await RebootCoreAsync(ct).ConfigureAwait(false);
    }

    private async Task RebootCoreAsync(CancellationToken ct)
    {
        var serial = Snapshot.MatchedFastbootSerial;
        if (serial is null || Snapshot.FlashResult?.Outcome != FlashOutcome.Succeeded)
        {
            throw Failure(RootErrorCode.InvalidState, "A verified flashed device is required before rebooting.");
        }

        _retryStep = RetryStep.Reboot;
        Publish(current => current with { State = RootWizardState.Rebooting });
        await _fastboot.RebootAsync(serial, ct).ConfigureAwait(false);
        Publish(current => current with { State = RootWizardState.Completed, ErrorCode = RootErrorCode.None });
        _retryStep = RetryStep.None;
        TryCleanupWorkDirectory(Snapshot);
    }

    private void PrepareFlashRetry()
    {
        var current = Snapshot;
        if (current.State is not (RootWizardState.Failed or RootWizardState.FailedPartialFlash)
            || current.FlashResult?.Outcome is not (FlashOutcome.FailedBeforeWrite or FlashOutcome.PartiallyWritten))
        {
            throw Failure(RootErrorCode.InvalidState, "The current flash result cannot be retried safely.");
        }

        Publish(snapshot => snapshot with
        {
            State = RootWizardState.AwaitingFlashConfirm,
            ErrorCode = RootErrorCode.None,
        });
    }

    private Task StartOperationAsync(Func<CancellationToken, Task> operation, CancellationToken externalToken)
    {
        lock (_sync)
        {
            if (_activeOperation is not null)
            {
                throw Failure(RootErrorCode.InvalidState, "Another root workflow operation is already running.");
            }

            _activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            _activeOperation = ExecuteOperationAsync(operation, _activeCancellation);
            return _activeOperation;
        }
    }

    private async Task ExecuteOperationAsync(
        Func<CancellationToken, Task> operation,
        CancellationTokenSource cancellation)
    {
        await Task.Yield();
        try
        {
            await operation(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (Snapshot.State == RootWizardState.Rebooting
                && Snapshot.FlashResult?.Outcome == FlashOutcome.Succeeded)
            {
                Publish(current => current with
                {
                    State = RootWizardState.Failed,
                    ErrorCode = RootErrorCode.RebootFailed,
                });
                _retryStep = RetryStep.Reboot;
                return;
            }

            var outcomeUnknown = _flashCommandStarted || Snapshot.State == RootWizardState.Flashing;
            Publish(current => current with
            {
                State = outcomeUnknown ? RootWizardState.FlashOutcomeUnknown : RootWizardState.Cancelled,
                ErrorCode = outcomeUnknown ? RootErrorCode.FlashOutcomeUnknown : RootErrorCode.OperationCancelled,
                FlashResult = outcomeUnknown
                    ? current.FlashResult ?? new FlashResult
                    {
                        RequestedPartitions = Array.Empty<string>(),
                        Outcome = FlashOutcome.Unknown,
                        ErrorCode = RootErrorCode.FlashOutcomeUnknown,
                    }
                    : current.FlashResult,
            });
            _retryStep = RetryStep.None;
            TryCleanupWorkDirectory(Snapshot);
        }
        catch (RootWorkflowException ex)
        {
            HandleFailure(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected root workflow failure.");
            HandleFailure(new RootWorkflowException(
                RootErrorCode.UnexpectedFailure,
                "An unexpected root workflow failure occurred.",
                ex));
        }
        finally
        {
            lock (_sync)
            {
                cancellation.Dispose();
                if (ReferenceEquals(_activeCancellation, cancellation))
                {
                    _activeCancellation = null;
                    _activeOperation = null;
                }
            }
        }
    }

    private void HandleFailure(RootWorkflowException exception)
    {
        _logger.LogWarning(exception, "Root workflow step failed with {ErrorCode}.", exception.ErrorCode);
        var blockedState = exception.ErrorCode switch
        {
            RootErrorCode.PackageMetadataMismatch
                or RootErrorCode.TargetPartitionUnknown
                or RootErrorCode.TargetEvidenceConflict
                or RootErrorCode.TargetImageMissing
                or RootErrorCode.RecoveryOnlyUnsupported => RootWizardState.BlockedUnsupportedTarget,
            RootErrorCode.FastbootIdentityConflict
                or RootErrorCode.FastbootIdentityUnverified => RootWizardState.BlockedFastbootIdentity,
            RootErrorCode.BootloaderLocked => RootWizardState.BlockedLocked,
            RootErrorCode.BootLayoutUnknown
                or RootErrorCode.BootLayoutConflict => RootWizardState.BlockedBootLayout,
            RootErrorCode.FlashPartiallyWritten => RootWizardState.FailedPartialFlash,
            RootErrorCode.FlashOutcomeUnknown => RootWizardState.FlashOutcomeUnknown,
            _ => RootWizardState.Failed,
        };

        Publish(current => current with
        {
            State = blockedState,
            ErrorCode = exception.ErrorCode,
            DiagnosticSummary = exception.DiagnosticSummary,
        });
    }

    private void EnsureSelectionAllowed()
    {
        lock (_sync)
        {
            if (_activeOperation is not null
                || Snapshot.State is not (
                    RootWizardState.Idle
                    or RootWizardState.PackageSelected
                    or RootWizardState.Cancelled
                    or RootWizardState.Completed
                    or RootWizardState.BlockedUnsupportedTarget))
            {
                throw Failure(RootErrorCode.InvalidState, "Cancel the current root session before changing selection.");
            }
        }
    }

    private void Publish(Func<RootWizardSnapshot, RootWizardSnapshot> update)
    {
        RootWizardSnapshot next;
        lock (_sync)
        {
            next = update(_snapshot) with { UpdatedAtUtc = DateTimeOffset.UtcNow };
            Volatile.Write(ref _snapshot, next);
        }

        Changed?.Invoke(this, next);
    }

    private void TryCleanupWorkDirectory(RootWizardSnapshot snapshot)
    {
        var workDirectory = snapshot.WorkDirectory;
        if (string.IsNullOrWhiteSpace(workDirectory) || !Directory.Exists(workDirectory))
        {
            return;
        }

        try
        {
            var fullWorkDirectory = Path.GetFullPath(workDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var backupPath = snapshot.BackupPath;
            if (!string.IsNullOrWhiteSpace(backupPath)
                && Path.GetFullPath(backupPath).StartsWith(fullWorkDirectory, PathComparison))
            {
                _logger.LogWarning("Skipped root work cleanup because it contains the original image backup.");
                return;
            }

            Directory.Delete(workDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Failed to clean root work directory {WorkDirectory}.", workDirectory);
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static RootWorkflowException Failure(RootErrorCode code, string message) => new(code, message);

    private enum RetryStep
    {
        None,
        ExtractAndPatch,
        BackupAndPatch,
        Bootloader,
        DetectFastboot,
        Flash,
        Reboot
    }
}
