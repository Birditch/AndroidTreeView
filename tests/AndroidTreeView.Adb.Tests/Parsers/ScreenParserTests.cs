using AndroidTreeView.Adb.Parsers;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public class ScreenParserTests
{
    [Fact]
    public void ParseSize_PhysicalOnly()
    {
        Assert.Equal("1080x2340", ScreenParser.ParseSize("Physical size: 1080x2340"));
    }

    [Fact]
    public void ParseSize_OverrideWins()
    {
        const string output = "Physical size: 1080x2340\nOverride size: 720x1560\n";
        Assert.Equal("720x1560", ScreenParser.ParseSize(output));
    }

    [Fact]
    public void ParseDensity_PhysicalOnly()
    {
        Assert.Equal(440, ScreenParser.ParseDensity("Physical density: 440"));
    }

    [Fact]
    public void ParseDensity_OverrideWins()
    {
        const string output = "Physical density: 440\nOverride density: 320\n";
        Assert.Equal(320, ScreenParser.ParseDensity(output));
    }

    [Fact]
    public void Parse_NullOrGarbage_ReturnsNull()
    {
        Assert.Null(ScreenParser.ParseSize(null));
        Assert.Null(ScreenParser.ParseSize("no size here"));
        Assert.Null(ScreenParser.ParseDensity(null));
        Assert.Null(ScreenParser.ParseDensity("nope"));
    }
}
