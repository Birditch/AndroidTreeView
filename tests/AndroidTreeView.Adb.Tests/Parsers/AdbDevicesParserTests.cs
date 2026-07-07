using AndroidTreeView.Adb.Parsers;
using AndroidTreeView.Models.Devices;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public class AdbDevicesParserTests
{
    private const string Listing =
        "List of devices attached\n" +
        "* daemon not running; starting now at tcp:5037\n" +
        "emulator-5554          device product:sdk_gphone_x86_64 model:sdk_gphone_x86_64 device:emu64x transport_id:1\n" +
        "0A1B2C3D4E5F           unauthorized usb:1-1\n" +
        "192.168.1.44:5555      offline\n" +
        "FA7890ABCDEF           device usb:2-1 product:redfin model:Pixel_5 device:redfin transport_id:3\n" +
        "99001FFAZ00CAT         no permissions (user in plugdev group; are your udev rules wrong?); see [http://developer.android.com/tools/device.html]\n" +
        "recoverydev123         recovery\n" +
        "\n";

    [Fact]
    public void Parse_SkipsHeaderAndDaemonAndBlankLines()
    {
        var devices = AdbDevicesParser.Parse(Listing);

        Assert.Equal(6, devices.Count);
        Assert.DoesNotContain(devices, d => d.Serial.StartsWith('*'));
        Assert.DoesNotContain(devices, d => d.Serial.Contains("List"));
    }

    [Fact]
    public void Parse_OnlineDevice_MapsStateAndDescriptors()
    {
        var device = Single(AdbDevicesParser.Parse(Listing), "emulator-5554");

        Assert.Equal(DeviceConnectionState.Online, device.State);
        Assert.True(device.IsOnline);
        Assert.Equal("device", device.RawState);
        Assert.Equal("sdk_gphone_x86_64", device.Model);
        Assert.Equal("emu64x", device.Device);
        Assert.Equal("1", device.TransportId);
        Assert.Equal("sdk_gphone_x86_64", device.Descriptors["product"]);
    }

    [Fact]
    public void Parse_UnauthorizedDevice_IsNotAuthorized()
    {
        var device = Single(AdbDevicesParser.Parse(Listing), "0A1B2C3D4E5F");

        Assert.Equal(DeviceConnectionState.Unauthorized, device.State);
        Assert.False(device.IsAuthorized);
        Assert.Equal("1-1", device.UsbPath);
    }

    [Fact]
    public void Parse_OfflineDeviceWithIpSerial_KeepsColonInSerial()
    {
        var device = Single(AdbDevicesParser.Parse(Listing), "192.168.1.44:5555");

        Assert.Equal(DeviceConnectionState.Offline, device.State);
        Assert.Empty(device.Descriptors);
    }

    [Fact]
    public void Parse_FullDescriptorRow_MapsAllFields()
    {
        var device = Single(AdbDevicesParser.Parse(Listing), "FA7890ABCDEF");

        Assert.Equal(DeviceConnectionState.Online, device.State);
        Assert.Equal("Pixel_5", device.Model);
        Assert.Equal("redfin", device.Product);
        Assert.Equal("redfin", device.Device);
        Assert.Equal("2-1", device.UsbPath);
        Assert.Equal("3", device.TransportId);
    }

    [Fact]
    public void Parse_NoPermissions_MapsStateAndIgnoresUrlAsDescriptor()
    {
        var device = Single(AdbDevicesParser.Parse(Listing), "99001FFAZ00CAT");

        Assert.Equal(DeviceConnectionState.NoPermission, device.State);
        Assert.Equal("no permissions", device.RawState);
        // The trailing help URL must not be mistaken for a -l descriptor.
        Assert.Empty(device.Descriptors);
    }

    [Fact]
    public void Parse_RecoveryState_IsMapped()
    {
        var device = Single(AdbDevicesParser.Parse(Listing), "recoverydev123");
        Assert.Equal(DeviceConnectionState.Recovery, device.State);
    }

    [Theory]
    [InlineData("serial1 bootloader", DeviceConnectionState.Bootloader)]
    [InlineData("serial2 sideload", DeviceConnectionState.Sideload)]
    [InlineData("serial3 authorizing", DeviceConnectionState.Authorizing)]
    [InlineData("serial4 connecting", DeviceConnectionState.Connecting)]
    [InlineData("serial5 somethingweird", DeviceConnectionState.Unknown)]
    public void Parse_StateTokens_MapToEnum(string line, DeviceConnectionState expected)
    {
        var devices = AdbDevicesParser.Parse(line);
        Assert.Single(devices);
        Assert.Equal(expected, devices[0].State);
    }

    [Fact]
    public void Parse_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Empty(AdbDevicesParser.Parse(null));
        Assert.Empty(AdbDevicesParser.Parse("   "));
        Assert.Empty(AdbDevicesParser.Parse("List of devices attached\n"));
    }

    private static AdbDevice Single(IReadOnlyList<AdbDevice> devices, string serial) =>
        devices.Single(d => d.Serial == serial);
}
