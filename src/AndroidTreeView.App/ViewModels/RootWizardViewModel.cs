using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AndroidTreeView.App.Services;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Models.Devices;
using AndroidTreeView.Models.Rooting;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AndroidTreeView.App.ViewModels;

/// <summary>Presentation and user-confirmation layer for the semi-automatic root workflow.</summary>
public sealed partial class RootWizardViewModel : ViewModelBase
{
    private readonly IRootWizardService _wizard;
    private readonly IDeviceMonitor _monitor;
    private readonly IFilePickerService _filePicker;
    private readonly IDialogService _dialog;
    private readonly ILocalizationService _localization;
    private readonly ILogger<RootWizardViewModel> _logger;
    private IReadOnlyList<AdbDevice> _latestDevices = Array.Empty<AdbDevice>();
    private bool _adbAvailable;
    private CancellationTokenSource? _operationCts;

    [ObservableProperty]
    private RootDeviceOptionViewModel? _selectedDevice;

    [ObservableProperty]
    private RootWizardState _state;

    [ObservableProperty]
    private string _stateText = string.Empty;

    [ObservableProperty]
    private string _deviceListMessage = string.Empty;

    [ObservableProperty]
    private string? _selectedPackagePath;

    [ObservableProperty]
    private string _packageDisplayName = string.Empty;

    [ObservableProperty]
    private string _targetPartitionText = string.Empty;

    [ObservableProperty]
    private string _packageMatchText = string.Empty;

    [ObservableProperty]
    private string _identityText = string.Empty;

    [ObservableProperty]
    private string _slotText = string.Empty;

    [ObservableProperty]
    private string? _backupPath;

    [ObservableProperty]
    private string? _patchedImagePath;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isSelectionLocked;

    [ObservableProperty]
    private bool _hasAcknowledgedRisk;

    [ObservableProperty]
    private bool _isAbLayout;

    [ObservableProperty]
    private bool _isPackageMatchUnverified;

    [ObservableProperty]
    private bool _showFlashConfirmation;

    [ObservableProperty]
    private bool _showCompleted;

    [ObservableProperty]
    private bool _showBlockedGuidance;

    [ObservableProperty]
    private string _blockedGuidanceText = string.Empty;

    [ObservableProperty]
    private bool _showPartialFlashWarning;

    public RootWizardViewModel(
        IRootWizardService wizard,
        IDeviceMonitor monitor,
        IFilePickerService filePicker,
        IDialogService dialog,
        ILocalizationService localization,
        ILogger<RootWizardViewModel> logger)
    {
        _wizard = wizard ?? throw new ArgumentNullException(nameof(wizard));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Steps = new ObservableCollection<RootWizardStepItem>(Enumerable.Range(0, 6).Select(i => new RootWizardStepItem(i)));
        _wizard.Changed += OnWizardChanged;
        _monitor.DevicesChanged += OnDevicesChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        ApplySnapshot(_wizard.Snapshot);
    }

    public ObservableCollection<RootDeviceOptionViewModel> Devices { get; } = new();

    public ObservableCollection<RootWizardStepItem> Steps { get; }

    public bool HasDevices => Devices.Count > 0;

    public bool IsDeviceSelectionEnabled => !IsSelectionLocked && !IsBusy;

    public bool CanPickPackage => !IsSelectionLocked && !IsBusy;

    public bool CanStart =>
        State == RootWizardState.PackageSelected
        && SelectedDevice is not null
        && !string.IsNullOrWhiteSpace(SelectedPackagePath)
        && !IsBusy;

    public bool CanConfirmBootloader => State == RootWizardState.AwaitingBootloaderConfirm && !IsBusy;

    public bool CanRecheckFastboot =>
        State is RootWizardState.RebootingToBootloader
            or RootWizardState.BlockedFastbootIdentity
            or RootWizardState.BlockedLocked
            or RootWizardState.BlockedBootLayout
        && !IsBusy;

    public bool CanRetry => State == RootWizardState.Failed && !IsRetryBlocked(_wizard.Snapshot.ErrorCode) && !IsBusy;

    public bool CanCancel => State is not (
        RootWizardState.Idle
        or RootWizardState.FlashOutcomeUnknown
        or RootWizardState.Cancelled
        or RootWizardState.Completed);

