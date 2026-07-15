using System.Diagnostics;
using AndroidTreeView.Adb.Commands;
using AndroidTreeView.Adb.Parsers;
using AndroidTreeView.Core.Exceptions;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;
using AndroidTreeView.Models.Rooting;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.Adb.Services;

/// <summary>Strict, identity-preserving fastboot operations for the root workflow.</summary>
public sealed class RootFastbootService : IRootFastbootService
{
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GetVarTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan FlashTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RebootTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IAdbEnvironment _environment;
    private readonly IAdbCommandExecutor _adb;
    private readonly IExternalCommandRunner _runner;
    private readonly RootToolPaths _rootToolPaths;
    private readonly ILogger<RootFastbootService> _logger;

    public RootFastbootService(
        IAdbEnvironment environment,
        IAdbCommandExecutor adb,
        IExternalCommandRunner runner,
        RootToolPaths rootToolPaths,
        ILogger<RootFastbootService> logger)
    {
        _environment = environment;
        _adb = adb;
        _runner = runner;
        _rootToolPaths = rootToolPaths;
        _logger = logger;
    }

    public string? ExecutablePath
    {
        get
        {
            var name = OperatingSystem.IsWindows() ? "fastboot.exe" : "fastboot";
            var bundled = Path.Combine(AppContext.BaseDirectory, "scrcpy", name);
            if (File.Exists(bundled))
            {
                return bundled;
            }

            var adbPath = _environment.Location?.ExecutablePath;
            var adbDirectory = string.IsNullOrWhiteSpace(adbPath) ? null : Path.GetDirectoryName(adbPath);
            if (!string.IsNullOrWhiteSpace(adbDirectory))
            {
                var sibling = Path.Combine(adbDirectory, name);
                if (File.Exists(sibling))
                {
                    return sibling;
                }
            }

            return null;
        }
    }

