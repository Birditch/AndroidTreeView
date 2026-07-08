using AndroidTreeView.Adb.Services;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Services;

public class AdbDeviceActionsServiceTests
{
    private const string Serial = "emulator-5554";

    private static AdbDeviceActionsService Build(FakeAdbCommandExecutor fake)
        => new(fake, NullLogger<AdbDeviceActionsService>.Instance);

    // ── RebootAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RebootAsync_System_IssuesGlobalRebootCommand()
    {
        var fake = new FakeAdbCommandExecutor();
        await Build(fake).RebootAsync(Serial, RebootTarget.System);

        var req = Assert.Single(fake.Captured);
        Assert.Equal(Serial, req.Serial);
        Assert.False(req.RunInShell, "reboot must be a global adb command, not a shell command");
        Assert.Equal(new[] { "reboot" }, req.Arguments);
    }

    [Fact]
    public async Task RebootAsync_Recovery_IssuesGlobalRebootRecoveryCommand()
    {
        var fake = new FakeAdbCommandExecutor();
        await Build(fake).RebootAsync(Serial, RebootTarget.Recovery);

        var req = Assert.Single(fake.Captured);
        Assert.False(req.RunInShell);
        Assert.Equal(new[] { "reboot", "recovery" }, req.Arguments);
    }

    [Fact]
    public async Task RebootAsync_Bootloader_IssuesGlobalRebootBootloaderCommand()
    {
        var fake = new FakeAdbCommandExecutor();
        await Build(fake).RebootAsync(Serial, RebootTarget.Bootloader);

        var req = Assert.Single(fake.Captured);
        Assert.False(req.RunInShell);
        Assert.Equal(new[] { "reboot", "bootloader" }, req.Arguments);
    }

    // ── PowerOffAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task PowerOffAsync_IssuesShellRebootMinusP()
    {
        var fake = new FakeAdbCommandExecutor();
        await Build(fake).PowerOffAsync(Serial);

        var req = Assert.Single(fake.Captured);
        Assert.Equal(Serial, req.Serial);
        Assert.True(req.RunInShell);
        Assert.Equal(new[] { "reboot", "-p" }, req.Arguments);
    }

    // ── RemoveFrpAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveFrpAsync_IssuesBothSettingsWriteCommands()
    {
        var fake = new FakeAdbCommandExecutor();
        await Build(fake).RemoveFrpAsync(Serial);

        Assert.Equal(2, fake.Captured.Count);
        Assert.Equal(
            new[] { "settings", "put", "secure", "user_setup_complete", "1" },
            fake.Captured[0].Arguments);
        Assert.Equal(
            new[] { "settings", "put", "global", "device_provisioned", "1" },
            fake.Captured[1].Arguments);
        Assert.True(fake.Captured[0].RunInShell);
        Assert.True(fake.Captured[1].RunInShell);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AdbCommandResult SuccessResult(string stdout)
        => new() { ExitCode = 0, StandardOutput = stdout };

    private static AdbCommandResult FailResult()
        => new() { ExitCode = 1, StandardOutput = "" };

    // ── Fake IAdbCommandExecutor ──────────────────────────────────────────────

    private sealed class FakeAdbCommandExecutor : IAdbCommandExecutor
    {
        private readonly bool _throws;
        private readonly List<AdbCommandRequest> _captured = [];
        private readonly Queue<AdbCommandResult> _queue = new();

        public IReadOnlyList<AdbCommandRequest> Captured => _captured;

        public FakeAdbCommandExecutor(bool throws = false) => _throws = throws;

        /// <summary>Enqueue a specific result for the next <see cref="ExecuteAsync"/> call.</summary>
        public void Enqueue(AdbCommandResult result) => _queue.Enqueue(result);

        public Task<AdbCommandResult> ExecuteAsync(AdbCommandRequest request, CancellationToken ct = default)
        {
            if (_throws)
                throw new InvalidOperationException("Simulated executor failure.");

            _captured.Add(request);

            var result = _queue.Count > 0
                ? _queue.Dequeue()
                : new AdbCommandResult { ExitCode = 0, StandardOutput = "" };

            return Task.FromResult(result);
        }

        public IAsyncEnumerable<string> StreamAsync(AdbCommandRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("StreamAsync is not used by AdbDeviceActionsService.");
    }
}
