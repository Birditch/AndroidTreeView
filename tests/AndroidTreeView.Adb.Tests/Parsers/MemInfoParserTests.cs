using AndroidTreeView.Adb.Parsers;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public class MemInfoParserTests
{
    private const string MemInfo =
        "MemTotal:        3809036 kB\n" +
        "MemFree:          123456 kB\n" +
        "MemAvailable:    1500000 kB\n" +
        "Buffers:           10000 kB\n" +
        "Cached:           500000 kB\n";

    [Fact]
    public void Parse_ConvertsKilobytesToBytes()
    {
        var memory = MemInfoParser.Parse(MemInfo);

        Assert.Equal(3809036L * 1024, memory.TotalBytes);
        Assert.Equal(123456L * 1024, memory.FreeBytes);
        Assert.Equal(1500000L * 1024, memory.AvailableBytes);
    }

    [Fact]
    public void Parse_MissingFields_AreNull()
    {
        var memory = MemInfoParser.Parse("MemTotal: 1000 kB\n");

        Assert.Equal(1000L * 1024, memory.TotalBytes);
        Assert.Null(memory.FreeBytes);
        Assert.Null(memory.AvailableBytes);
    }

    [Fact]
    public void Parse_Empty_ReturnsNulls()
    {
        var memory = MemInfoParser.Parse(string.Empty);
        Assert.Null(memory.TotalBytes);
    }
}
