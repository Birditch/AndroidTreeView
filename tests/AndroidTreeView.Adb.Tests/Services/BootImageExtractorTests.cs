using System.IO.Compression;
using AndroidTreeView.Adb.Parsers;
using AndroidTreeView.Adb.Services;
using AndroidTreeView.Adb.Tests.Parsers;
using AndroidTreeView.Adb.Tests.TestDoubles;
using AndroidTreeView.Core.Exceptions;
using AndroidTreeView.Core.Services;
using AndroidTreeView.Models.Rooting;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Services;

public sealed class BootImageExtractorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"atv-extractor-{Guid.NewGuid():N}");

    public BootImageExtractorTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task ExtractAsync_PlainZip_SelectsBootForAndroid12()
    {
        var bootImage = AndroidBootImageHeaderParserTests.CreateImage(2, ramdiskSize: 2);
        var initBootImage = AndroidBootImageHeaderParserTests.CreateImage(4, ramdiskSize: 2, kernelSize: 0);
        var package = CreateZip(("boot.img", bootImage), ("init_boot.img", initBootImage));
        var adb = SuccessfulProbe("no", "32", "5.10.1-test");
        var extractor = Build(adb);

        var result = await extractor.ExtractAsync(package, new RootDeviceIdentity { Serial = "S" });

        Assert.Equal(BootPartitionTarget.Boot, result.TargetPartition);
        Assert.Equal(BootImageSource.PlainZip, result.Source);
        Assert.Equal(bootImage, await File.ReadAllBytesAsync(result.Path));
    }

    [Fact]
    public async Task ExtractAsync_PixelNestedZip_ExtractsExactlyOneLevel()
    {
        var bootImage = AndroidBootImageHeaderParserTests.CreateImage(1, ramdiskSize: 2);
        var inner = CreateZip(("boot.img", bootImage));
        var package = CreateZip(("image-oriole.zip", await File.ReadAllBytesAsync(inner)));
        var extractor = Build(SuccessfulProbe("no", "32", "5.10.1"));

        var result = await extractor.ExtractAsync(package, new RootDeviceIdentity { Serial = "S" });

        Assert.Equal(BootImageSource.NestedZip, result.Source);
        Assert.Equal(bootImage, await File.ReadAllBytesAsync(result.Path));
    }

    [Fact]
    public async Task ExtractAsync_ZipPayload_UsesArgvAndRequiresProducedImage()
    {
        var package = CreateZip(("payload.bin", "CrAUtest"u8.ToArray()));
        var workRoot = Path.Combine(_directory, "payload-work");
        var tools = Path.Combine(_directory, "payload-tools");
        var paths = new RootToolPaths(tools, workRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.PayloadDumperPath)!);
        await File.WriteAllTextAsync(paths.PayloadDumperPath, "test");
        var runner = new RootTestExternalCommandRunner
        {
            Handler = (request, _) =>
            {
                var outputDirectory = request.Arguments[3];
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllBytes(
                    Path.Combine(outputDirectory, "boot.img"),
                    AndroidBootImageHeaderParserTests.CreateImage(2, ramdiskSize: 2));
                return Task.FromResult(new ExternalCommandResult());
            }
        };
        var extractor = new BootImageExtractor(SuccessfulProbe("no", "32", "5.10.1"), runner, paths);

        var result = await extractor.ExtractAsync(package, new RootDeviceIdentity { Serial = "S" });

        Assert.Equal(BootImageSource.Payload, result.Source);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(paths.PayloadDumperPath, request.FileName);
        Assert.Equal(new[] { "-p", "boot", "-o" }, request.Arguments.Take(3));
        Assert.Equal(BootRamdiskEvidence.Present,
            AndroidBootImageHeaderParser.Parse(await File.ReadAllBytesAsync(result.Path), new FileInfo(result.Path).Length));
    }

    [Fact]
    public async Task ExtractAsync_BootWithoutRamdisk_BlocksRecoveryOnlyAndCleansWork()
    {
        var package = CreateZip(("boot.img", AndroidBootImageHeaderParserTests.CreateImage(2, ramdiskSize: 0)));
        var workRoot = Path.Combine(_directory, "recovery-only-work");
        var extractor = Build(SuccessfulProbe("no", "32", "5.10.1"), workRoot);

        var error = await Assert.ThrowsAsync<RootWorkflowException>(
            () => extractor.ExtractAsync(package, new RootDeviceIdentity { Serial = "S" }));

        Assert.Equal(RootErrorCode.RecoveryOnlyUnsupported, error.ErrorCode);
        Assert.False(Directory.Exists(workRoot) && Directory.EnumerateFileSystemEntries(workRoot).Any());
    }

    [Fact]
    public async Task ExtractAsync_UnrecognizedBootHeader_BlocksWithoutGuessing()
    {
        var package = CreateZip(("boot.img", new byte[4096]));
        var extractor = Build(SuccessfulProbe("no", "32", "5.10.1"));

        var error = await Assert.ThrowsAsync<RootWorkflowException>(
            () => extractor.ExtractAsync(package, new RootDeviceIdentity { Serial = "S" }));

        Assert.Equal(RootErrorCode.TargetEvidenceConflict, error.ErrorCode);
    }

    [Fact]
    public async Task ExtractAsync_Android13InitBoot_RequiresRamdiskEvidence()
    {
        var bootImage = AndroidBootImageHeaderParserTests.CreateImage(4, ramdiskSize: 0);
        var initBootImage = AndroidBootImageHeaderParserTests.CreateImage(4, ramdiskSize: 3, kernelSize: 0);
        var package = CreateZip(("boot.img", bootImage), ("init_boot.img", initBootImage));
        var extractor = Build(SuccessfulProbe("yes", "33", "5.10.1"));

        var result = await extractor.ExtractAsync(package, new RootDeviceIdentity { Serial = "S" });

        Assert.Equal(BootPartitionTarget.InitBoot, result.TargetPartition);
        Assert.Equal(initBootImage, await File.ReadAllBytesAsync(result.Path));
    }

    [Fact]
    public async Task ExtractAsync_PathTraversal_IsRejectedAndWorkDirectoryCleaned()
    {
        var package = CreateZip(("boot.img", new byte[] { 1 }), ("../escape.txt", new byte[] { 2 }));
        var workRoot = Path.Combine(_directory, "work");
        var extractor = Build(SuccessfulProbe("no", "32", "5.10.1"), workRoot);

        var error = await Assert.ThrowsAsync<RootWorkflowException>(
            () => extractor.ExtractAsync(package, new RootDeviceIdentity { Serial = "S" }));

        Assert.Equal(RootErrorCode.PackagePathUnsafe, error.ErrorCode);
        Assert.False(Directory.Exists(workRoot) && Directory.EnumerateFileSystemEntries(workRoot).Any());
    }

    [Fact]
    public async Task ExtractAsync_ProbeFailure_BlocksInsteadOfAssumingBoot()
    {
        var package = CreateZip(("boot.img", new byte[] { 1 }));
        var adb = new RootTestAdbCommandExecutor
        {
            Handler = static (request, _) => Task.FromResult(request.Arguments.Contains("getprop")
                ? new AdbCommandResult { ExitCode = 1, StandardError = "device offline" }
                : new AdbCommandResult { StandardOutput = request.Arguments.Contains("uname") ? "5.10.1" : "no" })
        };
        var extractor = Build(adb);

        var error = await Assert.ThrowsAsync<RootWorkflowException>(
            () => extractor.ExtractAsync(package, new RootDeviceIdentity { Serial = "S" }));

        Assert.Equal(RootErrorCode.DeviceUnavailable, error.ErrorCode);
    }

    [Fact]
    public async Task ExtractAsync_ProbeUsesDirectTestCommandWithoutNestedShellQuoting()
    {
        var package = CreateZip(("boot.img", AndroidBootImageHeaderParserTests.CreateImage(2, ramdiskSize: 2)));
        var adb = SuccessfulProbe("no", "31", "4.14.186");
        var extractor = Build(adb);

        await extractor.ExtractAsync(package, new RootDeviceIdentity { Serial = "S" });

        var request = Assert.Single(adb.Requests, request => request.Arguments.Contains("test"));
        Assert.True(request.RunInShell);
        Assert.Equal(new[] { "test", "-e", "/dev/block/by-name/init_boot" }, request.Arguments);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private BootImageExtractor Build(RootTestAdbCommandExecutor adb, string? workRoot = null)
    {
        var tools = Path.Combine(_directory, "tools");
        var paths = new RootToolPaths(tools, workRoot ?? Path.Combine(_directory, "work"));
        return new BootImageExtractor(adb, new RootTestExternalCommandRunner(), paths);
    }

    private RootTestAdbCommandExecutor SuccessfulProbe(string initBoot, string sdk, string kernel)
        => new()
        {
            Handler = (request, _) => Task.FromResult(request.Arguments.Contains("getprop")
                ? new AdbCommandResult { StandardOutput = sdk }
                : request.Arguments.Contains("uname")
                    ? new AdbCommandResult { StandardOutput = kernel }
                    : new AdbCommandResult { ExitCode = initBoot == "yes" ? 0 : 1 })
        };

    private string CreateZip(params (string Name, byte[] Content)[] entries)
    {
        var path = Path.Combine(_directory, $"package-{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var item in entries)
        {
            var entry = archive.CreateEntry(item.Name);
            using var stream = entry.Open();
            stream.Write(item.Content);
        }

        return path;
    }
}