    public async Task<FastbootBaseline> CaptureBaselineAsync(CancellationToken ct = default)
    {
        var devices = await ListDevicesStrictAsync(RootErrorCode.FastbootBaselineFailed, ct).ConfigureAwait(false);
        return new FastbootBaseline
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Devices = devices
        };
    }

    public async Task RebootToBootloaderAsync(string adbSerial, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adbSerial);
        AdbCommandResult result;
        try
        {
            result = await _adb.ExecuteAsync(new AdbCommandRequest
            {
                Serial = adbSerial,
                Arguments = new[] { "reboot", "bootloader" },
                Timeout = RebootTimeout
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RootWorkflowException(RootErrorCode.RebootToBootloaderFailed,
                "ADB could not reboot the selected device to bootloader.", ex);
        }

        if (!result.IsSuccess)
        {
            throw Failure(
                RootErrorCode.RebootToBootloaderFailed,
                "ADB could not reboot the selected device to bootloader.",
                result.StandardError,
                result.TimedOut);
        }
    }

    public async Task<FastbootIdentityMatch> WaitForMatchingDeviceAsync(
        RootDeviceIdentity device,
        FastbootBaseline baseline,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(baseline);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var baselineSerials = baseline.Devices
            .Select(static item => item.Serial)
            .ToHashSet(StringComparer.Ordinal);
        var stopwatch = Stopwatch.StartNew();
        FastbootIdentityMatch? lastResult = null;

        while (stopwatch.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<FastbootDeviceIdentity> listed;
            try
            {
                listed = await ListDevicesStrictAsync(RootErrorCode.FastbootDeviceNotFound, ct).ConfigureAwait(false);
            }
            catch (RootWorkflowException ex) when (ex.ErrorCode == RootErrorCode.FastbootDeviceNotFound)
            {
                listed = Array.Empty<FastbootDeviceIdentity>();
            }

            var candidates = listed.Where(item => !baselineSerials.Contains(item.Serial)).ToArray();
            var enriched = await EnrichProductsAsync(candidates, ct).ConfigureAwait(false);
            lastResult = FastbootVarParser.MatchIdentity(device, enriched);
            if (lastResult.IsVerified
                || lastResult.Status is FastbootIdentityMatchStatus.Ambiguous
                    or FastbootIdentityMatchStatus.ConflictingEvidence)
            {
                return lastResult;
            }

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining < PollInterval ? remaining : PollInterval, ct).ConfigureAwait(false);
            }
        }

        return lastResult is { Status: FastbootIdentityMatchStatus.ConflictingEvidence }
            ? lastResult
            : new FastbootIdentityMatch { Status = FastbootIdentityMatchStatus.TargetNotFound };
    }

    public async Task<bool> IsBootloaderUnlockedAsync(string fastbootSerial, CancellationToken ct = default)
    {
        var value = await GetVariableStrictAsync(fastbootSerial, "unlocked", ct).ConfigureAwait(false);
        return FastbootVarParser.ParseBoolean(value) == true;
    }

    public async Task<FastbootIdentityMatch> VerifyCurrentIdentityAsync(
        RootDeviceIdentity device,
        string expectedSerial,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSerial);

        var listed = await ListDevicesStrictAsync(RootErrorCode.FastbootDeviceNotFound, ct).ConfigureAwait(false);
        var expected = listed
            .Where(candidate => candidate.Serial.Equals(expectedSerial, StringComparison.Ordinal))
            .ToArray();
        if (expected.Length == 0)
        {
            return new FastbootIdentityMatch { Status = FastbootIdentityMatchStatus.TargetNotFound };
        }

        var enriched = await EnrichProductsAsync(expected, ct).ConfigureAwait(false);
        return FastbootVarParser.MatchIdentity(device, enriched);
    }

    public async Task<FastbootBootLayout> GetBootLayoutAsync(
        string fastbootSerial,
        BootPartitionTarget target,
        CancellationToken ct = default)
    {
        var partition = FastbootArgs.PartitionName(target);
        var slotCountValue = await GetVariableOptionalAsync(fastbootSerial, "slot-count", ct).ConfigureAwait(false);
        var hasSlotValue = await GetVariableOptionalAsync(fastbootSerial, $"has-slot:{partition}", ct).ConfigureAwait(false);
        var currentSlotValue = await GetVariableOptionalAsync(fastbootSerial, "current-slot", ct).ConfigureAwait(false);

        int? slotCount = int.TryParse(slotCountValue, out var count) && count >= 0 ? count : null;
        var hasSlot = FastbootVarParser.ParseBoolean(hasSlotValue);
        var currentSlot = NormalizeSlot(currentSlotValue);

        // Legacy bootloaders predate A/B and answer "GetVar Variable Not found" for has-slot and
        // current-slot, so an explicit slot-count of 0 or 1 is their only affirmative way to report
        // a single-slot layout. Absent variables still count as no evidence, never as a slot.
        var singleSlotEvidence = hasSlot == false || slotCount is 0 or 1;
        var abSlotEvidence = hasSlot == true || slotCount > 1 || currentSlot is not null;

        var kind = FastbootBootLayoutKind.Unknown;
        if (singleSlotEvidence && !abSlotEvidence)
        {
            kind = FastbootBootLayoutKind.Single;
        }
        else if (hasSlot == true && slotCount == 2 && currentSlot is not null)
        {
            kind = FastbootBootLayoutKind.Ab;
        }

        return new FastbootBootLayout
        {
            Kind = kind,
            CurrentSlot = currentSlot,
            SlotCount = slotCount,
            TargetHasSlot = hasSlot
        };
    }

    public async Task<FlashResult> FlashAsync(
        string fastbootSerial,
        BootPartitionTarget target,
        string imagePath,
        FastbootBootLayout layout,
        IReadOnlyCollection<string>? alreadySucceededPartitions = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fastbootSerial);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentNullException.ThrowIfNull(layout);
        if (!File.Exists(imagePath) || new FileInfo(imagePath).Length == 0)
        {
            throw new RootWorkflowException(RootErrorCode.PatchedImageInvalid, "The patched boot image is missing or empty.");
        }

        var targetName = FastbootArgs.PartitionName(target);
        var requested = layout.Kind switch
        {
            FastbootBootLayoutKind.Single => new[] { targetName },
            FastbootBootLayoutKind.Ab => new[] { $"{targetName}_a", $"{targetName}_b" },
            _ => Array.Empty<string>()
        };
        if (requested.Length == 0)
        {
            return FailedResult(requested, Array.Empty<string>(), null, FlashOutcome.FailedBeforeWrite,
                RootErrorCode.BootLayoutUnknown, "Boot layout is unknown.");
        }

        var succeeded = (alreadySucceededPartitions ?? Array.Empty<string>())
            .Where(partition => requested.Contains(partition, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var partition in requested.Where(partition => !succeeded.Contains(partition, StringComparer.Ordinal)))
        {
            if (ct.IsCancellationRequested && succeeded.Count > 0)
            {
                return FailedResult(requested, succeeded, partition, FlashOutcome.PartiallyWritten,
                    RootErrorCode.FlashPartiallyWritten,
                    "Flash was cancelled before the next partition write started.");
            }

            ct.ThrowIfCancellationRequested();
            ExternalCommandResult result;
            try
            {
                result = await RunFastbootAsync(
                    FastbootArgs.Flash(fastbootSerial, partition, Path.GetFullPath(imagePath)),
                    FlashTimeout,
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return FailedResult(requested, succeeded, partition, FlashOutcome.Unknown,
                    RootErrorCode.FlashOutcomeUnknown, "Flash command was cancelled; device write status is unknown.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fastboot flash process failed for partition {Partition}.", partition);
                return FailedResult(requested, succeeded, partition, FlashOutcome.Unknown,
                    RootErrorCode.FlashOutcomeUnknown, "Fastboot process ended unexpectedly; device write status is unknown.");
            }

            if (result.TimedOut)
            {
                return FailedResult(requested, succeeded, partition, FlashOutcome.Unknown,
                    RootErrorCode.FlashOutcomeUnknown, Diagnostic(result.StandardError, timedOut: true));
            }

            if (!result.IsSuccess)
            {
                return FailedResult(requested, succeeded, partition, FlashOutcome.Unknown,
                    RootErrorCode.FlashOutcomeUnknown, Diagnostic(result.StandardError));
            }

            succeeded.Add(partition);
        }

        return new FlashResult
        {
            RequestedPartitions = requested,
            SucceededPartitions = succeeded,
            Outcome = FlashOutcome.Succeeded,
            ErrorCode = RootErrorCode.None
        };
    }

    public async Task RebootAsync(string fastbootSerial, CancellationToken ct = default)
    {
        ExternalCommandResult result;
        try
        {
            result = await RunFastbootAsync(FastbootArgs.Reboot(fastbootSerial), RebootTimeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RootWorkflowException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RootWorkflowException(RootErrorCode.RebootFailed,
                "Fastboot could not reboot the selected device.", ex);
        }

        if (!result.IsSuccess)
        {
            throw Failure(RootErrorCode.RebootFailed, "Fastboot could not reboot the selected device.",
                result.StandardError, result.TimedOut);
        }
    }

    private async Task<IReadOnlyList<FastbootDeviceIdentity>> ListDevicesStrictAsync(
        RootErrorCode errorCode,
        CancellationToken ct)
    {
        ExternalCommandResult result;
        try
        {
            result = await RunFastbootAsync(FastbootArgs.DevicesLong, ListTimeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RootWorkflowException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RootWorkflowException(errorCode, "Fastboot device enumeration failed.", ex);
        }

        if (!result.IsSuccess)
        {
            throw Failure(errorCode, "Fastboot device enumeration failed.", result.StandardError, result.TimedOut);
        }

        return FastbootVarParser.ParseDevices(result.StandardOutput + "\n" + result.StandardError);
    }

    private async Task<IReadOnlyList<FastbootDeviceIdentity>> EnrichProductsAsync(
        IReadOnlyList<FastbootDeviceIdentity> candidates,
        CancellationToken ct)
    {
        var enriched = new List<FastbootDeviceIdentity>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate.Product))
            {
                enriched.Add(candidate);
                continue;
            }

            string? product = null;
            try
            {
                product = await GetVariableStrictAsync(candidate.Serial, "product", ct).ConfigureAwait(false);
            }
            catch (RootWorkflowException)
            {
                // Missing product evidence keeps the candidate unverified; it must never be guessed.
            }

            enriched.Add(candidate with { Product = product });
        }

        return enriched;
    }

    private async Task<string?> GetVariableStrictAsync(string serial, string variable, CancellationToken ct)
    {
        ExternalCommandResult result;
        try
        {
            result = await RunFastbootAsync(FastbootArgs.GetVar(serial, variable), GetVarTimeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RootWorkflowException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RootWorkflowException(RootErrorCode.FastbootDeviceNotFound,
                $"Fastboot getvar {variable} failed.", ex);
        }

        if (!result.IsSuccess)
        {
            throw Failure(RootErrorCode.FastbootDeviceNotFound, $"Fastboot getvar {variable} failed.",
                result.StandardError, result.TimedOut);
        }

        var combined = result.StandardOutput + "\n" + result.StandardError;
        return FastbootVarParser.ParseValue(combined, variable);
    }

    private async Task<string?> GetVariableOptionalAsync(string serial, string variable, CancellationToken ct)
    {
        try
        {
            var result = await RunFastbootAsync(
                FastbootArgs.GetVar(serial, variable),
                GetVarTimeout,
                ct).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return null;
            }

            return FastbootVarParser.ParseValue(
                result.StandardOutput + "\n" + result.StandardError,
                variable);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RootWorkflowException ex) when (ex.ErrorCode == RootErrorCode.FastbootUnavailable)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Optional fastboot variable {Variable} could not be read.", variable);
            return null;
        }
    }

    private async Task<ExternalCommandResult> RunFastbootAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var executable = ExecutablePath;
        if (executable is null)
        {
            throw new RootWorkflowException(RootErrorCode.FastbootUnavailable, "The fastboot executable is unavailable.");
        }

        return await _runner.RunAsync(new ExternalCommandRequest
        {
            FileName = executable,
            Arguments = arguments,
            Timeout = timeout
        }, ct).ConfigureAwait(false);
    }

    private static FlashResult FailedResult(
        IReadOnlyList<string> requested,
        IReadOnlyList<string> succeeded,
        string? failedPartition,
        FlashOutcome outcome,
        RootErrorCode errorCode,
        string diagnostic)
        => new()
        {
            RequestedPartitions = requested,
            SucceededPartitions = succeeded.ToArray(),
            FailedPartition = failedPartition,
            Outcome = outcome,
            ErrorCode = errorCode,
            DiagnosticSummary = diagnostic
        };

    private static RootWorkflowException Failure(
        RootErrorCode code,
        string message,
        string? standardError,
        bool timedOut)
        => new(code, message) { DiagnosticSummary = Diagnostic(standardError, timedOut) };

    private static string Diagnostic(string? value, bool timedOut = false)
    {
        var normalized = string.Join(' ', (value ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (normalized.Length > 300)
        {
            normalized = normalized[..300];
        }

        if (timedOut)
        {
            return normalized.Length == 0 ? "Command timed out." : $"Command timed out. {normalized}";
        }

        return normalized.Length == 0 ? "Command failed without diagnostic output." : normalized;
    }

    private static string? NormalizeSlot(string? value)
    {
        var normalized = value?.Trim().TrimStart('_').ToLowerInvariant();
        return normalized is "a" or "b" ? normalized : null;
    }
}
