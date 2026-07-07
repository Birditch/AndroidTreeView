using AndroidTreeView.Adb.Commands;
using AndroidTreeView.Adb.Parsers;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public class OverviewBuilderTests
{
    private static Dictionary<string, string> Props() => new()
    {
        [PropKeys.Manufacturer] = "Google",
        [PropKeys.Brand] = "google",
        [PropKeys.Model] = "Pixel 5",
        [PropKeys.ProductName] = "redfin",
        [PropKeys.Device] = "redfin",
        [PropKeys.SerialNo] = "0A1B2C3D",
        [PropKeys.AndroidVersion] = "13",
        [PropKeys.Sdk] = "33",
        [PropKeys.BuildDisplayId] = "TQ3A.230805.001",
        [PropKeys.Fingerprint] = "google/redfin/redfin:13/TQ3A.230805.001/keys",
        [PropKeys.SecurityPatch] = "2023-08-05",
        [PropKeys.BuildTags] = "release-keys",
        [PropKeys.BuildType] = "user"
    };

    [Fact]
    public void Build_MapsAllFields()
    {
        var overview = OverviewBuilder.Build(Props());

        Assert.Equal("Google", overview.Manufacturer);
        Assert.Equal("google", overview.Brand);
        Assert.Equal("Pixel 5", overview.Model);
        Assert.Equal("redfin", overview.Product);
        Assert.Equal("redfin", overview.Codename);
        Assert.Equal("0A1B2C3D", overview.SerialNumber);
        Assert.Equal("13", overview.AndroidVersion);
        Assert.Equal(33, overview.ApiLevel);
        Assert.Equal("TQ3A.230805.001", overview.BuildNumber);
        Assert.Equal("2023-08-05", overview.SecurityPatch);
        Assert.Equal("release-keys", overview.BuildTags);
        Assert.Equal("user", overview.BuildType);
    }

    [Fact]
    public void Build_DisplayName_PrependsManufacturerWhenModelLacksIt()
    {
        var overview = OverviewBuilder.Build(Props());
        Assert.Equal("Google Pixel 5", overview.DisplayName);
    }

    [Fact]
    public void Build_DisplayName_DoesNotDuplicateManufacturer()
    {
        var props = new Dictionary<string, string>
        {
            [PropKeys.Manufacturer] = "samsung",
            [PropKeys.Model] = "samsung SM-G991B"
        };

        var overview = OverviewBuilder.Build(props);
        Assert.Equal("samsung SM-G991B", overview.DisplayName);
    }

    [Fact]
    public void Build_SerialNumber_FallsBackToBootSerial()
    {
        var props = new Dictionary<string, string>
        {
            [PropKeys.BootSerialNo] = "BOOTSERIAL123"
        };

        var overview = OverviewBuilder.Build(props);
        Assert.Equal("BOOTSERIAL123", overview.SerialNumber);
    }

    [Fact]
    public void Build_MissingValues_AreNull()
    {
        var overview = OverviewBuilder.Build(new Dictionary<string, string>());

        Assert.Null(overview.Model);
        Assert.Null(overview.ApiLevel);
        Assert.Null(overview.SerialNumber);
    }
}
