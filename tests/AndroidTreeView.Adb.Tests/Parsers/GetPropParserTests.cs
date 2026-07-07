using AndroidTreeView.Adb.Parsers;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public class GetPropParserTests
{
    private const string Output =
        "[ro.product.manufacturer]: [Google]\n" +
        "[ro.product.model]: [Pixel 5]\n" +
        "[ro.build.version.sdk]: [30]\n" +
        "[persist.sys.timezone]: [America/New_York]\n" +
        "[ro.product.cpu.abilist]: [arm64-v8a,armeabi-v7a,armeabi]\n" +
        "[empty.value]: []\n" +
        "garbage line without brackets\n";

    [Fact]
    public void Parse_ReadsKeyValuePairs()
    {
        var props = GetPropParser.Parse(Output);

        Assert.Equal("Google", props["ro.product.manufacturer"]);
        Assert.Equal("Pixel 5", props["ro.product.model"]);
        Assert.Equal("30", props["ro.build.version.sdk"]);
        Assert.Equal("America/New_York", props["persist.sys.timezone"]);
    }

    [Fact]
    public void Parse_PreservesValueWithCommas()
    {
        var props = GetPropParser.Parse(Output);
        Assert.Equal("arm64-v8a,armeabi-v7a,armeabi", props["ro.product.cpu.abilist"]);
    }

    [Fact]
    public void Parse_KeepsEmptyValue()
    {
        var props = GetPropParser.Parse(Output);
        Assert.True(props.ContainsKey("empty.value"));
        Assert.Equal(string.Empty, props["empty.value"]);
    }

    [Fact]
    public void Parse_IgnoresUnrecognizedLines()
    {
        var props = GetPropParser.Parse(Output);
        Assert.Equal(6, props.Count);
    }

    [Fact]
    public void Parse_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(GetPropParser.Parse(null));
        Assert.Empty(GetPropParser.Parse(string.Empty));
    }
}
