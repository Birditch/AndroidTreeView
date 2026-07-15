using System.IO.Compression;
using AndroidTreeView.Adb.Services;
using AndroidTreeView.Adb.Tests.TestDoubles;
using AndroidTreeView.Core.Services;
using AndroidTreeView.Models.Rooting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Services;

public sealed class MagiskPatcherTests : IDisposable
{
    // Verbatim app_init output from a real cannon (M2007J22C, Android 12, MIUI 14).
    private const string ProbeOutput = """
        SYSTEM_AS_ROOT=true
        PATCHVBMETAFLAG=false
        LEGACYSAR=false
        RECOVERYMODE=false
        KEEPVERITY=true
        KEEPFORCEENCRYPT=true
        """;

    // Verbatim .backup/.magisk shape: no PATCHVBMETAFLAG, no LEGACYSAR.
    private const string PatchedConfig = """
        KEEPVERITY=true
        KEEPFORCEENCRYPT=true
        RECOVERYMODE=false
        PREINITDEVICE=cache
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"atv-magisk-{Guid.NewGuid():N}");

    public MagiskPatcherTests() => Directory.CreateDirectory(_directory);

    private static RootTestAdbCommandExecutor HealthyAdb(string? probe = null, string? config = null) => new()
    {
        Handler = (request, _) =>
        {
            var command = string.Join(' ', request.Arguments);
            if (command.Contains("app_init", StringComparison.Ordinal))
            {
                return Task.FromResult(new AdbCommandResult { StandardOutput = probe ?? ProbeOutput });
            }

            if (command.Contains("config.verify", StringComparison.Ordinal))
            {
                return Task.FromResult(new AdbCommandResult { StandardOutput = config ?? PatchedConfig });
            }

            if (request.Arguments.Contains("getprop"))
            {
                return Task.FromResult(new AdbCommandResult { StandardOutput = "arm64-v8a\n" });
            }

            if (request.Arguments.FirstOrDefault() == "install")
            {
                return Task.FromResult(new AdbCommandResult { StandardOutput = "Success\n" });
            }

            if (request.Arguments.FirstOrDefault() == "pull")
            {
                File.WriteAllBytes(request.Arguments[2], new byte[] { 9, 8, 7 });
            }

            return Task.FromResult(new AdbCommandResult());
        }
    };

    // Naming boot_patch.sh is not invoking it: `push` and `chmod` both carry its path as an argument.
    private static bool IsPatchInvocation(AdbCommandRequest r) =>
        r.RunInShell
        && r.Arguments.Contains("sh")
        && r.Arguments.Any(a => a.EndsWith("boot_patch.sh", StringComparison.Ordinal));

    private static bool IsFlagProbe(AdbCommandRequest r) =>
        r.RunInShell && r.Arguments.Contains("app_init");

    private async Task<(MagiskPatcher Patcher, BootImageInfo Boot)> ArrangeAsync(
        RootTestAdbCommandExecutor adb,
        string name)
    {
        var work = Path.Combine(_directory, name);
        Directory.CreateDirectory(work);
        var bootPath = Path.Combine(work, "boot.img");
        await File.WriteAllBytesAsync(bootPath, new byte[] { 1, 2, 3 });
        return (new MagiskPatcher(adb, CreateTools("arm64-v8a"), NullLogger<MagiskPatcher>.Instance),
            new BootImageInfo
            {
                Path = bootPath,
                WorkDirectory = work,
                OriginalPackageName = "factory.zip",
                TargetPartition = BootPartitionTarget.Boot
            });
    }

    [Fact]
    public async Task PatchAsync_PassesProbedFlagsToBootPatchScript()
    {
        var adb = HealthyAdb();
        var (patcher, boot) = await ArrangeAsync(adb, "flags-work");

        await patcher.PatchAsync("SERIAL", boot);

        var patch = string.Join(' ', adb.Requests.First(IsPatchInvocation).Arguments);
        Assert.Contains("KEEPVERITY=true", patch);
        Assert.Contains("KEEPFORCEENCRYPT=true", patch);
        Assert.Contains("PATCHVBMETAFLAG=false", patch);
        Assert.Contains("RECOVERYMODE=false", patch);
        Assert.Contains("LEGACYSAR=false", patch);
    }

    [Fact]
    public async Task PatchAsync_ProbesFlagsBeforePatching()
    {
        var adb = HealthyAdb();
        var (patcher, boot) = await ArrangeAsync(adb, "order-work");

        await patcher.PatchAsync("SERIAL", boot);

        Assert.InRange(adb.Requests.FindIndex(IsFlagProbe), 0, adb.Requests.FindIndex(IsPatchInvocation) - 1);
    }

    [Fact]
    public async Task PatchAsync_IncompleteProbe_AbortsBeforePatching()
    {
        var adb = HealthyAdb(probe: "KEEPVERITY=true");
        var (patcher, boot) = await ArrangeAsync(adb, "probe-work");

        var error = await Assert.ThrowsAsync<AndroidTreeView.Core.Exceptions.RootWorkflowException>(
            () => patcher.PatchAsync("SERIAL", boot));

        Assert.Equal(RootErrorCode.MagiskFlagProbeFailed, error.ErrorCode);
        Assert.DoesNotContain(adb.Requests, IsPatchInvocation);
    }

    [Fact]
    public async Task PatchAsync_PatchedConfigContradictsProbe_IsRejectedBeforePull()
    {
        // The exact shape of the real bootloop: device needs KEEPVERITY=true, image recorded false.
        var adb = HealthyAdb(config: "KEEPVERITY=false\nKEEPFORCEENCRYPT=false");
        var (patcher, boot) = await ArrangeAsync(adb, "mismatch-work");

        var error = await Assert.ThrowsAsync<AndroidTreeView.Core.Exceptions.RootWorkflowException>(
            () => patcher.PatchAsync("SERIAL", boot));

        Assert.Equal(RootErrorCode.PatchedImageFlagMismatch, error.ErrorCode);
        Assert.DoesNotContain(adb.Requests, r => r.Arguments.FirstOrDefault() == "pull");
    }

    [Fact]
    public async Task PatchAsync_UnreadablePatchedConfig_IsRejected()
    {
        var adb = HealthyAdb(config: "Loading cpio: [ramdisk.cpio]");
        var (patcher, boot) = await ArrangeAsync(adb, "unreadable-work");

        var error = await Assert.ThrowsAsync<AndroidTreeView.Core.Exceptions.RootWorkflowException>(
            () => patcher.PatchAsync("SERIAL", boot));

        Assert.Equal(RootErrorCode.PatchedImageFlagMismatch, error.ErrorCode);
    }

    [Fact]
    public async Task PatchAsync_ExtractsOfficialComponentsLocally_AndTargetsEveryAdbCommand()
    {
        var adb = HealthyAdb();
        var (patcher, boot) = await ArrangeAsync(adb, "work");

        var patched = await patcher.PatchAsync("SERIAL", boot);

        Assert.True(File.Exists(patched));
        Assert.All(adb.Requests, request => Assert.Equal("SERIAL", request.Serial));
        Assert.DoesNotContain(adb.Requests, request => request.Arguments.Contains("unzip") || request.Arguments.Contains("cp"));
        // 8 components (app_functions.sh included) plus the boot image itself.
        Assert.Equal(9, adb.Requests.Count(request => request.Arguments.FirstOrDefault() == "push"));
        var script = Assert.Single(adb.Requests, IsPatchInvocation);
        Assert.Equal("sh", script.Arguments[5]);
        Assert.False(Directory.EnumerateDirectories(boot.WorkDirectory, ".magisk-components-*").Any());
        Assert.Equal("rm", adb.Requests[^1].Arguments[0]);
    }

    [Fact]
    public async Task PatchAsync_InstallExitZeroWithFailureOutput_IsRejected()
    {
        var work = Path.Combine(_directory, "failure-work");
        Directory.CreateDirectory(work);
        var bootPath = Path.Combine(work, "boot.img");
        await File.WriteAllBytesAsync(bootPath, new byte[] { 1, 2, 3 });
        var paths = CreateTools("arm64-v8a");
        var adb = new RootTestAdbCommandExecutor
        {
            Handler = static (request, _) => Task.FromResult(
                request.Arguments.FirstOrDefault() == "install"
                    ? new AdbCommandResult { StandardOutput = "Failure [INSTALL_FAILED_VERSION_DOWNGRADE]\n" }
                    : new AdbCommandResult())
        };
        var patcher = new MagiskPatcher(adb, paths, NullLogger<MagiskPatcher>.Instance);

        var error = await Assert.ThrowsAsync<AndroidTreeView.Core.Exceptions.RootWorkflowException>(
            () => patcher.PatchAsync("SERIAL", new BootImageInfo
            {
                Path = bootPath,
                WorkDirectory = work,
                OriginalPackageName = "factory.zip",
                TargetPartition = BootPartitionTarget.Boot
            }));

        Assert.Equal(RootErrorCode.MagiskInstallFailed, error.ErrorCode);
        Assert.Contains("Failure", error.DiagnosticSummary);
        Assert.Equal(2, adb.Requests.Count);
        Assert.Equal("install", adb.Requests[0].Arguments[0]);
        Assert.Equal("rm", adb.Requests[1].Arguments[0]);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private RootToolPaths CreateTools(string abi)
    {
        var appBase = Path.Combine(_directory, "app");
        var paths = new RootToolPaths(appBase, Path.Combine(_directory, "root-work"));
        Directory.CreateDirectory(Path.GetDirectoryName(paths.MagiskApkPath)!);
        using var archive = ZipFile.Open(paths.MagiskApkPath, ZipArchiveMode.Create);
        foreach (var entryName in new[]
        {
            "assets/boot_patch.sh",
            "assets/util_functions.sh",
            "assets/app_functions.sh",
            "assets/stub.apk",
            $"lib/{abi}/libmagiskboot.so",
            $"lib/{abi}/libmagiskinit.so",
            $"lib/{abi}/libmagisk.so",
            $"lib/{abi}/libinit-ld.so"
        })
        {
            var entry = archive.CreateEntry(entryName);
            using var stream = entry.Open();
            stream.WriteByte(1);
        }

        return paths;
    }
}
