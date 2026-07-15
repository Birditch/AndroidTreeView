using AndroidTreeView.Adb.Services;
using AndroidTreeView.Adb.Tests.TestDoubles;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;
using AndroidTreeView.Models.Rooting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Services;

public sealed class RootFastbootServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"atv-root-fastboot-{Guid.NewGuid():N}");
    private readonly string _imagePath;

    public RootFastbootServiceTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, OperatingSystem.IsWindows() ? "fastboot.exe" : "fastboot"), "test");
        _imagePath = Path.Combine(_directory, "patched.img");
        File.WriteAllBytes(_imagePath, new byte[] { 1, 2, 3 });
    }

    [Fact]
    public async Task WaitForMatchingDevice_SerialChanged_VerifiesUsbAndProduct()
    {
        var runner = new RootTestExternalCommandRunner
        {
            Handler = static (request, _) => Task.FromResult(request.Arguments.Contains("devices")
                ? new ExternalCommandResult { StandardOutput = "FB fastboot usb:1-2\n" }
                : new ExternalCommandResult { StandardError = "(bootloader) product: oriole" })
        };
        var service = Build(runner);

        var match = await service.WaitForMatchingDeviceAsync(
            new RootDeviceIdentity { Serial = "ADB", UsbPath = "1-2", Product = "oriole" },
            new FastbootBaseline(),
            TimeSpan.FromSeconds(1));

        Assert.True(match.IsVerified);
        Assert.All(runner.Requests.Where(request => request.Arguments.Contains("getvar")),
            request => Assert.Equal(new[] { "-s", "FB" }, request.Arguments.Take(2)));
    }

    [Fact]
    public async Task WaitForMatchingDevice_StableSerial_VerifiesWhenProductNameChanges()
    {
        var runner = new RootTestExternalCommandRunner
        {
            Handler = static (request, _) => Task.FromResult(request.Arguments.Contains("devices")
                ? new ExternalCommandResult { StandardOutput = "SERIAL fastboot\n" }
                : new ExternalCommandResult { StandardError = "(bootloader) product: bootloader-product" })
        };
        var service = Build(runner);

        var match = await service.WaitForMatchingDeviceAsync(
            new RootDeviceIdentity { Serial = "SERIAL", Product = "adb_product" },
            new FastbootBaseline(),
            TimeSpan.FromSeconds(1));

        Assert.True(match.IsVerified);
        Assert.Equal(FastbootIdentityEvidence.Serial, match.Evidence);
    }

    [Fact]
    public async Task FlashAsync_SecondSlotCancelledBeforeStart_ReturnsPartialResult()
    {
        using var cts = new CancellationTokenSource();
        var runner = new RootTestExternalCommandRunner
        {
            Handler = (_, _) =>
            {
                cts.Cancel();
                return Task.FromResult(new ExternalCommandResult());
            }
        };
        var service = Build(runner);

        var result = await service.FlashAsync(
            "FB",
            BootPartitionTarget.Boot,
            _imagePath,
            new FastbootBootLayout { Kind = FastbootBootLayoutKind.Ab, SlotCount = 2, TargetHasSlot = true, CurrentSlot = "a" },
            ct: cts.Token);

        Assert.Equal(FlashOutcome.PartiallyWritten, result.Outcome);
        Assert.Equal(RootErrorCode.FlashPartiallyWritten, result.ErrorCode);
        Assert.Equal(new[] { "boot_a" }, result.SucceededPartitions);
        Assert.Equal("boot_b", result.FailedPartition);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task FlashAsync_SecondSlotFails_ReturnsUnknownAndPreservesConfirmedFirstSlot()
    {
        var call = 0;
        var runner = new RootTestExternalCommandRunner
        {
            Handler = (_, _) => Task.FromResult(++call == 1
                ? new ExternalCommandResult()
                : new ExternalCommandResult { ExitCode = 1, StandardError = "FAILED remote failure" })
        };
        var service = Build(runner);

        var result = await service.FlashAsync(
            "FB",
            BootPartitionTarget.InitBoot,
            _imagePath,
            new FastbootBootLayout { Kind = FastbootBootLayoutKind.Ab, SlotCount = 2, TargetHasSlot = true, CurrentSlot = "b" });

        Assert.Equal(FlashOutcome.Unknown, result.Outcome);
        Assert.Equal(RootErrorCode.FlashOutcomeUnknown, result.ErrorCode);
        Assert.Equal(new[] { "init_boot_a" }, result.SucceededPartitions);
        Assert.Equal("init_boot_b", result.FailedPartition);
        Assert.Equal(2, runner.Requests.Count);
        Assert.All(runner.Requests, request => Assert.Equal(new[] { "-s", "FB" }, request.Arguments.Take(2)));
    }

    [Fact]
    public async Task FlashAsync_SingleSlotLayout_WritesTargetPartition()
    {
        var runner = new RootTestExternalCommandRunner();
        var service = Build(runner);

        var result = await service.FlashAsync(
            "FB",
            BootPartitionTarget.Boot,
            _imagePath,
            new FastbootBootLayout { Kind = FastbootBootLayoutKind.Single, TargetHasSlot = false });

        Assert.Equal(FlashOutcome.Succeeded, result.Outcome);
        Assert.Equal(RootErrorCode.None, result.ErrorCode);
        Assert.Equal(new[] { "boot" }, result.SucceededPartitions);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task VerifyCurrentIdentity_OnlyExpectedSerialWithUsbAndProduct_IsVerified()
    {
        var runner = new RootTestExternalCommandRunner
        {
            Handler = static (request, _) => Task.FromResult(request.Arguments.Contains("devices")
                ? new ExternalCommandResult
                {
                    StandardOutput = "OTHER fastboot usb:9-9 product:raven\nFB fastboot usb:1-2\n"
                }
                : new ExternalCommandResult { StandardError = "(bootloader) product: oriole" })
        };
        var service = Build(runner);

        var result = await service.VerifyCurrentIdentityAsync(
            new RootDeviceIdentity { Serial = "ADB", UsbPath = "1-2", Product = "oriole" },
            "FB");

        Assert.True(result.IsVerified);
        Assert.Equal("FB", result.Device!.Serial);
        Assert.DoesNotContain(runner.Requests,
            request => request.Arguments.Contains("getvar") && request.Arguments.Contains("OTHER"));
    }

    [Fact]
    public async Task VerifyCurrentIdentity_UniqueExpectedSerialWithoutEvidence_RemainsUnverified()
    {
        var runner = new RootTestExternalCommandRunner
        {
            Handler = static (request, _) => Task.FromResult(request.Arguments.Contains("devices")
                ? new ExternalCommandResult { StandardOutput = "FB fastboot\n" }
                : new ExternalCommandResult { ExitCode = 1 })
        };
        var service = Build(runner);

        var result = await service.VerifyCurrentIdentityAsync(
            new RootDeviceIdentity { Serial = "ADB" },
            "FB");

        Assert.Equal(FastbootIdentityMatchStatus.Unverified, result.Status);
        Assert.False(result.IsVerified);
    }

    [Fact]
    public async Task GetBootLayout_HasSlotNo_IsSingleWhenSlotVariablesAreUnsupported()
    {
        var runner = new RootTestExternalCommandRunner
        {
            Handler = static (request, _) => Task.FromResult(request.Arguments[^1] == "has-slot:boot"
                ? new ExternalCommandResult { StandardError = "(bootloader) has-slot:boot: no" }
                : new ExternalCommandResult { ExitCode = 1, StandardError = "unknown variable" })
        };
        var service = Build(runner);

        var layout = await service.GetBootLayoutAsync("FB", BootPartitionTarget.Boot);

        Assert.Equal(FastbootBootLayoutKind.Single, layout.Kind);
        Assert.False(layout.TargetHasSlot);
        Assert.All(runner.Requests, request => Assert.Equal(new[] { "-s", "FB" }, request.Arguments.Take(2)));
    }

    [Fact]
    public async Task GetBootLayout_LegacyBootloaderReportingZeroSlots_IsSingle()
    {
        // Verified against a Xiaomi "cannon" bootloader: slot-count answers 0, while has-slot and
        // current-slot fail with "GetVar Variable Not found".
        var runner = new RootTestExternalCommandRunner
        {
            Handler = static (request, _) => Task.FromResult(request.Arguments[^1] == "slot-count"
                ? new ExternalCommandResult { StandardOutput = "slot-count: 0" }
                : new ExternalCommandResult
                {
                    ExitCode = 1,
                    StandardError = $"getvar:{request.Arguments[^1]} FAILED (remote: 'GetVar Variable Not found')"
                })
        };
        var service = Build(runner);

        var layout = await service.GetBootLayoutAsync("FB", BootPartitionTarget.Boot);

        Assert.Equal(FastbootBootLayoutKind.Single, layout.Kind);
        Assert.Equal(0, layout.SlotCount);
        Assert.Null(layout.TargetHasSlot);
        Assert.Null(layout.CurrentSlot);
    }

    [Fact]
    public async Task GetBootLayout_ZeroSlotsButActiveSlotReported_StaysUnknown()
    {
        var runner = new RootTestExternalCommandRunner
        {
            Handler = static (request, _) => Task.FromResult(request.Arguments[^1] switch
            {
                "slot-count" => new ExternalCommandResult { StandardOutput = "slot-count: 0" },
                "current-slot" => new ExternalCommandResult { StandardOutput = "current-slot: a" },
                _ => new ExternalCommandResult { ExitCode = 1, StandardError = "unknown variable" }
            })
        };
        var service = Build(runner);

        var layout = await service.GetBootLayoutAsync("FB", BootPartitionTarget.Boot);

        Assert.Equal(FastbootBootLayoutKind.Unknown, layout.Kind);
    }

    [Fact]
    public async Task GetBootLayout_NoSlotEvidenceAtAll_StaysUnknown()
    {
        var runner = new RootTestExternalCommandRunner
        {
            Handler = static (_, _) => Task.FromResult(
                new ExternalCommandResult { ExitCode = 1, StandardError = "unknown variable" })
        };
        var service = Build(runner);

        var layout = await service.GetBootLayoutAsync("FB", BootPartitionTarget.Boot);

        Assert.Equal(FastbootBootLayoutKind.Unknown, layout.Kind);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private RootFastbootService Build(RootTestExternalCommandRunner runner)
        => new(
            new TestEnvironment(Path.Combine(_directory, OperatingSystem.IsWindows() ? "adb.exe" : "adb")),
            new RootTestAdbCommandExecutor(),
            runner,
            new RootToolPaths(_directory, Path.Combine(_directory, "work")),
            NullLogger<RootFastbootService>.Instance);

    private sealed class TestEnvironment(string executablePath) : IAdbEnvironment
    {
        public bool IsAvailable => true;
        public AdbLocation? Location { get; } = new()
        {
            ExecutablePath = executablePath,
            Source = AdbLocationSource.Configured
        };
        public string ExecutablePath => executablePath;
        public event EventHandler? Changed { add { } remove { } }
        public void Set(AdbLocation? location) => throw new NotSupportedException();
    }
}
