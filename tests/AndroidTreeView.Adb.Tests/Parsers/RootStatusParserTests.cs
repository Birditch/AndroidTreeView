using AndroidTreeView.Adb.Parsers;
using AndroidTreeView.Models.Devices;
using Xunit;

namespace AndroidTreeView.Adb.Tests.Parsers;

public class RootStatusParserTests
{
    private const string ShellId = "uid=2000(shell) gid=2000(shell) groups=2000(shell),1004(input)";
    private const string RootId = "uid=0(root) gid=0(root) groups=0(root)";

    [Fact]
    public void Parse_SuGrantsRoot_IsConfirmed()
    {
        var status = RootStatusParser.Parse(
            ShellId,
            "/system/xbin/su",
            RootId,
            "Permissive",
            "26.1");

        Assert.Equal(RootDetectionLevel.Confirmed, status.Level);
        Assert.True(status.IsRooted);
        Assert.True(status.SuBinaryExists);
        Assert.True(status.SuGrantsRoot);
        Assert.Equal("26.1", status.MagiskVersion);
        Assert.Equal("Permissive", status.SelinuxMode);
        Assert.Contains("uid=0", status.RootUserId);
    }

    [Fact]
    public void Parse_SuBinaryButNoGrant_IsLikely()
    {
        var status = RootStatusParser.Parse(
            ShellId,
            "/sbin/su",
            "su: permission denied",
            "Enforcing",
            null);

        Assert.Equal(RootDetectionLevel.Likely, status.Level);
        Assert.True(status.IsRooted);
        Assert.True(status.SuBinaryExists);
        Assert.False(status.SuGrantsRoot);
        Assert.Null(status.RootUserId);
    }

    [Fact]
    public void Parse_NoSuAnywhere_IsNotRooted()
    {
        var status = RootStatusParser.Parse(
            ShellId,
            "/system/bin/sh: which: su: not found",
            "/system/bin/sh: su: not found",
            "Enforcing",
            null);

        Assert.Equal(RootDetectionLevel.NotRooted, status.Level);
        Assert.False(status.IsRooted);
        Assert.False(status.SuBinaryExists);
        Assert.False(status.SuGrantsRoot);
    }

    [Fact]
    public void Parse_CurrentUserIsRoot_IsConfirmed()
    {
        var status = RootStatusParser.Parse(
            RootId,
            string.Empty,
            null,
            "Permissive",
            null);

        Assert.Equal(RootDetectionLevel.Confirmed, status.Level);
    }

    [Fact]
    public void Parse_MagiskPresentWithoutSu_IsLikely()
    {
        var status = RootStatusParser.Parse(
            ShellId,
            string.Empty,
            null,
            "Enforcing",
            "27.0");

        Assert.Equal(RootDetectionLevel.Likely, status.Level);
        Assert.Equal("27.0", status.MagiskVersion);
    }

    [Fact]
    public void Parse_CapturesCurrentUserId()
    {
        var status = RootStatusParser.Parse(ShellId, null, null, null, null);
        Assert.Equal(ShellId, status.CurrentUserId);
    }
}
