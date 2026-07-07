using AndroidTreeView.Adb.Parsers;
using AndroidTreeView.Models.Storage;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public class StorageParserTests
{
    private const string BlockFormat =
        "Filesystem      1K-blocks    Used Available Use% Mounted on\n" +
        "/dev/block/dm-0   2000000 1500000   500000  75% /system\n" +
        "/dev/block/dm-5  50000000 30000000 20000000 60% /data\n" +
        "tmpfs              100000       0   100000   0% /dev\n";

    private const string HumanFormat =
        "Filesystem      Size Used Avail Use% Mounted on\n" +
        "/dev/block/dm-5  46G  28G   18G  61% /data\n" +
        "tmpfs           1.9G    0  1.9G   0% /dev\n";

    [Fact]
    public void Parse_BlockFormat_ConvertsBlocksToBytes()
    {
        var storage = StorageParser.Parse(BlockFormat);
        var data = Partition(storage, "/data");

        Assert.Equal(50000000L * 1024, data.TotalBytes);
        Assert.Equal(30000000L * 1024, data.UsedBytes);
        Assert.Equal(20000000L * 1024, data.AvailableBytes);
        Assert.Equal(60, data.UsePercent);
        Assert.Equal("/dev/block/dm-5", data.Name);
    }

    [Fact]
    public void Parse_BlockFormat_ReturnsAllRows()
    {
        var storage = StorageParser.Parse(BlockFormat);
        Assert.Equal(3, storage.Partitions.Count);
    }

    [Fact]
    public void Parse_HumanFormat_ConvertsSuffixesToBytes()
    {
        var storage = StorageParser.Parse(HumanFormat);
        var data = Partition(storage, "/data");

        Assert.Equal(46L * 1024 * 1024 * 1024, data.TotalBytes);
        Assert.Equal(28L * 1024 * 1024 * 1024, data.UsedBytes);
        Assert.Equal(61, data.UsePercent);
    }

    [Fact]
    public void Parse_HumanFormat_HandlesDecimalSuffix()
    {
        var storage = StorageParser.Parse(HumanFormat);
        var dev = Partition(storage, "/dev");

        Assert.Equal((long)(1.9 * 1024 * 1024 * 1024), dev.TotalBytes);
    }

    [Fact]
    public void Parse_SkipsHeaderRow()
    {
        var storage = StorageParser.Parse(HumanFormat);
        Assert.DoesNotContain(storage.Partitions, p => p.Name == "Filesystem");
    }

    [Theory]
    [InlineData("/data", true)]
    [InlineData("/system", true)]
    [InlineData("/cache", true)]
    [InlineData("/storage/emulated", true)]
    [InlineData("/sdcard", true)]
    [InlineData("/dev", false)]
    [InlineData("/proc", false)]
    [InlineData(null, false)]
    public void IsSignificantMount_ClassifiesKeyPartitions(string? mount, bool expected)
    {
        Assert.Equal(expected, StorageParser.IsSignificantMount(mount));
    }

    [Fact]
    public void ParseSize_BareInteger_TreatedAs1KBlocks()
    {
        Assert.Equal(1024L, StorageParser.ParseSize("1"));
    }

    [Fact]
    public void ParseSize_Suffixes_ConvertToBytes()
    {
        Assert.Equal(1024L, StorageParser.ParseSize("1K"));
        Assert.Equal(1024L * 1024, StorageParser.ParseSize("1M"));
        Assert.Equal(2L * 1024 * 1024 * 1024, StorageParser.ParseSize("2G"));
    }

    [Fact]
    public void Parse_Empty_ReturnsNoPartitions()
    {
        Assert.Empty(StorageParser.Parse(string.Empty).Partitions);
    }

    private static StoragePartition Partition(StorageInfo info, string mount) =>
        info.Partitions.Single(p => p.MountPoint == mount);
}
