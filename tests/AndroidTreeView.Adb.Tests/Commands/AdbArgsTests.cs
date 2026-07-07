using AndroidTreeView.Adb.Commands;
using AndroidTreeView.Models.Logs;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Commands;

public class AdbArgsTests
{
    [Fact]
    public void Logcat_Verbose_HasNoPriorityFilter()
    {
        var args = AdbArgs.Logcat(LogPriority.Verbose);
        Assert.Equal(new[] { "logcat", "-v", "threadtime" }, args);
    }

    [Fact]
    public void Logcat_AbovVerbose_AppendsPrioritySpec()
    {
        var args = AdbArgs.Logcat(LogPriority.Warn);
        Assert.Equal(new[] { "logcat", "-v", "threadtime", "*:W" }, args);
    }

    [Fact]
    public void Logcat_WithTagFilter_AppendsTagSpecAndSilencesRest()
    {
        var args = AdbArgs.Logcat(LogPriority.Error, "MyTag");
        Assert.Equal(new[] { "logcat", "-v", "threadtime", "MyTag:E", "*:S" }, args);
    }

    [Theory]
    [InlineData(LogPriority.Verbose, 'V')]
    [InlineData(LogPriority.Debug, 'D')]
    [InlineData(LogPriority.Info, 'I')]
    [InlineData(LogPriority.Warn, 'W')]
    [InlineData(LogPriority.Error, 'E')]
    [InlineData(LogPriority.Fatal, 'F')]
    [InlineData(LogPriority.Silent, 'S')]
    public void PriorityChar_MapsEachLevel(LogPriority priority, char expected)
    {
        Assert.Equal(expected, AdbArgs.PriorityChar(priority));
    }

    [Fact]
    public void Cat_BuildsShellArguments()
    {
        Assert.Equal(new[] { "cat", "/proc/cpuinfo" }, AdbArgs.Cat("/proc/cpuinfo"));
    }
}
