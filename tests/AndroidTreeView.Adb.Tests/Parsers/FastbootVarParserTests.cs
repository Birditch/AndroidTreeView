using AndroidTreeView.Adb.Parsers;
using AndroidTreeView.Models.Rooting;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public sealed class FastbootVarParserTests
{
    [Fact]
    public void ParseVariables_ReadsStderrStyleOutputAndIgnoresNoise()
    {
        var values = FastbootVarParser.ParseVariables(
            "(bootloader) unlocked: yes\n(bootloader) has-slot:boot: yes\nFinished. Total time: 0.01s");

        Assert.Equal("yes", values["unlocked"]);
        Assert.Equal("yes", values["has-slot:boot"]);
        Assert.Equal(2, values.Count);
    }

    [Fact]
    public void ParseDevices_ReadsLongDescriptors()
    {
        var device = Assert.Single(FastbootVarParser.ParseDevices(
            "FB123 fastboot usb:1-2 product:oriole transport_id:9\n"));

        Assert.Equal("FB123", device.Serial);
        Assert.Equal("1-2", device.UsbPath);
        Assert.Equal("oriole", device.Product);
    }

    [Fact]
    public void MatchIdentity_SerialChanged_RequiresUsbAndProduct()
    {
        var target = new RootDeviceIdentity { Serial = "ADB", UsbPath = "1-2", Product = "oriole" };
        var verified = FastbootVarParser.MatchIdentity(target,
            new[] { new FastbootDeviceIdentity { Serial = "FB", UsbPath = "1-2", Product = "oriole" } });
        var unverified = FastbootVarParser.MatchIdentity(target,
            new[] { new FastbootDeviceIdentity { Serial = "FB", Product = "oriole" } });

        Assert.True(verified.IsVerified);
        Assert.Equal(FastbootIdentityEvidence.UsbPath | FastbootIdentityEvidence.Product, verified.Evidence);
        Assert.Equal(FastbootIdentityMatchStatus.Unverified, unverified.Status);
    }

    [Fact]
    public void MatchIdentity_OnlyCandidateWithoutEvidence_IsNeverSelected()
    {
        var result = FastbootVarParser.MatchIdentity(
            new RootDeviceIdentity { Serial = "ADB" },
            new[] { new FastbootDeviceIdentity { Serial = "OTHER" } });

        Assert.Equal(FastbootIdentityMatchStatus.Unverified, result.Status);
        Assert.False(result.IsVerified);
    }

    [Fact]
    public void MatchIdentity_StableSerial_AllowsDifferentBootloaderProductName()
    {
        var result = FastbootVarParser.MatchIdentity(
            new RootDeviceIdentity { Serial = "SERIAL", Product = "adb_product" },
            new[] { new FastbootDeviceIdentity { Serial = "SERIAL", Product = "fastboot-product" } });

        Assert.True(result.IsVerified);
        Assert.Equal(FastbootIdentityEvidence.Serial, result.Evidence);
    }

    [Fact]
    public void MatchIdentity_MacUsbLocationFormats_AreEquivalent()
    {
        var result = FastbootVarParser.MatchIdentity(
            new RootDeviceIdentity
            {
                Serial = "ADB-SERIAL",
                UsbPath = "3-1.2.3.4",
                Product = "cannon"
            },
            new[]
            {
                new FastbootDeviceIdentity
                {
                    Serial = "FASTBOOT-SERIAL",
                    UsbPath = "51524608X",
                    Product = "cannon"
                }
            });

        Assert.True(result.IsVerified);
        Assert.Equal(
            FastbootIdentityEvidence.UsbPath | FastbootIdentityEvidence.Product,
            result.Evidence);
    }

    [Fact]
    public void MatchIdentity_DifferentMacUsbLocations_RemainConflicting()
    {
        var result = FastbootVarParser.MatchIdentity(
            new RootDeviceIdentity
            {
                Serial = "SAME-SERIAL",
                UsbPath = "3-1.2.3.2.1"
            },
            new[]
            {
                new FastbootDeviceIdentity
                {
                    Serial = "SAME-SERIAL",
                    UsbPath = "51524608X"
                }
            });

        Assert.Equal(FastbootIdentityMatchStatus.ConflictingEvidence, result.Status);
    }
}
