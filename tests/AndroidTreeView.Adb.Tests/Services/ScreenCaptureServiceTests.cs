using AndroidTreeView.Adb.Services;
using AndroidTreeView.Core.Exceptions;
using AndroidTreeView.Core.Interfaces;
using AndroidTreeView.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Services;

public class ScreenCaptureServiceTests
{
    private const string Serial = "emulator-5554";

    private static ScreenCaptureService Build(FakeAdbCommandExecutor fake, IAdbEnvironment? env = null)
        => new(env ?? new AvailableAdbEnvironment(), fake, NullLogger<ScreenCaptureService>.Instance);

    // ── TapAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task TapAsync_IssuesShellInputTapCommand()
    {
        var fake = new FakeAdbCommandExecutor();
        await Build(fake).TapAsync(Serial, 100, 200);

        var req = Assert.Single(fake.Captured);
        Assert.Equal(Serial, req.Serial);
        Assert.True(req.RunInShell, "input tap must be a shell command");
        Assert.Equal(new[] { "input", "tap", "100", "200" }, req.Arguments);
    }

    [Fact]
    public async Task TapAsync_CoordinatesAreFormattedCorrectly()
    {
        var fake = new FakeAdbCommandExecutor();
        await Build(fake).TapAsync(Serial, 0, 1920);

        var req = Assert.Single(fake.Captured);
        Assert.Equal(new[] { "input", "tap", "0", "1920" }, req.Arguments);
    }

    [Fact]
    public async Task TapAsync_WhenExecutorThrows_DoesNotPropagate()
    {
        var fake = new FakeAdbCommandExecutor(throws: true);
        // Must not throw for ordinary executor failures.
        await Build(fake).TapAsync(Serial, 50, 50);
    }

    [Fact]
    public async Task TapAsync_WhenCommandFails_DoesNotThrow()
    {
        var fake = new FakeAdbCommandExecutor();
        fake.Enqueue(new AdbCommandResult { ExitCode = 1, StandardOutput = "" });
        // Non-zero exit is a non-fatal, ordinary failure.
        await Build(fake).TapAsync(Serial, 10, 20);
    }

    // ── InstallApkAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task InstallApkAsync_IssuesGlobalInstallReplaceCommand()
    {
        const string apk = "/sdcard/test.apk";
        var fake = new FakeAdbCommandExecutor();
        fake.Enqueue(SuccessResult("Success\n"));

        await Build(fake).InstallApkAsync(Serial, apk);

        var req = Assert.Single(fake.Captured);
        Assert.Equal(Serial, req.Serial);
        Assert.False(req.RunInShell, "adb install must be a global command, not a shell command");
        Assert.Equal(new[] { "install", "-r", apk }, req.Arguments);
    }

    [Fact]
    public async Task InstallApkAsync_WhenStdoutContainsSuccess_ReturnsTrue()
    {
        var fake = new FakeAdbCommandExecutor();
        fake.Enqueue(SuccessResult("Performing Streamed Install\nSuccess\n"));
        Assert.True(await Build(fake).InstallApkAsync(Serial, "app.apk"));
    }

    [Fact]
    public async Task InstallApkAsync_WhenStderrContainsSuccess_ReturnsTrue()
    {
        var fake = new FakeAdbCommandExecutor();
        // Some adb builds emit "Success" on stderr instead of stdout.
        fake.Enqueue(new AdbCommandResult { ExitCode = 0, StandardOutput = "", StandardError = "Success" });
        Assert.True(await Build(fake).InstallApkAsync(Serial, "app.apk"));
    }

    [Fact]
    public async Task InstallApkAsync_WhenOutputLacksSuccess_ReturnsFalse()
    {
        var fake = new FakeAdbCommandExecutor();
        fake.Enqueue(SuccessResult("Failure [INSTALL_FAILED_ALREADY_EXISTS]\n"));
        Assert.False(await Build(fake).InstallApkAsync(Serial, "app.apk"));
    }

    [Fact]
    public async Task InstallApkAsync_WhenExitCodeNonZero_ReturnsFalse()
    {
        var fake = new FakeAdbCommandExecutor();
        fake.Enqueue(FailResult());
        Assert.False(await Build(fake).InstallApkAsync(Serial, "app.apk"));
    }

    [Fact]
    public async Task InstallApkAsync_WhenExecutorThrows_ReturnsFalse()
    {
        var fake = new FakeAdbCommandExecutor(throws: true);
        Assert.False(await Build(fake).InstallApkAsync(Serial, "app.apk"));
    }

    // ── CaptureFrameAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task CaptureFrameAsync_WhenAdbUnavailable_ThrowsAdbNotFoundException()
    {
        // CaptureFrameAsync accesses _environment.ExecutablePath which throws when adb is missing.
        // This is the one case the service is explicitly allowed to propagate.
        var fake = new FakeAdbCommandExecutor();
        var svc = new ScreenCaptureService(
            new UnavailableAdbEnvironment(),
            fake,
            NullLogger<ScreenCaptureService>.Instance);

        await Assert.ThrowsAsync<AdbNotFoundException>(() => svc.CaptureFrameAsync(Serial));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AdbCommandResult SuccessResult(string stdout)
        => new() { ExitCode = 0, StandardOutput = stdout };

    private static AdbCommandResult FailResult()
        => new() { ExitCode = 1, StandardOutput = "" };

    // ── Fake IAdbEnvironment ──────────────────────────────────────────────────

    private sealed class AvailableAdbEnvironment : IAdbEnvironment
    {
        public bool IsAvailable => true;
        public AdbLocation? Location => null;
        public string ExecutablePath => "adb";
        public event EventHandler? Changed { add { } remove { } }
        public void Set(AdbLocation? location) { }
    }

    private sealed class UnavailableAdbEnvironment : IAdbEnvironment
    {
        public bool IsAvailable => false;
        public AdbLocation? Location => null;
        public string ExecutablePath => throw new AdbNotFoundException();
        public event EventHandler? Changed { add { } remove { } }
        public void Set(AdbLocation? location) { }
    }

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
            => throw new NotSupportedException("StreamAsync is not used by ScreenCaptureService.");
    }
}
