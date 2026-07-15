using AndroidTreeView.App.ViewModels;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Models.Devices;
using AndroidTreeView.Models.Rooting;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AndroidTreeView.App.Tests;

public sealed class RootWizardViewModelTests
{
    [AvaloniaFact]
    public void Repeated_polls_with_an_unchanged_device_list_do_not_rebuild_the_collection()
    {
        var context = CreateContext();
        context.Monitor.Publish(Device("one"), Device("two"));

        var changes = 0;
        context.ViewModel.Devices.CollectionChanged += (_, _) => changes++;
        context.Monitor.Publish(Device("one"), Device("two"));
        context.Monitor.Publish(Device("one"), Device("two"));

        // A rebuild would clear the ListBox selection and yank the surrounding ScrollViewer
        // back to the device card on every poll tick.
        Assert.Equal(0, changes);
        Assert.Equal(2, context.ViewModel.Devices.Count);
    }

    [AvaloniaFact]
    public void Repeated_polls_keep_the_selection_instance_stable()
    {
        var context = CreateContext();
        context.Monitor.Publish(Device("only", product: "akita"));
        var selected = context.ViewModel.SelectedDevice;

        var selectionChanges = 0;
        context.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RootWizardViewModel.SelectedDevice))
            {
                selectionChanges++;
            }
        };
        context.Monitor.Publish(Device("only", product: "akita"));

        Assert.Equal(0, selectionChanges);
        Assert.Same(selected, context.ViewModel.SelectedDevice);
    }

    [AvaloniaFact]
    public void A_changed_device_list_still_rebuilds_the_collection()
    {
        var context = CreateContext();
        context.Monitor.Publish(Device("one"), Device("two"));

        context.Monitor.Publish(Device("one"), Device("three"));

        Assert.Equal(2, context.ViewModel.Devices.Count);
        Assert.Contains(context.ViewModel.Devices, device => device.Identity.Serial == "three");
        Assert.DoesNotContain(context.ViewModel.Devices, device => device.Identity.Serial == "two");
    }

    [AvaloniaFact]
    public void Multiple_online_devices_are_not_preselected()
    {
        var context = CreateContext();

        context.Monitor.Publish(Device("one"), Device("two"));

        Assert.Equal(2, context.ViewModel.Devices.Count);
        Assert.Null(context.ViewModel.SelectedDevice);
        Assert.False(context.ViewModel.CanStart);
        Assert.Equal("English:root.device.choose", context.ViewModel.DeviceListMessage);
    }

    [AvaloniaFact]
    public void One_online_device_is_preselected_and_sent_to_the_wizard()
    {
        var context = CreateContext();

        context.Monitor.Publish(Device("only", product: "akita"));

        Assert.Equal("only", context.ViewModel.SelectedDevice?.Identity.Serial);
        Assert.Equal("only", context.Wizard.Snapshot.DeviceIdentity?.Serial);
        Assert.Equal(1, context.Wizard.SelectDeviceCallCount);
    }

    [AvaloniaFact]
    public void Offline_unauthorized_and_fastboot_devices_are_filtered_out()
    {
        var context = CreateContext();

        context.Monitor.Publish(
            Device("online"),
            Device("offline", DeviceConnectionState.Offline),
            Device("unauthorized", DeviceConnectionState.Unauthorized),
            Device("fastboot", DeviceConnectionState.Bootloader));

        var selected = Assert.Single(context.ViewModel.Devices);
        Assert.Equal("online", selected.Identity.Serial);
    }

    [AvaloniaFact]
    public void Workflow_locks_the_selected_device_identity()
    {
        var context = CreateContext();
        context.Monitor.Publish(Device("one"), Device("two"));
        context.ViewModel.SelectedDevice = context.ViewModel.Devices[0];
        var callsBeforeLock = context.Wizard.SelectDeviceCallCount;
        context.Wizard.Publish(FlashReadySnapshot(RootWizardState.AwaitingBootloaderConfirm) with
        {
            DeviceIdentity = Identity("one")
        });

        context.ViewModel.SelectedDevice = context.ViewModel.Devices[1];

        Assert.True(context.ViewModel.IsSelectionLocked);
        Assert.False(context.ViewModel.IsDeviceSelectionEnabled);
        Assert.Equal(callsBeforeLock, context.Wizard.SelectDeviceCallCount);
        Assert.Equal("one", context.Wizard.Snapshot.DeviceIdentity?.Serial);
    }

    [AvaloniaFact]
    public void Locked_fastboot_target_is_not_replaced_by_another_online_adb_device()
    {
        var target = new RootDeviceIdentity
        {
            Serial = "fastboot-target",
            Product = "cannon",
            Model = "Redmi Note 9"
        };
        var context = CreateContext(FlashReadySnapshot(RootWizardState.BlockedFastbootIdentity) with
        {
            DeviceIdentity = target,
            FastbootIdentityMatch = new FastbootIdentityMatch
            {
                Status = FastbootIdentityMatchStatus.Unverified
            }
        });

        context.Monitor.Publish(Device("other-adb-device", product: "cannon"));

        Assert.Equal("fastboot-target", context.ViewModel.SelectedDevice?.Identity.Serial);
        Assert.Contains(context.ViewModel.Devices, device => device.Identity.Serial == "other-adb-device");
        Assert.Contains(context.ViewModel.Devices, device => device.Identity.Serial == "fastboot-target");
        Assert.Equal(0, context.Wizard.SelectDeviceCallCount);
    }

    [AvaloniaTheory]
    [InlineData(RootWizardState.Failed)]
    [InlineData(RootWizardState.BlockedUnsupportedTarget)]
    public void Failed_or_blocked_session_keeps_its_target_locked_until_cancel(RootWizardState state)
    {
        var context = CreateContext(FlashReadySnapshot(state) with
        {
            ErrorCode = RootErrorCode.TargetEvidenceConflict
        });

        Assert.True(context.ViewModel.IsSelectionLocked);
        Assert.False(context.ViewModel.IsDeviceSelectionEnabled);
        Assert.False(context.ViewModel.PickPackageCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Cancelling_the_file_picker_keeps_the_current_snapshot()
    {
        var context = CreateContext();
        context.Picker.RootPackagePath = null;

        await context.ViewModel.PickPackageCommand.ExecuteAsync(null);

        Assert.Equal(0, context.Wizard.SelectPackageCallCount);
        Assert.Equal(RootWizardState.Idle, context.ViewModel.State);
        Assert.Null(context.ViewModel.SelectedPackagePath);
    }

    [AvaloniaFact]
    public async Task Bootloader_confirmation_is_a_hard_gate()
    {
        var declined = CreateContext(FlashReadySnapshot(RootWizardState.AwaitingBootloaderConfirm));
        declined.Dialog.ConfirmResult = false;

        await declined.ViewModel.ConfirmBootloaderCommand.ExecuteAsync(null);

        Assert.Equal(0, declined.Wizard.ConfirmBootloaderCallCount);
        Assert.Equal(0, declined.Wizard.DetectFastbootCallCount);

        var accepted = CreateContext(FlashReadySnapshot(RootWizardState.AwaitingBootloaderConfirm));
        accepted.Dialog.ConfirmResult = true;

        await accepted.ViewModel.ConfirmBootloaderCommand.ExecuteAsync(null);

        Assert.Equal(1, accepted.Wizard.ConfirmBootloaderCallCount);
        Assert.Equal(1, accepted.Wizard.DetectFastbootCallCount);
    }

    [AvaloniaFact]
    public async Task Bootloader_reboot_failure_does_not_start_fastboot_detection()
    {
        var context = CreateContext(FlashReadySnapshot(RootWizardState.AwaitingBootloaderConfirm));
        context.Dialog.ConfirmResult = true;
        context.Wizard.ConfirmBootloaderResultState = RootWizardState.Failed;

        await context.ViewModel.ConfirmBootloaderCommand.ExecuteAsync(null);

        Assert.Equal(1, context.Wizard.ConfirmBootloaderCallCount);
        Assert.Equal(0, context.Wizard.DetectFastbootCallCount);
        Assert.Equal(RootWizardState.Failed, context.ViewModel.State);
    }

    [AvaloniaFact]
    public async Task Flash_requires_explicit_risk_acknowledgement()
    {
        var context = CreateContext(FlashReadySnapshot(RootWizardState.AwaitingFlashConfirm));

        Assert.False(context.ViewModel.FlashCommand.CanExecute(null));
        context.ViewModel.HasAcknowledgedRisk = true;
        Assert.True(context.ViewModel.FlashCommand.CanExecute(null));

        await context.ViewModel.FlashCommand.ExecuteAsync(null);

        Assert.Equal(1, context.Wizard.ConfirmFlashCallCount);
        Assert.True(context.Wizard.LastRiskAcknowledged);
    }

    [AvaloniaTheory]
    [InlineData(RootWizardState.BlockedFastbootIdentity, RootErrorCode.FastbootIdentityUnverified)]
    [InlineData(RootWizardState.BlockedLocked, RootErrorCode.BootloaderLocked)]
    [InlineData(RootWizardState.BlockedBootLayout, RootErrorCode.BootLayoutUnknown)]
    public void Safety_blocks_disable_flash_and_allow_recheck(RootWizardState state, RootErrorCode errorCode)
    {
        var context = CreateContext(FlashReadySnapshot(state) with { ErrorCode = errorCode });
        context.ViewModel.HasAcknowledgedRisk = true;

        Assert.False(context.ViewModel.FlashCommand.CanExecute(null));
        Assert.True(context.ViewModel.RecheckFastbootCommand.CanExecute(null));
        Assert.True(context.ViewModel.ShowBlockedGuidance);
        Assert.True(context.ViewModel.HasError);
    }

    [AvaloniaTheory]
    [InlineData(RootWizardState.FailedPartialFlash, RootErrorCode.FlashPartiallyWritten)]
    [InlineData(RootWizardState.FlashOutcomeUnknown, RootErrorCode.FlashOutcomeUnknown)]
    public void Partial_and_unknown_flash_outcomes_are_shown_as_dangerous(
        RootWizardState state,
        RootErrorCode errorCode)
    {
        var context = CreateContext(FlashReadySnapshot(state) with { ErrorCode = errorCode });

        Assert.True(context.ViewModel.ShowPartialFlashWarning);
        Assert.True(context.ViewModel.HasError);
        Assert.False(context.ViewModel.FlashCommand.CanExecute(null));
        Assert.Equal(state != RootWizardState.FlashOutcomeUnknown, context.ViewModel.CancelCommand.CanExecute(null));

        context.ViewModel.HasAcknowledgedRisk = true;
        Assert.Equal(
            state == RootWizardState.FailedPartialFlash,
            context.ViewModel.FlashCommand.CanExecute(null));
    }

    [AvaloniaTheory]
    [InlineData(RootErrorCode.FlashFailed)]
    [InlineData(RootErrorCode.RebootFailed)]
    public async Task Failed_flash_or_reboot_can_retry(RootErrorCode errorCode)
    {
        var context = CreateContext(FlashReadySnapshot(RootWizardState.Failed) with { ErrorCode = errorCode });

        Assert.True(context.ViewModel.RetryCommand.CanExecute(null));
        await context.ViewModel.RetryCommand.ExecuteAsync(null);
        Assert.Equal(1, context.Wizard.RetryCallCount);
    }

    [AvaloniaFact]
    public async Task Cancel_stops_the_active_wizard_session()
    {
        var context = CreateContext(FlashReadySnapshot(RootWizardState.AwaitingBootloaderConfirm));

        await context.ViewModel.CancelCommand.ExecuteAsync(null);

        Assert.Equal(1, context.Wizard.CancelCallCount);
        Assert.Equal(RootWizardState.Cancelled, context.ViewModel.State);
    }

    [AvaloniaFact]
    public void Language_change_recomputes_state_device_and_step_text()
    {
        var context = CreateContext(FlashReadySnapshot(RootWizardState.AwaitingFlashConfirm));
        context.Monitor.Publish(Device("one"));
        var englishState = context.ViewModel.StateText;
        var englishStep = context.ViewModel.Steps[4].Title;

        context.Localization.SetLanguage(AppLanguage.ChineseSimplified);

        Assert.NotEqual(englishState, context.ViewModel.StateText);
        Assert.Equal("ChineseSimplified:root.state.awaitingflashconfirm", context.ViewModel.StateText);
        Assert.NotEqual(englishStep, context.ViewModel.Steps[4].Title);
        Assert.StartsWith("ChineseSimplified:", context.ViewModel.Devices[0].Details);
    }

    [AvaloniaTheory]
    [InlineData(null, false)]
    [InlineData(FirmwarePackageMatchStatus.Unverified, true)]
    [InlineData(FirmwarePackageMatchStatus.Matched, false)]
    public void Package_match_warning_is_only_visible_for_unverified_metadata(
        FirmwarePackageMatchStatus? status,
        bool expected)
    {
        var metadata = status is null ? null : new FirmwarePackageMetadata
        {
            PackagePath = "/firmware/device.zip",
            OriginalPackageName = "device.zip",
            MatchStatus = status.Value
        };
        var context = CreateContext(FlashReadySnapshot(RootWizardState.AwaitingFlashConfirm) with
        {
            PackageMetadata = metadata
        });

        Assert.Equal(expected, context.ViewModel.IsPackageMatchUnverified);
    }

    [AvaloniaFact]
    public void Recovery_only_error_maps_to_specific_localized_guidance()
    {
        var context = CreateContext(FlashReadySnapshot(RootWizardState.BlockedUnsupportedTarget) with
        {
            ErrorCode = RootErrorCode.RecoveryOnlyUnsupported
        });

        Assert.Equal("English:root.error.recoveryonly", context.ViewModel.ErrorMessage);
    }

    private static TestContext CreateContext(RootWizardSnapshot? snapshot = null)
    {
        var wizard = new FakeRootWizardService(snapshot ?? new RootWizardSnapshot());
        var monitor = new FakeRootDeviceMonitor();
        var picker = new FakeFilePickerService();
        var dialog = new ConfigurableDialogService();
        var localization = new LanguageAwareLocalizationService();
        var viewModel = new RootWizardViewModel(
            wizard,
            monitor,
            picker,
            dialog,
            localization,
            NullLogger<RootWizardViewModel>.Instance);
        return new TestContext(viewModel, wizard, monitor, picker, dialog, localization);
    }

    private static RootWizardSnapshot FlashReadySnapshot(RootWizardState state) => new()
    {
        State = state,
        DeviceIdentity = Identity("device-1"),
        PackagePath = "/firmware/device.zip",
        BootImage = new BootImageInfo
        {
            Path = "/work/boot.img",
            WorkDirectory = "/work",
            OriginalPackageName = "device.zip",
            TargetPartition = BootPartitionTarget.Boot,
            Source = BootImageSource.PlainZip
        },
        PatchedImagePath = "/work/boot-patched.img",
        BackupPath = "/backups/boot-original.img",
        FastbootIdentityMatch = new FastbootIdentityMatch
        {
            Status = FastbootIdentityMatchStatus.Verified,
            Device = new FastbootDeviceIdentity { Serial = "device-1" },
            Evidence = FastbootIdentityEvidence.Serial
        },
        BootLayout = new FastbootBootLayout
        {
            Kind = FastbootBootLayoutKind.Single,
            TargetHasSlot = false
        }
    };

    private static RootDeviceIdentity Identity(string serial) => new() { Serial = serial };

    private static AdbDevice Device(
        string serial,
        DeviceConnectionState state = DeviceConnectionState.Online,
        string? product = null) => new()
        {
            Serial = serial,
            State = state,
            Product = product,
            Device = product,
            Model = serial
        };

    private sealed record TestContext(
        RootWizardViewModel ViewModel,
        FakeRootWizardService Wizard,
        FakeRootDeviceMonitor Monitor,
        FakeFilePickerService Picker,
        ConfigurableDialogService Dialog,
        LanguageAwareLocalizationService Localization);

    private sealed class FakeRootWizardService : IRootWizardService
    {
        public FakeRootWizardService(RootWizardSnapshot snapshot) => Snapshot = snapshot;

        public RootWizardSnapshot Snapshot { get; private set; }

        public int SelectDeviceCallCount { get; private set; }
        public int SelectPackageCallCount { get; private set; }
        public int ConfirmBootloaderCallCount { get; private set; }
        public int DetectFastbootCallCount { get; private set; }
        public int ConfirmFlashCallCount { get; private set; }
        public int RetryCallCount { get; private set; }
        public int CancelCallCount { get; private set; }
        public bool LastRiskAcknowledged { get; private set; }
        public RootWizardState ConfirmBootloaderResultState { get; set; } =
            RootWizardState.RebootingToBootloader;

        public event EventHandler<RootWizardSnapshot>? Changed;

        public void Publish(RootWizardSnapshot snapshot)
        {
            Snapshot = snapshot;
            Changed?.Invoke(this, snapshot);
        }

        public void SelectDevice(RootDeviceIdentity device)
        {
            SelectDeviceCallCount++;
            Snapshot = Snapshot with { DeviceIdentity = device };
        }

        public void SelectPackage(string packagePath)
        {
            SelectPackageCallCount++;
            Publish(Snapshot with { State = RootWizardState.PackageSelected, PackagePath = packagePath });
        }

        public Task ExtractAndPatchAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ConfirmBootloaderAsync(CancellationToken ct = default)
        {
            ConfirmBootloaderCallCount++;
            Publish(Snapshot with { State = ConfirmBootloaderResultState });
            return Task.CompletedTask;
        }

        public Task DetectFastbootAsync(CancellationToken ct = default)
        {
            DetectFastbootCallCount++;
            return Task.CompletedTask;
        }

        public Task ConfirmFlashAsync(bool riskAcknowledged, CancellationToken ct = default)
        {
            ConfirmFlashCallCount++;
            LastRiskAcknowledged = riskAcknowledged;
            return Task.CompletedTask;
        }

        public Task RetryAsync(CancellationToken ct = default)
        {
            RetryCallCount++;
            return Task.CompletedTask;
        }

        public Task CancelAsync()
        {
            CancelCallCount++;
            Publish(Snapshot with { State = RootWizardState.Cancelled, ErrorCode = RootErrorCode.OperationCancelled });
            return Task.CompletedTask;
        }
    }

}