    public bool CanFlash =>
        State is RootWizardState.AwaitingFlashConfirm or RootWizardState.FailedPartialFlash
        && _wizard.Snapshot.FastbootIdentityMatch?.IsVerified == true
        && _wizard.Snapshot.BootLayout?.IsKnown == true
        && _wizard.Snapshot.TargetPartition != BootPartitionTarget.Unknown
        && !string.IsNullOrWhiteSpace(PatchedImagePath)
        && !string.IsNullOrWhiteSpace(BackupPath)
        && HasAcknowledgedRisk
        && !IsBusy;

    public async Task RefreshDevicesAsync(CancellationToken ct = default)
    {
        try
        {
            var snapshot = await _monitor.RefreshNowAsync(ct).ConfigureAwait(false);
            RunOnUiThread(() => ApplyDevices(snapshot.Devices, snapshot.AdbAvailable));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Refreshing root target devices failed.");
            RunOnUiThread(() => DeviceListMessage = _localization.Get("root.device.refresh.failed"));
        }
    }

    partial void OnSelectedDeviceChanged(RootDeviceOptionViewModel? value)
    {
        if (value is null || IsSelectionLocked)
        {
            return;
        }

        try
        {
            _wizard.SelectDevice(value.Identity);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Selecting root target device failed.");
        }
    }

    partial void OnHasAcknowledgedRiskChanged(bool value) => FlashCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private Task RefreshTargetsAsync(CancellationToken ct) => RefreshDevicesAsync(ct);

    [RelayCommand(CanExecute = nameof(CanPickPackage))]
    private async Task PickPackageAsync()
    {
        var path = await _filePicker.PickRootPackageAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            _wizard.SelectPackage(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Selecting root firmware package failed.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync() => RunOperationAsync(ct => _wizard.ExtractAndPatchAsync(ct));

    [RelayCommand(CanExecute = nameof(CanConfirmBootloader))]
    private async Task ConfirmBootloaderAsync()
    {
        var confirmed = await _dialog.ConfirmAsync(
            _localization.Get("root.confirm.bootloader.title"),
            _localization.Get("root.confirm.bootloader.message"),
            _localization.Get("root.action.enter.bootloader"),
            _localization.Get("common.cancel")).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        await RunOperationAsync(async ct =>
        {
            await _wizard.ConfirmBootloaderAsync(ct).ConfigureAwait(false);
            if (_wizard.Snapshot.State != RootWizardState.RebootingToBootloader)
            {
                return;
            }

            await _wizard.DetectFastbootAsync(ct).ConfigureAwait(false);
        }).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanRecheckFastboot))]
    private Task RecheckFastbootAsync() => RunOperationAsync(ct => _wizard.DetectFastbootAsync(ct));

