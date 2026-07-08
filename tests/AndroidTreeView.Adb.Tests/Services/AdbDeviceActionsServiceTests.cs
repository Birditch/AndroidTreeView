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

    // ── EnableNetworkAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task EnableNetworkAsync_IssuesBothWifiAndDataEnableCommands()
    {
        var fake = new FakeAdbCommandExecutor();
        await Build(fake).EnableNetworkAsync(Serial);

        Assert.Equal(2, fake.Captured.Count);

        var wifi = fake.Captured[0];
        Assert.True(wifi.RunInShell);
        Assert.Equal(new[] { "svc", "wifi", "enable" }, wifi.Arguments);

        var data = fake.Captured[1];
        Assert.True(data.RunInShell);
        Assert.Equal(new[] { "svc", "data", "enable" }, data.Arguments);
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

    // ── DisableCaptivePortalAsync ────────────────────────────────────────────

    [Fact]
    public async Task DisableCaptivePortalAsync_IssuesBothSettingsWriteCommands()
    {
        var fake = new FakeAdbCommandExecutor();
        await Build(fake).DisableCaptivePortalAsync(Serial);

        Assert.Equal(2, fake.Captured.Count);
        Assert.Equal(
            new[] { "settings", "put", "global", "captive_portal_mode", "0" },
            fake.Captured[0].Arguments);
        Assert.Equal(
            new[] { "settings", "put", "global", "captive_portal_detection_enabled", "0" },
            fake.Captured[1].Arguments);
        Assert.True(fake.Captured[0].RunInShell);
        Assert.True(fake.Captured[1].RunInShell);
    }

    // ── SetChinaNtpAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task SetChinaNtpAsync_IssuesSingleNtpServerSettingsCommand()
    {
        var fake = new FakeAdbCommandExecutor();
        await Build(fake).SetChinaNtpAsync(Serial);

        var req = Assert.Single(fake.Captured);
        Assert.True(req.RunInShell);
        Assert.Equal(
            new[] { "settings", "put", "global", "ntp_server", "ntp.aliyun.com" },
            req.Arguments);
    }

    // ── IsCaptivePortalDisabledAsync — query shape ───────────────────────────

    [Fact]
    public async Task IsCaptivePortalDisabledAsync_IssuesCorrectShellQuery()
    {
        var fake = new FakeAdbCommandExecutor();
        fake.Enqueue(SuccessResult("0"));
        await Build(fake).IsCaptivePortalDisabledAsync(Serial);

        var req = Assert.Single(fake.Captured);
        Assert.True(req.RunInShell);
        Assert.Equal(new[] { "settings", "get", "global", "captive_portal_mode" }, req.Arguments);
    }

    // ── IsCaptivePortalDisabledAsync — return value ──────────────────────────

    [Fact]
    public async Task IsCaptivePortalDisabledAsync_WhenOutputIsZero_ReturnsTrue()
    {
        var fake = new FakeAdbCommandExecutor();
        fake.Enqueue(SuccessResult("0\n")); // trailing newline is normal adb output
        Assert.True(await Build(fake).IsCaptivePortalDisabledAsync(Serial));
    }

    [Fact]
    public async Task IsCaptivePortalDisabledAsync_WhenOutputIsOne_ReturnsFalse()
    {
        var fake = new FakeAdbCommandExecutor();
        fake.Enqueue(SuccessResult("1"));
        Assert.False(await Build(fake).IsCaptivePortalDisabledAsync(Serial));
    }

    [Fact]
    public async Task IsCaptivePortalDisabledAsync_WhenOutputIsLiteralNull_ReturnsFalse()
    {
        // Android outputs the literal string "null" for unset settings keys.
        var fake = new FakeAdbCommandExecutor();
        fake.Enqueue(SuccessResult("null"));
        Assert.False(await Build(fake).IsCaptivePortalDisabledAsync(Serial));
    }

    [Fact]
    public async Task IsCaptivePortalDisabledAsync_WhenCommandFails_ReturnsFalse()
    {
        var fake = new FakeAdbCommandExecutor();
        fake.Enqueue(FailResult());
        Assert.False(await Build(fake).IsCaptivePortalDisabledAsync(Serial));
    }

    [Fact]
    public async Task IsCaptivePortalDisabledAsync_WhenExecutorThrows_ReturnsFalse()
    {
        var fake = new FakeAdbCommandExecutor(throws: true);
        Assert.False(await Build(fake).IsCaptivePortalDisabledAsync(Serial));
    }

    // ── IsChinaNtpSetAsync — query shape ─────────────────────────────────────

    [Fact]
    public async Task IsChinaNtpSetAsync_IssuesCorrectShellQuery()
    {
        var fake = new FakeAdbCommandExecutor();
        fake.Enqueue(SuccessResult("ntp.aliyun.com"));
        await Build(fake).IsChinaNtpSetAsync(Serial);

        var req = Assert.Single(fake.Captured);
        Assert.True(req.RunInShell);
        Assert.Equal(new[] { "settings", "get", "global", "ntp_server" }, req.Arguments);
    }

    // ── IsChinaNtpSetAsync — return value ────────────────────────────────────

    [Fact]
    public async Task IsChinaNtpSetAsync_WhenOutputContainsNtpAliyun_ReturnsTrue()
    {
        var fake = new FakeAdbCommandExecutor();
        fake.Enqueue(SuccessResult("ntp.aliyun.com\n"));
        Assert.True(await Build(fake).IsChinaNtpSetAsync(Serial));
    }

    [Fact]
    public async Task IsChinaNtpSetAsync_WhenOutputIsOtherServer_ReturnsFalse()
    {
        var fake = new FakeAdbCommandExecutor();
        fake.Enqueue(SuccessResult("time.google.com"));
        Assert.False(await Build(fake).IsChinaNtpSetAsync(Serial));
    }

    [Fact]
    public async Task IsChinaNtpSetAsync_WhenOutputIsLiteralNull_ReturnsFalse()
    {
        var fake = new FakeAdbCommandExecutor();
        fake.Enqueue(SuccessResult("null"));
        Assert.False(await Build(fake).IsChinaNtpSetAsync(Serial));
    }

    [Fact]
    public async Task IsChinaNtpSetAsync_WhenCommandFails_ReturnsFalse()
    {
        var fake = new FakeAdbCommandExecutor();
        fake.Enqueue(FailResult());
        Assert.False(await Build(fake).IsChinaNtpSetAsync(Serial));
    }

    [Fact]
    public async Task IsChinaNtpSetAsync_WhenExecutorThrows_ReturnsFalse()
    {
        var fake = new FakeAdbCommandExecutor(throws: true);
        Assert.False(await Build(fake).IsChinaNtpSetAsync(Serial));
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
