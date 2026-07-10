using AndroidTreeView.Adb.Services;
using AndroidTreeView.Adb.Tests.TestDoubles;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Services;

public sealed class FastbootServiceTests : IDisposable
{
    private readonly string _toolsDirectory = Path.Combine(
        Path.GetTempPath(),
        $"AndroidTreeView-fastboot-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ListSerialsAsync_WhenFastbootIsMissing_ReturnsEmptyWithoutRunningCommand()
    {
        Directory.CreateDirectory(_toolsDirectory);
        var runner = new FakeExternalCommandRunner();
        var service = BuildService(runner);

        var serials = await service.ListSerialsAsync();

        Assert.Empty(serials);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task ListSerialsAsync_WhenCommandTimesOut_ReturnsEmptyAndPreservesRequestArguments()
    {
        var fastbootPath = CreateFastbootFile();
        var runner = new FakeExternalCommandRunner();
        runner.Enqueue(new ExternalCommandResult { ExitCode = -1, TimedOut = true });
        var service = BuildService(runner);

        var serials = await service.ListSerialsAsync();

        Assert.Empty(serials);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(fastbootPath, request.FileName);
        Assert.Equal(new[] { "devices" }, request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(4), request.Timeout);
    }

    [Fact]
    public async Task ListSerialsAsync_WhenCommandExitsNonZero_ReturnsEmpty()
    {
        CreateFastbootFile();
        var runner = new FakeExternalCommandRunner();
        runner.Enqueue(new ExternalCommandResult
        {
            ExitCode = 1,
            StandardOutput = "serial-ignored\tfastboot\n",
            StandardError = "failed"
        });
        var service = BuildService(runner);

        var serials = await service.ListSerialsAsync();

        Assert.Empty(serials);
    }

    [Fact]
    public async Task GetVariablesAsync_ParsesFastbootStderrAndTargetsRequestedSerial()
    {
        CreateFastbootFile();
        var runner = new FakeExternalCommandRunner();
        runner.Enqueue(new ExternalCommandResult
        {
            ExitCode = 0,
            StandardError = "(bootloader) product: devon\n(bootloader) unlocked: yes\nFinished. Total time: 0.001s"
        });
        var service = BuildService(runner);

        var variables = await service.GetVariablesAsync("device-123");

        Assert.Equal("devon", variables["product"]);
        Assert.Equal("yes", variables["unlocked"]);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(new[] { "-s", "device-123", "getvar", "all" }, request.Arguments);
    }

    [Fact]
    public async Task ListSerialsAsync_WhenCancelled_PropagatesCancellation()
    {
        CreateFastbootFile();
        var runner = new FakeExternalCommandRunner();
        var service = BuildService(runner);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ListSerialsAsync(cts.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_toolsDirectory))
        {
            Directory.Delete(_toolsDirectory, recursive: true);
        }
    }

    private FastbootService BuildService(FakeExternalCommandRunner runner)
    {
        var adbPath = Path.Combine(_toolsDirectory, OperatingSystem.IsWindows() ? "adb.exe" : "adb");
        var environment = new TestAdbEnvironment(adbPath);
        return new FastbootService(environment, runner, NullLogger<FastbootService>.Instance);
    }

    private string CreateFastbootFile()
    {
        Directory.CreateDirectory(_toolsDirectory);
        var path = Path.Combine(
            _toolsDirectory,
            OperatingSystem.IsWindows() ? "fastboot.exe" : "fastboot");
        File.WriteAllText(path, "test executable placeholder");
        return path;
    }

    private sealed class TestAdbEnvironment(string executablePath) : IAdbEnvironment
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