    [RelayCommand(CanExecute = nameof(CanFlash))]
    private Task FlashAsync() => RunOperationAsync(ct => _wizard.ConfirmFlashAsync(HasAcknowledgedRisk, ct));

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task RetryAsync() => RunOperationAsync(ct => _wizard.RetryAsync(ct));

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync()
    {
        _operationCts?.Cancel();
        await _wizard.CancelAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private Task OpenBackupFolderAsync()
    {
        var directory = string.IsNullOrWhiteSpace(BackupPath) ? null : Path.GetDirectoryName(BackupPath);
        return string.IsNullOrWhiteSpace(directory) ? Task.CompletedTask : _filePicker.OpenUrlAsync(directory);
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        try
        {
            IsBusy = true;
            NotifyCommandStates();
            await operation(_operationCts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_operationCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Root workflow UI operation failed.");
        }
        finally
        {
            IsBusy = IsBusyState(_wizard.Snapshot.State);
            NotifyCommandStates();
        }
    }

    private void OnWizardChanged(object? sender, RootWizardSnapshot snapshot) =>
        RunOnUiThread(() => ApplySnapshot(snapshot));

    private void OnDevicesChanged(object? sender, DeviceListChangedEventArgs e) =>
        RunOnUiThread(() => ApplyDevices(e.Devices, e.AdbAvailable));

    private void OnLanguageChanged(object? sender, EventArgs e) => RunOnUiThread(() =>
    {
        ApplyDevices(_latestDevices, _adbAvailable);
        ApplyPresentation(_wizard.Snapshot);
    });

    private void ApplyDevices(IReadOnlyList<AdbDevice> devices, bool adbAvailable)
    {
        _latestDevices = devices;
        _adbAvailable = adbAvailable;
        var online = devices.Where(device => device.IsOnline).ToArray();
        var lockedIdentity = IsSelectionLocked ? _wizard.Snapshot.DeviceIdentity : null;
        var selectedSerial = lockedIdentity?.Serial
            ?? SelectedDevice?.Identity.Serial
            ?? _wizard.Snapshot.DeviceIdentity?.Serial;

        var desired = new List<RootDeviceOptionViewModel>(online.Length);
        foreach (var device in online)
        {
            desired.Add(new RootDeviceOptionViewModel(
                new RootDeviceIdentity
                {
                    Serial = device.Serial,
                    UsbPath = device.UsbPath,
                    Product = device.Product,
                    Device = device.Device,
                    Model = device.Model,
                    TransportId = device.TransportId,
                },
                device.DisplayName,
                _localization.Format("root.device.details", device.Serial, device.Product ?? device.Device ?? _localization.Get("common.unavailable"))));
        }

        if (lockedIdentity is not null
            && desired.All(device => !string.Equals(
                device.Identity.Serial,
                lockedIdentity.Serial,
                StringComparison.Ordinal)))
        {
            desired.Insert(0, CreateDeviceOption(lockedIdentity));
        }

        // The device poll ticks every second. Rebuilding an unchanged collection makes the ListBox
        // drop and restore its selection, which drags the surrounding ScrollViewer back to the
        // device card while the user is reading further down the wizard.
        if (!Devices.SequenceEqual(desired))
        {
            Devices.Clear();
            foreach (var device in desired)
            {
                Devices.Add(device);
            }
        }

        SelectedDevice = Devices.FirstOrDefault(device => string.Equals(device.Identity.Serial, selectedSerial, StringComparison.Ordinal));
        if (SelectedDevice is null && Devices.Count == 1 && !IsSelectionLocked && _wizard.Snapshot.DeviceIdentity is null)
        {
            SelectedDevice = Devices[0];
        }

        DeviceListMessage = !adbAvailable
            ? _localization.Get("root.device.adb.missing")
            : Devices.Count == 0
                ? _localization.Get("root.device.empty")
                : Devices.Count > 1 && SelectedDevice is null
                    ? _localization.Get("root.device.choose")
                    : string.Empty;
        OnPropertyChanged(nameof(HasDevices));
    }

    private void ApplySnapshot(RootWizardSnapshot snapshot)
    {
        var previousState = State;
        State = snapshot.State;
        SelectedPackagePath = snapshot.PackagePath;
        PackageDisplayName = string.IsNullOrWhiteSpace(snapshot.PackagePath) ? string.Empty : Path.GetFileName(snapshot.PackagePath);
        BackupPath = snapshot.BackupPath;
        PatchedImagePath = snapshot.PatchedImagePath;
        IsBusy = IsBusyState(snapshot.State);
        IsSelectionLocked = !IsSelectionState(snapshot.State);
        IsAbLayout = snapshot.BootLayout?.Kind == FastbootBootLayoutKind.Ab;
        IsPackageMatchUnverified = snapshot.PackageMetadata?.MatchStatus == FirmwarePackageMatchStatus.Unverified;
        ShowFlashConfirmation = snapshot.State is RootWizardState.AwaitingFlashConfirm or RootWizardState.FailedPartialFlash;
        ShowCompleted = snapshot.State == RootWizardState.Completed;
        ShowPartialFlashWarning = snapshot.State is RootWizardState.FailedPartialFlash or RootWizardState.FlashOutcomeUnknown;

        if (previousState != snapshot.State && ShowFlashConfirmation)
        {
            HasAcknowledgedRisk = false;
        }

        var lockedSerial = snapshot.DeviceIdentity?.Serial;
        if (!string.IsNullOrWhiteSpace(lockedSerial))
        {
            var lockedDevice = Devices.FirstOrDefault(device => string.Equals(
                device.Identity.Serial,
                lockedSerial,
                StringComparison.Ordinal));
            if (lockedDevice is null && IsSelectionLocked && snapshot.DeviceIdentity is not null)
            {
                lockedDevice = CreateDeviceOption(snapshot.DeviceIdentity);
                Devices.Insert(0, lockedDevice);
            }

            SelectedDevice = lockedDevice;
        }

        ApplyPresentation(snapshot);
        NotifyCommandStates();
    }

    private RootDeviceOptionViewModel CreateDeviceOption(RootDeviceIdentity identity)
        => new(
            identity,
            identity.Model ?? identity.Serial,
            _localization.Format(
                "root.device.details",
                identity.Serial,
                identity.Product ?? identity.Device ?? _localization.Get("common.unavailable")));

    private void ApplyPresentation(RootWizardSnapshot snapshot)
    {
        StateText = _localization.Get(StateKey(snapshot.State));
        TargetPartitionText = snapshot.TargetPartition switch
        {
            BootPartitionTarget.Boot => "boot",
            BootPartitionTarget.InitBoot => "init_boot",
            _ => _localization.Get("common.unavailable"),
        };
        PackageMatchText = snapshot.PackageMetadata?.MatchStatus switch
        {
            FirmwarePackageMatchStatus.Matched => _localization.Get("root.package.match.matched"),
            FirmwarePackageMatchStatus.Mismatched => _localization.Get("root.package.match.mismatched"),
            FirmwarePackageMatchStatus.Unverified => _localization.Get("root.package.match.unverified"),
            _ => _localization.Get("common.unavailable"),
        };
        IdentityText = snapshot.FastbootIdentityMatch is { } match
            ? match.IsVerified
                ? _localization.Format("root.identity.verified", match.Device!.Serial, LocalizeEvidence(match.Evidence))
                : _localization.Get("root.identity.unverified")
            : _localization.Get("root.identity.pending");
        SlotText = snapshot.BootLayout?.Kind switch
        {
            FastbootBootLayoutKind.Single => _localization.Format("root.slot.single", TargetPartitionText),
            FastbootBootLayoutKind.Ab => _localization.Format("root.slot.ab", TargetPartitionText, TargetPartitionText),
            _ => _localization.Get("root.slot.pending"),
        };

        HasError = snapshot.ErrorCode != RootErrorCode.None && snapshot.ErrorCode != RootErrorCode.OperationCancelled;
        ErrorMessage = HasError ? _localization.Get(ErrorKey(snapshot.ErrorCode)) : string.Empty;
        ShowBlockedGuidance = snapshot.State is RootWizardState.BlockedUnsupportedTarget
            or RootWizardState.BlockedFastbootIdentity
            or RootWizardState.BlockedLocked
            or RootWizardState.BlockedBootLayout;
        BlockedGuidanceText = snapshot.State switch
        {
            RootWizardState.BlockedLocked => _localization.Get("root.blocked.locked.guide"),
            RootWizardState.BlockedFastbootIdentity => _localization.Get("root.blocked.identity.guide"),
            RootWizardState.BlockedBootLayout => _localization.Get("root.blocked.layout.guide"),
            RootWizardState.BlockedUnsupportedTarget => _localization.Get("root.blocked.target.guide"),
            _ => string.Empty,
        };
        UpdateSteps(snapshot.State);
    }

    private void UpdateSteps(RootWizardState state)
    {
        var current = StepIndex(state);
        var completed = state == RootWizardState.Completed;
        var titles = new[]
        {
            "root.step.select", "root.step.extract", "root.step.patch",
            "root.step.fastboot", "root.step.flash", "root.step.complete",
        };
        for (var i = 0; i < Steps.Count; i++)
        {
            Steps[i].Title = _localization.Get(titles[i]);
            Steps[i].IsCurrent = !completed && i == current;
            Steps[i].IsComplete = completed || i < current;
            Steps[i].StatusText = Steps[i].IsComplete
                ? _localization.Get("root.step.done")
                : Steps[i].IsCurrent
                    ? _localization.Get("root.step.current")
                    : _localization.Get("root.step.pending");
        }
    }

    private void NotifyCommandStates()
    {
        OnPropertyChanged(nameof(IsDeviceSelectionEnabled));
        OnPropertyChanged(nameof(CanPickPackage));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanConfirmBootloader));
        OnPropertyChanged(nameof(CanRecheckFastboot));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanFlash));
        PickPackageCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        ConfirmBootloaderCommand.NotifyCanExecuteChanged();
        RecheckFastbootCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        FlashCommand.NotifyCanExecuteChanged();
    }

    private static bool IsBusyState(RootWizardState state) => state is
        RootWizardState.Extracting or RootWizardState.BootExtracted or RootWizardState.Patching
        or RootWizardState.RebootingToBootloader or RootWizardState.InFastboot
        or RootWizardState.Flashing or RootWizardState.Rebooting;

    private static bool IsSelectionState(RootWizardState state) => state is
        RootWizardState.Idle or RootWizardState.PackageSelected or RootWizardState.Cancelled
        or RootWizardState.Completed;

    private static bool IsRetryBlocked(RootErrorCode code) => code is
        RootErrorCode.RiskNotAcknowledged or RootErrorCode.FlashPartiallyWritten
        or RootErrorCode.FlashOutcomeUnknown;

    private string LocalizeEvidence(FastbootIdentityEvidence evidence)
    {
        var values = new List<string>(3);
        if (evidence.HasFlag(FastbootIdentityEvidence.Serial))
        {
            values.Add(_localization.Get("root.identity.evidence.serial"));
        }

        if (evidence.HasFlag(FastbootIdentityEvidence.UsbPath))
        {
            values.Add(_localization.Get("root.identity.evidence.usb"));
        }

        if (evidence.HasFlag(FastbootIdentityEvidence.Product))
        {
            values.Add(_localization.Get("root.identity.evidence.product"));
        }

        return values.Count == 0
            ? _localization.Get("root.identity.evidence.none")
            : string.Join(_localization.Get("root.identity.evidence.separator"), values);
    }

    private static int StepIndex(RootWizardState state) => state switch
    {
        RootWizardState.Idle or RootWizardState.PackageSelected => 0,
        RootWizardState.Extracting or RootWizardState.BootExtracted or RootWizardState.BlockedUnsupportedTarget => 1,
        RootWizardState.Patching or RootWizardState.BootPatched or RootWizardState.AwaitingBootloaderConfirm => 2,
        RootWizardState.RebootingToBootloader or RootWizardState.InFastboot or RootWizardState.BlockedFastbootIdentity
            or RootWizardState.BlockedLocked or RootWizardState.BlockedBootLayout => 3,
        RootWizardState.AwaitingFlashConfirm or RootWizardState.Flashing or RootWizardState.FailedPartialFlash
            or RootWizardState.FlashOutcomeUnknown => 4,
        RootWizardState.Rebooting or RootWizardState.Completed => 5,
        _ => 0,
    };

    private static string StateKey(RootWizardState state) => $"root.state.{state.ToString().ToLowerInvariant()}";

    private static string ErrorKey(RootErrorCode code) => code switch
    {
        RootErrorCode.DeviceNotSelected or RootErrorCode.DeviceUnavailable or RootErrorCode.DeviceUnauthorized => "root.error.device",
        RootErrorCode.PackageNotFound or RootErrorCode.PackageUnsupported or RootErrorCode.PackageCorrupt
            or RootErrorCode.PackageExtractionFailed or RootErrorCode.PackageSizeLimitExceeded
            or RootErrorCode.PackagePathUnsafe => "root.error.package",
        RootErrorCode.PackageMetadataMismatch => "root.error.package.mismatch",
        RootErrorCode.PayloadToolUnavailable or RootErrorCode.PayloadExtractionFailed => "root.error.payload",
        RootErrorCode.TargetImageMissing or RootErrorCode.TargetPartitionUnknown
            or RootErrorCode.TargetEvidenceConflict => "root.error.target",
        RootErrorCode.RecoveryOnlyUnsupported => "root.error.recoveryonly",
        RootErrorCode.BackupSourceMissing or RootErrorCode.BackupFailed or RootErrorCode.BackupVerificationFailed => "root.error.backup",
        RootErrorCode.MagiskToolUnavailable or RootErrorCode.MagiskInstallFailed or RootErrorCode.DeviceAbiUnsupported
            or RootErrorCode.ImagePushFailed or RootErrorCode.MagiskPatchFailed
            or RootErrorCode.PatchedImagePullFailed or RootErrorCode.PatchedImageInvalid
            or RootErrorCode.MagiskFlagProbeFailed => "root.error.magisk",
        RootErrorCode.PatchedImageFlagMismatch => "root.error.patched.flags",
        RootErrorCode.FastbootUnavailable or RootErrorCode.FastbootBaselineFailed
            or RootErrorCode.FastbootDeviceNotFound or RootErrorCode.RebootToBootloaderFailed => "root.error.fastboot",
        RootErrorCode.FastbootIdentityUnverified or RootErrorCode.FastbootIdentityConflict => "root.error.identity",
        RootErrorCode.BootloaderLocked => "root.error.locked",
        RootErrorCode.BootLayoutUnknown or RootErrorCode.BootLayoutConflict => "root.error.layout",
        RootErrorCode.FlashPartiallyWritten => "root.error.flash.partial",
        RootErrorCode.FlashOutcomeUnknown => "root.error.flash.unknown",
        RootErrorCode.FlashFailed => "root.error.flash",
        RootErrorCode.RebootFailed => "root.error.reboot",
        RootErrorCode.RiskNotAcknowledged => "root.error.risk",
        _ => "root.error.generic",
    };

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}

public sealed record RootDeviceOptionViewModel(RootDeviceIdentity Identity, string DisplayName, string Details);

public sealed partial class RootWizardStepItem : ObservableObject
{
    public RootWizardStepItem(int index) => Index = index;

    public int Index { get; }

    public int Number => Index + 1;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private bool _isComplete;
}
