using AndroidTreeView.Adb.Parsers;
using AndroidTreeView.Models.Logs;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public class LogcatParserTests
{
    [Fact]
    public void Parse_ThreadTimeLine_ExtractsAllFields()
    {
        const string line = "06-25 14:23:01.123  1234  5678 I ActivityManager: Start proc 9999:com.example/u0a123";

        var entry = LogcatParser.Parse(line);

        Assert.Equal("06-25 14:23:01.123", entry.Timestamp);
        Assert.Equal(1234, entry.Pid);
        Assert.Equal(5678, entry.Tid);
        Assert.Equal(LogPriority.Info, entry.Priority);
        Assert.Equal("ActivityManager", entry.Tag);
        Assert.Equal("Start proc 9999:com.example/u0a123", entry.Message);
    }

    [Fact]
    public void Parse_ErrorLine_MapsPriority()
    {
        const string line = "06-25 14:23:02.000  1234  5678 E AndroidRuntime: FATAL EXCEPTION: main";

        var entry = LogcatParser.Parse(line);

        Assert.Equal(LogPriority.Error, entry.Priority);
        Assert.Equal("AndroidRuntime", entry.Tag);
        Assert.Equal("FATAL EXCEPTION: main", entry.Message);
    }

    [Theory]
    [InlineData('V', LogPriority.Verbose)]
    [InlineData('D', LogPriority.Debug)]
    [InlineData('I', LogPriority.Info)]
    [InlineData('W', LogPriority.Warn)]
    [InlineData('E', LogPriority.Error)]
    [InlineData('F', LogPriority.Fatal)]
    [InlineData('S', LogPriority.Silent)]
    public void Parse_PriorityCode_MapsToEnum(char code, LogPriority expected)
    {
        var entry = LogcatParser.Parse($"06-25 14:23:01.123  10  20 {code} Tag: message body");
        Assert.Equal(expected, entry.Priority);
    }

    [Fact]
    public void Parse_UnparseableLine_PreservesRawMessage()
    {
        const string line = "--------- beginning of main";

        var entry = LogcatParser.Parse(line);

        Assert.Equal(LogPriority.Unknown, entry.Priority);
        Assert.Equal(line, entry.Message);
        Assert.Null(entry.Timestamp);
    }
}
