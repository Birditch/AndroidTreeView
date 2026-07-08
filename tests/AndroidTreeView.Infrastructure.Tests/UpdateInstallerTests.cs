using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using AndroidTreeView.Core.Options;
using AndroidTreeView.Infrastructure.Update;
using AndroidTreeView.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AndroidTreeView.Infrastructure.Tests;

public sealed class UpdateInstallerTests
{
    [Fact]
    public async Task InstallAsync_ZipContainingMsiWithoutManifest_ReturnsUnsupportedPackage()
    {
        var zipBytes = CreateZip(("AndroidTreeView-2.0.0-x64.msi", [1, 2, 3]));
        var launcher = new FakeInstallerLauncher();
        var installer = CreateInstaller(zipBytes, launcher, out var stageRoot);
        var update = Update(zipBytes);

        var result = await installer.InstallAsync(update);

        Assert.Equal(UpdateInstallStatus.UnsupportedPackage, result.Status);
        Assert.NotNull(result.PackagePath);
        Assert.True(File.Exists(result.PackagePath));
        Assert.Null(result.InstallerPath);
        Assert.Null(launcher.LastPath);

        Directory.Delete(stageRoot, recursive: true);
    }

    [Fact]
    public async Task InstallAsync_InvalidChecksum_DoesNotLaunchInstaller()
    {
        var zipBytes = CreateZip(("AndroidTreeView-2.0.0-x64.msi", [1, 2, 3]));
        var launcher = new FakeInstallerLauncher();
        var installer = CreateInstaller(zipBytes, launcher, out var stageRoot);
        var update = Update(zipBytes, sha256: new string('0', 64));

        var result = await installer.InstallAsync(update);

        Assert.Equal(UpdateInstallStatus.InvalidChecksum, result.Status);
        Assert.Null(launcher.LastPath);

        Directory.Delete(stageRoot, recursive: true);
    }

    [Fact]
    public async Task InstallAsync_ZipWithoutInstaller_ReturnsUnsupportedPackage()
    {
        var zipBytes = CreateZip(("readme.txt", [1, 2, 3]));
        var launcher = new FakeInstallerLauncher();
        var installer = CreateInstaller(zipBytes, launcher, out var stageRoot);
        var update = Update(zipBytes);

        var result = await installer.InstallAsync(update);

        Assert.Equal(UpdateInstallStatus.UnsupportedPackage, result.Status);
        Assert.Null(launcher.LastPath);

        Directory.Delete(stageRoot, recursive: true);
    }

    [Fact]
    public async Task InstallAsync_PortableX64Zip_CreatesAndLaunchesApplyScript()
    {
        var zipBytes = CreatePortableZip();
        var launcher = new FakeInstallerLauncher();
        var installer = CreateInstaller(zipBytes, launcher, out var stageRoot);
        var update = Update(zipBytes);

        var result = await installer.InstallAsync(update);

        Assert.Equal(UpdateInstallStatus.Started, result.Status);
        Assert.NotNull(result.InstallerPath);
        Assert.EndsWith(".cmd", result.InstallerPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(result.InstallerPath, launcher.LastPath);
        Assert.True(File.Exists(result.InstallerPath));
        var scriptPath = Path.Combine(Path.GetDirectoryName(result.InstallerPath)!, "apply-update.ps1");
        Assert.True(File.Exists(scriptPath));
        Assert.Contains("-Verb RunAs", File.ReadAllText(scriptPath));

        Directory.Delete(stageRoot, recursive: true);
    }

    [Theory]
    [InlineData("android-tree-view-mini", "x64", "2.0.0")]
    [InlineData("android-tree-view-app", "x86", "2.0.0")]
    [InlineData("android-tree-view-app", "x64", "9.9.9")]
    public async Task InstallAsync_PortableZipWithMismatchedManifest_ReturnsUnsupportedPackage(
        string appKey,
        string arch,
        string version)
    {
        var zipBytes = CreatePortableZip(appKey: appKey, arch: arch, version: version);
        var launcher = new FakeInstallerLauncher();
        var installer = CreateInstaller(zipBytes, launcher, out var stageRoot);
        var update = Update(zipBytes);

        var result = await installer.InstallAsync(update);

        Assert.Equal(UpdateInstallStatus.UnsupportedPackage, result.Status);
        Assert.Null(result.InstallerPath);
        Assert.Null(launcher.LastPath);

        Directory.Delete(stageRoot, recursive: true);
    }

    private static UpdateInstaller CreateInstaller(
        byte[] packageBytes,
        FakeInstallerLauncher launcher,
        out string stageRoot)
    {
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(packageBytes)
            };
            response.Content.Headers.ContentDisposition =
                new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment")
                {
                    FileName = "AndroidTreeView.zip"
                };
            return response;
        });

        stageRoot = Path.Combine(Path.GetTempPath(), "AndroidTreeView-update-tests-" + Guid.NewGuid().ToString("N"));
        return new UpdateInstaller(
            new HttpClient(handler),
            UpdateProductOptions.ForMainApp(),
            launcher,
            NullLogger<UpdateInstaller>.Instance,
            stageRoot);
    }

    private static UpdateCheckResult Update(byte[] packageBytes, string? sha256 = null) => new()
    {
        CurrentVersion = "1.0.0",
        LatestVersion = "2.0.0",
        UpdateAvailable = true,
        DownloadUrl = "https://updates.local/AndroidTreeView.zip",
        Sha256 = sha256 ?? Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant(),
        Status = UpdateCheckStatus.UpdateAvailable,
    };

    private static byte[] CreateZip(params (string Name, byte[] Bytes)[] files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in files)
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                entryStream.Write(bytes);
            }
        }

        return stream.ToArray();
    }

    private static byte[] CreatePortableZip(
        string appKey = "android-tree-view-app",
        string arch = "x64",
        string version = "2.0.0")
    {
        var manifest = $$"""
        {
          "packageKind": "portable-x64",
          "product": "App",
          "appKey": "{{appKey}}",
          "version": "{{version}}",
          "arch": "{{arch}}",
          "executable": "AndroidTreeView.App.exe"
        }
        """;

        return CreateZip(
            ("release.json", System.Text.Encoding.UTF8.GetBytes(manifest)),
            ("AndroidTreeView.App.exe", [1, 2, 3]),
            ("AndroidTreeView.App.dll", [4, 5, 6]));
    }

    private sealed class FakeInstallerLauncher : IInstallerLauncher
    {
        public string? LastPath { get; private set; }

        public bool Launch(string installerPath, out string? errorMessage)
        {
            LastPath = installerPath;
            errorMessage = null;
            return true;
        }
    }
}
