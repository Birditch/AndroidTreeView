using AndroidTreeView.Core.Exceptions;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;
using AndroidTreeView.Models.Rooting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AndroidTreeView.Core.Tests;

public sealed class RootWizardServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AndroidTreeView-wizard-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExtractAndPatchAsync_ExtractsThenBacksUpThenPatches()
    {
        var fixture = CreateFixture();
        var states = new List<RootWizardState>();
        fixture.Wizard.Changed += (_, snapshot) => states.Add(snapshot.State);

        await fixture.Wizard.ExtractAndPatchAsync();

        Assert.Equal(["extract", "backup", "patch"], fixture.Calls.Take(3));
        Assert.Equal(RootWizardState.AwaitingBootloaderConfirm, fixture.Wizard.Snapshot.State);
        Assert.True(File.Exists(fixture.Wizard.Snapshot.BackupPath));
        Assert.Contains(RootWizardState.BootExtracted, states);
        Assert.Contains(RootWizardState.BootPatched, states);
    }

    [Fact]
    public async Task RetryAsync_AfterPatchFailureReusesExtractionAndBackup()
    {
        var fixture = CreateFixture();
        fixture.Patcher.FailuresRemaining = 1;

        await fixture.Wizard.ExtractAndPatchAsync();
        Assert.Equal(RootWizardState.Failed, fixture.Wizard.Snapshot.State);

        await fixture.Wizard.RetryAsync();

        Assert.Equal(RootWizardState.AwaitingBootloaderConfirm, fixture.Wizard.Snapshot.State);
        Assert.Equal(1, fixture.Extractor.CallCount);
        Assert.Equal(1, fixture.Backup.CallCount);
        Assert.Equal(2, fixture.Patcher.CallCount);
    }

    [Fact]
    public async Task RetryAsync_AfterExtractionFailureUsesLockedDeviceAndPackage()
    {
        var fixture = CreateFixture();
        fixture.Extractor.FailuresRemaining = 1;

        await fixture.Wizard.ExtractAndPatchAsync();
        Assert.Equal(RootWizardState.Failed, fixture.Wizard.Snapshot.State);
        await fixture.Wizard.RetryAsync();

        Assert.Equal(RootWizardState.AwaitingBootloaderConfirm, fixture.Wizard.Snapshot.State);
        Assert.Equal(2, fixture.Extractor.CallCount);
        Assert.Equal("ADB-1", fixture.Wizard.Snapshot.DeviceIdentity?.Serial);
    }

    [Fact]
    public async Task ConfirmBootloaderAsync_CapturesBaselineBeforeReboot()
    {
        var fixture = CreateFixture();
        await fixture.Wizard.ExtractAndPatchAsync();
        fixture.Calls.Clear();

        await fixture.Wizard.ConfirmBootloaderAsync();

        Assert.Equal(["baseline", "reboot-bootloader"], fixture.Calls);
        Assert.NotNull(fixture.Wizard.Snapshot.FastbootBaseline);
    }

    [Fact]
    public async Task DetectFastbootAsync_InvalidStateFailsClosedWithoutFastbootProbe()
    {
        var fixture = CreateFixture();
        await AdvanceToFlashAsync(fixture);
        var waitCalls = fixture.Fastboot.WaitCalls;
        var unlockCalls = fixture.Fastboot.UnlockCalls;
        var layoutCalls = fixture.Fastboot.LayoutCalls;

        await fixture.Wizard.DetectFastbootAsync();

        Assert.Equal(RootWizardState.Failed, fixture.Wizard.Snapshot.State);
        Assert.Equal(RootErrorCode.InvalidState, fixture.Wizard.Snapshot.ErrorCode);
        Assert.Equal(waitCalls, fixture.Fastboot.WaitCalls);
        Assert.Equal(unlockCalls, fixture.Fastboot.UnlockCalls);
        Assert.Equal(layoutCalls, fixture.Fastboot.LayoutCalls);
    }

    [Theory]
    [InlineData(RootWizardState.BlockedFastbootIdentity)]
    [InlineData(RootWizardState.BlockedLocked)]
    [InlineData(RootWizardState.BlockedBootLayout)]
    public async Task DetectFastbootAsync_ExplicitSafetyBlockCanBeRechecked(RootWizardState blockedState)
    {
        var fixture = CreateFixture();
        switch (blockedState)
        {
            case RootWizardState.BlockedFastbootIdentity:
                fixture.Fastboot.IdentityMatch = new FastbootIdentityMatch
                {
                    Status = FastbootIdentityMatchStatus.Unverified,
                };
                break;
            case RootWizardState.BlockedLocked:
                fixture.Fastboot.Unlocked = false;
                break;
            case RootWizardState.BlockedBootLayout:
                fixture.Fastboot.Layout = new FastbootBootLayout { Kind = FastbootBootLayoutKind.Unknown };
                break;
        }

        await AdvanceToDetectionAsync(fixture);
        await fixture.Wizard.DetectFastbootAsync();
        Assert.Equal(blockedState, fixture.Wizard.Snapshot.State);

        fixture.Fastboot.IdentityMatch = FakeRootFastbootService.VerifiedIdentity();
        fixture.Fastboot.Unlocked = true;
        fixture.Fastboot.Layout = new FastbootBootLayout { Kind = FastbootBootLayoutKind.Single };
        await fixture.Wizard.DetectFastbootAsync();

        Assert.Equal(RootWizardState.AwaitingFlashConfirm, fixture.Wizard.Snapshot.State);
    }

    [Fact]
    public async Task RetryAsync_AfterFastbootProbeFailureCanDetectAgain()
    {
        var fixture = CreateFixture();
        fixture.Fastboot.WaitFailuresRemaining = 1;
        await AdvanceToDetectionAsync(fixture);

        await fixture.Wizard.DetectFastbootAsync();
        Assert.Equal(RootWizardState.Failed, fixture.Wizard.Snapshot.State);

        await fixture.Wizard.RetryAsync();

        Assert.Equal(2, fixture.Fastboot.WaitCalls);
        Assert.Equal(RootWizardState.AwaitingFlashConfirm, fixture.Wizard.Snapshot.State);
    }

    [Fact]
    public async Task DetectFastbootAsync_UnverifiedIdentityHardBlocksAllLaterProbes()
    {
        var fixture = CreateFixture();
        fixture.Fastboot.IdentityMatch = new FastbootIdentityMatch
        {
            Status = FastbootIdentityMatchStatus.Unverified,
        };
        await AdvanceToDetectionAsync(fixture);

        await fixture.Wizard.DetectFastbootAsync();

        Assert.Equal(RootWizardState.BlockedFastbootIdentity, fixture.Wizard.Snapshot.State);
        Assert.Equal(0, fixture.Fastboot.UnlockCalls);
        Assert.Equal(0, fixture.Fastboot.LayoutCalls);
        Assert.Equal(0, fixture.Fastboot.FlashCalls);
    }

    [Theory]
    [InlineData(false, FastbootBootLayoutKind.Single, RootWizardState.BlockedLocked)]
    [InlineData(true, FastbootBootLayoutKind.Unknown, RootWizardState.BlockedBootLayout)]
    public async Task DetectFastbootAsync_UnlockAndLayoutAreHardGates(
        bool unlocked,
        FastbootBootLayoutKind layout,
        RootWizardState expected)
    {
        var fixture = CreateFixture();
        fixture.Fastboot.Unlocked = unlocked;
        fixture.Fastboot.Layout = new FastbootBootLayout { Kind = layout };
        await AdvanceToDetectionAsync(fixture);

        await fixture.Wizard.DetectFastbootAsync();

        Assert.Equal(expected, fixture.Wizard.Snapshot.State);
        Assert.Equal(0, fixture.Fastboot.FlashCalls);
    }

    [Fact]
    public async Task ConfirmFlashAsync_RequiresExplicitRiskAcknowledgement()
    {
        var fixture = CreateFixture();
        await AdvanceToFlashAsync(fixture);

        await fixture.Wizard.ConfirmFlashAsync(riskAcknowledged: false);

        Assert.Equal(RootWizardState.AwaitingFlashConfirm, fixture.Wizard.Snapshot.State);
        Assert.Equal(RootErrorCode.RiskNotAcknowledged, fixture.Wizard.Snapshot.ErrorCode);
        Assert.Equal(0, fixture.Fastboot.FlashCalls);
    }

    [Fact]
    public async Task RetryAsync_PartialFlashSkipsAlreadyWrittenPartition()
    {
        var fixture = CreateFixture();
        fixture.Fastboot.Layout = new FastbootBootLayout
        {
            Kind = FastbootBootLayoutKind.Ab,
            SlotCount = 2,
            TargetHasSlot = true,
        };
        fixture.Fastboot.Results.Enqueue(new FlashResult
        {
            RequestedPartitions = ["boot_a", "boot_b"],
            SucceededPartitions = ["boot_a"],
            FailedPartition = "boot_b",
            Outcome = FlashOutcome.PartiallyWritten,
            ErrorCode = RootErrorCode.FlashPartiallyWritten,
        });
        fixture.Fastboot.Results.Enqueue(new FlashResult
        {
            RequestedPartitions = ["boot_a", "boot_b"],
            SucceededPartitions = ["boot_a", "boot_b"],
            Outcome = FlashOutcome.Succeeded,
        });
        await AdvanceToFlashAsync(fixture);

        await fixture.Wizard.ConfirmFlashAsync(riskAcknowledged: true);
        Assert.Equal(RootWizardState.FailedPartialFlash, fixture.Wizard.Snapshot.State);
        await fixture.Wizard.RetryAsync();

        Assert.Equal(RootWizardState.AwaitingFlashConfirm, fixture.Wizard.Snapshot.State);
        Assert.Equal(1, fixture.Fastboot.FlashCalls);
        await fixture.Wizard.ConfirmFlashAsync(riskAcknowledged: true);

        Assert.Equal(RootWizardState.Completed, fixture.Wizard.Snapshot.State);
        Assert.Equal(["boot_a"], fixture.Fastboot.AlreadySucceeded[1]);
        Assert.True(File.Exists(fixture.Wizard.Snapshot.BackupPath));
        Assert.False(Directory.Exists(fixture.WorkDirectory));
    }

    [Fact]
    public async Task CancelAsync_DuringFlashMarksOutcomeUnknownAndPreservesBackup()
    {
        var fixture = CreateFixture();
        fixture.Fastboot.BlockFlashUntilCancellation = true;
        await AdvanceToFlashAsync(fixture);

        var flashing = fixture.Wizard.ConfirmFlashAsync(riskAcknowledged: true);
        await fixture.Fastboot.FlashStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.Wizard.CancelAsync();
        await flashing;

        Assert.Equal(RootWizardState.FlashOutcomeUnknown, fixture.Wizard.Snapshot.State);
        Assert.Equal(FlashOutcome.Unknown, fixture.Wizard.Snapshot.FlashResult?.Outcome);
        Assert.True(File.Exists(fixture.Wizard.Snapshot.BackupPath));
        Assert.False(Directory.Exists(fixture.WorkDirectory));

        await fixture.Wizard.RetryAsync();
        Assert.Equal(RootWizardState.FlashOutcomeUnknown, fixture.Wizard.Snapshot.State);
        Assert.Equal(1, fixture.Fastboot.FlashCalls);

        await fixture.Wizard.CancelAsync();
        Assert.Equal(RootWizardState.FlashOutcomeUnknown, fixture.Wizard.Snapshot.State);
    }

    [Fact]
    public async Task RetryAsync_FailedBeforeWriteRequiresFreshConfirmation()
    {
        var fixture = CreateFixture();
        fixture.Fastboot.Results.Enqueue(new FlashResult
        {
            RequestedPartitions = ["boot"],
            Outcome = FlashOutcome.FailedBeforeWrite,
            ErrorCode = RootErrorCode.FlashFailed,
        });
        fixture.Fastboot.Results.Enqueue(FakeRootFastbootService.Success());
        await AdvanceToFlashAsync(fixture);

        await fixture.Wizard.ConfirmFlashAsync(riskAcknowledged: true);
        Assert.Equal(RootWizardState.Failed, fixture.Wizard.Snapshot.State);
        Assert.Equal(RootErrorCode.FlashFailed, fixture.Wizard.Snapshot.ErrorCode);
        await fixture.Wizard.RetryAsync();

        Assert.Equal(RootWizardState.AwaitingFlashConfirm, fixture.Wizard.Snapshot.State);
        Assert.Equal(1, fixture.Fastboot.FlashCalls);
        await fixture.Wizard.ConfirmFlashAsync(riskAcknowledged: true);

        Assert.Equal(RootWizardState.Completed, fixture.Wizard.Snapshot.State);
        Assert.Equal(2, fixture.Fastboot.FlashCalls);
    }

    [Fact]
    public async Task ConfirmFlashAsync_RevalidatesIdentityImmediatelyBeforeWrite()
    {
        var fixture = CreateFixture();
        await AdvanceToFlashAsync(fixture);
        fixture.Fastboot.CurrentIdentityMatch = new FastbootIdentityMatch
        {
            Status = FastbootIdentityMatchStatus.ConflictingEvidence,
            Device = new FastbootDeviceIdentity { Serial = "FASTBOOT-1", Product = "other" },
        };

        await fixture.Wizard.ConfirmFlashAsync(riskAcknowledged: true);

        Assert.Equal(RootWizardState.BlockedFastbootIdentity, fixture.Wizard.Snapshot.State);
        Assert.Equal(RootErrorCode.FastbootIdentityConflict, fixture.Wizard.Snapshot.ErrorCode);
        Assert.Equal(1, fixture.Fastboot.VerifyIdentityCalls);
        Assert.Equal(0, fixture.Fastboot.FlashCalls);
    }

    [Fact]
    public async Task ConfirmFlashAsync_BlocksWhenUnlockStateChanges()
    {
        var fixture = CreateFixture();
        await AdvanceToFlashAsync(fixture);
        fixture.Fastboot.Unlocked = false;

        await fixture.Wizard.ConfirmFlashAsync(riskAcknowledged: true);

        Assert.Equal(RootWizardState.BlockedLocked, fixture.Wizard.Snapshot.State);
        Assert.Equal(0, fixture.Fastboot.FlashCalls);
    }

    [Fact]
    public async Task ConfirmFlashAsync_BlocksAndPublishesChangedLayout()
    {
        var fixture = CreateFixture();
        await AdvanceToFlashAsync(fixture);
        var changedLayout = new FastbootBootLayout
        {
            Kind = FastbootBootLayoutKind.Ab,
            SlotCount = 2,
            TargetHasSlot = true,
        };
        fixture.Fastboot.Layout = changedLayout;

        await fixture.Wizard.ConfirmFlashAsync(riskAcknowledged: true);

        Assert.Equal(RootWizardState.BlockedBootLayout, fixture.Wizard.Snapshot.State);
        Assert.Equal(RootErrorCode.BootLayoutConflict, fixture.Wizard.Snapshot.ErrorCode);
        Assert.Equal(changedLayout, fixture.Wizard.Snapshot.BootLayout);
        Assert.Equal(0, fixture.Fastboot.FlashCalls);
    }

    [Fact]
    public async Task SelectionAfterSuccessfulFlashAndFailedRebootRequiresCancel()
    {
        var fixture = CreateFixture();
        fixture.Fastboot.RebootFailuresRemaining = 1;
        await AdvanceToFlashAsync(fixture);

        await fixture.Wizard.ConfirmFlashAsync(riskAcknowledged: true);
        Assert.Equal(RootWizardState.Failed, fixture.Wizard.Snapshot.State);
        Assert.Equal(FlashOutcome.Succeeded, fixture.Wizard.Snapshot.FlashResult?.Outcome);

        var exception = Assert.Throws<RootWorkflowException>(() =>
            fixture.Wizard.SelectDevice(new RootDeviceIdentity { Serial = "ADB-2" }));
        Assert.Equal(RootErrorCode.InvalidState, exception.ErrorCode);
        Assert.Equal("ADB-1", fixture.Wizard.Snapshot.DeviceIdentity?.Serial);

        await fixture.Wizard.CancelAsync();
        fixture.Wizard.SelectDevice(new RootDeviceIdentity { Serial = "ADB-2" });
        Assert.Equal("ADB-2", fixture.Wizard.Snapshot.DeviceIdentity?.Serial);
    }

    [Fact]
    public async Task CancelAsync_DuringRebootPreservesSuccessfulFlashForRebootRetry()
    {
        var fixture = CreateFixture();
        fixture.Fastboot.BlockRebootUntilCancellation = true;
        await AdvanceToFlashAsync(fixture);

        var flashing = fixture.Wizard.ConfirmFlashAsync(riskAcknowledged: true);
        await fixture.Fastboot.RebootStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.Wizard.CancelAsync();
        await flashing;

        Assert.Equal(RootWizardState.Failed, fixture.Wizard.Snapshot.State);
        Assert.Equal(RootErrorCode.RebootFailed, fixture.Wizard.Snapshot.ErrorCode);
        Assert.Equal(FlashOutcome.Succeeded, fixture.Wizard.Snapshot.FlashResult?.Outcome);
        Assert.True(Directory.Exists(fixture.WorkDirectory));

        fixture.Fastboot.BlockRebootUntilCancellation = false;
        await fixture.Wizard.RetryAsync();
        Assert.Equal(RootWizardState.Completed, fixture.Wizard.Snapshot.State);
        Assert.False(Directory.Exists(fixture.WorkDirectory));
        Assert.Equal(1, fixture.Fastboot.FlashCalls);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup for transient test runner file locks.
        }
    }

    private Fixture CreateFixture()
    {
        var calls = new List<string>();
        var work = Path.Combine(_root, "work");
        var backups = Path.Combine(_root, "backups");
        Directory.CreateDirectory(work);
        var original = Path.Combine(work, "boot.img");
        File.WriteAllBytes(original, [1, 2, 3]);
        var image = new BootImageInfo
        {
            Path = original,
            WorkDirectory = work,
            OriginalPackageName = "firmware.zip",
            TargetPartition = BootPartitionTarget.Boot,
            Source = BootImageSource.PlainZip,
        };
        var extractor = new FakeExtractor(image, calls);
        var backup = new FakeBackup(backups, calls);
        var patcher = new FakePatcher(work, calls);
        var fastboot = new FakeRootFastbootService(calls);
        var wizard = new RootWizardService(
            extractor,
            backup,
            patcher,
            fastboot,
            NullLogger<RootWizardService>.Instance);
        wizard.SelectDevice(new RootDeviceIdentity { Serial = "ADB-1", Product = "pixel" });
        wizard.SelectPackage(Path.Combine(_root, "firmware.zip"));
        return new Fixture(wizard, extractor, backup, patcher, fastboot, calls, work);
    }

    private static async Task AdvanceToDetectionAsync(Fixture fixture)
    {
        await fixture.Wizard.ExtractAndPatchAsync();
        await fixture.Wizard.ConfirmBootloaderAsync();
    }

    private static async Task AdvanceToFlashAsync(Fixture fixture)
    {
        await AdvanceToDetectionAsync(fixture);
        await fixture.Wizard.DetectFastbootAsync();
        Assert.Equal(RootWizardState.AwaitingFlashConfirm, fixture.Wizard.Snapshot.State);
    }

    private sealed record Fixture(
        RootWizardService Wizard,
        FakeExtractor Extractor,
        FakeBackup Backup,
        FakePatcher Patcher,
        FakeRootFastbootService Fastboot,
        List<string> Calls,
        string WorkDirectory);

    private sealed class FakeExtractor(BootImageInfo image, List<string> calls) : IBootImageExtractor
    {
        public int CallCount { get; private set; }
        public int FailuresRemaining { get; set; }

        public Task<BootImageInfo> ExtractAsync(
            string packagePath,
            RootDeviceIdentity device,
            CancellationToken ct = default)
        {
            CallCount++;
            calls.Add("extract");
            if (FailuresRemaining-- > 0)
            {
                throw new InvalidOperationException("extract failed");
            }

            return Task.FromResult(image);
        }
    }

    private sealed class FakeBackup(string directory, List<string> calls) : IBootBackupService
    {
        public int CallCount { get; private set; }

        public async Task<string> BackupAsync(
            string sourcePath,
            string serial,
            BootPartitionTarget target,
            CancellationToken ct = default)
        {
            CallCount++;
            calls.Add("backup");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"backup-{CallCount}.img");
            await File.WriteAllBytesAsync(path, [1, 2, 3], ct);
            return path;
        }
    }

    private sealed class FakePatcher(string workDirectory, List<string> calls) : IMagiskPatcher
    {
        public int CallCount { get; private set; }
        public int FailuresRemaining { get; set; }

        public async Task<string> PatchAsync(
            string serial,
            BootImageInfo bootImage,
            CancellationToken ct = default)
        {
            CallCount++;
            calls.Add("patch");
            if (FailuresRemaining-- > 0)
            {
                throw new InvalidOperationException("patch failed");
            }

            var path = Path.Combine(workDirectory, "patched.img");
            await File.WriteAllBytesAsync(path, [4, 5, 6], ct);
            return path;
        }
    }

    private sealed class FakeRootFastbootService(List<string> calls) : IRootFastbootService
    {
        public string? ExecutablePath => "/tools/fastboot";
        public bool Unlocked { get; set; } = true;
        public FastbootBootLayout Layout { get; set; } = new() { Kind = FastbootBootLayoutKind.Single };
        public FastbootIdentityMatch IdentityMatch { get; set; } = VerifiedIdentity();
        public FastbootIdentityMatch? CurrentIdentityMatch { get; set; }
        public Queue<FlashResult> Results { get; } = new();
        public List<IReadOnlyCollection<string>> AlreadySucceeded { get; } = [];
        public int UnlockCalls { get; private set; }
        public int LayoutCalls { get; private set; }
        public int FlashCalls { get; private set; }
        public int VerifyIdentityCalls { get; private set; }
        public int WaitCalls { get; private set; }
        public int WaitFailuresRemaining { get; set; }
        public int RebootFailuresRemaining { get; set; }
        public bool BlockFlashUntilCancellation { get; set; }
        public bool BlockRebootUntilCancellation { get; set; }
        public TaskCompletionSource<bool> FlashStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> RebootStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<FastbootBaseline> CaptureBaselineAsync(CancellationToken ct = default)
        {
            calls.Add("baseline");
            return Task.FromResult(new FastbootBaseline());
        }

        public Task RebootToBootloaderAsync(string adbSerial, CancellationToken ct = default)
        {
            calls.Add("reboot-bootloader");
            return Task.CompletedTask;
        }

        public Task<FastbootIdentityMatch> WaitForMatchingDeviceAsync(
            RootDeviceIdentity device,
            FastbootBaseline baseline,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            WaitCalls++;
            if (WaitFailuresRemaining-- > 0)
            {
                throw new InvalidOperationException("fastboot probe failed");
            }

            return Task.FromResult(IdentityMatch);
        }

        public Task<FastbootIdentityMatch> VerifyCurrentIdentityAsync(
            RootDeviceIdentity device,
            string expectedSerial,
            CancellationToken ct = default)
        {
            VerifyIdentityCalls++;
            return Task.FromResult(CurrentIdentityMatch ?? IdentityMatch);
        }

        public Task<bool> IsBootloaderUnlockedAsync(string fastbootSerial, CancellationToken ct = default)
        {
            UnlockCalls++;
            return Task.FromResult(Unlocked);
        }

        public Task<FastbootBootLayout> GetBootLayoutAsync(
            string fastbootSerial,
            BootPartitionTarget target,
            CancellationToken ct = default)
        {
            LayoutCalls++;
            return Task.FromResult(Layout);
        }

        public async Task<FlashResult> FlashAsync(
            string fastbootSerial,
            BootPartitionTarget target,
            string imagePath,
            FastbootBootLayout layout,
            IReadOnlyCollection<string>? alreadySucceededPartitions = null,
            CancellationToken ct = default)
        {
            FlashCalls++;
            AlreadySucceeded.Add(alreadySucceededPartitions?.ToArray() ?? []);
            FlashStarted.TrySetResult(true);
            if (BlockFlashUntilCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }

            return Results.TryDequeue(out var result) ? result : Success();
        }

        public async Task RebootAsync(string fastbootSerial, CancellationToken ct = default)
        {
            RebootStarted.TrySetResult(true);
            if (BlockRebootUntilCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }

            if (RebootFailuresRemaining-- > 0)
            {
                throw new InvalidOperationException("reboot failed");
            }
        }

        public static FlashResult Success() => new()
        {
            RequestedPartitions = ["boot"],
            SucceededPartitions = ["boot"],
            Outcome = FlashOutcome.Succeeded,
        };

        public static FastbootIdentityMatch VerifiedIdentity() => new()
        {
            Status = FastbootIdentityMatchStatus.Verified,
            Device = new FastbootDeviceIdentity { Serial = "FASTBOOT-1", Product = "pixel" },
            Evidence = FastbootIdentityEvidence.Product,
        };
    }
}
