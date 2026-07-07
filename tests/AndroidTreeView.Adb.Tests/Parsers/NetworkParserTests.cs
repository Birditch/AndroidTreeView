using AndroidTreeView.Adb.Parsers;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public class NetworkParserTests
{
    private const string IpAddr =
        "1: lo: <LOOPBACK,UP,LOWER_UP> mtu 65536 qdisc noqueue state UNKNOWN group default qlen 1000\n" +
        "    link/loopback 00:00:00:00:00:00 brd 00:00:00:00:00:00\n" +
        "    inet 127.0.0.1/8 scope host lo\n" +
        "2: wlan0: <BROADCAST,MULTICAST,UP,LOWER_UP> mtu 1500 qdisc mq state UP group default qlen 3000\n" +
        "    link/ether 12:34:56:78:9a:bc brd ff:ff:ff:ff:ff:ff\n" +
        "    inet 192.168.1.55/24 brd 192.168.1.255 scope global wlan0\n" +
        "    inet6 fe80::1234:5678:9abc:def0/64 scope link\n" +
        "3: rmnet0: <POINTOPOINT,MULTICAST,NOARP> mtu 1500 qdisc pfifo_fast state DOWN group default qlen 1000\n";

    [Fact]
    public void Parse_ReturnsAllInterfaces()
    {
        var interfaces = NetworkParser.Parse(IpAddr);
        Assert.Equal(3, interfaces.Count);
    }

    [Fact]
    public void Parse_Wlan0_HasIpMacAndState()
    {
        var wlan = NetworkParser.Parse(IpAddr).Single(i => i.Name == "wlan0");

        Assert.Equal("192.168.1.55", wlan.IpAddress);
        Assert.Equal("12:34:56:78:9a:bc", wlan.MacAddress);
        Assert.Equal("UP", wlan.State);
    }

    [Fact]
    public void Parse_Loopback_HasLocalIp()
    {
        var lo = NetworkParser.Parse(IpAddr).Single(i => i.Name == "lo");
        Assert.Equal("127.0.0.1", lo.IpAddress);
    }

    [Fact]
    public void Parse_DownInterface_HasNoIp()
    {
        var rmnet = NetworkParser.Parse(IpAddr).Single(i => i.Name == "rmnet0");

        Assert.Equal("DOWN", rmnet.State);
        Assert.Null(rmnet.IpAddress);
    }

    [Fact]
    public void FindWifi_ReturnsWlanInterface()
    {
        var wifi = NetworkParser.FindWifi(NetworkParser.Parse(IpAddr));

        Assert.NotNull(wifi);
        Assert.Equal("wlan0", wifi!.Name);
        Assert.Equal("192.168.1.55", wifi.IpAddress);
    }

    [Fact]
    public void ParseMacAddress_ValidValue_IsReturned()
    {
        Assert.Equal("aa:bb:cc:dd:ee:ff", NetworkParser.ParseMacAddress("aa:bb:cc:dd:ee:ff\n"));
    }

    [Fact]
    public void ParseMacAddress_Invalid_ReturnsNull()
    {
        Assert.Null(NetworkParser.ParseMacAddress("not-a-mac"));
        Assert.Null(NetworkParser.ParseMacAddress(null));
    }

    [Fact]
    public void Parse_Empty_ReturnsEmpty()
    {
        Assert.Empty(NetworkParser.Parse(string.Empty));
    }
}
