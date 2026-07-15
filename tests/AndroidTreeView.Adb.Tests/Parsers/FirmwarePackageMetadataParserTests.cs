using AndroidTreeView.Adb.Parsers;
using AndroidTreeView.Models.Rooting;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public sealed class FirmwarePackageMetadataParserTests
{
    [Fact]
    public void Parse_OtaPreDevice_MatchesLockedDevice()
    {
        var metadata = FirmwarePackageMetadataParser.Parse(
            "/tmp/ota.zip",
            FirmwarePackageType.Payload,
            new RootDeviceIdentity { Serial = "S", Device = "oriole" },
            "pre-device=oriole|raven\npost-build=google/oriole/oriole:16/test",
            null);

        Assert.Equal(FirmwarePackageMatchStatus.Matched, metadata.MatchStatus);
        Assert.Contains("oriole", metadata.MatchingValues);
    }

    [Fact]
    public void Parse_ExplicitOtherProduct_IsMismatch()
    {
        var metadata = FirmwarePackageMetadataParser.Parse(
            "/tmp/factory.zip",
            FirmwarePackageType.NestedZip,
            new RootDeviceIdentity { Serial = "S", Product = "oriole" },
            null,
            "require product=panther");

        Assert.Equal(FirmwarePackageMatchStatus.Mismatched, metadata.MatchStatus);
    }
}
