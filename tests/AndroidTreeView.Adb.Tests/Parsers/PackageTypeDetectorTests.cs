using AndroidTreeView.Adb.Parsers;
using AndroidTreeView.Models.Rooting;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public sealed class PackageTypeDetectorTests
{
    [Theory]
    [InlineData("boot.img", FirmwarePackageType.PlainZip)]
    [InlineData("INIT_BOOT.IMG", FirmwarePackageType.PlainZip)]
    [InlineData("image-oriole.zip", FirmwarePackageType.NestedZip)]
    [InlineData("payload.bin", FirmwarePackageType.Payload)]
    [InlineData("system.img", FirmwarePackageType.Unknown)]
    public void DetectZipEntries_ClassifiesSupportedContainer(string entry, FirmwarePackageType expected)
        => Assert.Equal(expected, PackageTypeDetector.DetectZipEntries(new[] { entry }));

    [Fact]
    public void HeaderDetection_DoesNotTrustExtension()
    {
        Assert.True(PackageTypeDetector.IsZipHeader(new byte[] { 0x50, 0x4b, 3, 4 }));
        Assert.True(PackageTypeDetector.IsPayloadHeader("CrAU"u8));
    }
}
